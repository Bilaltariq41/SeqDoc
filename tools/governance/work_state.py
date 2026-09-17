#!/usr/bin/env python3
"""Repository-native work-state validation and transactional operations.

The registry is authoritative.  JSON execution output and remote labels are
derived projections; all mutating operations validate a complete candidate
before replacing any repository file.
"""
import argparse
import base64
import copy
import hashlib
import json
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

STATES = {"Draft", "Blocked", "Ready", "Active", "ReviewRequired", "ResolvingFindings", "Verifying", "Closed", "Cancelled"}
LABELS = {"Blocked": "blocked", "Ready": "ready", "Active": "active", "ReviewRequired": "review-required", "ResolvingFindings": "resolving-findings", "Verifying": "verifying"}
TRANSITIONS = {
    "Draft": {"Blocked", "Ready", "Cancelled"}, "Blocked": {"Ready", "Cancelled"},
    "Ready": {"Active", "Blocked", "Cancelled"}, "Active": {"ReviewRequired", "Blocked", "Verifying", "Cancelled"},
    "ReviewRequired": {"ResolvingFindings", "Verifying", "Active", "Cancelled"},
    "ResolvingFindings": {"ReviewRequired", "Verifying", "Blocked"},
    "Verifying": {"Closed", "ReviewRequired", "Blocked"}, "Closed": set(), "Cancelled": set(),
}
CAPSULE_STATES = {"Ready": "NotStarted", "Active": "Building", "ReviewRequired": "ReviewRequired", "ResolvingFindings": "ResolvingFindings", "Verifying": "Verifying", "Closed": "Closed", "Blocked": "Blocked", "Cancelled": "Cancelled"}
SHA = re.compile(r"^[0-9a-f]{40}$")
URL = re.compile(r"^https://github\.com/[^/]+/[^/]+/(?:issues|pull)/[0-9]+(?:#.*)?$")
BRANCH = re.compile(r"^[A-Za-z0-9._/-]+$")
ID = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:-]*$")
CLAIM_KINDS = {"path", "fixture", "governance-tool", "exclusive"}


def schema(root):
    return json.loads((root / "docs/project/work-state.schema.json").read_text(encoding="utf-8"))


def load(root):
    return [json.loads(p.read_text(encoding="utf-8")) for p in sorted((root / "docs/project/work-items").glob("*.json"))]


def dump(value):
    return json.dumps(value, indent=2, ensure_ascii=False, sort_keys=True) + "\n"


def json_type(value, kind):
    return ((kind == "null" and value is None) or (kind == "boolean" and isinstance(value, bool)) or
            (kind == "integer" and isinstance(value, int) and not isinstance(value, bool)) or
            (kind == "string" and isinstance(value, str)) or (kind == "array" and isinstance(value, list)) or
            (kind == "object" and isinstance(value, dict)))


def normalize_claim(value):
    value = value.replace("\\", "/")
    if value.startswith("/") or re.match(r"^[A-Za-z]:", value):
        raise ValueError("absolute claim")
    parts = []
    for part in value.split("/"):
        if part in ("", "."):
            continue
        if part == "..":
            if not parts:
                raise ValueError("parent traversal claim")
            parts.pop()
        else:
            parts.append(part.lower())
    if not parts:
        raise ValueError("empty claim")
    return "/".join(parts)


def normalize_claim_record(record):
    if not isinstance(record, dict) or set(record) != {"kind", "value"}:
        raise ValueError("claim object must contain only kind and value")
    if record["kind"] not in CLAIM_KINDS:
        raise ValueError("invalid claim kind")
    return {"kind": record["kind"], "value": normalize_claim(record["value"])}


def claim_records(item):
    raw = item.get("claims", [])
    if not isinstance(raw, list):
        return ["claims must be an array"]
    errors = []
    normalized = []
    for record in raw:
        try:
            normalized.append(normalize_claim_record(record))
        except ValueError as error:
            errors.append(str(error))
    keys = [(x["kind"], x["value"]) for x in normalized]
    if len(keys) != len(set(keys)):
        errors.append("duplicate claim")
    return errors


def metadata_errors(item):
    errors = []
    lifecycle = item.get("lifecycle")
    execution_id = item.get("executionId")
    worktree_id = item.get("worktreeId")
    active_states = {"Active", "ReviewRequired", "ResolvingFindings", "Verifying"}
    if execution_id is not None and (not isinstance(execution_id, str) or not ID.fullmatch(execution_id)):
        errors.append("invalid execution identity")
    if worktree_id is not None and (not isinstance(worktree_id, str) or not ID.fullmatch(worktree_id) or "/" in worktree_id or "\\" in worktree_id):
        errors.append("invalid worktree identity")
    # Older records may be active without instance metadata.  They remain
    # readable until an operation explicitly activates or migrates them.
    if lifecycle not in active_states and execution_id is not None:
        errors.append("execution metadata inconsistent with lifecycle")
    errors.extend(claim_records(item))
    findings = item.get("reviewFindings")
    if findings is not None and (not isinstance(findings, list) or any(not isinstance(x, str) or not valid_finding(x) for x in findings) or len(findings) != len(set(findings)) or findings != sorted(findings)):
        errors.append("invalid review findings")
    epoch = item.get("reviewEpoch")
    peer = item.get("reviewPeer")
    if (epoch is not None and (not isinstance(epoch, str) or not ID.fullmatch(epoch))) or (peer is not None and (not isinstance(peer, str) or not ID.fullmatch(peer))):
        errors.append("invalid review metadata")
    review_states = {"ReviewRequired", "ResolvingFindings", "Verifying", "Closed"}
    if lifecycle in review_states and lifecycle != "Closed" and item.get("executionId") is not None and (not epoch or not peer or not isinstance(findings, list)):
        errors.append("review metadata missing")
    closeout = item.get("closeout")
    if closeout is not None:
        required = {"executionId", "pr", "head", "mergeSha", "focused", "final", "attribution", "findings"}
        if not isinstance(closeout, dict) or set(closeout) != required or not isinstance(closeout.get("findings"), list):
            errors.append("invalid closeout record")
    return errors


