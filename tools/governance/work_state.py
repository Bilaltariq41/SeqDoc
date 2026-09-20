#!/usr/bin/env python3
"""Repository-native work-state validation and transactional operations.

The registry is authoritative.  JSON execution output and remote labels are
derived projections; all mutating operations validate a complete candidate
before replacing any repository file.
"""
import argparse
import base64
import copy
from contextlib import contextmanager
from functools import wraps
import hashlib
import json
import os
import re
import subprocess
import stat
import sys
import tempfile
import time
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
CAPSULE_STATES = {"Draft": "NotStarted", "Ready": "NotStarted", "Active": "Building", "ReviewRequired": "ReviewRequired", "ResolvingFindings": "ResolvingFindings", "Verifying": "Verifying", "Closed": "Closed", "Blocked": "Blocked", "Cancelled": "Cancelled"}
SHA = re.compile(r"^[0-9a-f]{40}$")
NODE_ID = re.compile(r"^[A-Za-z0-9_]{1,100}$")
URL = re.compile(r"^https://github\.com/[^/]+/[^/]+/(?:issues|pull)/[0-9]+(?:#.*)?$")
PR_URL = re.compile(r"^https://github\.com/([^/]+)/([^/]+)/pull/([0-9]+)(?:#.*)?$")
BRANCH = re.compile(r"^[A-Za-z0-9._/-]+$")
ID = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:-]*$")
EPOCH = re.compile(r"^[0-9]+$")
CLAIM_KINDS = {"path", "fixture", "governance-tool", "exclusive"}
DEFAULT_HANDOFF_NEXT_ACTION = "Obtain one latest-head non-author peer review."


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
    if not isinstance(value, str):
        raise ValueError("claim must be a string")
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
    if not isinstance(record["kind"], str) or record["kind"] not in CLAIM_KINDS:
        raise ValueError("invalid claim kind")
    return {"kind": record["kind"], "value": normalize_claim(record["value"])}


def is_reparse_point(path):
    """Inspect one component without resolving a filesystem alias."""
    try:
        info = path.lstat()
    except FileNotFoundError:
        return False
    if stat.S_ISLNK(info.st_mode):
        return True
    attributes = getattr(info, "st_file_attributes", 0)
    # lstat rejects aliases first.  The second metadata read is retained for
    # Windows attribute providers (and platform-independent mocked providers).
    try:
        attributes |= getattr(path.stat(), "st_file_attributes", 0)
    except FileNotFoundError:
        pass
    return bool(attributes & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400))


def claim_reparse_errors(root, record):
    if record["kind"] == "exclusive":
        return []
    current = root
    for component in record["value"].split("/"):
        current = current / component
        if is_reparse_point(current):
            return ["claim follows symlink or reparse point"]
        if not current.exists():
            break
    return []


def claim_records(item, root=None):
    raw = item.get("claims", [])
    if not isinstance(raw, list):
        return ["claims must be an array"]
    errors = []
    normalized = []
    for record in raw:
        try:
            canonical = normalize_claim_record(record)
            normalized.append(canonical)
            if record != canonical:
                errors.append("noncanonical claim")
            if root is not None:
                errors.extend(claim_reparse_errors(root, canonical))
        except ValueError as error:
            errors.append(str(error))
    keys = [(x["kind"], x["value"]) for x in normalized]
    if len(keys) != len(set(keys)):
        errors.append("duplicate claim")
    return errors


def metadata_errors(item, root=None):
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
    errors.extend(claim_records(item, root))
    findings = item.get("reviewFindings")
    if findings is not None and (not isinstance(findings, list) or any(not isinstance(x, str) or not valid_finding(x) for x in findings) or len(findings) != len(set(findings)) or findings != sorted(findings)):
        errors.append("invalid review findings")
    epoch = item.get("reviewEpoch")
    peer = item.get("reviewPeer")
    if (epoch is not None and (not isinstance(epoch, str) or not ID.fullmatch(epoch))) or (peer is not None and (not isinstance(peer, str) or not ID.fullmatch(peer))):
        errors.append("invalid review metadata")
    review_states = {"ReviewRequired", "ResolvingFindings", "Verifying", "Closed"}
    if lifecycle in review_states and lifecycle != "Closed" and item.get("executionId") is not None and not item.get("resume") and (not epoch or not peer or not isinstance(findings, list)):
        errors.append("review metadata missing")
    closeout = item.get("closeout")
    if closeout is not None:
        required = {"executionId", "pr", "head", "mergeSha", "focused", "final", "attribution", "findings"}
        if not isinstance(closeout, dict) or set(closeout) != required or not isinstance(closeout.get("findings"), list):
            errors.append("invalid closeout record")
        else:
            if not (isinstance(closeout["executionId"], str) and ID.fullmatch(closeout["executionId"]) and
                    isinstance(closeout["pr"], str) and PR_URL.match(closeout["pr"]) and
                    SHA.fullmatch(closeout["head"]) and SHA.fullmatch(closeout["mergeSha"]) and
                    all(isinstance(closeout[key], str) and closeout[key].strip() for key in ("focused", "final", "attribution")) and
                    closeout["findings"] == ["resolved"]):
                errors.append("invalid closeout record")
    review = item.get("review")
    if review is not None:
        required = {"executionId", "pr", "requestHead", "author", "peer", "epoch", "findings"}
        peer_change = {"peerChangeReason", "peerChangeEvidence", "replacementEligibility"}
        if not isinstance(review, dict) or set(review) not in (required, required | peer_change) or not isinstance(review.get("findings"), list):
            errors.append("invalid review record")
        elif not (isinstance(review["executionId"], str) and ID.fullmatch(review["executionId"]) and
                  isinstance(review["pr"], str) and PR_URL.match(review["pr"]) and SHA.fullmatch(review["requestHead"]) and
                  all(isinstance(review[key], str) and ID.fullmatch(review[key]) for key in ("author", "peer")) and EPOCH.fullmatch(review["epoch"]) and
                  review["author"].casefold() != review["peer"].casefold() and review["findings"] == sorted(set(review["findings"])) and
                  all(valid_finding(value) for value in review["findings"]) and
                  (set(review) == required or all(isinstance(review[key], str) and review[key].strip() for key in peer_change))):
             errors.append("invalid review record")
        if isinstance(review, dict):
            if not isinstance(review.get("peer"), str) or not isinstance(peer, str) or peer.casefold() != review["peer"].casefold():
                errors.append("review peer metadata mismatch")
            if not isinstance(review.get("epoch"), str) or epoch != review.get("epoch"):
                errors.append("review epoch metadata mismatch")
            if not isinstance(review.get("findings"), list) or findings != review.get("findings"):
                errors.append("review findings metadata mismatch")
    resume_record = item.get("resume")
    if resume_record is not None and (not isinstance(resume_record, dict) or set(resume_record) != {"reason", "startHead"} or
                                      not isinstance(resume_record.get("reason"), str) or not resume_record["reason"].strip() or
                                      not SHA.fullmatch(resume_record.get("startHead", ""))):
        errors.append("invalid resume record")
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
        return bool(re.match(r"^\s*final non-author peer accepted disposition\s*:\s*\S", evidence, re.I))
    return False


