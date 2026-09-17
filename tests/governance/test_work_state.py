import contextlib, io, json, shutil, tempfile, unittest
from pathlib import Path
from unittest.mock import patch
import tools.governance.work_state as ws

ROOT = Path(__file__).parents[2]

class WorkStateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.d = Path(self.temp.name)
        (self.d / "docs/project/work-items").mkdir(parents=True)
        (self.d / "docs/project").mkdir(exist_ok=True)
        shutil.copy(ROOT / "docs/project/work-state.schema.json", self.d / "docs/project/work-state.schema.json")
        shutil.copytree(ROOT / "docs/work", self.d / "docs/work")
        self.items = [json.loads(p.read_text()) for p in (ROOT / "docs/project/work-items").glob("*.json")]
        self.normalize_lifecycle_fixture()
        self.write()
        self.assertEqual(ws.execution(self.d, False), 0)
    def tearDown(self): self.temp.cleanup()
    def write(self):
        for x in self.items:
            (self.d / "docs/project/work-items" / (x["id"] + ".json")).write_text(json.dumps(x))
    def find(self, i): return next(x for x in self.items if x["id"] == i)
    def normalize_lifecycle_fixture(self):
        for x in self.items: x["selectedForExecution"] = False
        for i in ("GH-12", "GH-13"):
            x=self.find(i); x["lifecycle"]="Blocked"; x["lifecycleLabel"]="blocked"; x["expectedGithubState"]="OPEN"
        issue13=self.find("GH-13")
        issue13["selectedForExecution"]=False
        issue13["checkpointId"]=None
        issue13["checkpointPath"]=None
        issue13["nextAction"]=None
        issue13["branch"]=None
        issue13["pr"]=None
        capsule=self.d/"docs/work/persistence/I12/checkpoint.md"
        lines=capsule.read_text().splitlines(keepends=True)
        lines[4]="`Blocked`\n"
        capsule.write_text("".join(lines))
    def reset_gws1_transition_fixture(self):
        gws1=self.find("GWS1"); gws1["lifecycle"]="Verifying"; gws1["lifecycleLabel"]="verifying"; gws1["selectedForExecution"]=True
        issue12=self.find("GH-12"); issue12["lifecycle"]="Active"; issue12["lifecycleLabel"]="active"; issue12["expectedGithubState"]="OPEN"; issue12["nextAction"]="continue governance transition test"; issue12["selectedForExecution"]=False
        capsule=self.d/"docs/work/governance/GWS1/checkpoint.md"
        lines=capsule.read_text().splitlines(keepends=True)
        lines[4]="`Verifying`\n"
        capsule.write_text("".join(lines))
        i12=self.d/"docs/work/persistence/I12/checkpoint.md"
        lines=i12.read_text().splitlines(keepends=True)
        lines[4]="`Building`\n"
        i12.write_text("".join(lines))
        self.write(); self.assertEqual(ws.execution(self.d, False),0)
    def test_migrated_registry_is_valid_and_zero_selection_is_idle(self):
        self.assertEqual(ws.validate(self.d), 0)
        for x in self.items: x["selectedForExecution"] = False
        self.write()
        self.assertEqual(ws.validate(self.d), 0)
        self.assertEqual(ws.execution_object(self.items)["mode"], "idle")
        self.assertIsNone(ws.execution_object(self.items)["activeCheckpointId"])
    def test_missing_dependency_and_cycle_are_rejected(self):
        self.find("GH-13")["dependencies"] = ["GH-999"]; self.write(); self.assertNotEqual(ws.validate(self.d), 0)
        self.find("GH-13")["dependencies"] = ["GH-12"]; self.find("GH-12")["dependencies"] = ["GH-13"]; self.write(); self.assertNotEqual(ws.validate(self.d), 0)
    def test_active_requires_frozen_contract_and_baseline(self):
        x=self.find("GH-12"); x["lifecycle"]="Active"; x["lifecycleLabel"]="active"; x["baseline"]="bad"; self.write(); self.assertNotEqual(ws.validate(self.d), 0)
    def test_closed_dependency_satisfies_active_child_but_active_does_not(self):
        self.assertEqual(ws.validate(self.d), 0)
        self.find("GH-9")["lifecycle"]="Active"; self.find("GH-9")["lifecycleLabel"]="active"; self.write(); self.assertNotEqual(ws.validate(self.d), 0)
    def test_parallel_active_items_reject_two_selections(self):
        self.find("GH-3")["selectedForExecution"] = True; self.write(); self.assertNotEqual(ws.validate(self.d), 0)
    def test_projection_is_deterministic_and_check_detects_stale_output(self):
        self.assertEqual(ws.execution(self.d, False), 0); first=(self.d/"docs/project/execution.json").read_text(); self.assertEqual(ws.execution(self.d, True), 0); self.assertEqual(first,(self.d/"docs/project/execution.json").read_text()); (self.d/"docs/project/execution.json").write_text("{}\n"); self.assertNotEqual(ws.execution(self.d, True), 0)
    def test_transition_rejects_illegal_state_change(self):
        self.assertNotEqual(ws.transition(self.d, type("A",(),{"id":"GH-13","state":"Closed","reason":None,"select":False,"check":True,"dry_run":False})()), 0)
        x=self.find("GH-16"); x["lifecycle"]="Draft"; x["lifecycleLabel"]=None; self.write()
        self.assertNotEqual(ws.transition(self.d, type("A",(),{"id":"GH-16","state":"Ready","reason":None,"select":True,"check":True,"dry_run":False})()), 0)
    def test_transition_check_is_non_mutating(self):
        self.reset_gws1_transition_fixture()
        before=(self.d/"docs/project/work-items/GWS1.json").read_text(); a=type("A",(),{"id":"GWS1","state":"ReviewRequired","reason":"returning to review after verification","select":True,"check":True,"dry_run":False})()
        self.assertEqual(ws.transition(self.d,a),0); self.assertEqual(before,(self.d/"docs/project/work-items/GWS1.json").read_text())
    def test_github_drift_parsing_is_read_only(self):
        payload=json.dumps([{"number":n,"state":next(x["expectedGithubState"] for x in self.items if x.get("number")==n),"labels":([{"name":ws.LABELS.get(self.find("GH-"+str(n))["lifecycle"])}] if self.find("GH-"+str(n))["lifecycle"] != "Closed" else [])} for n in [x["number"] for x in self.items if "number" in x]])
        with patch("subprocess.run", return_value=type("R",(),{"stdout":payload})()) as run: self.assertEqual(ws.gh(type("A",(),{"command":"check-github","repository":"x"})(),self.d),0); run.assert_called_once()
        for state in ("Draft", "Cancelled"):
            x=self.find("GH-12"); x["lifecycle"]=state; x["lifecycleLabel"]=None; self.write()
            remote=[]
            for y in self.items:
                if "number" in y:
                    label=ws.LABELS.get(y["lifecycle"])
                    remote.append({"number":y["number"],"state":y["expectedGithubState"],"labels":[{"name":label}] if label else []})
            with patch("subprocess.run", return_value=type("R",(),{"stdout":json.dumps(remote)})()): self.assertEqual(ws.gh(type("A",(),{"command":"check-github","repository":"x"})(),self.d),0)
        a=type("A",(),{"command":"check-github","repository":"x"})()
        for result in ([], "not-json", OSError("offline")):
            with self.subTest(result=result):
                with patch("subprocess.run", side_effect=result if isinstance(result, BaseException) else type("R",(),{"stdout":json.dumps(result) if not isinstance(result,str) else result})()): self.assertNotEqual(ws.gh(a,self.d),0)
    def test_sync_dry_run_forms_only_label_commands(self):
        a=type("A",(),{"command":"sync-github","repository":"x","dry_run":True})()
        remote=[{"number":x["number"],"state":x["expectedGithubState"],"labels":[{"name":"unrelated"}]} for x in self.items if "number" in x]
        with patch("subprocess.run", return_value=type("R",(),{"stdout":json.dumps(remote)})()) as run:
            with patch("builtins.print") as printed: self.assertEqual(ws.gh(a,self.d),0)
            self.assertEqual(run.call_count,1)
            output=" ".join(str(c) for c in printed.call_args_list)
            self.assertNotIn("unrelated", output)
        remote=[]
        for x in self.items:
            if "number" not in x: continue
            label=ws.LABELS.get(x["lifecycle"])
            labels=[{"name":"active"},{"name":"keep"}] if x["number"]==12 else ([] if x["number"]==13 else ([{"name":label}] if label else []))
            remote.append({"number":x["number"],"state":x["expectedGithubState"],"labels":labels})
        with patch("subprocess.run", return_value=type("R",(),{"stdout":json.dumps(remote)})()):
            with patch("builtins.print") as printed: self.assertEqual(ws.gh(a,self.d),0)
        text=" ".join(str(c) for c in printed.call_args_list)
        self.assertIn("--remove-label active --add-label blocked", text)
        self.assertNotIn("keep", text); self.assertNotIn("issue edit 14", text)

    def test_schema_rejects_version_extra_missing_wrong_type_and_duplicate_dependency(self):
        for mutation in (lambda x: x.update(schemaVersion=2), lambda x: x.update(extra=1), lambda x: x.pop("owner"), lambda x: x.update(dependencies="GH-9"), lambda x: x.update(dependencies=["GH-9","GH-9"])):
            with self.subTest(mutation=mutation):
                candidate=__import__("copy").deepcopy(self.items); x=next(i for i in candidate if i["id"]=="GH-12"); mutation(x); self.assertTrue(ws.validate_items(candidate,self.d))
        duplicate=__import__("copy").deepcopy(self.items); duplicate.append(__import__("copy").deepcopy(duplicate[0])); duplicate_id=duplicate[0]["id"]; errors=ws.validate_items(duplicate,self.d); self.assertEqual([e for e in errors if e == f"duplicate id {duplicate_id}"], [f"duplicate id {duplicate_id}"])

    def test_capsule_projection_and_execution_identity(self):
        x=self.find("GH-12"); self.assertEqual(x["checkpointId"],"I12"); self.assertEqual(ws.validate_items(self.items,self.d),[])
        self.find("GWS1")["selectedForExecution"]=False; x["selectedForExecution"]=True; self.assertEqual(ws.execution_object(self.items)["activeCheckpointId"],"I12")
        candidate=__import__("copy").deepcopy(self.items); x=next(i for i in candidate if i["id"]=="GWS1"); x["selectedForExecution"]=False; x["lifecycle"]="Ready"; x["lifecycleLabel"]="ready"; selected=next(i for i in candidate if i["id"]=="GH-12"); selected["selectedForExecution"]=True; selected["nextAction"]=None
        self.assertTrue(any("selected item incomplete" in e for e in ws.validate_items(candidate,self.d)))
        broken={"docs/work/governance/GWS1":"# no state\n"}
        self.assertTrue(any("ambiguous capsule state" in e for e in ws.validate_items(self.items,self.d,broken)))

    def test_transition_invalid_is_non_mutating(self):
        before={p:p.read_bytes() for p in (self.d/"docs/project/work-items").glob("*.json")}; a=type("A",(),{"id":"GH-12","state":"Closed","reason":None,"select":False,"check":False,"dry_run":False})()
        self.assertNotEqual(ws.transition(self.d,a),0); self.assertEqual(before,{p:p.read_bytes() for p in before})
        self.reset_gws1_transition_fixture()
        paths=list(before)+[self.d/"docs/work/governance/GWS1/checkpoint.md",self.d/"docs/project/execution.json"]
        before={p:p.read_bytes() for p in paths}
        for recipient_id in ("GH-999", "GH-13"):
            invalid=type("A",(),{"id":"GWS1","state":"Closed","reason":None,"select":False,"select_id":recipient_id,"check":False,"dry_run":False})()
            self.assertNotEqual(ws.transition(self.d,invalid),0)
            self.assertEqual(before,{p:p.read_bytes() for p in paths})
        valid=type("A",(),{"id":"GWS1","state":"Closed","reason":None,"select":False,"select_id":"GH-12","check":False,"dry_run":False})()
        self.assertEqual(ws.transition(self.d,valid),0)
        records={x["id"]:json.loads((self.d/"docs/project/work-items"/(x["id"]+".json")).read_text()) for x in self.items}
        self.assertEqual(records["GWS1"]["lifecycle"],"Closed"); self.assertFalse(records["GWS1"]["selectedForExecution"])
        self.assertEqual((self.d/"docs/work/governance/GWS1/checkpoint.md").read_text().splitlines()[4],"`Closed`")
        self.assertEqual(records["GH-12"]["lifecycle"],"Active"); self.assertTrue(records["GH-12"]["selectedForExecution"])
        execution=json.loads((self.d/"docs/project/execution.json").read_text())
        self.assertEqual((execution["activeCheckpointId"],execution["activeCheckpointPath"]),("I12","docs/work/persistence/I12"))
        item=json.loads((self.d/"docs/project/work-items/GH-12.json").read_text()); item["lifecycle"]="Draft"; item["lifecycleLabel"]=None; item["selectedForExecution"]=False; (self.d/"docs/project/work-items/GH-12.json").write_text(json.dumps(item)); a=type("A",(),{"id":"GH-12","state":"Cancelled","reason":"cancelled governance test","select":False,"dry_run":False,"check":False})(); self.assertEqual(ws.transition(self.d,a),0); cancelled=json.loads((self.d/"docs/project/work-items/GH-12.json").read_text()); self.assertEqual(cancelled["lifecycle"],"Cancelled"); self.assertEqual(ws.validate(self.d),0); self.assertEqual((self.d/"docs/work/persistence/I12/checkpoint.md").read_text().splitlines()[4],"`Cancelled`")

    def test_transition_selected_to_blocked_without_recipient_leaves_idle(self):
        self.reset_gws1_transition_fixture()
        a=type("A",(),{"id":"GWS1","state":"Blocked","reason":"blocked for governance test","select":False,"dry_run":False,"check":False})()
        self.assertEqual(ws.transition(self.d, a), 0)
        records=json.loads((self.d/"docs/project/work-items/GWS1.json").read_text())
        self.assertEqual(records["lifecycle"], "Blocked")
        self.assertFalse(records["selectedForExecution"])
        execution=json.loads((self.d/"docs/project/execution.json").read_text())
        self.assertEqual(execution["mode"], "idle")
        self.assertIsNone(execution["sourceId"])

    def test_replace_failure_rolls_back_all_payloads(self):
        self.reset_gws1_transition_fixture()
        a=type("A",(),{"id":"GWS1","state":"ReviewRequired","reason":"returning to review after verification","select":True,"check":False,"dry_run":False})(); paths=list((self.d/"docs/project/work-items").glob("*.json"))+[self.d/"docs/work/governance/GWS1/checkpoint.md",self.d/"docs/project/execution.json"]; before={p:p.read_bytes() for p in paths}; real=__import__("os").replace; count=[0]
        def fail(src,dst):
            count[0]+=1
            if count[0]==2: raise OSError("simulated")
            return real(src,dst)
        with patch("os.replace",side_effect=fail): self.assertNotEqual(ws.transition(self.d,a),0)
        self.assertGreaterEqual(count[0],2)
        self.assertEqual(before,{p:p.read_bytes() for p in before})

    # Issue 57 uses small, complete registries below rather than making the new
    # operation tests depend on whichever real item happens to be selected.
    def synthetic(self, *, second=False, dependency=False):
        root = Path(tempfile.mkdtemp(dir=self.d))
        (root / "docs/project/work-items").mkdir(parents=True)
        shutil.copy(ROOT / "docs/project/work-state.schema.json", root / "docs/project/work-state.schema.json")
        (root / "docs/work/checkpoints/A").mkdir(parents=True)
        (root / "docs/work/checkpoints/B").mkdir(parents=True)
        (root / "docs/project/execution.json").write_text(ws.execution_payload([]), encoding="utf-8")
        capsule = """# checkpoint\n\n## State\n\n`NotStarted`\n\n## Objective\n\nA complete synthetic objective.\n\n## Target paths\n\n- `tests/governance/test_work_state.py`\n\n## Non-goals\n\n- Product behavior.\n\n## Risks\n\n- Transactional write failure.\n\n## Existing coverage\n\n- Synthetic baseline.\n\n## Test budget\n\n- 5 claims.\n\n## Focused verification\n\n`python -B -m unittest tests.governance.test_work_state`\n\n## Final gate\n\n`python -B -m unittest tests.governance.test_work_state`\n\n## Review boundary\n\nA latest-head non-author peer reviews the candidate.\n\n## Acceptance proof\n\nThe synthetic operation is observable.\n"""
        for name in ("A", "B"):
            (root / f"docs/work/checkpoints/{name}/checkpoint.md").write_text(capsule, encoding="utf-8")
        def record(i, checkpoint, branch, deps=None, lifecycle="Ready"):
            return {"schemaVersion": 1, "id": i, "kind": "synthetic", "title": i,
                    "owner": "tester", "track": "governance", "lifecycle": lifecycle,
                    "dependencies": deps or [], "contractRevision": "contract-v1",
                    "baseline": "a" * 40, "checkpointId": checkpoint,
                    "checkpointPath": f"docs/work/checkpoints/{checkpoint}", "branch": branch,
                    "pr": None, "selectedForExecution": False, "nextAction": None,
                    "expectedGithubState": "NONE", "lifecycleLabel": ws.LABELS.get(lifecycle),
                    "statusReason": "synthetic test record"}
        records = [record("A", "A", "feature/a")]
        if second:
            records.append(record("B", "B", "feature/b", ["A"] if dependency else [], "Blocked" if dependency else "Ready"))
            if dependency:
                blocked = root / "docs/work/checkpoints/B/checkpoint.md"
                blocked.write_text(blocked.read_text(encoding="utf-8").replace("`NotStarted`", "`Blocked`"), encoding="utf-8")
        for item in records:
            (root / "docs/project/work-items" / f"{item['id']}.json").write_text(ws.dump(item), encoding="utf-8")
        return root

    def operation(self, root, command, **options):
        argv = ["work_state.py", command, "--root", str(root)]
        for key, value in options.items():
            flag = "--" + key.replace("_", "-")
            if isinstance(value, bool):
                if value: argv.append(flag)
            else:
                argv.extend([flag, str(value)])
        output = io.StringIO()
        with patch("sys.argv", argv), contextlib.redirect_stdout(output), contextlib.redirect_stderr(output):
            try:
                code = ws.main()
            except SystemExit as exc:
                code = exc.code
        return code, output.getvalue()

    def activate(self, root, item="A", execution_id="exec-a", **options):
        """Every successful activation carries the complete observed admission evidence."""
        ordinal = item[-1].lower()
        facts = {"id": item, "execution_id": execution_id, "expected_baseline": "a" * 40,
                 "current_head": "a" * 40, "current_branch": f"feature/{ordinal}", "clean": True,
                 "worktree_id": f"worktree-{ordinal}"}
        facts.update(options)
        return self.operation(root, "activate", **facts)

    def test_prepare_scaffold_rejects_unknown_or_incomplete_fields_then_reports_complete_capsule(self):
        root = self.synthetic()
        capsule = root / "docs/work/checkpoints/A/checkpoint.md"
        text = capsule.read_text(encoding="utf-8") + "\n## Unknown field\n\nsecret\n"
        capsule.write_text(text, encoding="utf-8")
        code, output = self.operation(root, "prepare")
        self.assertNotEqual(code, 0)
        self.assertRegex(output, r"(?i)(unknown|objective|target|non-goal|risk|focused|final|review|proof)")
        capsule.write_text(capsule.read_text(encoding="utf-8").replace("\n## Unknown field\n\nsecret\n", ""), encoding="utf-8")
        self.assertEqual(ws.validate(root), 0)
        code, output = self.operation(root, "prepare")
        self.assertEqual(code, 0, output)
        self.assertIn("A", output)

    def test_activate_dry_run_is_nonmutating_and_apply_records_identity_and_claims(self):
        root = self.synthetic()
        self.assertEqual(ws.validate(root), 0)
        before = {p: p.read_bytes() for p in root.rglob("*") if p.is_file()}
        code, packet = self.activate(root, dry_run=True, claim="src/a.py", resource="registry")
        self.assertEqual(code, 0, packet)
        self.assertEqual(before, {p: p.read_bytes() for p in before})
        self.assertNotRegex(packet, r"(?i)(timestamp|2026-|[A-Z]:\\|C:/checkout|password|token|session dump)")
        self.assertEqual(self.activate(root, claim="src/a.py", resource="registry")[0], 0)
        state = json.loads((root / "docs/project/work-items/A.json").read_text(encoding="utf-8"))
        self.assertEqual(state.get("lifecycle"), "Active")
        self.assertEqual(state.get("executionId"), "exec-a")
        self.assertEqual(state.get("worktreeId"), "worktree-a")
        self.assertTrue(state.get("claims"))

    def test_activate_rejects_ineligible_dependency_baseline_identity_capsule_and_overlap(self):
        cases = ("closed", "dependency", "baseline", "branch", "worktree", "empty-worktree", "dirty", "head", "capsule")
        root = self.synthetic(second=True, dependency=True)
        self.assertEqual(ws.validate(root), 0)
        for case in cases:
            with self.subTest(case=case):
                candidate = root / "docs/project/work-items/A.json"
                item = json.loads(candidate.read_text(encoding="utf-8"))
                if case == "closed": item["lifecycle"] = "Closed"
                elif case == "dependency": item["dependencies"] = ["missing"]
                elif case == "baseline": item["baseline"] = "b" * 40
                elif case == "branch": item["branch"] = "feature/other"
                elif case == "capsule": (root / "docs/work/checkpoints/A/checkpoint.md").write_text("# incomplete\n", encoding="utf-8")
                candidate.write_text(ws.dump(item), encoding="utf-8")
                code, _ = self.operation(root, "activate", id="A", baseline="a" * 40,
                                          branch="feature/other" if case == "branch" else "feature/a",
                                          expected_baseline="a" * 40,
                                          current_head="b" * 40 if case == "head" else "a" * 40,
                                          current_branch="feature/a", clean=case != "dirty",
                                          dirty=True if case == "dirty" else False,
                                          worktree_id=("/tmp/other" if case == "worktree" else
                                                       "" if case == "empty-worktree" else "worktree-a"),
                                          claim="src/a")
                self.assertNotEqual(code, 0)
                # Restore the complete synthetic input for the next partition.
                root = self.synthetic(second=True, dependency=True)
                self.assertEqual(ws.validate(root), 0)

    def test_parallel_disjoint_executions_survive_each_others_closeout(self):
        root = self.synthetic(second=True)
        self.assertEqual(ws.validate(root), 0)
        self.assertEqual(self.activate(root, execution_id="a", claim="src/a")[0], 0)
        self.assertEqual(self.activate(root, item="B", execution_id="b", claim="src/b")[0], 0)
        self.assertEqual(self.operation(root, "closeout", id="A", execution_id="a", focused_receipt="focused",
                                        final_receipt="final", attribution="tester", pr="https://github.com/o/r/pull/1",
                                        merge_sha="a" * 40)[0], 0)
        state = json.loads((root / "docs/project/work-items/B.json").read_text(encoding="utf-8"))
        self.assertEqual(state.get("executionId"), "b")

    def test_claim_normalization_rejects_windows_ancestor_fixture_and_tool_conflicts_but_not_disjoint(self):
        root = self.synthetic(second=True)
        self.assertEqual(ws.validate(root), 0)
        self.assertEqual(self.activate(root, execution_id="a", claim="src\\x/../a")[0], 0)
        for claim in ("SRC/a", "src"):
            code, _ = self.activate(root, item="B", execution_id="b", claim=claim)
            self.assertNotEqual(code, 0, claim)
        for option, value in (("fixture", "fixture-a"), ("governance_tool", "tools/governance/work_state.py"),
                              ("resource", "registry")):
            root = self.synthetic(second=True)
            self.assertEqual(ws.validate(root), 0)
            self.assertEqual(self.activate(root, execution_id="a", **{option: value})[0], 0)
            code, _ = self.activate(root, item="B", execution_id="b", **{option: value})
            self.assertNotEqual(code, 0, f"{option}:{value}")
        root = self.synthetic(second=True)
        self.assertEqual(ws.validate(root), 0)
        self.assertEqual(self.activate(root, execution_id="a", claim="src/a")[0], 0)
        self.assertEqual(self.activate(root, item="B", execution_id="b", claim="docs/b", fixture="fixture-b",
                                       governance_tool="tools/other.py", resource="other")[0], 0)

    def test_failed_transaction_rolls_back_and_recovery_does_not_replace_newer_state(self):
        root = self.synthetic()
        self.assertEqual(ws.validate(root), 0)
        before = {p: p.read_bytes() for p in root.rglob("*") if p.is_file()}
        with patch("os.replace", side_effect=OSError("disk full")):
            code, _ = self.activate(root, execution_id="a", claim="src/a")
        self.assertNotEqual(code, 0)
        self.assertEqual(before, {p: p.read_bytes() for p in before})
        self.assertEqual(ws.validate(root), 0)
        self.assertEqual(self.activate(root, execution_id="new", claim="src/a")[0], 0)
        (root / "docs/project/work-state.journal.json").write_text(json.dumps({"executionId": "old", "generation": 1,
                                                                                   "status": "interrupted"}), encoding="utf-8")
        self.assertNotEqual(self.operation(root, "recover", execution_id="old")[0], 0)
        self.assertEqual(json.loads((root / "docs/project/work-items/A.json").read_text(encoding="utf-8")).get("executionId"), "new")

    def test_packets_and_projection_are_order_and_checkout_independent(self):
        left, right = self.synthetic(), self.synthetic()
        self.assertEqual(ws.validate(left), 0)
        self.assertEqual(ws.validate(right), 0)
        self.assertEqual(self.activate(left, execution_id="e", claim="z", resource="r")[0], 0)
        self.assertEqual(self.activate(right, execution_id="e", claim="z", resource="r")[0], 0)
        self.assertEqual((left / "docs/project/execution.json").read_bytes(), (right / "docs/project/execution.json").read_bytes())

    def test_handoff_requires_latest_pr_sha_and_non_author_peer_and_records_epoch(self):
        root = self.synthetic()
        self.assertEqual(ws.validate(root), 0)
        self.assertEqual(self.activate(root, execution_id="e", claim="src/a")[0], 0)
        for options in ({"pr": "https://github.com/o/r/pull/1", "head": "c" * 40, "observed_head": "c" * 40,
                         "observed_author": "tester", "peer": "reviewer"},
                        {"pr": "https://github.com/o/r/pull/1", "head": "b" * 40, "observed_head": "c" * 40,
                         "observed_author": "reviewer", "peer": "reviewer"}):
            self.assertNotEqual(self.operation(root, "handoff", id="A", **options)[0], 0)
        self.assertEqual(self.operation(root, "handoff", id="A", pr="https://github.com/o/r/pull/1",
                                        head="c" * 40, observed_head="c" * 40, observed_author="author",
                                        peer="reviewer", epoch="1")[0], 0)

    def test_closeout_requires_receipts_identity_and_resolved_findings_and_promotes_only_ready_dependents(self):
        root = self.synthetic(second=True, dependency=True)
        self.assertEqual(ws.validate(root), 0)
        self.assertEqual(self.activate(root, execution_id="a", claim="src/a")[0], 0)
        for finding in ("open", "unresolved"):
            self.assertNotEqual(self.operation(root, "closeout", id="A", execution_id="a", findings=finding)[0], 0)
        self.assertEqual(self.operation(root, "closeout", id="A", execution_id="a", findings="resolved",
                                        focused_receipt="focused", final_receipt="final", attribution="tester",
                                        pr="https://github.com/o/r/pull/1", merge_sha="a" * 40)[0], 0)
        dependent = json.loads((root / "docs/project/work-items/B.json").read_text(encoding="utf-8"))
        self.assertNotEqual(dependent.get("lifecycle"), "Active")
        self.assertEqual(self.operation(root, "promote", id="B")[0], 0)
        dependent = json.loads((root / "docs/project/work-items/B.json").read_text(encoding="utf-8"))
        self.assertEqual(dependent.get("lifecycle"), "Ready")

    def test_github_projection_is_dry_run_idempotent_permission_aware_and_preserves_unrelated_labels(self):
        root = self.synthetic()
        item_path = root / "docs/project/work-items/A.json"
        item = json.loads(item_path.read_text(encoding="utf-8"))
        item.update(kind="github-issue", number=1, sourceUrl="https://github.com/o/r/issues/1",
                    expectedGithubState="OPEN")
        item_path.write_text(ws.dump(item), encoding="utf-8")
        self.assertEqual(ws.validate(root), 0)
        drift = json.dumps([{"number": 1, "state": "OPEN", "labels": [{"name": "blocked"}, {"name": "keep"}]}])
        with patch("subprocess.run", return_value=type("R", (), {"stdout": drift})()) as run:
            code, output = self.operation(root, "project", dry_run=True)
        self.assertEqual(code, 0, output)
        self.assertEqual(run.call_count, 1)
        self.assertNotIn("--remove-label keep", output)
        self.assertIn("--add-label ready", output)
        self.assertIn("DRY-RUN", output)
        remote = json.dumps([{"number": 1, "state": "OPEN", "labels": [{"name": "ready"}, {"name": "keep"}]}])
        with patch("subprocess.run", return_value=type("R", (), {"stdout": remote})()) as run:
            self.assertEqual(self.operation(root, "project")[0], 0)
            self.assertEqual(run.call_count, 1)
        with patch("subprocess.run", side_effect=PermissionError("forbidden")):
            self.assertNotEqual(self.operation(root, "project")[0], 0)
        drift = json.dumps([{"number": 1, "state": "OPEN", "labels": [{"name": "blocked"}]}])
        with patch("subprocess.run", side_effect=[type("R", (), {"stdout": drift})(), OSError("write failed")]):
            self.assertNotEqual(self.operation(root, "project")[0], 0)

    def test_real_registry_and_checked_in_projection_are_read_only(self):
        registry = {p: p.read_bytes() for p in (ROOT / "docs/project/work-items").glob("*.json")}
        projection = (ROOT / "docs/project/execution.json").read_bytes()
        self.assertEqual(ws.validate(ROOT), 0)
        self.assertEqual(ws.execution(ROOT, True), 0)
        self.assertEqual(registry, {p: p.read_bytes() for p in registry})
        self.assertEqual(projection, (ROOT / "docs/project/execution.json").read_bytes())


if __name__ == "__main__": unittest.main()