def valid_finding(value):
    if not isinstance(value, str):
        return False
    disposition, separator, evidence = value.partition(":")
    if not separator or not evidence.strip():
        return False
    disposition = disposition.strip()
    if disposition == "Fixed":
        return True
    if disposition == "Rejected":
        return bool(evidence.strip())
    if disposition == "Deferred":
        return bool(re.search(r"owner approval\s*[:=-]\s*\S", evidence, re.I))
    return False


def capsule_text(root, item):
    if not item.get("checkpointPath"):
        return None
    path = root / item["checkpointPath"] / "checkpoint.md"
    return path.read_text(encoding="utf-8") if path.exists() else None


def capsule_errors(text, expected=None, strict=False):
    if not text:
        return ["missing checkpoint capsule"]
    lines = text.splitlines()
    headings = [line[3:].strip() for line in lines if line.startswith("## ")]
    required = {
        "objective": ("objective",), "targets": ("target paths", "target paths (allowlist)"),
        "non-goals": ("non-goals",), "risks": ("risks", "risk inventory", "risk inventory and existing coverage"),
        "coverage": ("existing coverage", "existing relevant coverage", "existing relevant coverage and test plan", "existing coverage and review findings"),
        "budget": ("test budget", "soft test budget", "test assignment and budget", "test budget and verification"),
        "focused": ("focused verification", "focused implementation command", "focused command", "focused verification command"),
        "final": ("final gate", "final verification evidence", "final verification receipt"),
        "review": ("review boundary", "review boundary and next action"),
        "proof": ("acceptance proof", "completion assertions", "evidence chain and first observable consumers"),
    }
    errors = []
    if strict:
        known_words = ("authority", "path", "resource", "stop", "canonical", "migration", "required", "implementation", "independent", "repair", "delivery", "frozen", "review", "verification", "objective", "contract", "risk", "coverage", "test", "focused", "final", "acceptance", "completion", "evidence", "state", "target", "non-goal")
        for heading in headings:
            lower = heading.lower()
            if not any(word in lower for word in known_words):
                errors.append("unknown capsule field: " + heading)
        for name, alternatives in required.items():
            if not any(h.lower() in alternatives for h in headings):
                errors.append("capsule missing " + name)
        for index, line in enumerate(lines):
            if line.startswith("## "):
                next_lines = []
                for following in lines[index + 1:]:
                    if following.startswith("## "):
                        break
                    if following.strip():
                        next_lines.append(following.strip())
                if not next_lines:
                    errors.append("empty capsule section: " + line[3:].strip())
                if any(re.search(r"\b(?:TODO|TBD|placeholder|unknown evidence)\b", value, re.I) for value in next_lines):
                    errors.append("blocking placeholder in " + line[3:].strip())
    state_lines = [i for i, line in enumerate(lines) if line.strip() == "## State"]
    if len(state_lines) != 1:
        errors.append("ambiguous capsule state")
    else:
        value = next((line.strip() for line in lines[state_lines[0] + 1:] if line.strip()), "")
        if strict and not (value.startswith("`") and value.endswith("`") and len(value) > 2):
            errors.append("invalid capsule state")
        if expected and value.strip("`") != CAPSULE_STATES.get(expected):
            errors.append("capsule state mismatch")
    return errors


def validate_items(items, root=None, capsule_overrides=None):
    root = root or Path(".")
    record_schema = schema(root)
    properties = record_schema["properties"]
    errors, ids, numbers = [], {}, {}
    for item in items:
        if not isinstance(item, dict):
            errors.append("record is not an object")
            continue
        item_id = item.get("id")
        errors.extend(f"{item_id or '<unknown>'} missing {key}" for key in record_schema["required"] if key not in item)
        errors.extend(f"{item_id or '<unknown>'} unknown property {key}" for key in sorted(set(item) - set(properties)))
        if item_id in ids:
            errors.append(f"duplicate id {item_id}")
        ids[item_id] = item
        for key, rule in properties.items():
            if key not in item:
                continue
            value = item[key]
            types = rule.get("type", [])
            types = types if isinstance(types, list) else [types]
            if "const" in rule and value != rule["const"]:
                errors.append(f"{item_id} invalid {key}")
            if "enum" in rule and value not in rule["enum"]:
                errors.append(f"{item_id} invalid {key}")
            if types and not any(json_type(value, kind) for kind in types):
                errors.append(f"{item_id} invalid type {key}")
                continue
            if isinstance(value, str) and len(value) < rule.get("minLength", 0):
                errors.append(f"{item_id} short {key}")
            if isinstance(value, str) and "pattern" in rule and not re.search(rule["pattern"], value):
                errors.append(f"{item_id} invalid pattern {key}")
            if isinstance(value, list) and rule.get("uniqueItems") and len(value) != len({json.dumps(v, sort_keys=True) for v in value}):
                errors.append(f"{item_id} duplicate {key}")
        if isinstance(item_id, str) and item_id.startswith("GH-"):
            number = item.get("number")
            if number in numbers:
                errors.append(f"duplicate issue number {number}")
            numbers[number] = item_id
            if item_id != f"GH-{number}":
                errors.append(f"id/number mismatch {item_id}")
            if not URL.match(item.get("sourceUrl", "")):
                errors.append(f"invalid source URL {item_id}")
            if item.get("expectedGithubState") not in {"OPEN", "CLOSED"}:
                errors.append(f"GitHub state missing {item_id}")
        lifecycle = item.get("lifecycle")
        if lifecycle not in STATES:
            errors.append(f"invalid lifecycle {item_id}")
        if isinstance(item_id, str) and item_id.startswith("GH-") and ((lifecycle == "Closed") != (item.get("expectedGithubState") == "CLOSED")):
            errors.append(f"lifecycle/GitHub state mismatch {item_id}")
        if item.get("kind") == "synthetic" and item.get("expectedGithubState") != "NONE":
            errors.append(f"synthetic item has GitHub state {item_id}")
        if item.get("lifecycleLabel") != LABELS.get(lifecycle):
            errors.append(f"lifecycle label mismatch {item_id}")
        if item.get("branch") and (not BRANCH.match(item["branch"]) or item["branch"].startswith("/") or item["branch"].endswith("/")):
            errors.append(f"invalid branch {item_id}")
        if item.get("pr") and not URL.match(item["pr"]):
            errors.append(f"invalid PR URL {item_id}")
        if lifecycle in {"Ready", "Active"} and item.get("kind") not in {"parent", "synthetic"}:
            for key in ("owner", "track", "contractRevision", "baseline"):
                if not item.get(key):
                    errors.append(f"{item_id} missing {key}")
            if item.get("baseline") and not SHA.match(item["baseline"]):
                errors.append(f"{item_id} invalid baseline")
        if item.get("selectedForExecution") and (lifecycle not in {"Active", "ReviewRequired", "ResolvingFindings", "Verifying"} or not item.get("checkpointId") or not item.get("checkpointPath") or not item.get("nextAction")):
            errors.append(f"selected item incomplete {item_id}")
        errors.extend(f"{item_id} {error}" for error in metadata_errors(item))
        text = (capsule_overrides or {}).get(item.get("checkpointPath"), capsule_text(root, item))
        if item.get("checkpointPath") and text is None:
            errors.append(f"{item_id} missing checkpoint capsule")
        elif item.get("checkpointPath"):
            errors.extend(f"{item_id} {error}" for error in capsule_errors(text, lifecycle))
            if re.search(r"\b(?:TODO|TBD|placeholder|unknown evidence)\b", text, re.I):
                errors.append(f"{item_id} blocking capsule placeholder")
    if sum(1 for item in items if item.get("selectedForExecution")) > 1:
        errors.append("expected at most one selected item")
    for item in items:
        for dependency in item.get("dependencies", []):
            if dependency not in ids:
                errors.append(f"{item.get('id')} missing dependency {dependency}")
            if dependency == item.get("id"):
                errors.append(f"self dependency {dependency}")
        if item.get("lifecycle") in {"Ready", "Active"} and item.get("kind") not in {"parent", "synthetic"}:
            for dependency in item.get("dependencies", []):
                if ids.get(dependency, {}).get("lifecycle") != "Closed":
                    errors.append(f"{item['id']} dependency not closed: {dependency}")
    def visit(item_id, stack):
        if item_id in stack:
            errors.append("dependency cycle: " + " -> ".join(stack + [item_id]))
            return
        for dependency in ids.get(item_id, {}).get("dependencies", []):
            visit(dependency, stack + [item_id])
    for item_id in ids:
        visit(item_id, [])
    return sorted(set(errors))