def capsule_text(root, item):
    if not item.get("checkpointPath"):
        return None
    try:
        path = checkpoint_path(root, item["checkpointPath"])
    except ValueError:
        return None
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
        try:
            if item.get("checkpointPath") is not None:
                checkpoint_path(root, item["checkpointPath"])
        except ValueError as error:
            errors.append(f"{item_id} invalid checkpoint path: {error}")
        errors.extend(f"{item_id} {error}" for error in metadata_errors(item, root))
        text = None if any(error.startswith(f"{item_id} invalid checkpoint path") for error in errors) else (capsule_overrides or {}).get(item.get("checkpointPath"), capsule_text(root, item))
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
    with repository_lock(root):
        if not check:
            try:
                reject_unresolved_journal(root)
            except OSError:
                return 1
        return _execution_unlocked(root, check)


def _execution_unlocked(root, check=False):
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
        try:
            atomic_write(root, {path: expected})
        except OSError:
            return 1
    print("execution projection: " + ("current" if check else "written"))
    return 0


def claims_from_args(args, root=None):
    records = []
    values = (("path", args.claim), ("fixture", args.fixture), ("governance-tool", args.governance_tool), ("exclusive", args.resource))
    for kind, entries in values:
        if entries is None:
            continue
        if not isinstance(entries, (list, tuple)):
            entries = [entries]
        for value in entries:
            records.append(normalize_claim_record({"kind": kind, "value": value}))
            if root is not None:
                errors = claim_reparse_errors(root, records[-1])
                if errors:
                    raise ValueError(errors[0])
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


def write_all(fd, data):
    view = memoryview(data)
    written = 0
    while written < len(view):
        progress = os.write(fd, view[written:])
        remaining = len(view) - written
        if isinstance(progress, bool) or not isinstance(progress, int) or progress <= 0 or progress > remaining:
            raise OSError("invalid file-descriptor write progress")
        written += progress


def create_stage(directory, prefix, data):
    fd, temporary = tempfile.mkstemp(dir=directory, prefix=prefix)
    path = Path(temporary)
    try:
        try:
            write_all(fd, data)
            os.fsync(fd)
        except BaseException:
            try:
                os.close(fd)
            except OSError:
                pass
            path.unlink(missing_ok=True)
            raise
        try:
            os.close(fd)
        except BaseException:
            path.unlink(missing_ok=True)
            raise
        return path
    except BaseException:
        path.unlink(missing_ok=True)
        raise


@contextmanager
def repository_lock(root):
    lock_path = runtime_journal(root).with_name("lock")
    fd = os.open(lock_path, os.O_CREAT | os.O_RDWR)
    acquired = False
    try:
        size = os.fstat(fd).st_size
        if size != 1:
            os.ftruncate(fd, 1)
            if size == 0:
                os.lseek(fd, 0, os.SEEK_SET)
                write_all(fd, b"0")
            os.fsync(fd)
        if os.name == "nt":
            import msvcrt
            while True:
                os.lseek(fd, 0, os.SEEK_SET)
                try:
                    msvcrt.locking(fd, msvcrt.LK_NBLCK, 1)
                    break
                except OSError as error:
                    if getattr(error, "winerror", None) not in {33, 36} and getattr(error, "errno", None) not in {11, 13, 36}:
                        raise
                    time.sleep(0.01)
            acquired = True
        else:
            import fcntl
            fcntl.flock(fd, fcntl.LOCK_EX)
            acquired = True
        yield
    finally:
        try:
            if acquired and os.name == "nt":
                import msvcrt
                os.lseek(fd, 0, os.SEEK_SET)
                msvcrt.locking(fd, msvcrt.LK_UNLCK, 1)
            elif acquired:
                import fcntl
                fcntl.flock(fd, fcntl.LOCK_UN)
        finally:
            os.close(fd)


def filesystem_operation(function):
    @wraps(function)
    def locked(root, args):
        with repository_lock(root):
            return function(root, args)
    return locked


def file_hash(value):
    return hashlib.sha256(value or b"").hexdigest()


def reject_unresolved_journal(root):
    if read_journal(root) is not None:
        message = "unresolved transaction journal exists; run recover before retrying"
        print(message, file=sys.stderr)
        raise OSError(message)


