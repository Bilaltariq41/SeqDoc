import base64, contextlib, io, json, shutil, subprocess, tempfile, unittest
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
        valid=type("A",(),{"id":"GWS1","state":"Closed","reason":"metadata receipt","select":False,"select_id":"GH-12","check":False,"dry_run":False,
                            "checkpoint_id":"GWS1","checkpoint_path":"docs/work/governance/GWS1","next_action":"closed next",
                            "pr":"https://github.com/o/r/pull/1","branch":"governance/gws1","baseline":"a"*40,
                            "contract_revision":"contract-v2"})()
        self.assertEqual(ws.transition(self.d,valid),0)
        records={x["id"]:json.loads((self.d/"docs/project/work-items"/(x["id"]+".json")).read_text()) for x in self.items}
        self.assertEqual(records["GWS1"]["lifecycle"],"Closed"); self.assertFalse(records["GWS1"]["selectedForExecution"])
        self.assertEqual({records["GWS1"][k] for k in ("statusReason", "checkpointId", "checkpointPath", "nextAction", "pr", "branch", "baseline", "contractRevision")},
                         {"metadata receipt", "GWS1", "docs/work/governance/GWS1", "closed next", "https://github.com/o/r/pull/1", "governance/gws1", "a"*40, "contract-v2"})
        self.assertEqual((self.d/"docs/work/governance/GWS1/checkpoint.md").read_text().splitlines()[4],"`Closed`")
        self.assertEqual(records["GH-12"]["lifecycle"],"Active"); self.assertTrue(records["GH-12"]["selectedForExecution"])
        execution=json.loads((self.d/"docs/project/execution.json").read_text())
        self.assertEqual((execution["activeCheckpointId"],execution["activeCheckpointPath"]),("I12","docs/work/persistence/I12"))
        item=json.loads((self.d/"docs/project/work-items/GH-12.json").read_text()); item["lifecycle"]="Draft"; item["lifecycleLabel"]=None; item["selectedForExecution"]=False; (self.d/"docs/project/work-items/GH-12.json").write_text(json.dumps(item)); a=type("A",(),{"id":"GH-12","state":"Cancelled","reason":"cancelled governance test","select":False,"dry_run":False,"check":False})(); self.assertEqual(ws.transition(self.d,a),0); cancelled=json.loads((self.d/"docs/project/work-items/GH-12.json").read_text()); self.assertEqual(cancelled["lifecycle"],"Cancelled"); self.assertEqual(ws.validate(self.d),0); self.assertEqual((self.d/"docs/work/persistence/I12/checkpoint.md").read_text().splitlines()[4],"`Cancelled`")

    def test_transition_selected_to_blocked_without_recipient_leaves_idle(self):
        root = self.synthetic(second=True)
        self.assertEqual(self.activate(root, execution_id="a", claim="src/a")[0], 0)
        self.assertEqual(self.activate(root, item="B", execution_id="b", claim="src/b")[0], 0)
        b = root / "docs/project/work-items/B.json"
        active_b = json.loads(b.read_text(encoding="utf-8"))
        active_b.update(reviewEpoch="1", reviewPeer="peer", reviewFindings=["Fixed: verification"],
                        review={"executionId":"b", "pr":"https://github.com/o/r/pull/2", "requestHead":"a"*40,
                                "author":"author", "peer":"peer", "epoch":"1", "findings":["Fixed: verification"]})
        b.write_text(ws.dump(active_b), encoding="utf-8")
        for state in ("Blocked", "Cancelled"):
            action = type("A",(),{"id":"B","state":state,"reason":"blocked/cancelled governance test","select":False,
                                   "dry_run":False,"check":False})()
            self.assertEqual(ws.transition(root, action), 0)
            selected = json.loads(b.read_text(encoding="utf-8"))
            self.assertEqual(selected["lifecycle"], state)
            self.assertFalse(selected["selectedForExecution"])
            for field in ("executionId", "worktreeId", "claims", "review", "reviewEpoch", "reviewPeer", "reviewFindings"):
                self.assertNotIn(field, selected, field)
            survivor = json.loads((root / "docs/project/work-items/A.json").read_text(encoding="utf-8"))
            self.assertEqual((survivor["executionId"], survivor["worktreeId"]), ("a", "worktree-a"))
            self.assertEqual(ws.validate(root), 0)
            if state == "Blocked":
                b_value = json.loads(b.read_text(encoding="utf-8")); b_value["lifecycle"] = "Blocked"; b_value["lifecycleLabel"] = "blocked"
                b.write_text(ws.dump(b_value), encoding="utf-8")
        execution = json.loads((root / "docs/project/execution.json").read_text(encoding="utf-8"))
        self.assertIn("a", {entry["executionId"] for entry in execution["executions"]})

        def blocked_root():
            candidate = self.synthetic(second=True)
            item_path = candidate / "docs/project/work-items/A.json"
            item = json.loads(item_path.read_text(encoding="utf-8"))
            item["lifecycle"], item["lifecycleLabel"] = "Blocked", "blocked"
            item["statusReason"] = "pending authorization"
            item_path.write_text(ws.dump(item), encoding="utf-8")
            capsule = candidate / "docs/work/checkpoints/A/checkpoint.md"
            capsule.write_text(capsule.read_text(encoding="utf-8").replace("`NotStarted`", "`Blocked`"), encoding="utf-8")
            self.assertEqual(ws.validate(candidate), 0)
            return candidate, item

        start_head = "1" * 40
        candidate, original = blocked_root()
        with patch("tools.governance.work_state.observe_git", return_value=(start_head, "feature/a", True)) as observed:
            code, output = self.operation(candidate, "resume", id="A", execution_id="takeover-1", worktree_id="resume-a",
                                          expected_baseline=original["baseline"], current_head=start_head,
                                          current_branch="feature/a", clean=True, start_head=start_head,
                                          claim=["SRC\\Repair/../repair.py"], authorization_receipt="owner-approved-epoch-1",
                                          reason="resume after bounded takeover", next_action="rerun focused verification")
        self.assertEqual(code, 0, output)
        observed.assert_called_once()
        resumed = json.loads((candidate / "docs/project/work-items/A.json").read_text(encoding="utf-8"))
        for field in ("owner", "branch", "checkpointId", "checkpointPath", "pr", "baseline"):
            self.assertEqual(resumed[field], original[field], field)
        self.assertEqual((resumed["lifecycle"], resumed["executionId"], resumed["worktreeId"]),
                         ("ResolvingFindings", "takeover-1", "resume-a"))
        self.assertEqual(resumed["statusReason"], "resume after bounded takeover")
        self.assertEqual(resumed["nextAction"], "rerun focused verification")
        self.assertNotIn("pending authorization", json.dumps(resumed).lower())
        self.assertTrue(resumed["selectedForExecution"])
        self.assertEqual(resumed["claims"], [{"kind":"path", "value":"src/repair.py"}])
        self.assertEqual(resumed["takeover"], {"authorizationReceipt":"owner-approved-epoch-1",
                                               "reason":"resume after bounded takeover", "startHead":start_head})
        self.assertIn("`ResolvingFindings`", (candidate / "docs/work/checkpoints/A/checkpoint.md").read_text(encoding="utf-8"))
        self.assertEqual(ws.validate(candidate), 0)
        before = {p: p.read_bytes() for p in candidate.rglob("*") if p.is_file()}
        with patch("tools.governance.work_state.observe_git", return_value=(start_head, "feature/a", True)):
            self.assertNotEqual(self.operation(candidate, "resume", id="A", execution_id="takeover-2", worktree_id="resume-b",
                                                expected_baseline=original["baseline"], current_head=start_head,
                                                current_branch="feature/a", clean=True, start_head=start_head,
                                                claim="other.py", authorization_receipt="second", reason="second",
                                                next_action="second")[0], 0)
        self.assertEqual(before, {p: p.read_bytes() for p in before})
        for options in ({"clean":False}, {"current_head":"2"*40}, {"current_head":"bad"},
                        {"current_branch":"feature/other"}, {"authorization_receipt":""}, {"reason":""},
                        {"next_action":""}, {"current_head":None}, {"start_head":None}, {"current_branch":None},
                        {"start_head":"1"*39}, {"current_head":"1"*39}, {"start_head":"A"*40}, {"current_head":"A"*40}):
            candidate, original = blocked_root()
            values = {"id":"A", "execution_id":"takeover-1", "worktree_id":"resume-a", "expected_baseline":original["baseline"],
                      "current_head":start_head, "current_branch":"feature/a", "clean":True, "start_head":start_head,
                      "claim":"repair.py", "authorization_receipt":"owner-approved", "reason":"bounded resume",
                      "next_action":"rerun focused verification"}
            values.update(options)
            before = {p: p.read_bytes() for p in candidate.rglob("*") if p.is_file()}
            with patch("tools.governance.work_state.observe_git", return_value=(start_head, "feature/a", True)):
                self.assertNotEqual(self.operation(candidate, "resume", **values)[0], 0, options)
            self.assertEqual(before, {p: p.read_bytes() for p in before})
        for remove_directory in (False, True):
            candidate, original = blocked_root()
            capsule = candidate / "docs/work/checkpoints/A/checkpoint.md"
            capsule.unlink()
            if remove_directory:
                shutil.rmtree(capsule.parent)
            before = {p: p.read_bytes() for p in candidate.rglob("*") if p.is_file()}
            values = {"id":"A", "execution_id":"takeover-1", "worktree_id":"resume-a", "expected_baseline":original["baseline"],
                      "current_head":start_head, "current_branch":"feature/a", "clean":True, "start_head":start_head,
                      "claim":"repair.py", "authorization_receipt":"owner-approved", "reason":"bounded resume",
                      "next_action":"rerun focused verification"}
            with patch("tools.governance.work_state.observe_git", return_value=(start_head, "feature/a", True)):
                self.assertNotEqual(self.operation(candidate, "resume", **values)[0], 0)
            self.assertEqual(before, {p: p.read_bytes() for p in before})
            self.assertFalse(capsule.exists())
        candidate, original = blocked_root()
        before = {p: p.read_bytes() for p in candidate.rglob("*") if p.is_file()}
        replace = __import__("os").replace
        replacements = [0]
        def fail_after_one(source, destination):
            replacements[0] += 1
            if replacements[0] == 2:
                raise OSError("resume rollback test")
            return replace(source, destination)
        with patch("tools.governance.work_state.observe_git", return_value=(start_head, "feature/a", True)):
            with patch("os.replace", side_effect=fail_after_one):
                code, _ = self.operation(candidate, "resume", id="A", execution_id="takeover-1", worktree_id="resume-a",
                                          expected_baseline=original["baseline"], current_head=start_head,
                                          current_branch="feature/a", clean=True, start_head=start_head,
                                          claim="repair.py", authorization_receipt="owner-approved",
                                          reason="bounded resume", next_action="rerun focused verification")
        self.assertNotEqual(code, 0)
        self.assertGreaterEqual(replacements[0], 2)
        self.assertEqual(before, {p: p.read_bytes() for p in before})
        self.assertFalse(ws.runtime_journal(candidate).exists())
        self.assertFalse(list(candidate.rglob(".work-state-*")))
        candidate, original = blocked_root()
        self.assertEqual(self.activate(candidate, item="B", execution_id="other", claim="occupied.py")[0], 0)
        values = {"id":"A", "execution_id":"takeover-1", "worktree_id":"resume-a", "expected_baseline":original["baseline"],
                  "current_head":start_head, "current_branch":"feature/a", "clean":True, "start_head":start_head,
                  "claim":"occupied.py", "authorization_receipt":"owner-approved", "reason":"bounded resume",
                  "next_action":"rerun focused verification"}
        with patch("tools.governance.work_state.observe_git", return_value=(start_head, "feature/a", True)):
            self.assertNotEqual(self.operation(candidate, "resume", **values)[0], 0)
        candidate, original = blocked_root()
        values["claim"] = ["repair.py", "repair.py"]
        before = {p: p.read_bytes() for p in candidate.rglob("*") if p.is_file()}
        with patch("tools.governance.work_state.observe_git", return_value=(start_head, "feature/a", True)):
            self.assertNotEqual(self.operation(candidate, "resume", **values)[0], 0)
        self.assertEqual(before, {p: p.read_bytes() for p in before})

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
        options.setdefault("repository", "o/r")
        for key, value in options.items():
            flag = "--" + key.replace("_", "-")
            if isinstance(value, bool):
                if value: argv.append(flag)
            elif isinstance(value, (list, tuple)):
                for entry in value: argv.extend([flag, str(entry)])
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
        capsule.write_text(capsule.read_text(encoding="utf-8") + "\n## Authority\n\nSynthetic authority.\n\n## Stop conditions\n\nStop on conflict.\n", encoding="utf-8")
        self.assertEqual(ws.validate(root), 0)
        code, output = self.operation(root, "prepare")
        self.assertEqual(code, 0, output)
        self.assertIn("A", output)
        capsule.unlink()
        code, output = self.operation(root, "prepare", id="A", scaffold=True)
        self.assertEqual(code, 0, output)
        self.assertTrue(capsule.exists())
        self.assertRegex(capsule.read_text(encoding="utf-8"), r"(?i)(todo|placeholder|blocked)")
        self.assertNotEqual(ws.validate(root), 0)
        canonical = self.synthetic()
        record_path = canonical / "docs/project/work-items/A.json"
        record = json.loads(record_path.read_text(encoding="utf-8"))
        record["checkpointId"], record["checkpointPath"] = None, None
        record_path.write_text(ws.dump(record), encoding="utf-8")
        old_capsule = canonical / "docs/work/checkpoints/A/checkpoint.md"
        old_capsule.unlink()
        before = {p: p.read_bytes() for p in canonical.rglob("*") if p.is_file()}
        code, output = self.operation(canonical, "prepare", id="A", scaffold=True)
        self.assertEqual(code, 0, output)
        prepared = json.loads(record_path.read_text(encoding="utf-8"))
        self.assertTrue(prepared["checkpointId"])
        self.assertTrue(prepared["checkpointPath"])
        self.assertFalse(Path(prepared["checkpointPath"]).is_absolute())
        self.assertTrue((canonical / prepared["checkpointPath"] / "checkpoint.md").exists())
        self.assertFalse((canonical / "checkpoint.md").exists())
        self.assertNotEqual(ws.validate(canonical), 0)
        failed = self.synthetic()
        failed_record = failed / "docs/project/work-items/A.json"
        value = json.loads(failed_record.read_text(encoding="utf-8"))
        value["checkpointId"], value["checkpointPath"] = None, None
        failed_record.write_text(ws.dump(value), encoding="utf-8")
        failed_before = {p: p.read_bytes() for p in failed.rglob("*") if p.is_file()}
        with patch("os.replace", side_effect=OSError("scaffold write failed")):
            code, _ = self.operation(failed, "prepare", id="A", scaffold=True)
        self.assertNotEqual(code, 0)
        self.assertEqual(failed_before, {p: p.read_bytes() for p in failed_before})
        self.assertFalse((failed / "checkpoint.md").exists())
        supplied = self.synthetic()
        supplied_record = supplied / "docs/project/work-items/A.json"
        supplied_value = json.loads(supplied_record.read_text(encoding="utf-8"))
        supplied_value["checkpointId"], supplied_value["checkpointPath"] = None, None
        supplied_record.write_text(ws.dump(supplied_value), encoding="utf-8")
        code, output = self.operation(supplied, "prepare", id="A", scaffold=True,
                                      checkpoint_id="custom-a", checkpoint_path="docs/work/custom-a")
        self.assertEqual(code, 0, output)
        stored = json.loads(supplied_record.read_text(encoding="utf-8"))
        self.assertEqual((stored["checkpointId"], stored["checkpointPath"]), ("custom-a", "docs/work/custom-a"))
        self.assertTrue((supplied / "docs/work/custom-a/checkpoint.md").exists())
        for checkpoint_id, checkpoint_path in (("custom-a", "/outside"), ("custom-a", "../outside"),
                                                ("custom-a", "docs/work"), ("bad/id", "docs/work/bad-id"),
                                                ("other", "docs/work/custom-a")):
            candidate = self.synthetic()
            record_path = candidate / "docs/project/work-items/A.json"
            record = json.loads(record_path.read_text(encoding="utf-8"))
            record["checkpointId"], record["checkpointPath"] = None, None
            record_path.write_text(ws.dump(record), encoding="utf-8")
            before = {p: p.read_bytes() for p in candidate.rglob("*") if p.is_file()}
            code, output = self.operation(candidate, "prepare", id="A", scaffold=True,
                                          checkpoint_id=checkpoint_id, checkpoint_path=checkpoint_path)
            self.assertNotEqual(code, 0, (checkpoint_id, checkpoint_path, output))
            self.assertEqual(before, {p: p.read_bytes() for p in before})

    def test_activate_dry_run_is_nonmutating_and_apply_records_identity_and_claims(self):
        root = self.synthetic()
        self.assertEqual(ws.validate(root), 0)
        before = {p: p.read_bytes() for p in root.rglob("*") if p.is_file()}
        code, packet = self.activate(root, dry_run=True, claim=["src/a.py", "src/b.py"], fixture="Fixture-A",
                                     governance_tool="tools/governance/work_state.py", resource="REGISTRY")
        self.assertEqual(code, 0, packet)
        self.assertEqual(before, {p: p.read_bytes() for p in before})
        self.assertNotRegex(packet, r"(?i)(timestamp|2026-|[A-Z]:\\|C:/checkout|password|token|session dump)")
        self.assertEqual(self.activate(root, claim=["src/a.py", "src/b.py"], fixture="Fixture-A",
                                       governance_tool="tools/governance/work_state.py", resource="REGISTRY")[0], 0)
        state = json.loads((root / "docs/project/work-items/A.json").read_text(encoding="utf-8"))
        self.assertEqual(state.get("lifecycle"), "Active")
        self.assertEqual(state.get("executionId"), "exec-a")
        self.assertEqual(state.get("worktreeId"), "worktree-a")
        self.assertEqual(sorted(state["claims"], key=lambda x: (x["kind"], x["value"])), [
            {"kind": "exclusive", "value": "registry"}, {"kind": "fixture", "value": "fixture-a"},
            {"kind": "governance-tool", "value": "tools/governance/work_state.py"},
            {"kind": "path", "value": "src/a.py"}, {"kind": "path", "value": "src/b.py"}])
        self.assertEqual(ws.validate(root), 0)
        self.assertIn("`Building`", (root / "docs/work/checkpoints/A/checkpoint.md").read_text(encoding="utf-8"))
        projection = json.loads((root / "docs/project/execution.json").read_text(encoding="utf-8"))
        self.assertIn("exec-a", {x["executionId"] for x in projection["executions"]})
        self.assertFalse((root / "docs/project/work-state.journal.json").exists())
        self.assertFalse(list(root.rglob(".work-state-*")))

    def test_activate_rejects_ineligible_dependency_baseline_identity_capsule_and_overlap(self):
        cases = (("closed", "eligible"), ("dependency", "dependency"), ("baseline", "baseline identity"),
                 ("branch", "branch identity"), ("worktree", "worktree identity"), ("empty-worktree", "worktree identity"),
                 ("dirty", "dirty"), ("head", "stale"), ("capsule", "capsule"))
        root = self.synthetic(second=True, dependency=True)
        self.assertEqual(ws.validate(root), 0)
        for case, expected in cases:
            with self.subTest(case=case):
                candidate = root / "docs/project/work-items/A.json"
                item = json.loads(candidate.read_text(encoding="utf-8"))
                if case == "closed": item["lifecycle"] = "Closed"
                elif case == "dependency": item["dependencies"] = ["missing"]
                elif case == "baseline": item["baseline"] = "b" * 40
                elif case == "branch": item["branch"] = "feature/other"
                elif case == "capsule": (root / "docs/work/checkpoints/A/checkpoint.md").write_text("# incomplete\n", encoding="utf-8")
                candidate.write_text(ws.dump(item), encoding="utf-8")
                code, output = self.operation(root, "activate", id="A", execution_id="valid-execution",
                                              baseline="a" * 40,
                                              branch="feature/other" if case == "branch" else "feature/a",
                                              expected_baseline="a" * 40,
                                              current_head="b" * 40 if case == "head" else "a" * 40,
                                              current_branch="feature/a", clean=case != "dirty",
                                              dirty=True if case == "dirty" else False,
                                              worktree_id=("/tmp/other" if case == "worktree" else
                                                           "" if case == "empty-worktree" else "worktree-a"),
                                              claim=["src/a", "src/b"], fixture="fixture-a",
                                              governance_tool="tools/governance/work_state.py", resource="registry")
                self.assertNotEqual(code, 0)
                self.assertIn(expected, output.lower(), output)
                # Restore the complete synthetic input for the next partition.
                root = self.synthetic(second=True, dependency=True)
                self.assertEqual(ws.validate(root), 0)
        checkout = self.synthetic()
        for command in (("git", "init", "-q"), ("git", "config", "user.email", "test@example.invalid"),
                        ("git", "config", "user.name", "SeqDoc Test"), ("git", "add", "."),
                        ("git", "commit", "-qm", "baseline"), ("git", "branch", "-M", "feature/a")):
            subprocess.run(command, cwd=checkout, check=True, capture_output=True, text=True)
        code, output = self.operation(checkout, "activate", id="A", execution_id="git-observer-test",
                                      expected_baseline="a" * 40, current_head="a" * 40,
                                      current_branch="feature/a", clean=True, worktree_id="worktree-a",
                                      claim="src/a")
        self.assertNotEqual(code, 0)
        self.assertRegex(output.lower(), r"(?i)(head|baseline|observed|repository)")

    def test_parallel_disjoint_executions_survive_each_others_closeout(self):
        root = self.synthetic(second=True)
        self.assertEqual(ws.validate(root), 0)
        self.assertEqual(self.activate(root, execution_id="a", claim="src/a")[0], 0)
        self.assertEqual(self.activate(root, item="B", execution_id="b", claim="src/b")[0], 0)
        observed = {"number": 1, "state": "OPEN", "isDraft": False, "author": {"login": "external-author"},
                    "headRefOid": "a" * 40, "mergeCommit": None, "reviewDecision": "APPROVED"}
        merged = dict(observed, state="MERGED", mergeCommit={"oid": "a" * 40})
        with patch("tools.governance.work_state.authenticated_pr", side_effect=[observed, merged]), patch("tools.governance.work_state.authenticated_reviews"):
            self.assertEqual(self.operation(root, "handoff", id="A", execution_id="a", pr="https://github.com/o/r/pull/1",
                                            head="a" * 40, observed_head="a" * 40, observed_author="external-author",
                                            peer="reviewer", epoch="1", finding=["Fixed: focused verification"])[0], 0)
            self.assertEqual(self.operation(root, "closeout", id="A", execution_id="a", select_id="B", focused_receipt="focused",
                                            final_receipt="final", attribution="tester", pr="https://github.com/o/r/pull/1",
                                            head="a" * 40, observed_head="a" * 40, peer="reviewer",
                                            merge_sha="a" * 40)[0], 0)
        state = json.loads((root / "docs/project/work-items/B.json").read_text(encoding="utf-8"))
        self.assertEqual(state.get("executionId"), "b")
        self.assertTrue(state.get("selectedForExecution"))

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
        recovery_code, recovery_output = self.operation(root, "recover", execution_id="old")
        self.assertNotEqual(recovery_code, 0)
        self.assertIn("newer state", recovery_output.lower())
        self.assertEqual(json.loads((root / "docs/project/work-items/A.json").read_text(encoding="utf-8")).get("executionId"), "new")
        outside = root.parent / "recovery-outside-sentinel.txt"
        outside.write_text("must remain", encoding="utf-8")
        generation = ws.hashlib.sha256(ws.execution_payload(ws.load(root)).encode()).hexdigest()[:16]
        for paths, extra in (([str(outside)], {}), (["../recovery-outside-sentinel.txt"], {}),
                             (["docs/project/work-items/A.json"], {"preimages": {"docs/project/work-items/A.json": "%%%"}}),
                             (["docs/project/work-items/A.json"], {"generation": "not-a-generation"})):
            journal = {"executionId": "new", "generation": generation, "status": "interrupted", "paths": paths}
            journal.update(extra)
            (root / "docs/project/work-state.journal.json").write_text(json.dumps(journal), encoding="utf-8")
            code, output = self.operation(root, "recover", execution_id="new")
            self.assertNotEqual(code, 0)
            self.assertIn("refused", output.lower())
            self.assertEqual(outside.read_text(encoding="utf-8"), "must remain")
        absent = root / "docs/project/recovery-absent.json"
        empty = root / "docs/project/recovery-empty.json"
        absent.unlink(missing_ok=True)
        empty.write_bytes(b"")
        (root / "docs/project/work-state.journal.json").write_text(json.dumps({
            "executionId": "new", "generation": generation, "status": "interrupted",
            "entries": [{"path": "docs/project/recovery-absent.json", "original": None},
                        {"path": "docs/project/recovery-empty.json", "original": ""}]}), encoding="utf-8")
        code, output = self.operation(root, "recover", execution_id="new")
        self.assertEqual(code, 0, output)
        self.assertFalse(absent.exists())
        self.assertEqual(empty.read_bytes(), b"")
        for parent_link in (False, True):
            link_root = self.synthetic()
            outside_dir = Path(tempfile.mkdtemp(dir=link_root.parent))
            outside_file = outside_dir / "sentinel.txt"
            outside_file.write_text("outside", encoding="utf-8")
            try:
                if parent_link:
                    link = link_root / "docs/project/linked"
                    link.symlink_to(outside_dir, target_is_directory=True)
                    target = link / "sentinel.txt"
                else:
                    target = link_root / "docs/project/work-items/linked.txt"
                    target.symlink_to(outside_file)
                payload = target.read_bytes()
                journal = {"executionId":"safe", "entries":[{"path":str(target.relative_to(link_root)).replace("\\","/"),
                    "originalExists":True, "originalHash":ws.file_hash(payload), "targetHash":ws.file_hash(b"replacement"),
                    "original":base64.b64encode(payload).decode(), "target":base64.b64encode(b"replacement").decode()}]}
                (link_root / "docs/project/work-state.journal.json").write_text(json.dumps(journal), encoding="utf-8")
                code, output = self.operation(link_root, "recover", execution_id="safe")
                self.assertNotEqual(code, 0, output)
                self.assertEqual(outside_file.read_text(encoding="utf-8"), "outside")
            except OSError as error:
                self.skipTest("symlink fixture unavailable: " + str(error))

    def test_packets_and_projection_are_order_and_checkout_independent(self):
        left, right = self.synthetic(), self.synthetic()
        self.assertEqual(ws.validate(left), 0)
        self.assertEqual(ws.validate(right), 0)
        left_code, left_packet = self.activate(left, execution_id="e", claim="z", resource="r")
        right_code, right_packet = self.activate(right, execution_id="e", claim="z", resource="r")
        self.assertEqual(left_code, 0); self.assertEqual(right_code, 0)
        self.assertEqual(left_packet, right_packet)
        self.assertEqual((left / "docs/project/execution.json").read_bytes(), (right / "docs/project/execution.json").read_bytes())
        for root in (left, right):
            self.assertFalse(list(root.rglob(".work-state-*")))
            self.assertFalse((root / "docs/project/work-state.journal.json").exists())
        failed_root = self.synthetic(); before = {p: p.read_bytes() for p in failed_root.rglob("*") if p.is_file()}
        real_replace = __import__("os").replace; replacements = [0]
        def fail_after_one(src, dst):
            replacements[0] += 1
            if replacements[0] == 2: raise OSError("after replacement")
            return real_replace(src, dst)
        with patch("os.replace", side_effect=fail_after_one):
            self.assertNotEqual(self.activate(failed_root, claim="z")[0], 0)
        self.assertGreaterEqual(replacements[0], 2)
        self.assertEqual(before, {p: p.read_bytes() for p in before})

    def test_handoff_requires_latest_pr_sha_and_non_author_peer_and_records_epoch(self):
        root = self.synthetic()
        self.assertEqual(ws.validate(root), 0)
        self.assertEqual(self.activate(root, execution_id="e", claim="src/a")[0], 0)
        def pr_view(**changes):
            value = {"number":1, "url":"https://github.com/o/r/pull/1", "state":"OPEN", "isDraft":False,
                     "author":{"login":"actual-author", "id":"U_kgDODXRwzA", "is_bot":False,
                               "name":"Actual Author", "url":"https://github.com/actual-author"},
                     "headRefOid":"c"*40, "mergeCommit":None, "reviewDecision":None}
            value.update(changes)
            return type("R",(),{"stdout":json.dumps(value)})()
        base = {"id":"A", "execution_id":"e", "head":"c"*40, "observed_head":"spoofed",
                "observed_author":"spoofed", "peer":"reviewer", "epoch":"1", "finding":["Fixed: receipt"]}
        for options, response in (({"pr":"https://github.com/o/r/pull/2"}, pr_view()),
                                  ({"pr":"https://github.com/o/r/pull/1", "head":"b"*40}, pr_view()),
                                  ({"pr":"https://github.com/o/r/pull/1"}, pr_view(isDraft=True)),
                                  ({"pr":"https://github.com/o/r/pull/1"}, pr_view(state="CLOSED")),
                                  ({"pr":"https://github.com/o/r/pull/1", "peer":"actual-author"}, pr_view()),
                                  ({"pr":"https://github.com/o/r/pull/1", "head":"c"*39}, pr_view()),
                                   ({"pr":"https://github.com/o/r/pull/1"}, pr_view(author={"login":"actual-author", "id":12345, "is_bot":False, "name":"Actual Author"})),
                                   ({"pr":"https://github.com/o/r/pull/1"}, pr_view(author={"login":7, "id":"U_kgDODXRwzA", "is_bot":False, "name":"Actual Author"})),
                                   ({"pr":"https://github.com/o/r/pull/1"}, pr_view(author={"login":"actual-author", "id":"U_kgDODXRwzA", "is_bot":True, "name":"Actual Author"})),
                                   ({"pr":"https://github.com/o/r/pull/1"}, pr_view(author={"login":"actual-author", "is_bot":False, "name":"Actual Author"})),
                                   ({"pr":"https://github.com/o/r/pull/1"}, pr_view(author={"login":"actual-author", "id":"", "is_bot":False, "name":"Actual Author"})),
                                   ({"pr":"https://github.com/o/r/pull/1"}, pr_view(author={"login":"actual-author", "id":" ", "is_bot":False, "name":"Actual Author"})),
                                   ({"pr":"https://github.com/o/r/pull/1"}, pr_view(author={"login":"actual-author", "id":"not-a-node", "is_bot":False, "name":"Actual Author"})),
                                   ({"pr":"https://github.com/o/r/pull/1"}, pr_view(author={"login":"actual-author", "id":"U_kgDODXRwzA"*20, "is_bot":False, "name":"Actual Author"})),
                                   ({"pr":"https://github.com/o/r/pull/1"}, pr_view(author={"login":"actual-author", "id":True, "is_bot":False, "name":"Actual Author"})),
                                   ({"pr":"https://github.com/o/r/pull/1"}, pr_view(author={"login":"actual-author", "id":"U_kgDODXRwzA", "is_bot":"false", "name":"Actual Author"}))):
            with patch("subprocess.run", return_value=response):
                self.assertNotEqual(self.operation(root, "handoff", **dict(base, **options))[0], 0, options)
        with patch("subprocess.run", return_value=pr_view()) as run:
            handoff_code, _ = self.operation(root, "handoff", **dict(base, pr="https://github.com/o/r/pull/1"))
        self.assertEqual(handoff_code, 0)
        self.assertTrue(run.called)
        self.assertEqual(ws.validate(root), 0)
        self.assertIn("`ReviewRequired`", (root / "docs/work/checkpoints/A/checkpoint.md").read_text(encoding="utf-8"))
        reviewed = json.loads((root / "docs/project/work-items/A.json").read_text(encoding="utf-8"))
        self.assertEqual(reviewed["reviewFindings"], ["Fixed: receipt"])
        self.assertEqual(reviewed["reviewPeer"], "reviewer")
        self.assertEqual(reviewed["pr"], "https://github.com/o/r/pull/1")
        self.assertEqual(reviewed["review"]["author"], "actual-author")
        nullable_root = self.synthetic()
        self.assertEqual(self.activate(nullable_root, execution_id="nullable", claim="src/a")[0], 0)
        with patch("subprocess.run", return_value=pr_view(author={"login":"actual-author", "id":"U_kgDODXRwzA",
                                                                    "is_bot":False, "name":None,
                                                                    "url":"https://github.com/actual-author"})):
            self.assertEqual(self.operation(nullable_root, "handoff", id="A", execution_id="nullable",
                                            pr="https://github.com/o/r/pull/1", head="c"*40,
                                            observed_head="spoofed", observed_author="spoofed", peer="reviewer",
                                            epoch="1", finding=["Fixed: nullable name"])[0], 0)
        self.assertEqual(reviewed["review"]["requestHead"], "c" * 40)
        resolving = type("A",(),{"id":"A","state":"ResolvingFindings","reason":"repair in progress",
                                  "select":False,"dry_run":False,"check":False})()
        self.assertEqual(ws.transition(root, resolving), 0)
        repaired_findings = ["Fixed: final verification", "Deferred: owner approval: bounded follow-up"]
        repaired_view = pr_view(headRefOid="d" * 40)
        for epoch, authenticated_head in (("1", "d" * 40), ("2", "c" * 40)):
            with patch("subprocess.run", return_value=repaired_view):
                rejected, _ = self.operation(root, "handoff", id="A", execution_id="e",
                                             pr="https://github.com/o/r/pull/1", head=authenticated_head,
                                             observed_head="spoofed", observed_author="spoofed", peer="reviewer",
                                             epoch=epoch, finding=repaired_findings)
            self.assertNotEqual(rejected, 0, (epoch, authenticated_head))
        with patch("subprocess.run", return_value=repaired_view):
            rehandoff, _ = self.operation(root, "handoff", id="A", execution_id="e",
                                          pr="https://github.com/o/r/pull/1", head="d" * 40,
                                          observed_head="spoofed", observed_author="spoofed", peer="reviewer",
                                          epoch="2", finding=repaired_findings)
        self.assertEqual(rehandoff, 0)
        repaired = json.loads((root / "docs/project/work-items/A.json").read_text(encoding="utf-8"))
        self.assertEqual(repaired["lifecycle"], "ReviewRequired")
        self.assertEqual(repaired["reviewEpoch"], "2")
        self.assertEqual(repaired["review"]["requestHead"], "d" * 40)
        self.assertEqual(repaired["reviewFindings"], sorted(repaired_findings))
        self.assertEqual(ws.validate(root), 0)

    def test_execution_metadata_shapes_and_lifecycle_consistency_are_rejected(self):
        root = self.synthetic()
        baseline = json.loads((root / "docs/project/work-items/A.json").read_text(encoding="utf-8"))
        cases = (
            ("worktreeId", "/tmp/elsewhere", "worktree"),
            ("worktreeId", "", "worktree"),
            ("executionId", 7, "execution"),
            ("claims", [{"kind": "unknown", "value": "src/a"}], "claim"),
            ("claims", [{"kind": "path", "value": "/absolute"}], "claim"),
            ("claims", [{"kind": "path", "value": "src/a"}, {"kind": "path", "value": "src/a"}], "claim"),
            ("reviewFindings", "fixed", "review"),
            ("reviewFindings", ["open"], "review"),
            ("closeout", {"executionId": "e"}, "closeout"),
            ("review", {"executionId":"e", "pr":"not-a-url", "requestHead":"bad", "author":7,
                         "peer":"peer", "epoch":"1", "findings":[]}, "review"),
            ("closeout", {"executionId":"e", "pr":"not-a-url", "head":"bad", "mergeSha":"bad",
                          "focused":7, "final":None, "attribution":7, "findings":"resolved"}, "closeout"),
            ("executionId", "orphan", "lifecycle"),
        )
        for field, value, diagnostic in cases:
            with self.subTest(field=field, value=value):
                candidate = __import__("copy").deepcopy(baseline)
                candidate[field] = value
                if field in {"executionId", "worktreeId", "claims"}:
                    candidate["lifecycle"] = "Active"
                    candidate["lifecycleLabel"] = "active"
                if field == "executionId" and value == "orphan":
                    candidate["lifecycle"] = "Ready"
                    candidate["lifecycleLabel"] = "ready"
                expected_state = "Building" if candidate["lifecycle"] == "Active" else "NotStarted"
                capsule = (root / candidate["checkpointPath"] / "checkpoint.md").read_text(encoding="utf-8")
                capsule = capsule.replace("`NotStarted`", f"`{expected_state}`")
                errors = ws.validate_items([candidate], root, {candidate["checkpointPath"]: capsule})
                self.assertTrue(errors, (field, value))
                self.assertTrue(any(diagnostic in error.lower() or field.lower() in error.lower() for error in errors), errors)
        root = self.synthetic()
        required = {"expected_baseline": "a" * 40, "current_head": "a" * 40,
                    "current_branch": "feature/a", "clean": True, "worktree_id": "worktree-a",
                    "claim": "src/a"}
        for option, value, diagnostic in (("expected_baseline", "b" * 40, "baseline identity mismatch"),
                                          ("current_head", "b" * 40, "current HEAD is stale"),
                                          ("current_branch", "feature/other", "branch identity mismatch"),
                                          ("worktree_id", "/tmp/other", "invalid worktree identity"),
                                          ("claim", "/absolute", "absolute claim")):
            with self.subTest(option=option):
                packet = dict(required)
                packet[option] = value
                code, output = self.operation(root, "activate", id="A", execution_id="valid-execution", **packet)
                self.assertNotEqual(code, 0)
                self.assertIn(diagnostic, output)

    def test_real_prepare_and_projection_reads_are_nonmutating_and_github_start_close_are_observable(self):
        paths = {p: p.read_bytes() for p in ROOT.rglob("*") if p.is_file() and ".git" not in p.parts}
        code, output = self.operation(ROOT, "prepare", id="GH-57")
        self.assertEqual(code, 0, output)
        self.assertIn("GH-57", output)
        self.assertNotRegex(output, r"(?i)([A-Z]:\\|/Users/|/home/|token|password|session|timestamp|2026-)")
        self.assertEqual(paths, {p: p.read_bytes() for p in paths})
        self.assertEqual(ws.validate(ROOT), 0)
        self.assertEqual(ws.execution(ROOT, True), 0)
        canonical = next(x for x in self.items if x["id"] == "GH-57")
        disk_item = json.loads((ROOT / "docs/project/work-items/GH-57.json").read_text(encoding="utf-8"))
        self.assertEqual(disk_item["owner"], canonical["owner"])
        self.assertEqual(disk_item["branch"], canonical["branch"])
        self.assertEqual(disk_item["dependencies"], canonical["dependencies"])
        self.assertEqual(disk_item["lifecycleLabel"], ws.LABELS.get(disk_item["lifecycle"]))
        execution = json.loads((ROOT / "docs/project/execution.json").read_text(encoding="utf-8"))
        active_lifecycles = {"Active", "ReviewRequired", "ResolvingFindings", "Verifying"}
        active_selected = bool(disk_item.get("selectedForExecution")) and disk_item["lifecycle"] in active_lifecycles
        matching = [entry for entry in execution.get("executions", []) if entry.get("sourceId") == disk_item["id"]]
        if active_selected:
            for field in ("executionId", "worktreeId", "claims"):
                self.assertIn(field, disk_item)
            self.assertEqual(len(matching), 1)
            entry = matching[0]
            for field in ("owner", "branch", "worktreeId", "checkpointId", "checkpointPath"):
                self.assertEqual(entry[field], disk_item[field], field)
            self.assertEqual(entry["sourceId"], disk_item["id"])
            self.assertEqual(entry["executionId"], disk_item["executionId"])
            self.assertEqual(entry["dependencies"], sorted(disk_item["dependencies"]))
            self.assertEqual(entry["claims"], sorted(disk_item["claims"], key=lambda claim: (claim["kind"], claim["value"])))
        else:
            self.assertFalse(matching)
            if not any(value.get("selectedForExecution") and value.get("lifecycle") in active_lifecycles for value in self.items):
                self.assertEqual(execution["executions"], [])
        for field in ("sourceId", "activeCheckpointId", "activeCheckpointPath", "mode"):
            self.assertIn(field, execution)
        root = self.synthetic()
        item_path = root / "docs/project/work-items/A.json"
        item = json.loads(item_path.read_text(encoding="utf-8"))
        item.update(kind="github-issue", number=57, sourceUrl="https://github.com/o/r/issues/57", expectedGithubState="OPEN")
        item_path.write_text(ws.dump(item), encoding="utf-8")
        self.assertEqual(ws.validate(root), 0)
        remote = json.dumps([{"number": 57, "state": "OPEN", "labels": []}])
        with patch("subprocess.run", return_value=type("R", (), {"stdout": remote})()) as run:
            code, output = self.operation(root, "sync-github", dry_run=True)
        self.assertEqual(code, 0, output)
        run.assert_called_once()
        self.assertIn("--add-label ready", output)
        self.assertIn("DRY-RUN", output)
        item["lifecycle"], item["lifecycleLabel"] = "Active", "active"
        item_path.write_text(ws.dump(item), encoding="utf-8")
        with patch("subprocess.run", return_value=type("R", (), {"stdout": json.dumps([{"number": 57, "state": "OPEN", "labels": [{"name": "ready"}]}])})()):
            code, output = self.operation(root, "sync-github", dry_run=True)
        self.assertEqual(code, 0, output)
        self.assertIn("--remove-label ready --add-label active", output)
        item["lifecycle"], item["lifecycleLabel"], item["expectedGithubState"] = "Closed", None, "CLOSED"
        item_path.write_text(ws.dump(item), encoding="utf-8")
        with patch("subprocess.run", return_value=type("R", (), {"stdout": json.dumps([{"number": 57, "state": "CLOSED", "labels": [{"name": "active"}]}])})()):
            code, output = self.operation(root, "sync-github", dry_run=True)
        self.assertEqual(code, 0, output)
        self.assertIn("--remove-label active", output)

    def test_closeout_requires_receipts_identity_and_resolved_findings_and_promotes_only_ready_dependents(self):
        root = self.synthetic(second=True, dependency=True)
        self.assertEqual(ws.validate(root), 0)
        self.assertEqual(self.activate(root, execution_id="a", claim="src/a")[0], 0)
        h1, h2, h3 = "d"*40, "e"*40, "f"*40
        pr = {"number":1,"url":"https://github.com/o/r/pull/1","state":"OPEN","isDraft":False,
              "author":{"login":"actual-author", "id":"U_kgDODXRwzA", "is_bot":False,
                         "name":"Actual Author", "url":"https://github.com/actual-author"},
              "headRefOid":h1,"mergeCommit":None,"reviewDecision":"APPROVED"}
        def gh_view(merged=False, head=h2, review_state="APPROVED", review_head=h2, review_peer="reviewer", merge_sha="a"*40):
            view = dict(pr, state="MERGED" if merged else "OPEN", headRefOid=head, mergeCommit={"oid":merge_sha} if merged else None)
            reviews = [{"user":{"login":review_peer}, "state":review_state, "commit_id":review_head}]
            def run(command, *args, **kwargs):
                text = " ".join(command) if isinstance(command, (list, tuple)) else str(command)
                return type("R",(),{"stdout":json.dumps(reviews if "reviews" in text else view)})()
            return run
        with patch("subprocess.run", side_effect=gh_view(head=h1, review_head=h1)) as run:
            handoff_code, _ = self.operation(root, "handoff", id="A", execution_id="a", pr="https://github.com/o/r/pull/1",
                                             head=h1, observed_head="spoofed", observed_author="spoofed",
                                             peer="reviewer", epoch="1", finding=["Fixed: focused verification receipt"])
        self.assertEqual(handoff_code, 0)
        self.assertEqual(ws.validate(root), 0)
        handoff_record = json.loads((root / "docs/project/work-items/A.json").read_text(encoding="utf-8"))
        self.assertEqual(handoff_record["review"]["requestHead"], h1)
        for finding in ("open", "unresolved"):
            self.assertNotEqual(self.operation(root, "closeout", id="A", execution_id="a", findings=finding)[0], 0)
        for variant, caller_head in ((dict(merged=False), h2), (dict(merged=True, merge_sha="b"*40), h2),
                                      (dict(merged=True, review_head=h1), h2),
                                      (dict(merged=True, review_head=h3), h2),
                                      (dict(merged=True, review_peer="other"), h2),
                                      (dict(merged=True), h1), (dict(merged=True), h3),
                                      (dict(merged=True, review_state="COMMENTED"), h2)):
            with patch("subprocess.run", side_effect=gh_view(**variant)):
                rejected, _ = self.operation(root, "closeout", id="A", execution_id="a", findings="resolved",
                                              focused_receipt="focused", final_receipt="final", attribution="tester",
                                              pr="https://github.com/o/r/pull/1", head=caller_head,
                                              observed_head="spoofed", peer="reviewer", merge_sha="a"*40)
            self.assertNotEqual(rejected, 0, variant)
        with patch("subprocess.run", side_effect=gh_view(merged=True)):
            close_code, close_packet = self.operation(root, "closeout", id="A", execution_id="a", findings="resolved",
                                                   focused_receipt="focused", final_receipt="final", attribution="tester",
                                                   pr="https://github.com/o/r/pull/1", head=h2,
                                                   observed_head="spoofed", peer="reviewer", merge_sha="a" * 40)
        self.assertEqual(close_code, 0, close_packet)
        self.assertNotRegex(close_packet, r"(?i)([A-Z]:\\|/Users/|/home/|token|password|session|timestamp|2026-)")
        dependent = json.loads((root / "docs/project/work-items/B.json").read_text(encoding="utf-8"))
        closed = json.loads((root / "docs/project/work-items/A.json").read_text(encoding="utf-8"))
        self.assertEqual(closed.get("claims"), [])
        self.assertEqual(closed["closeout"], {"executionId":"a", "pr":"https://github.com/o/r/pull/1",
                                               "head":h2, "mergeSha":"a"*40, "focused":"focused",
                                               "final":"final", "attribution":"tester", "findings":["resolved"]})
        self.assertNotEqual(dependent.get("lifecycle"), "Active")
        self.assertEqual(dependent.get("claims"), None)
        self.assertIn("`Closed`", (root / "docs/work/checkpoints/A/checkpoint.md").read_text(encoding="utf-8"))
        self.assertEqual(ws.validate(root), 0)
        b_capsule = root / "docs/work/checkpoints/B/checkpoint.md"
        complete_b = b_capsule.read_text(encoding="utf-8")
        b_capsule.write_text("# incomplete\n", encoding="utf-8")
        self.assertNotEqual(self.operation(root, "promote", id="B")[0], 0)
        b_capsule.write_text(complete_b, encoding="utf-8")
        self.assertEqual(self.operation(root, "promote", id="B")[0], 0)
        dependent = json.loads((root / "docs/project/work-items/B.json").read_text(encoding="utf-8"))
        self.assertEqual(dependent.get("lifecycle"), "Ready")
        self.assertFalse(dependent.get("selectedForExecution")); self.assertNotIn("executionId", dependent)
        self.assertIn("`NotStarted`", (root / "docs/work/checkpoints/B/checkpoint.md").read_text(encoding="utf-8"))
        self.assertEqual(ws.validate(root), 0)

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
        closed_drift = json.dumps([{"number": 1, "state": "CLOSED", "labels": [{"name": "blocked"}],
                                   "comments": [{"body": "SeqDoc packet: start A; closure A; marker=seqdoc-state-v1"}]}])
        with patch("subprocess.run", side_effect=[type("R", (), {"stdout": closed_drift})(),
                                                   type("R", (), {"stdout": ""})()]) as run:
            code, output = self.operation(root, "project", dry_run=True)
        self.assertNotEqual(code, 0)
        self.assertEqual(run.call_count, 1)
        self.assertIn("CLOSED", output)
        item["lifecycle"], item["lifecycleLabel"] = "Active", "active"
        item_path.write_text(ws.dump(item), encoding="utf-8")
        marker_remote = json.dumps([{"number": 1, "state": "OPEN", "labels": [{"name": "ready"}, {"name": "keep"}],
                                     "comments": [{"body": "seqdoc-state-v1:A:Closed\nitem=A state=Closed checkpoint=old next=closed"}]}])
        with patch("subprocess.run", return_value=type("R", (), {"stdout": marker_remote})()) as run:
            code, output = self.operation(root, "project", dry_run=True)
        self.assertEqual(code, 0, output)
        self.assertEqual(run.call_count, 1)
        self.assertIn("--body", output)
        self.assertIn("Active", output)
        self.assertNotIn("timestamp", output.lower())
        self.assertNotIn("session", output.lower())
        duplicate = json.dumps([{"number": 1, "state": "OPEN", "labels": [{"name": "active"}, {"name": "keep"}],
                                "comments": [{"body": "seqdoc-state-v1:A:Active\nitem=A state=Active checkpoint=A next=continue"}]}])
        with patch("subprocess.run", return_value=type("R", (), {"stdout": duplicate})()) as run:
            code, output = self.operation(root, "project", dry_run=True)
        self.assertEqual(code, 0, output)
        self.assertEqual(run.call_count, 1)
        self.assertNotIn("--body", output)

    def test_real_registry_and_checked_in_projection_are_read_only(self):
        registry = {p: p.read_bytes() for p in (ROOT / "docs/project/work-items").glob("*.json")}
        projection = (ROOT / "docs/project/execution.json").read_bytes()
        self.assertEqual(ws.validate(ROOT), 0)
        self.assertEqual(ws.execution(ROOT, True), 0)
        self.assertEqual(registry, {p: p.read_bytes() for p in registry})
        self.assertEqual(projection, (ROOT / "docs/project/execution.json").read_bytes())


if __name__ == "__main__": unittest.main()