def validate(root):
    errors = validate_items(load(root), root)
    if errors:
        print("\n".join(errors), file=sys.stderr)
        return 1
    print(f"valid: {len(load(root))} work items")
    return 0


def execution_object(items):
    selected = next((item for item in items if item.get("selectedForExecution")), None)
    executions = []
    for item in sorted(items, key=lambda value: value.get("id", "")):
        execution_id = item.get("executionId")
        if not execution_id and item.get("selectedForExecution") and item.get("lifecycle") in {"Active", "ReviewRequired", "ResolvingFindings", "Verifying"}:
            execution_id = f"{item['id']}:{item.get('checkpointId') or 'execution'}"
        if execution_id:
            claims = sorted(item.get("claims", []), key=lambda claim: (claim.get("kind", ""), claim.get("value", "")))
            executions.append({"executionId": execution_id, "sourceId": item["id"], "owner": item.get("owner"), "branch": item.get("branch"), "worktreeId": item.get("worktreeId"), "dependencies": sorted(item.get("dependencies", [])), "checkpointId": item.get("checkpointId"), "checkpointPath": item.get("checkpointPath"), "claims": claims})
    return {"schemaVersion": 2, "generatedBy": "tools/governance/work_state.py", "sourceId": selected.get("id") if selected else None, "mode": "active" if selected else "idle", "activePlanPath": None, "activeCheckpointPath": selected.get("checkpointPath") if selected else None, "activeCheckpointId": selected.get("checkpointId") if selected else None, "nextAction": selected.get("nextAction") if selected else "No work item is selected.", "executions": executions}


def execution_payload(items):
    return dump(execution_object(items))


def execution(root, check=False):
    items = load(root)
    errors = validate_items(items, root)
    if errors:
        print("\n".join(errors), file=sys.stderr)
        return 1
    path = root / "docs/project/execution.json"
    expected = execution_payload(items)
    actual = path.read_text(encoding="utf-8") if path.exists() else ""
    if check and actual != expected:
        print("execution.json is stale", file=sys.stderr)
        return 1
    if not check and actual != expected:
        path.write_text(expected, encoding="utf-8")
    print("execution projection: " + ("current" if check else "written"))
    return 0


def claims_from_args(args):
    records = []
    values = (("path", args.claim), ("fixture", args.fixture), ("governance-tool", args.governance_tool), ("exclusive", args.resource))
    for kind, entries in values:
        if entries is None:
            continue
        if not isinstance(entries, (list, tuple)):
            entries = [entries]
        for value in entries:
            records.append(normalize_claim_record({"kind": kind, "value": value}))
    return sorted(records, key=lambda claim: (claim["kind"], claim["value"]))


def claims_conflict(left, right):
    if left["kind"] != right["kind"]:
        return False
    if left["kind"] == "path":
        return left["value"] == right["value"] or left["value"].startswith(right["value"] + "/") or right["value"].startswith(left["value"] + "/")
    return left["value"] == right["value"]


def runtime_journal(root):
    # .git is intentionally not modified; a per-checkout temp journal is not
    # committed and contains only repository-relative names.
    identity = hashlib.sha256(str(root.resolve()).encode()).hexdigest()[:16]
    directory = Path(tempfile.gettempdir()) / "seqdoc-work-state" / identity
    directory.mkdir(parents=True, exist_ok=True)
    return directory / "journal.json"


def file_hash(value):
    return hashlib.sha256(value or b"").hexdigest()