def write_journal(path, value):
    encoded = dump(value).encode("utf-8")
    fd = os.open(path, os.O_CREAT | os.O_WRONLY | os.O_TRUNC)
    failure = None
    try:
        try:
            write_all(fd, encoded)
            os.fsync(fd)
        except BaseException as error:
            failure = error
    finally:
        os.close(fd)
    if failure is not None:
        try:
            if path.stat().st_size == 0:
                path.unlink()
        except OSError:
            pass
        raise failure


def atomic_write(root, payloads):
    if not payloads:
        return
    reject_unresolved_journal(root)
    paths = sorted(payloads, key=lambda path: str(path.relative_to(root)).replace("\\", "/"))
    originals = {path: (path.read_bytes() if path.exists() else None) for path in paths}
    entries = []
    for path in paths:
        relative = str(path.relative_to(root)).replace("\\", "/")
        target = payloads[path].encode("utf-8")
        entries.append({"path": relative, "originalExists": originals[path] is not None, "originalHash": file_hash(originals[path]), "targetHash": file_hash(target), "original": base64.b64encode(originals[path] or b"").decode("ascii"), "target": base64.b64encode(target).decode("ascii")})
    generation = hashlib.sha256(dump([{key: entry[key] for key in ("path", "originalHash", "targetHash")} for entry in entries]).encode("utf-8")).hexdigest()
    journal_path = runtime_journal(root)
    journal_value = {"generation": generation, "status": "prepared", "entries": entries}
    write_journal(journal_path, journal_value)
    staged, originals_staged, replaced = [], {}, []
    try:
        entry_by_path = {entry["path"]: entry for entry in entries}
        for path in paths:
            path.parent.mkdir(parents=True, exist_ok=True)
            temporary = create_stage(path.parent, ".work-state-", payloads[path].encode("utf-8"))
            staged.append((path, temporary))
            entry_by_path[str(path.relative_to(root)).replace("\\", "/")]["targetStage"] = temporary.resolve().relative_to(root.resolve()).as_posix()
            if originals[path] is not None:
                original_temporary = create_stage(path.parent, ".work-state-original-", originals[path])
                originals_staged[path] = original_temporary
                entry_by_path[str(path.relative_to(root)).replace("\\", "/")]["originalStage"] = original_temporary.resolve().relative_to(root.resolve()).as_posix()
        journal_value["entries"] = entries
        write_journal(journal_path, journal_value)
        for path, temporary in staged:
            os.replace(temporary, path)
            replaced.append(path)
        for temporary in originals_staged.values():
            temporary.unlink(missing_ok=True)
        journal_path.unlink(missing_ok=True)
    except OSError as error:
        rollback_error = None
        try:
            for path in reversed(replaced):
                if originals[path] is None:
                    path.unlink(missing_ok=True)
                else:
                    original_stage = originals_staged[path]
                    os.replace(original_stage, path)
            for _, temporary in staged:
                temporary.unlink(missing_ok=True)
            for temporary in originals_staged.values():
                temporary.unlink(missing_ok=True)
        except OSError as failure:
            rollback_error = failure
        if rollback_error is None:
            journal_path.unlink(missing_ok=True)
            print(f"transaction rolled back; recovery is unnecessary: {error}", file=sys.stderr)
        else:
            journal_value["status"] = "interrupted"
            write_journal(journal_path, journal_value)
            print(f"transaction interrupted and rollback failed; run recover: {rollback_error}", file=sys.stderr)
        raise


def read_journal(root):
    path = runtime_journal(root)
    if path.exists():
        return path
    legacy = root / "docs/project/work-state.journal.json"
    return legacy if legacy.exists() else None


def confined_path(root, relative):
    """Resolve a journal target without following any reparse component."""
    relative = normalize_journal_path(relative)
    root = root.resolve()
    current = root
    for component in relative.split("/"):
        current = current / component
        if is_reparse_point(current):
            raise ValueError("journal path follows symlink or reparse point")
    resolved = current.resolve(strict=False)
    if resolved != root and root not in resolved.parents:
        raise ValueError("journal path escapes repository")
    return current


def confined_stage(root, target, value, prefix):
    if not isinstance(value, str):
        raise ValueError("malformed staged evidence")
    relative = normalize_journal_path(value)
    if relative != value:
        raise ValueError("staged path is not canonical")
    stage = confined_path(root, relative)
    if (stage.parent.resolve() != target.parent.resolve() or not stage.name.startswith(prefix) or
            (prefix == ".work-state-" and stage.name.startswith(".work-state-original-"))):
        raise ValueError("staged path is not adjacent and canonical")
    return stage


def validate_stage_file(stage, expected, expected_hash):
    if is_reparse_point(stage):
        raise ValueError("staged evidence follows symlink or reparse point")
    try:
        mode = stage.lstat().st_mode
    except FileNotFoundError:
        return
    if not stat.S_ISREG(mode):
        raise ValueError("staged evidence is not a regular file")
    value = stage.read_bytes()
    if value != expected or file_hash(value) != expected_hash:
        raise ValueError("staged evidence content does not match journal")


@filesystem_operation
def prepare(root, args):
    items = load(root)
    targets = [item for item in items if not args.id or item.get("id") == args.id]
    if not targets:
        print("unknown item", file=sys.stderr)
        return 1
    if args.scaffold:
        candidate = copy.deepcopy(items)
        targets = [item for item in candidate if not args.id or item.get("id") == args.id]
        payloads = {}
        for item in targets:
            supplied_id = getattr(args, "checkpoint_id", None)
            supplied_path = getattr(args, "checkpoint_path", None)
            if (supplied_id is None) != (supplied_path is None):
                print(f"{item['id']} checkpoint ID and path must be supplied together", file=sys.stderr)
                return 1
            checkpoint_id = supplied_id or item.get("checkpointId") or item["id"]
            checkpoint_relative = supplied_path or item.get("checkpointPath") or f"docs/work/checkpoints/{checkpoint_id}"
            if not ID.fullmatch(checkpoint_id):
                print(f"{item['id']} invalid checkpoint ID", file=sys.stderr)
                return 1
            try:
                relative = normalize_repository_path(checkpoint_relative)
            except ValueError as error:
                print(f"{item['id']} invalid checkpoint path: {error}", file=sys.stderr)
                return 1
            if supplied_id is not None and relative.rstrip("/").split("/")[-1] != checkpoint_id:
                print(f"{item['id']} checkpoint ID/path mismatch", file=sys.stderr)
                return 1
            item["checkpointId"], item["checkpointPath"] = checkpoint_id, relative
            path = checkpoint_path(root, relative)
            if not path.exists():
                state = CAPSULE_STATES.get(item.get("lifecycle"), "NotStarted")
                if item.get("lifecycle") == "Active":
                    payloads[path] = (f"# checkpoint\n\n## State\n\n`{state}`\n\n## Objective\n\nScaffolded active checkpoint.\n\n## Target paths\n\n- `governance target`\n\n## Non-goals\n\n- Product behavior.\n\n## Risks\n\n- Transactional write failure.\n\n## Existing coverage\n\n- Synthetic baseline.\n\n## Test budget\n\n- Focused governance coverage.\n\n## Focused verification\n\n`python -B -m unittest tests.governance.test_work_state`\n\n## Final gate\n\n`python -B -m unittest tests.governance.test_work_state`\n\n## Review boundary\n\nA latest-head non-author peer reviews the candidate.\n\n## Acceptance proof\n\nThe operation is observable and transactional.\n")
                else:
                    payloads[path] = f"# checkpoint\n\n## State\n\n`{state}`\n\n## Objective\n\nTODO: blocking placeholder.\n\n## Target paths\n\nTODO: blocking placeholder.\n\n## Non-goals\n\nTODO: blocking placeholder.\n\n## Risks\n\nTODO: blocking placeholder.\n\n## Existing coverage\n\nTODO: blocking placeholder.\n\n## Test budget\n\nTODO: blocking placeholder.\n\n## Focused verification\n\nTODO: blocking placeholder.\n\n## Final gate\n\nTODO: blocking placeholder.\n\n## Review boundary\n\nTODO: blocking placeholder.\n\n## Acceptance proof\n\nTODO: blocking placeholder.\n"
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
            raise ValueError("noncanonical path")
        if part == "..":
            raise ValueError("parent traversal")
        parts.append(part)
    if not parts or parts[-1].lower() == "checkpoint.md":
        raise ValueError("checkpoint path must name a directory")
    return "/".join(parts)


def checkpoint_path(root, value):
    """Validate a persisted capsule directory without following reparse aliases."""
    relative = normalize_repository_path(value)
    if relative != value:
        raise ValueError("noncanonical path")
    current = root.resolve()
    for component in relative.split("/"):
        current = current / component
        if is_reparse_point(current):
            raise ValueError("path follows symlink or reparse point")
    return root / relative / "checkpoint.md"


def normalize_journal_path(value):
    if not isinstance(value, str) or not value or value.startswith("/") or re.match(r"^[A-Za-z]:", value):
        raise ValueError("absolute or empty path")
    parts = []
    for part in value.replace("\\", "/").split("/"):
        if part in ("", "."):
            continue
        if part == "..":
            raise ValueError("parent traversal")
        parts.append(part)
    if not parts:
        raise ValueError("empty path")
    return "/".join(parts)


def pr_parts(value, repository):
    match = PR_URL.fullmatch(value or "")
    if not match or "/".join(match.group(1, 2)) != repository:
        raise ValueError("PR URL/repository mismatch")
    return int(match.group(3))


def canonical_repository_binding(item, repository):
    source = item.get("sourceUrl")
    source_match = re.fullmatch(r"https://github\.com/([^/]+)/([^/]+)/issues/([0-9]+)", source or "")
    pr_match = re.fullmatch(PR_URL.pattern, item.get("pr") or "", re.IGNORECASE)
    if item.get("kind") != "github-issue" or not source_match or not pr_match:
        raise ValueError("canonical repository identity is missing or malformed")
    if (source_match.group(1).casefold(), source_match.group(2).casefold()) != tuple(part.casefold() for part in repository.split("/")):
        raise ValueError("canonical repository identity mismatch")
    if (pr_match.group(1).casefold(), pr_match.group(2).casefold()) != (source_match.group(1).casefold(), source_match.group(2).casefold()):
        raise ValueError("canonical repository identity mismatch")
    if (not source_match.group(3).isdigit() or int(source_match.group(3)) <= 0 or
            not pr_match.group(3).isdigit() or int(pr_match.group(3)) <= 0 or
            source_match.group(3) != str(item.get("number"))):
        raise ValueError("canonical issue identity mismatch")
    return int(source_match.group(3))