def atomic_write(root, payloads):
    if not payloads:
        return
    paths = sorted(payloads, key=lambda path: str(path.relative_to(root)).replace("\\", "/"))
    originals = {path: (path.read_bytes() if path.exists() else None) for path in paths}
    entries = []
    for path in paths:
        relative = str(path.relative_to(root)).replace("\\", "/")
        target = payloads[path].encode("utf-8")
        entries.append({"path": relative, "originalExists": originals[path] is not None, "originalHash": file_hash(originals[path]), "targetHash": file_hash(target), "original": base64.b64encode(originals[path] or b"").decode("ascii"), "target": base64.b64encode(target).decode("ascii")})
    generation = hashlib.sha256(dump([{key: entry[key] for key in ("path", "originalHash", "targetHash")} for entry in entries]).encode("utf-8")).hexdigest()
    journal_path = runtime_journal(root)
    journal_fd = os.open(journal_path, os.O_CREAT | os.O_WRONLY | os.O_TRUNC)
    try:
        os.write(journal_fd, dump({"generation": generation, "status": "prepared", "entries": entries}).encode("utf-8"))
        os.fsync(journal_fd)
    finally:
        os.close(journal_fd)
    staged, replaced = [], []
    try:
        for path in paths:
            path.parent.mkdir(parents=True, exist_ok=True)
            fd, temporary = tempfile.mkstemp(dir=path.parent, prefix=".work-state-")
            os.write(fd, payloads[path].encode("utf-8")); os.fsync(fd); os.close(fd)
            staged.append((path, Path(temporary)))
        for path, temporary in staged:
            os.replace(temporary, path)
            replaced.append(path)
        journal_path.unlink(missing_ok=True)
    except OSError as error:
        rollback_error = None
        try:
            for path in reversed(replaced):
                if originals[path] is None:
                    path.unlink(missing_ok=True)
                else:
                    path.write_bytes(originals[path])
            for _, temporary in staged:
                temporary.unlink(missing_ok=True)
        except OSError as failure:
            rollback_error = failure
        if rollback_error is None:
            journal_path.unlink(missing_ok=True)
            print(f"transaction rolled back; recovery is unnecessary: {error}", file=sys.stderr)
        else:
            print(f"transaction interrupted and rollback failed; run recover: {rollback_error}", file=sys.stderr)
        raise


def read_journal(root):
    path = runtime_journal(root)
    if path.exists():
        return path
    legacy = root / "docs/project/work-state.journal.json"
    return legacy if legacy.exists() else None


def prepare(root, args):
    items = load(root)
    targets = [item for item in items if not args.id or item.get("id") == args.id]
    if not targets:
        print("unknown item", file=sys.stderr)
        return 1
    if args.scaffold:
        candidate = copy.deepcopy(items)
        payloads = {}
        for item in targets:
            checkpoint_id = item.get("checkpointId") or item["id"]
            checkpoint_path = item.get("checkpointPath") or f"docs/work/checkpoints/{checkpoint_id}"
            try:
                relative = normalize_repository_path(checkpoint_path)
            except ValueError as error:
                print(f"{item['id']} invalid checkpoint path: {error}", file=sys.stderr)
                return 1
            item["checkpointId"], item["checkpointPath"] = checkpoint_id, relative
            path = root / relative / "checkpoint.md"
            if not path.exists():
                payloads[path] = "# checkpoint\n\n## State\n\n`NotStarted`\n\n## Objective\n\nTODO: blocking placeholder.\n\n## Target paths\n\nTODO: blocking placeholder.\n\n## Non-goals\n\nTODO: blocking placeholder.\n\n## Risks\n\nTODO: blocking placeholder.\n\n## Existing coverage\n\nTODO: blocking placeholder.\n\n## Test budget\n\nTODO: blocking placeholder.\n\n## Focused verification\n\nTODO: blocking placeholder.\n\n## Final gate\n\nTODO: blocking placeholder.\n\n## Review boundary\n\nTODO: blocking placeholder.\n\n## Acceptance proof\n\nTODO: blocking placeholder.\n"
            payloads[root / "docs/project/work-items" / (item["id"] + ".json")] = dump(item)
        payloads[root / "docs/project/execution.json"] = execution_payload(candidate)
        try:
            atomic_write(root, payloads)
        except OSError:
            return 1
        print("scaffolded: " + ", ".join(sorted(item["id"] for item in targets)))
        return 0
    errors = []
    for item in targets:
        errors.extend(f"{item['id']} {error}" for error in capsule_errors(capsule_text(root, item), item.get("lifecycle"), True))
    if errors:
        print("\n".join(sorted(set(errors))), file=sys.stderr)
        return 1
    print("prepared: " + ", ".join(sorted(item["id"] for item in targets)))
    return 0


def observed_activation(args):
    expected = args.expected_baseline or args.baseline
    branch = args.current_branch or args.branch
    return expected, branch


def normalize_repository_path(value):
    if not isinstance(value, str) or not value or value.startswith("/") or re.match(r"^[A-Za-z]:", value):
        raise ValueError("absolute or empty path")
    parts = []
    for part in value.replace("\\", "/").split("/"):
        if part in ("", "."):
            continue
        if part == "..":
            raise ValueError("parent traversal")
        parts.append(part)
    if not parts or parts[-1].lower() == "checkpoint.md":
        raise ValueError("checkpoint path must name a directory")
    return "/".join(parts)


def observe_git(root):
    """Obtain trusted checkout facts when the CLI caller did not supply them."""
    try:
        head = subprocess.run(["git", "rev-parse", "HEAD"], cwd=root, capture_output=True, text=True, check=True).stdout.strip()
        branch = subprocess.run(["git", "branch", "--show-current"], cwd=root, capture_output=True, text=True, check=True).stdout.strip()
        status = subprocess.run(["git", "status", "--porcelain"], cwd=root, capture_output=True, text=True, check=True).stdout
    except (OSError, subprocess.SubprocessError) as error:
        raise ValueError(f"git observation unavailable: {error}")
    return head, branch, not status


def activation_packet(item, execution_id, claims):
    lines = ["# Work activation", "", f"- Item: `{item['id']}`", f"- Execution: `{execution_id}`", f"- Checkpoint: `{item.get('checkpointId')}`", "- State: `Building`", "- Claims:"]
    lines.extend(f"  - `{claim['kind']}: {claim['value']}`" for claim in claims)
    return "\n".join(lines) + "\n"