def authenticated_pr(repository, pr):
    match = re.fullmatch(PR_URL.pattern, pr or "", re.IGNORECASE)
    if not match:
        raise ValueError("malformed PR URL")
    number = int(match.group(3))
    command = ["gh", "pr", "view", str(number), "--repo", repository,
               "--json", "number,state,isDraft,author,headRefOid,mergeCommit,reviewDecision"]
    try:
        result = subprocess.run(command, capture_output=True, text=True, check=True)
        value = json.loads(result.stdout)
    except (OSError, subprocess.SubprocessError, TypeError, json.JSONDecodeError) as error:
        raise ValueError(f"authenticated PR observation failed: {error}")
    required = {"number", "state", "isDraft", "author", "headRefOid", "mergeCommit", "reviewDecision"}
    if (not isinstance(value, dict) or (match.group(1).casefold(), match.group(2).casefold()) != tuple(part.casefold() for part in repository.split("/")) or
            not required <= set(value) or set(value) - required - {"url"} or value["number"] != number or
            value["state"] not in {"OPEN", "CLOSED", "MERGED"} or not isinstance(value["isDraft"], bool) or
            value["reviewDecision"] is not None and not isinstance(value["reviewDecision"], str)):
        raise ValueError("malformed authenticated PR observation")
    if "url" in value and (not isinstance(value["url"], str) or value["url"].casefold() != pr.casefold()):
        raise ValueError("authenticated PR URL mismatch")
    author = value["author"]
    if not isinstance(author, dict) or not isinstance(author.get("login"), str) or not author["login"].strip() or not ID.fullmatch(author["login"]):
        raise ValueError("malformed authenticated PR author")
    if (not isinstance(author.get("id"), str) or not NODE_ID.fullmatch(author["id"])):
        raise ValueError("malformed authenticated PR author")
    if ("name" in author and author["name"] is not None and
            not isinstance(author["name"], str)):
        raise ValueError("malformed authenticated PR author")
    if not isinstance(author.get("is_bot"), bool):
        raise ValueError("malformed authenticated PR author")
    if not isinstance(value["headRefOid"], str) or not SHA.fullmatch(value["headRefOid"]):
        raise ValueError("malformed authenticated PR head")
    merge = value.get("mergeCommit")
    if merge is not None and (not isinstance(merge, dict) or set(merge) != {"oid"} or not SHA.fullmatch(str(merge["oid"]))):
        raise ValueError("malformed authenticated merge commit")
    return value


def authenticated_reviews(repository, number, peer, head):
    command = ["gh", "api", f"repos/{repository}/pulls/{number}/reviews", "--paginate", "--slurp"]
    try:
        result = subprocess.run(command, capture_output=True, text=True, check=True)
        value = json.loads(result.stdout)
    except (OSError, subprocess.SubprocessError, TypeError, json.JSONDecodeError) as error:
        raise ValueError(f"authenticated review observation failed: {error}")
    if not isinstance(value, list) or any(not isinstance(page, list) for page in value):
        raise ValueError("malformed authenticated review observation")
    for review in [review for page in value for review in page]:
        if not isinstance(review, dict) or not {"user", "state", "commit_id"} <= set(review):
            raise ValueError("malformed authenticated review observation")
        user = review["user"]
        if (not isinstance(user, dict) or not isinstance(user.get("login"), str) or
                user.get("type") != "User" or not isinstance(review["state"], str) or not isinstance(review["commit_id"], str)):
            raise ValueError("malformed authenticated review observation")
        if user["login"].casefold() == peer.casefold() and review["state"] == "APPROVED" and review["commit_id"] == head:
            return
    raise ValueError("no exact-head peer approval")


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


@filesystem_operation
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
        claims = claims_from_args(args, root)
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
    capsule_path = checkpoint_path(root, item["checkpointPath"])
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


@filesystem_operation
def resume(root, args):
    items = load(root)
    candidate = copy.deepcopy(items)
    item = next((value for value in candidate if value.get("id") == args.id), None)
    errors = []
    if not item or item.get("lifecycle") != "Blocked":
        errors.append("item is not blocked")
    if any(value.get("selectedForExecution") for value in candidate):
        errors.append("an execution is already selected")
    if not args.execution_id or not ID.fullmatch(args.execution_id) or not args.worktree_id or not ID.fullmatch(args.worktree_id) or "/" in args.worktree_id or "\\" in args.worktree_id:
        errors.append("invalid execution identity")
    if not isinstance(args.reason, str) or not args.reason.strip():
        errors.append("reason is required")
    if (not isinstance(args.next_action, str) or not args.next_action.strip()):
        errors.append("next action is required")
    if not args.expected_baseline or not SHA.fullmatch(args.expected_baseline) or (item and args.expected_baseline != item.get("baseline")):
        errors.append("baseline identity mismatch")
    if item:
        try:
            if not item.get("checkpointId") or not item.get("checkpointPath") or not checkpoint_path(root, item["checkpointPath"]).is_file():
                errors.append("checkpoint is missing")
        except ValueError as error:
            errors.append(f"invalid checkpoint path: {error}")
    if item and any(next((dependency for dependency in candidate if dependency.get("id") == dep), {}).get("lifecycle") != "Closed"
                    for dep in item.get("dependencies", [])):
        errors.append("dependency not closed")
    try:
        actual_head, actual_branch, clean = observe_git(root)
    except ValueError as error:
        print(str(error), file=sys.stderr)
        return 1
    if (not isinstance(args.current_head, str) or not SHA.fullmatch(args.current_head) or
            args.current_head != actual_head or not isinstance(args.start_head, str) or
            not SHA.fullmatch(args.start_head) or args.start_head != actual_head):
        errors.append("observed HEAD differs from supplied expectation")
    if not SHA.fullmatch(actual_head):
        errors.append("malformed observed HEAD")
    if not isinstance(args.current_branch, str) or not args.current_branch or args.current_branch != actual_branch:
        errors.append("observed branch differs from supplied expectation")
    if item and actual_branch != item.get("branch"):
        errors.append("branch identity mismatch")
    if not args.clean or not clean or args.dirty:
        errors.append("worktree is dirty")
    try:
        claims = claims_from_args(args, root)
    except ValueError as error:
        errors.append(str(error)); claims = []
    if not claims:
        errors.append("claims are required")
    if len(claims) != len({(c["kind"], c["value"]) for c in claims}):
        errors.append("duplicate claims")
    for other in candidate:
        if item is not None and other is item:
            continue
        for old in other.get("claims", []):
            try:
                if any(claims_conflict(normalize_claim_record(old), new) for new in claims):
                    errors.append("claim overlap")
            except ValueError:
                continue
    if errors:
        print("\n".join(sorted(set(errors))), file=sys.stderr)
        return 1
    item.update(lifecycle="ResolvingFindings", lifecycleLabel="resolving-findings", executionId=args.execution_id,
                worktreeId=args.worktree_id, selectedForExecution=True, claims=claims,
                nextAction=args.next_action, statusReason=args.reason,
                resume={"reason": args.reason, "startHead": actual_head})
    capsule_path = checkpoint_path(root, item["checkpointPath"])
    lines = capsule_path.read_text(encoding="utf-8").splitlines()
    state_index = next((i for i, line in enumerate(lines) if line.strip() == "## State"), None)
    value_index = next((i for i in range((state_index or 0) + 1, len(lines)) if lines[i].strip()), None) if state_index is not None else None
    if value_index is None:
        print("capsule state is missing", file=sys.stderr)
        return 1
    lines[value_index] = "`ResolvingFindings`"
    overrides = {item["checkpointPath"]: "\n".join(lines) + "\n"}
    validation_errors = validate_items(candidate, root, overrides)
    if validation_errors:
        print("\n".join(validation_errors), file=sys.stderr)
        return 1
    try:
        atomic_write(root, {root / "docs/project/work-items" / (item["id"] + ".json"): dump(item), capsule_path: overrides[item["checkpointPath"]], root / "docs/project/execution.json": execution_payload(candidate)})
    except OSError:
        return 1
    print(dump(item), end="")
    return 0