def activate(root, args):
    items = load(root)
    candidate = copy.deepcopy(items)
    item = next((value for value in candidate if value.get("id") == args.id), None)
    expected, branch = observed_activation(args)
    current_head = args.current_head
    clean = args.clean
    errors = []
    git_checkout = (root / ".git").exists()
    if git_checkout or current_head is None or branch is None or (not args.clean and not args.dirty):
        try:
            observed_head, observed_branch, observed_clean = observe_git(root)
            if git_checkout and args.current_head and args.current_head != observed_head:
                errors = ["observed HEAD differs from supplied expectation"]
            elif git_checkout and (args.current_branch or args.branch) and (args.current_branch or args.branch) != observed_branch:
                errors = ["observed branch differs from supplied expectation"]
            elif git_checkout and args.clean and not observed_clean:
                errors = ["observed worktree is dirty"]
            else:
                errors = []
            current_head = current_head or observed_head
            branch = branch or observed_branch
            clean = observed_clean
        except ValueError as error:
            errors.append(str(error))
    if not item:
        errors.append("unknown item")
    elif item.get("lifecycle") != "Ready":
        errors.append("item is not eligible for activation")
    if item and any(next((dependency for dependency in candidate if dependency.get("id") == dep), {}).get("lifecycle") != "Closed" for dep in item.get("dependencies", [])):
        errors.append("dependency not closed")
    if item and expected != item.get("baseline"):
        errors.append("baseline identity mismatch")
    if current_head != expected:
        errors.append("current HEAD is stale")
    if item and branch != item.get("branch"):
        errors.append("branch identity mismatch")
    if item:
        errors.extend(capsule_errors(capsule_text(root, item), item.get("lifecycle"), True))
    if not clean or args.dirty:
        errors.append("worktree is dirty")
    if not args.execution_id or not args.worktree_id or not ID.fullmatch(args.worktree_id) or "/" in args.worktree_id or "\\" in args.worktree_id:
        errors.append("invalid worktree identity")
    try:
        claims = claims_from_args(args)
    except ValueError as error:
        errors.append(str(error)); claims = []
    if item:
        for other in candidate:
            if other is item:
                continue
            for old in other.get("claims", []):
                try:
                    old = normalize_claim_record(old)
                except ValueError:
                    continue
                if any(claims_conflict(old, new) for new in claims):
                    errors.append("claim overlap")
    if errors:
        print("\n".join(sorted(set(errors))), file=sys.stderr)
        return 1
    item.update(lifecycle="Active", lifecycleLabel="active", executionId=args.execution_id, worktreeId=args.worktree_id, claims=claims, selectedForExecution=not any(value.get("selectedForExecution") for value in candidate), nextAction=item.get("nextAction") or "continue active execution")
    capsule_path = root / item["checkpointPath"] / "checkpoint.md"
    capsule_lines = capsule_path.read_text(encoding="utf-8").splitlines()
    state_index = next((index for index, line in enumerate(capsule_lines) if line.strip() == "## State"), None)
    if state_index is None:
        print("capsule state is missing", file=sys.stderr)
        return 1
    value_index = next((index for index in range(state_index + 1, len(capsule_lines)) if capsule_lines[index].strip()), None)
    if value_index is None:
        print("capsule state value is missing", file=sys.stderr)
        return 1
    capsule_lines[value_index] = "`Building`"
    overrides = {item["checkpointPath"]: "\n".join(capsule_lines) + "\n"}
    validation_errors = validate_items(candidate, root, overrides)
    if validation_errors:
        print("\n".join(validation_errors), file=sys.stderr)
        return 1
    packet = activation_packet(item, args.execution_id, claims)
    if args.dry_run:
        print(packet, end="")
        return 0
    payloads = {root / "docs/project/work-items" / (item["id"] + ".json"): dump(item), capsule_path: overrides[item["checkpointPath"]], root / "docs/project/execution.json": execution_payload(candidate)}
    try:
        atomic_write(root, payloads)
    except OSError:
        return 1
    print(packet, end="")
    return 0


def handoff(root, args):
    items = load(root)
    current = next((item for item in items if item.get("id") == args.id), None)
    errors = []
    if not current or current.get("executionId") != args.execution_id or current.get("lifecycle") != "Active":
        errors.append("execution identity mismatch")
    if not args.pr or not URL.match(args.pr) or (current and current.get("pr") and current.get("pr") != args.pr):
        errors.append("PR identity mismatch")
    if not args.head or not SHA.match(args.head) or args.head != args.observed_head:
        errors.append("stale head")
    if not args.peer or args.peer == args.observed_author:
        errors.append("author-as-peer")
    if not args.epoch or not ID.fullmatch(args.epoch) or not args.observed_author:
        errors.append("review identity missing")
    if args.finding and (any(not valid_finding(value) for value in args.finding) or args.finding != sorted(set(args.finding))):
        errors.append("invalid finding")
    if errors:
        print("\n".join(sorted(set(errors))), file=sys.stderr)
        return 1
    candidate = copy.deepcopy(items)
    item = next(value for value in candidate if value["id"] == current["id"])
    findings = sorted(set(args.finding or []))
    item.update(lifecycle="ReviewRequired", lifecycleLabel="review-required", pr=args.pr, reviewEpoch=args.epoch, reviewPeer=args.peer, reviewFindings=findings, review={"executionId": args.execution_id, "pr": args.pr, "head": args.head, "author": args.observed_author, "peer": args.peer, "epoch": args.epoch, "findings": findings})
    capsule_path = root / item["checkpointPath"] / "checkpoint.md"
    lines = capsule_path.read_text(encoding="utf-8").splitlines()
    state_index = next((index for index, line in enumerate(lines) if line.strip() == "## State"), None)
    if state_index is None:
        print("capsule state is missing", file=sys.stderr)
        return 1
    value_index = next((index for index in range(state_index + 1, len(lines)) if lines[index].strip()), None)
    if value_index is None:
        print("capsule state value is missing", file=sys.stderr)
        return 1
    lines[value_index] = "`ReviewRequired`"
    overrides = {item["checkpointPath"]: "\n".join(lines) + "\n"}
    validation_errors = validate_items(candidate, root, overrides)
    if validation_errors:
        print("\n".join(validation_errors), file=sys.stderr)
        return 1
    try:
        atomic_write(root, {root / "docs/project/work-items" / (item["id"] + ".json"): dump(item), capsule_path: overrides[item["checkpointPath"]], root / "docs/project/execution.json": execution_payload(candidate)})
    except OSError:
        return 1
    return 0


def closeout(root, args):
    items = load(root)
    current = next((item for item in items if item.get("id") == args.id), None)
    errors = []
    review = current.get("review", {}) if current else {}
    if not current or current.get("executionId") != args.execution_id:
        errors.append("execution identity mismatch")
    if current and current.get("lifecycle") != "ReviewRequired":
        errors.append("review handoff required")
    if not isinstance(review, dict) or review.get("executionId") != args.execution_id:
        errors.append("stored review evidence missing")
    if not args.pr or args.pr != review.get("pr") or not URL.match(args.pr):
        errors.append("PR identity mismatch")
    if not args.peer or args.peer != review.get("peer"):
        errors.append("review peer mismatch")
    if not args.head or args.head != review.get("head") or args.observed_head != args.head:
        errors.append("head identity mismatch")
    if not args.focused_receipt or not args.final_receipt or not args.attribution or not SHA.match(args.merge_sha or ""):
        errors.append("closeout receipt or identity missing")
    effective_findings = args.findings or ("resolved" if review.get("findings") and all(value.startswith("Fixed:") for value in review.get("findings", [])) else None)
    if effective_findings not in {"resolved", "none"}:
        errors.append("findings unresolved")
    if errors:
        print("\n".join(sorted(set(errors))), file=sys.stderr)
        return 1
    candidate = copy.deepcopy(items)
    item = next(value for value in candidate if value["id"] == current["id"])
    item.update(lifecycle="Closed", lifecycleLabel=None, expectedGithubState="CLOSED" if item.get("kind") == "github-issue" else item.get("expectedGithubState"), selectedForExecution=False, claims=[], closeout={"executionId": args.execution_id, "pr": args.pr, "head": args.head or "legacy", "mergeSha": args.merge_sha, "focused": args.focused_receipt, "final": args.final_receipt, "attribution": args.attribution, "findings": ["resolved"]})
    item.pop("executionId", None); item.pop("worktreeId", None); item.pop("review", None)
    recipient = None
    if args.select_id:
        recipient = next((value for value in candidate if value.get("id") == args.select_id), None)
        if not recipient or recipient is item or recipient.get("lifecycle") not in {"Active", "ReviewRequired", "ResolvingFindings", "Verifying"}:
            print("selection recipient is missing or not selectable", file=sys.stderr)
            return 1
        for value in candidate:
            value["selectedForExecution"] = value is recipient
    capsule_path = root / item["checkpointPath"] / "checkpoint.md"
    lines = capsule_path.read_text(encoding="utf-8").splitlines()
    state_index = next((index for index, line in enumerate(lines) if line.strip() == "## State"), None)
    if state_index is None:
        print("capsule state is missing", file=sys.stderr)
        return 1
    value_index = next((index for index in range(state_index + 1, len(lines)) if lines[index].strip()), None)
    if value_index is None:
        print("capsule state value is missing", file=sys.stderr)
        return 1
    lines[value_index] = "`Closed`"
    overrides = {item["checkpointPath"]: "\n".join(lines) + "\n"}
    validation_errors = validate_items(candidate, root, overrides)
    if validation_errors:
        print("\n".join(validation_errors), file=sys.stderr)
        return 1
    payloads = {root / "docs/project/work-items" / (value["id"] + ".json"): dump(value) for value in candidate if dump(value) != (root / "docs/project/work-items" / (value["id"] + ".json")).read_text(encoding="utf-8")}
    payloads[capsule_path] = overrides[item["checkpointPath"]]
    payloads[root / "docs/project/execution.json"] = execution_payload(candidate)
    try:
        atomic_write(root, payloads)
    except OSError:
        return 1
    print("closed: " + item["id"])
    return 0


def promote(root, args):
    items = load(root)
    current = next((item for item in items if item.get("id") == args.id), None)
    if not current or current.get("lifecycle") not in {"Blocked", "Draft"} or any(next((dep for dep in items if dep.get("id") == dependency), {}).get("lifecycle") != "Closed" for dependency in current.get("dependencies", [])):
        print("dependent is not promotion-ready", file=sys.stderr)
        return 1
    candidate = copy.deepcopy(items)
    item = next(value for value in candidate if value["id"] == current["id"])
    item.update(lifecycle="Ready", lifecycleLabel="ready"); item.pop("executionId", None); item.pop("worktreeId", None); item.pop("claims", None)
    capsule_path = root / item["checkpointPath"] / "checkpoint.md"
    lines = capsule_path.read_text(encoding="utf-8").splitlines()
    state_index = next((index for index, line in enumerate(lines) if line.strip() == "## State"), None)
    if state_index is None:
        print("capsule state is missing", file=sys.stderr)
        return 1
    value_index = next((index for index in range(state_index + 1, len(lines)) if lines[index].strip()), None)
    if value_index is None:
        print("capsule state value is missing", file=sys.stderr)
        return 1
    lines[value_index] = "`NotStarted`"
    overrides = {item["checkpointPath"]: "\n".join(lines) + "\n"}
    validation_errors = capsule_errors(overrides[item["checkpointPath"]], "Ready", True)
    if validation_errors:
        print("\n".join(validation_errors), file=sys.stderr)
        return 1
    validation_errors = validate_items(candidate, root, overrides)
    if validation_errors:
        print("\n".join(validation_errors), file=sys.stderr)
        return 1
    try:
        atomic_write(root, {root / "docs/project/work-items" / (item["id"] + ".json"): dump(item), capsule_path: overrides[item["checkpointPath"]], root / "docs/project/execution.json": execution_payload(candidate)})
    except OSError:
        return 1
    print("promoted: " + item["id"])
    return 0