@filesystem_operation
def handoff(root, args):
    items = load(root)
    current = next((item for item in items if item.get("id") == args.id), None)
    errors = []
    if not current or current.get("executionId") != args.execution_id or current.get("lifecycle") not in {"Active", "ResolvingFindings"}:
        errors.append("execution identity mismatch")
    if current:
        try:
            checkpoint_path(root, current.get("checkpointPath"))
        except ValueError as error:
            errors.append(f"invalid checkpoint path: {error}")
    if not args.pr or (current and current.get("pr") and current.get("pr") != args.pr):
        errors.append("PR identity mismatch")
    if not SHA.fullmatch(args.head or ""):
        errors.append("head identity mismatch")
    observation = None
    if not errors:
        try:
            if current.get("kind") == "github-issue":
                canonical_repository_binding(current, args.repository)
            observation = authenticated_pr(args.repository, args.pr)
        except ValueError as error:
            errors.append(str(error))
    if observation and (observation["state"] != "OPEN" or observation["isDraft"] or observation["headRefOid"] != args.head):
        errors.append("PR is not an open non-draft latest-head match")
    author = observation["author"]["login"] if observation else None
    if observation and observation["author"].get("is_bot") is True:
        errors.append("bot author is not eligible for human peer review")
    if not args.peer or (author and args.peer.casefold() == author.casefold()):
        errors.append("author-as-peer")
    prior_review = current.get("review") if current and current.get("lifecycle") == "ResolvingFindings" else None
    if not args.epoch or not EPOCH.fullmatch(args.epoch) or not author:
        errors.append("review identity missing")
    if not args.finding or any(not valid_finding(value) for value in args.finding) or len(args.finding) != len(set(args.finding)):
        errors.append("invalid finding")
    next_action = DEFAULT_HANDOFF_NEXT_ACTION if args.next_action is None else args.next_action
    if not isinstance(next_action, str) or not next_action.strip():
        errors.append("next action is required")
    if prior_review:
        if not isinstance(prior_review, dict) or not EPOCH.fullmatch(str(prior_review.get("epoch", ""))) or int(args.epoch) <= int(prior_review["epoch"]):
            errors.append("review epoch is not increasing")
        if not isinstance(prior_review, dict) or observation is None or observation["headRefOid"] == prior_review.get("requestHead"):
            errors.append("PR head did not advance")
        allow_peer_change = getattr(args, "allow_peer_change", False)
        peer_changed = not isinstance(prior_review, dict) or args.peer.casefold() != prior_review.get("peer", "").casefold()
        change_fields = ("peer_change_reason", "peer_change_evidence", "replacement_eligibility")
        change_values = {name: getattr(args, name, None) for name in change_fields}
        if peer_changed:
            if not allow_peer_change:
                errors.append("review peer changed without explicit authorization")
            if any(not isinstance(value, str) or not value.strip() for value in change_values.values()):
                errors.append("peer change metadata is incomplete")
        elif any(value is not None for value in change_values.values()):
            errors.append("peer change metadata is unauthorized")
    if errors:
        print("\n".join(sorted(set(errors))), file=sys.stderr)
        return 1
    candidate = copy.deepcopy(items)
    item = next(value for value in candidate if value["id"] == current["id"])
    findings = sorted(set(args.finding or []))
    review_value = {"executionId": args.execution_id, "pr": args.pr, "requestHead": args.head, "author": author, "peer": args.peer, "epoch": args.epoch, "findings": findings}
    if prior_review and args.peer.casefold() != prior_review.get("peer", "").casefold():
        review_value.update(peerChangeReason=args.peer_change_reason, peerChangeEvidence=args.peer_change_evidence, replacementEligibility=args.replacement_eligibility)
    item.update(lifecycle="ReviewRequired", lifecycleLabel="review-required", pr=args.pr, reviewEpoch=args.epoch, reviewPeer=args.peer, reviewFindings=findings, nextAction=next_action, review=review_value)
    capsule_path = checkpoint_path(root, item["checkpointPath"])
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