def recover(root, args):
    path = read_journal(root)
    if not path:
        print("no interrupted operation; no action needed")
        return 0
    try:
        journal = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        print(f"recovery refused: malformed journal: {error}", file=sys.stderr)
        return 1
    entries = journal.get("entries", [])
    if not entries:
        if journal.get("status") == "interrupted" or journal.get("executionId"):
            print("recovery refused: newer state exists; inspect journal before retrying", file=sys.stderr)
            return 1
        print("recovery refused: journal has no safe preimages; inspect it and restore from version control", file=sys.stderr)
        return 1
    if not isinstance(entries, list) or not entries:
        print("recovery refused: journal has no safe entries", file=sys.stderr)
        return 1
    plans = []
    for entry in entries:
        if not isinstance(entry, dict) or not isinstance(entry.get("path"), str):
            print("recovery refused: malformed journal path", file=sys.stderr)
            return 1
        try:
            relative = normalize_repository_path(entry["path"])
        except ValueError:
            print("recovery refused: journal path escapes repository", file=sys.stderr)
            return 1
        if relative != entry["path"]:
            print("recovery refused: journal path is not canonical", file=sys.stderr)
            return 1
        target = root / relative
        current = target.read_bytes() if target.exists() else None
        if "originalHash" not in entry:
            original_exists = entry.get("original") is not None
            original = entry.get("original")
            if original is not None and not isinstance(original, str):
                print("recovery refused: malformed preimage", file=sys.stderr)
                return 1
            expected = original.encode("utf-8") if original_exists else None
            if current != expected:
                print("recovery refused: newer state exists; inspect journal before retrying", file=sys.stderr)
                return 1
            plans.append((target, expected, current, original_exists))
            continue
        if not re.fullmatch(r"[0-9a-f]{64}", str(entry.get("originalHash"))) or not re.fullmatch(r"[0-9a-f]{64}", str(entry.get("targetHash"))):
            print("recovery refused: malformed journal hashes", file=sys.stderr)
            return 1
        try:
            original = base64.b64decode(entry.get("original", ""), validate=True)
            target_bytes = base64.b64decode(entry.get("target", ""), validate=True)
        except (ValueError, TypeError):
            print("recovery refused: malformed journal encoding", file=sys.stderr)
            return 1
        original_exists = entry.get("originalExists", True)
        if not isinstance(original_exists, bool) or (not original_exists and original):
            print("recovery refused: malformed journal existence flag", file=sys.stderr)
            return 1
        if file_hash(original if original_exists else None) != entry["originalHash"] or file_hash(target_bytes) != entry["targetHash"]:
            print("recovery refused: journal hash does not match its preimage", file=sys.stderr)
            return 1
        if file_hash(current) not in {entry["originalHash"], entry["targetHash"]}:
            print("recovery refused: newer state exists; inspect journal before retrying", file=sys.stderr)
            return 1
        plans.append((target, original, current, original_exists, entry["targetHash"]))
    for plan in plans:
        target, original, current, original_exists, *target_hash = plan
        if target_hash and file_hash(current) == target_hash[0]:
            if original_exists:
                target.write_bytes(original)
            else:
                target.unlink(missing_ok=True)
    path.unlink(missing_ok=True)
    print("recovered interrupted operation")
    return 0


def read_remote(repository):
    command = ["gh", "issue", "list", "--repo", repository, "--state", "all", "--limit", "1000", "--json", "number,state,labels,comments"]
    try:
        result = subprocess.run(command, capture_output=True, text=True, check=True)
    except (OSError, subprocess.SubprocessError, TypeError) as error:
        print(f"GitHub read failed: {error}", file=sys.stderr)
        return None
    try:
        data = json.loads(result.stdout)
    except json.JSONDecodeError as error:
        print(f"GitHub read failed: malformed response: {error}", file=sys.stderr)
        return None
    if not isinstance(data, list) or any(not isinstance(value, dict) or not isinstance(value.get("number"), int) or not isinstance(value.get("labels", []), list) for value in data):
        print("GitHub read failed: malformed response", file=sys.stderr)
        return None
    return {value["number"]: value for value in data}


def github_projection(args, root):
    remote = read_remote(args.repository)
    if remote is None:
        return 1
    lifecycle_labels = set(LABELS.values()); commands = []
    for item in load(root):
        if "number" not in item:
            continue
        observed = remote.get(item["number"])
        if not observed:
            print(f"GH-{item['number']}: missing", file=sys.stderr)
            return 1
        expected = LABELS.get(item["lifecycle"])
        attached = {label.get("name") for label in observed.get("labels", []) if isinstance(label, dict)}
        if observed.get("state") != item.get("expectedGithubState"):
            print(f"GH-{item['number']}: observed state {observed.get('state')} != expected {item.get('expectedGithubState')}", file=sys.stderr)
            return 1
        current = attached & lifecycle_labels
        wrong = current - ({expected} if expected else set())
        changes = [("--remove-label", label) for label in sorted(wrong)]
        if expected and expected not in attached:
            changes.append(("--add-label", expected))
        if changes:
            commands.append(["gh", "issue", "edit", str(item["number"]), "--repo", args.repository] + [value for pair in changes for value in pair])
        if args.command == "project" and "comments" in observed:
            marker = f"seqdoc-state-v1:{item['id']}:{item['lifecycle']}"
            comments = observed.get("comments", [])
            if any(isinstance(comment, dict) and "seqdoc-state-v1" in str(comment.get("body", "")) for comment in comments):
                print(f"OBSERVED {marker} start closure")
            else:
                phase = "closure" if item["lifecycle"] == "Closed" else "start"
                commands.append(["gh", "issue", "comment", str(item["number"]), "--repo", args.repository, "--body", marker + f"\nSeqDoc packet: {phase} {item['id']}"])
    if args.command == "check-github":
        drift = []
        for item in load(root):
            if "number" in item:
                observed = remote[item["number"]]; expected = LABELS.get(item["lifecycle"]); labels = {label.get("name") for label in observed.get("labels", []) if isinstance(label, dict)} & lifecycle_labels
                if observed.get("state") != item.get("expectedGithubState") or labels != ({expected} if expected else set()):
                    drift.append(f"GH-{item['number']}: projection drift")
        if drift:
            print("\n".join(drift), file=sys.stderr)
            return 1
        print("GitHub projection: current")
        return 0
    if args.command == "sync-github":
        observed_labels = {label.get("name") for value in remote.values() for label in value.get("labels", []) if isinstance(label, dict)}
        for label in sorted(lifecycle_labels - observed_labels):
            commands.insert(0, ["gh", "label", "create", label, "--repo", args.repository, "--color", "ededed", "--force"])
    for command in sorted(commands, key=lambda value: tuple(value)):
        print(("DRY-RUN " if args.dry_run else "") + " ".join(command))
        if not args.dry_run:
            try:
                subprocess.run(command, check=True)
            except (OSError, subprocess.SubprocessError) as error:
                print(f"GitHub write failed: {error}; inspect remote before retrying", file=sys.stderr)
                return 1
    return 0