@filesystem_operation
def closeout(root, args):
    items = load(root)
    current = next((item for item in items if item.get("id") == args.id), None)
    errors = []
    review = current.get("review", {}) if current else {}
    if not current or current.get("executionId") != args.execution_id:
        errors.append("execution identity mismatch")
    if current:
        try:
            checkpoint_path(root, current.get("checkpointPath"))
        except ValueError as error:
            errors.append(f"invalid checkpoint path: {error}")
    if current and current.get("lifecycle") != "ReviewRequired":
        errors.append("review handoff required")
    if not isinstance(review, dict) or review.get("executionId") != args.execution_id:
        errors.append("stored review evidence missing")
    if not args.pr or args.pr != review.get("pr"):
        errors.append("PR identity mismatch")
    if not args.peer or args.peer.casefold() != review.get("peer", "").casefold():
        errors.append("review peer mismatch")
    if not args.head or not SHA.match(args.head):
        errors.append("head identity mismatch")
    if not args.focused_receipt or not args.final_receipt or not args.attribution or not SHA.match(args.merge_sha or ""):
        errors.append("closeout receipt or identity missing")
    stored_findings = review.get("findings") if isinstance(review.get("findings"), list) else None
    effective_findings = None
    if args.findings not in {None, "resolved", "none"}:
        errors.append("findings unresolved")
    elif stored_findings is None:
        errors.append("stored review findings missing")
    elif args.findings == "none":
        if stored_findings:
            errors.append("findings unresolved")
        else:
            effective_findings = "none"
    elif args.findings == "resolved":
        if stored_findings and all(value.startswith(("Fixed:", "Rejected:", "Deferred:")) for value in stored_findings):
            effective_findings = "resolved"
        else:
            errors.append("findings unresolved")
    elif stored_findings and all(value.startswith("Fixed:") for value in stored_findings):
        effective_findings = "resolved"
    else:
        errors.append("findings unresolved")
    observation = None
    if not errors:
        try:
            if current.get("kind") == "github-issue":
                canonical_repository_binding(current, args.repository)
            match = PR_URL.fullmatch(args.pr or "")
            if not match:
                raise ValueError("malformed PR URL")
            number = int(match.group(3))
            observation = authenticated_pr(args.repository, args.pr)
            if observation["state"] != "MERGED" or not SHA.match(observation["headRefOid"]) or observation["headRefOid"] != args.head or not observation.get("mergeCommit"):
                raise ValueError("PR is not merged at the stored head")
            merge_sha = observation["mergeCommit"]["oid"]
            if merge_sha != args.merge_sha:
                raise ValueError("merge SHA mismatch")
            authenticated_reviews(args.repository, number, review.get("peer"), observation["headRefOid"])
            authenticated_author = observation["author"]["login"]
            if (authenticated_author.casefold() != review.get("author", "").casefold() or
                    args.attribution.casefold() != authenticated_author.casefold()):
                raise ValueError("closeout attribution does not match authenticated PR author")
        except ValueError as error:
            errors.append(str(error))
    if errors:
        print("\n".join(sorted(set(errors))), file=sys.stderr)
        return 1
    candidate = copy.deepcopy(items)
    item = next(value for value in candidate if value["id"] == current["id"])
    item.update(lifecycle="Closed", lifecycleLabel=None, expectedGithubState="CLOSED" if item.get("kind") == "github-issue" else item.get("expectedGithubState"), selectedForExecution=False, claims=[], closeout={"executionId": args.execution_id, "pr": args.pr, "head": args.head, "mergeSha": args.merge_sha, "focused": args.focused_receipt, "final": args.final_receipt, "attribution": observation["author"]["login"], "findings": ["resolved"]})
    item.pop("executionId", None); item.pop("worktreeId", None); item.pop("review", None)
    recipient = None
    if args.select_id:
        recipient = next((value for value in candidate if value.get("id") == args.select_id), None)
        if not recipient or recipient is item or recipient.get("lifecycle") not in {"Active", "ReviewRequired", "ResolvingFindings", "Verifying"}:
            print("selection recipient is missing or not selectable", file=sys.stderr)
            return 1
        for value in candidate:
            value["selectedForExecution"] = value is recipient
    capsule_path = checkpoint_path(root, item["checkpointPath"])
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


@filesystem_operation
def promote(root, args):
    items = load(root)
    current = next((item for item in items if item.get("id") == args.id), None)
    if not current or current.get("lifecycle") not in {"Blocked", "Draft"} or any(next((dep for dep in items if dep.get("id") == dependency), {}).get("lifecycle") != "Closed" for dependency in current.get("dependencies", [])):
        print("dependent is not promotion-ready", file=sys.stderr)
        return 1
    candidate = copy.deepcopy(items)
    item = next(value for value in candidate if value["id"] == current["id"])
    item.update(lifecycle="Ready", lifecycleLabel="ready"); item.pop("executionId", None); item.pop("worktreeId", None); item.pop("claims", None)
    capsule_path = checkpoint_path(root, item["checkpointPath"])
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