def gh(args, root):
    """Backward-compatible entry point used by older governance callers."""
    return github_projection(args, root)


def transition(root, args):
    items = load(root); target = next((item for item in items if item.get("id") == args.id), None)
    if not target or args.state not in TRANSITIONS.get(target.get("lifecycle"), set()):
        print(f"illegal transition {target.get('lifecycle') if target else '?'} -> {args.state}", file=sys.stderr)
        return 1
    select_id = getattr(args, "select_id", None)
    if args.select and select_id:
        print("--select and --select-id are mutually exclusive", file=sys.stderr)
        return 1
    candidate = copy.deepcopy(items); item = next(value for value in candidate if value["id"] == args.id)
    item.update(lifecycle=args.state, lifecycleLabel=LABELS.get(args.state))
    for source, destination in (("reason", "statusReason"), ("checkpoint_id", "checkpointId"), ("checkpoint_path", "checkpointPath"), ("next_action", "nextAction"), ("pr", "pr"), ("branch", "branch"), ("baseline", "baseline"), ("contract_revision", "contractRevision")):
        value = getattr(args, source, None)
        if value is not None:
            item[destination] = value
    if select_id:
        recipient = next((value for value in candidate if value.get("id") == select_id), None)
        if not recipient or recipient is item or recipient.get("lifecycle") not in {"Active", "ReviewRequired", "ResolvingFindings", "Verifying"} or not recipient.get("checkpointId") or not recipient.get("checkpointPath") or not recipient.get("nextAction"):
            print("selection recipient is missing or not selectable", file=sys.stderr)
            return 1
        for value in candidate:
            value["selectedForExecution"] = value is recipient
    elif args.select:
        if args.state not in {"Active", "ReviewRequired", "ResolvingFindings", "Verifying"}:
            print("selected lifecycle is not selectable", file=sys.stderr)
            return 1
        for value in candidate:
            value["selectedForExecution"] = value is item
    elif item.get("selectedForExecution") and args.state not in {"Active", "ReviewRequired", "ResolvingFindings", "Verifying"}:
        item["selectedForExecution"] = False
    overrides = {}
    if item.get("checkpointPath"):
        path = root / item["checkpointPath"] / "checkpoint.md"
        if path.exists():
            lines = path.read_text(encoding="utf-8").splitlines(); state_index = next((i for i, line in enumerate(lines) if line.strip() == "## State"), None)
            if state_index is not None:
                value_index = next((i for i in range(state_index + 1, len(lines)) if lines[i].strip()), None)
                if value_index is not None:
                    lines[value_index] = f"`{CAPSULE_STATES.get(item['lifecycle'])}`"; overrides[item["checkpointPath"]] = "\n".join(lines) + "\n"
    validation_errors = validate_items(candidate, root, overrides)
    if validation_errors:
        print("\n".join(validation_errors), file=sys.stderr)
        return 1
    if args.check or args.dry_run:
        print(dump(item), end="")
        return 0
    payloads = {root / "docs/project/work-items" / (value["id"] + ".json"): dump(value) for value in candidate if dump(value) != (root / "docs/project/work-items" / (value["id"] + ".json")).read_text(encoding="utf-8")}
    for checkpoint, text in overrides.items():
        payloads[root / checkpoint / "checkpoint.md"] = text
    payloads[root / "docs/project/execution.json"] = execution_payload(candidate)
    try:
        atomic_write(root, payloads)
    except OSError:
        return 1
    print(dump(item), end="")
    return 0


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=["validate", "project-execution", "check-github", "sync-github", "project", "transition", "prepare", "activate", "handoff", "closeout", "promote", "recover"])
    parser.add_argument("--root", type=Path, default=Path(".")); parser.add_argument("--check", action="store_true"); parser.add_argument("--dry-run", action="store_true"); parser.add_argument("--repository", default="Bilaltariq41/SeqDoc")
    for option in ("id", "state", "reason", "select-id", "checkpoint-id", "checkpoint-path", "next-action", "pr", "branch", "baseline", "contract-revision", "execution-id", "expected-baseline", "current-head", "current-branch", "worktree-id", "observed-head", "observed-author", "peer", "epoch", "findings", "focused-receipt", "final-receipt", "attribution", "merge-sha", "head"):
        parser.add_argument("--" + option)
    parser.add_argument("--select", action="store_true"); parser.add_argument("--clean", action="store_true"); parser.add_argument("--dirty", action="store_true"); parser.add_argument("--scaffold", action="store_true")
    parser.add_argument("--claim", action="append"); parser.add_argument("--fixture", action="append"); parser.add_argument("--governance-tool", dest="governance_tool", action="append"); parser.add_argument("--resource", action="append"); parser.add_argument("--finding", action="append")
    args = parser.parse_args()
    if args.command == "validate": return validate(args.root)
    if args.command == "project-execution": return execution(args.root, args.check)
    if args.command in {"check-github", "sync-github", "project"}: return github_projection(args, args.root)
    return {"transition": transition, "prepare": prepare, "activate": activate, "handoff": handoff, "closeout": closeout, "promote": promote, "recover": recover}[args.command](args.root, args)


if __name__ == "__main__":
    sys.exit(main())