@filesystem_operation
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
    evidence_stages = []
    for entry in entries:
        if not isinstance(entry, dict) or not isinstance(entry.get("path"), str):
            print("recovery refused: malformed journal path", file=sys.stderr)
            return 1
        try:
            relative = normalize_journal_path(entry["path"])
        except ValueError:
            print("recovery refused: journal path escapes repository", file=sys.stderr)
            return 1
        if relative != entry["path"]:
            print("recovery refused: journal path is not canonical", file=sys.stderr)
            return 1
        try:
            target = confined_path(root, relative)
        except ValueError as error:
            print(f"recovery refused: {error}", file=sys.stderr)
            return 1
        stage_paths = {}
        try:
            if entry.get("targetStage") is not None:
                stage_paths["target"] = confined_stage(root, target, entry["targetStage"], ".work-state-")
            if entry.get("originalStage") is not None:
                stage_paths["original"] = confined_stage(root, target, entry["originalStage"], ".work-state-original-")
        except ValueError as error:
            print(f"recovery refused: {error}", file=sys.stderr)
            return 1
        current = target.read_bytes() if target.exists() else None
        if "originalHash" not in entry:
            if stage_paths:
                print("recovery refused: legacy journal cannot authorize staged evidence", file=sys.stderr)
                return 1
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
        try:
            if "target" in stage_paths:
                validate_stage_file(stage_paths["target"], target_bytes, entry["targetHash"])
                evidence_stages.append(stage_paths["target"])
            if "original" in stage_paths:
                if not original_exists:
                    raise ValueError("original stage is present for an absent target")
                validate_stage_file(stage_paths["original"], original, entry["originalHash"])
                evidence_stages.append(stage_paths["original"])
        except ValueError as error:
            print(f"recovery refused: {error}", file=sys.stderr)
            return 1
        if file_hash(current) not in {entry["originalHash"], entry["targetHash"]}:
            print("recovery refused: newer state exists; inspect journal before retrying", file=sys.stderr)
            return 1
        plans.append((target, original, current, original_exists, entry["targetHash"]))
    restore_stages = []
    try:
        for plan in plans:
            target, original, current, original_exists, *target_hash = plan
            if target_hash and file_hash(current) == target_hash[0]:
                if original_exists:
                    temporary = create_stage(target.parent, ".work-state-recover-", original)
                    restore_stages.append(temporary)
                    os.replace(temporary, target)
                else:
                    target.unlink(missing_ok=True)
        for temporary in restore_stages:
            temporary.unlink(missing_ok=True)
        for temporary in evidence_stages:
            temporary.unlink(missing_ok=True)
        path.unlink(missing_ok=True)
    except OSError as error:
        print(f"recovery interrupted; journal and staged evidence retained: {error}", file=sys.stderr)
        return 1
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
    items = load(root)
    errors = validate_items(items, root)
    if errors:
        print("\n".join(errors), file=sys.stderr)
        return 1
    remote = read_remote(args.repository)
    if remote is None:
        return 1
    lifecycle_labels = set(LABELS.values()); label_commands = []; edit_commands = []; comment_commands = []
    for item in items:
        if "number" not in item:
            continue
        observed = remote.get(item["number"])
        if not observed:
            print(f"GH-{item['number']}: missing", file=sys.stderr)
            return 1
        if args.command == "project":
            comments = observed.get("comments")
            if (not isinstance(comments, list) or
                    any(not isinstance(comment, dict) or not isinstance(comment.get("body"), str) for comment in comments)):
                print(f"GH-{item['number']}: malformed comments", file=sys.stderr)
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
            edit_commands.append(["gh", "issue", "edit", str(item["number"]), "--repo", args.repository] + [value for pair in changes for value in pair])
        if args.command == "project" and "comments" in observed:
            marker = f"seqdoc-state-v1:{item['id']}:{item['lifecycle']}"
            comments = observed.get("comments", [])
            phase = "closure" if item["lifecycle"] == "Closed" else "start"
            if any(isinstance(comment, dict) and str(comment.get("body", "")).splitlines()[:1] == [marker] for comment in comments):
                print(f"OBSERVED {marker} {phase}")
            else:
                comment_commands.append(["gh", "issue", "comment", str(item["number"]), "--repo", args.repository, "--body", marker + f"\nSeqDoc packet: {phase} {item['id']}"])
    if args.command == "check-github":
        drift = []
        for item in items:
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
            label_commands.append(["gh", "label", "create", label, "--repo", args.repository, "--color", "ededed", "--force"])
    commands = sorted(label_commands, key=lambda value: tuple(value)) + sorted(edit_commands, key=lambda value: tuple(value)) + sorted(comment_commands, key=lambda value: tuple(value))
    for command in commands:
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


@filesystem_operation
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
    if args.select and not item.get("nextAction"):
        item["nextAction"] = "continue active execution"
    if args.state in {"Blocked", "Cancelled"}:
        for field in ("executionId", "worktreeId", "claims", "review", "reviewEpoch", "reviewPeer", "reviewFindings"):
            item.pop(field, None)
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
        try:
            path = checkpoint_path(root, item["checkpointPath"])
        except ValueError as error:
            print(f"invalid checkpoint path: {error}", file=sys.stderr)
            return 1
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
    parser.add_argument("command", choices=["validate", "project-execution", "check-github", "sync-github", "project", "transition", "prepare", "activate", "resume", "handoff", "closeout", "promote", "recover"])
    parser.add_argument("--root", type=Path, default=Path(".")); parser.add_argument("--check", action="store_true"); parser.add_argument("--dry-run", action="store_true"); parser.add_argument("--repository", default="Bilaltariq41/SeqDoc")
    for option in ("id", "state", "reason", "start-head", "select-id", "checkpoint-id", "checkpoint-path", "next-action", "pr", "branch", "baseline", "contract-revision", "execution-id", "expected-baseline", "current-head", "current-branch", "worktree-id", "observed-head", "observed-author", "peer", "epoch", "findings", "focused-receipt", "final-receipt", "attribution", "merge-sha", "head", "peer-change-reason", "peer-change-evidence", "replacement-eligibility"):
        parser.add_argument("--" + option)
    parser.add_argument("--allow-peer-change", action="store_true")
    parser.add_argument("--select", action="store_true"); parser.add_argument("--clean", action="store_true"); parser.add_argument("--dirty", action="store_true"); parser.add_argument("--scaffold", action="store_true")
    parser.add_argument("--claim", action="append"); parser.add_argument("--fixture", action="append"); parser.add_argument("--governance-tool", dest="governance_tool", action="append"); parser.add_argument("--resource", action="append"); parser.add_argument("--finding", action="append")
    args = parser.parse_args()
    if args.command == "validate": return validate(args.root)
    if args.command == "project-execution": return execution(args.root, args.check)
    if args.command in {"check-github", "sync-github", "project"}: return github_projection(args, args.root)
    return {"transition": transition, "prepare": prepare, "activate": activate, "resume": resume, "handoff": handoff, "closeout": closeout, "promote": promote, "recover": recover}[args.command](args.root, args)


if __name__ == "__main__":
    sys.exit(main())
