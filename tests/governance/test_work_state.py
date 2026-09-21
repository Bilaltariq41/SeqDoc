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
            capsule = self.d / "docs/work/persistence/I12/checkpoint.md"
            lines = capsule.read_text().splitlines(keepends=True); lines[4] = f"`{ws.CAPSULE_STATES[state]}`\n"; capsule.write_text("".join(lines))
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
        self.assertEqual(self.activate(root, item="A", execution_id="a", claim="src/a")[0], 0)
        self.assertEqual(self.activate(root, item="B", execution_id="b", claim="src/b")[0], 0)
        b = root / "docs/project/work-items/B.json"
        value = json.loads(b.read_text()); value.update(reviewEpoch="1", reviewPeer="peer", reviewFindings=["Fixed: check"], review={"executionId":"b","pr":"https://github.com/o/r/pull/2","requestHead":"a"*40,"author":"author","peer":"peer","epoch":"1","findings":["Fixed: check"]})
        b.write_text(ws.dump(value))
        for state in ("Blocked", "Cancelled"):
            action = type("A", (), {"id":"B", "state":state, "reason":"state transition", "select":False, "check":False, "dry_run":False})()
            self.assertEqual(ws.transition(root, action), 0)
            after = json.loads(b.read_text())
            self.assertEqual(after["lifecycle"], state); self.assertFalse(after["selectedForExecution"])
            for field in ("executionId", "worktreeId", "claims", "review", "reviewEpoch", "reviewPeer", "reviewFindings"): self.assertNotIn(field, after)
            survivor = json.loads((root / "docs/project/work-items/A.json").read_text())
            self.assertEqual((survivor["executionId"], survivor["worktreeId"]), ("a", "worktree-a"))
            self.assertIn("a", {x["executionId"] for x in json.loads((root / "docs/project/execution.json").read_text())["executions"]})
            if state == "Blocked":
                after["lifecycle"], after["lifecycleLabel"] = "Blocked", "blocked"; b.write_text(ws.dump(after))
        root = self.synthetic(second=True, dependency=True)
        dependency_path = root / "docs/project/work-items/B.json"; dependency_item = json.loads(dependency_path.read_text()); dependency_item["lifecycle"], dependency_item["lifecycleLabel"] = "Closed", None; dependency_path.write_text(ws.dump(dependency_item)); dependency_capsule = root / "docs/work/checkpoints/B/checkpoint.md"; dependency_capsule.write_text(dependency_capsule.read_text().replace("`Blocked`", "`Closed`").replace("`NotStarted`", "`Closed`"))
        path = root / "docs/project/work-items/A.json"; item = json.loads(path.read_text()); item["lifecycle"], item["lifecycleLabel"] = "Blocked", "blocked"; item["statusReason"] = "blocked"; path.write_text(ws.dump(item))
        capsule = root / "docs/work/checkpoints/A/checkpoint.md"; capsule.write_text(capsule.read_text().replace("`NotStarted`", "`Blocked`")); self.assertEqual(ws.validate(root), 0)
        head = "1" * 40
        values = dict(id="A", execution_id="resume-a", worktree_id="resume-worktree", expected_baseline=item["baseline"], current_head=head, start_head=head, current_branch="feature/a", clean=True, claim=["src\\repair/../repair.py"], reason="resume worker", next_action="verify worker")
        before = {p.relative_to(root).as_posix():p.read_bytes() for p in root.rglob("*") if p.is_file()}
        with patch("tools.governance.work_state.observe_git", return_value=(head, "feature/a", True)), patch("subprocess.run") as run:
            code, output = self.operation(root, "resume", **values)
        self.assertEqual(code, 0, output); run.assert_not_called()
        resumed = json.loads(path.read_text()); self.assertEqual(resumed["lifecycle"], "ResolvingFindings"); self.assertTrue(resumed["selectedForExecution"])
        self.assertEqual(resumed["claims"], [{"kind":"path", "value":"src/repair.py"}]); self.assertEqual(resumed["resume"], {"reason":"resume worker", "startHead":head}); self.assertNotIn("takeover", resumed)
        for field in ("baseline", "owner", "branch", "pr", "dependencies"): self.assertEqual(resumed[field], item[field])
        self.assertNotEqual(before, {p.relative_to(root).as_posix():p.read_bytes() for p in root.rglob("*") if p.is_file()}); self.assertEqual(ws.validate(root), 0)
        def reject(**changes):
            candidate = self.synthetic(second=True, dependency=True); p = candidate / "docs/project/work-items/A.json"; old = json.loads(p.read_text()); old["lifecycle"], old["lifecycleLabel"] = "Blocked", "blocked"; p.write_text(ws.dump(old)); (candidate / "docs/work/checkpoints/A/checkpoint.md").write_text((candidate / "docs/work/checkpoints/A/checkpoint.md").read_text().replace("`NotStarted`", "`Blocked`"));
            vals = dict(values, expected_baseline=old["baseline"]); vals.update(changes); snap = {q.relative_to(candidate).as_posix():q.read_bytes() for q in candidate.rglob("*") if q.is_file()}
            with patch("tools.governance.work_state.observe_git", return_value=(head, "feature/a", True)), patch("subprocess.run") as run: self.assertNotEqual(self.operation(candidate, "resume", **vals)[0], 0); run.assert_not_called()
            self.assertEqual(snap, {q.relative_to(candidate).as_posix():q.read_bytes() for q in candidate.rglob("*") if q.is_file()})
        reject(lifecycle="Active")
        retry = json.loads(path.read_text()); retry_before = {q.relative_to(root).as_posix():q.read_bytes() for q in root.rglob("*") if q.is_file()}
        with patch("tools.governance.work_state.observe_git", return_value=(head, "feature/a", True)), patch("subprocess.run") as run: self.assertNotEqual(self.operation(root, "resume", **values)[0], 0); run.assert_not_called()
        self.assertEqual(retry_before, {q.relative_to(root).as_posix():q.read_bytes() for q in root.rglob("*") if q.is_file()})
        reject(expected_baseline="b"*40); reject(current_head="2"*40); reject(start_head="2"*40); reject(current_branch="feature/b"); reject(clean=False); reject(dirty=True); reject(reason=""); reject(next_action=""); reject(claim=[]); reject(claim=["x", "x"]); reject(claim="/absolute/path"); reject(claim="../outside.py")
        dependency = self.synthetic(second=True); ap = dependency / "docs/project/work-items/A.json"; bp = dependency / "docs/project/work-items/B.json"; av = json.loads(ap.read_text()); bv = json.loads(bp.read_text()); av["lifecycle"], av["lifecycleLabel"], av["dependencies"] = "Blocked", "blocked", ["B"]; bv["lifecycle"], bv["lifecycleLabel"] = "Blocked", "blocked"; ap.write_text(ws.dump(av)); bp.write_text(ws.dump(bv)); (dependency / "docs/work/checkpoints/A/checkpoint.md").write_text((dependency / "docs/work/checkpoints/A/checkpoint.md").read_text().replace("`NotStarted`", "`Blocked`")); (dependency / "docs/work/checkpoints/B/checkpoint.md").write_text((dependency / "docs/work/checkpoints/B/checkpoint.md").read_text().replace("`NotStarted`", "`Blocked`")); snap = {q.relative_to(dependency).as_posix():q.read_bytes() for q in dependency.rglob("*") if q.is_file()}
        with patch("tools.governance.work_state.observe_git", return_value=(head, "feature/a", True)), patch("subprocess.run") as run: self.assertNotEqual(self.operation(dependency, "resume", **dict(values, expected_baseline=av["baseline"]))[0], 0); run.assert_not_called()
        self.assertEqual(snap, {q.relative_to(dependency).as_posix():q.read_bytes() for q in dependency.rglob("*") if q.is_file()})
        missing = self.synthetic(second=True, dependency=True); p = missing / "docs/project/work-items/A.json"; old = json.loads(p.read_text()); old["lifecycle"]="Blocked"; old["lifecycleLabel"]="blocked"; p.write_text(ws.dump(old)); (missing / "docs/work/checkpoints/A/checkpoint.md").unlink()
        with patch("tools.governance.work_state.observe_git", return_value=(head, "feature/a", True)), patch("subprocess.run") as run: self.assertNotEqual(self.operation(missing, "resume", **dict(values, expected_baseline=old["baseline"]))[0], 0); run.assert_not_called()
        occupied = self.synthetic(second=True); self.assertEqual(self.activate(occupied, item="B", execution_id="other", claim="src/repair.py")[0], 0); p = occupied / "docs/project/work-items/A.json"; old = json.loads(p.read_text()); old["lifecycle"]="Blocked"; old["lifecycleLabel"]="blocked"; p.write_text(ws.dump(old)); (occupied / "docs/work/checkpoints/A/checkpoint.md").write_text((occupied / "docs/work/checkpoints/A/checkpoint.md").read_text().replace("`NotStarted`", "`Blocked`"));
        with patch("tools.governance.work_state.observe_git", return_value=(head, "feature/a", True)): self.assertNotEqual(self.operation(occupied, "resume", **dict(values, expected_baseline=old["baseline"]))[0], 0)
        for flag in ("--authorization-receipt", "--peer-authorization-receipt", "--authorization-head"):
            with patch("sys.argv", ["work_state.py", "resume", flag]):
                with self.assertRaises(SystemExit): ws.main()
        broken = self.synthetic(second=True, dependency=True); p = broken / "docs/project/work-items/A.json"; old = json.loads(p.read_text()); old["lifecycle"]="Blocked"; old["lifecycleLabel"]="blocked"; p.write_text(ws.dump(old)); (broken / "docs/work/checkpoints/A/checkpoint.md").write_text((broken / "docs/work/checkpoints/A/checkpoint.md").read_text().replace("`NotStarted`", "`Blocked`")); before = {q.relative_to(broken).as_posix():q.read_bytes() for q in broken.rglob("*") if q.is_file()}; real = __import__("os").replace; count=[0]
        def fail(src, dst):
            count[0] += 1
            if count[0] == 2: raise OSError("atomic failure")
            return real(src, dst)
        with patch("tools.governance.work_state.observe_git", return_value=(head, "feature/a", True)), patch("os.replace", side_effect=fail): self.assertNotEqual(self.operation(broken, "resume", **dict(values, expected_baseline=old["baseline"]))[0], 0)
        self.assertGreaterEqual(count[0], 2); self.assertEqual(before, {q.relative_to(broken).as_posix():q.read_bytes() for q in broken.rglob("*") if q.is_file()}); self.assertFalse(ws.runtime_journal(broken).exists()); self.assertFalse(list(broken.rglob(".work-state-*")))
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
        strict_root = self.synthetic(); strict_action = type("A",(),{"id":"A","state":"Active","reason":"strict rollback","next_action":"strict rollback","select":True,"check":False,"dry_run":False})(); strict_before = {p.relative_to(strict_root).as_posix(): p.read_bytes() for p in strict_root.rglob("*") if p.is_file()}; strict_count = [0]; strict_replace = __import__("os").replace
        def strict_fail(source, target):
            strict_count[0] += 1
            if strict_count[0] == 2: raise OSError("commit replacement failure")
            return strict_replace(source, target)
        def forbidden_rewrite(path, data):
            raise AssertionError("rollback used Path.write_bytes")
        with patch("os.replace", side_effect=strict_fail), patch.object(Path, "write_bytes", new=forbidden_rewrite):
            try:
                strict_code = ws.transition(strict_root, strict_action)
            except AssertionError:
                strict_code = 1
        self.assertNotEqual(strict_code, 0)
        self.assertEqual(strict_before, {p.relative_to(strict_root).as_posix(): p.read_bytes() for p in strict_root.rglob("*") if p.is_file()}); self.assertFalse(ws.runtime_journal(strict_root).exists()); self.assertFalse(list(strict_root.rglob(".work-state-*")))
        self.assertEqual(ws.transition(strict_root, strict_action), 0)
        recover_root = self.synthetic()
        recover_action = type("A",(),{"id":"A","state":"Active","reason":"recoverable mutation","select":True,"check":False,"dry_run":False})()
        recover_before = {p.relative_to(recover_root).as_posix(): p.read_bytes() for p in recover_root.rglob("*") if p.is_file()}
        replace_count = [0]; real_replace = __import__("os").replace
        def fail_after_first(source, target):
            replace_count[0] += 1
            if replace_count[0] in (2, 3): raise OSError("interrupted commit or rollback")
            return real_replace(source, target)
        with patch("os.replace", side_effect=fail_after_first):
            self.assertNotEqual(ws.transition(recover_root, recover_action), 0)
        self.assertTrue(ws.runtime_journal(recover_root).exists())
        self.assertTrue(list(recover_root.rglob(".work-state-*")))
        self.assertEqual(self.operation(recover_root, "recover")[0], 0)
        self.assertFalse(ws.runtime_journal(recover_root).exists())
        self.assertFalse(list(recover_root.rglob(".work-state-*")))
        self.assertEqual(recover_before, {p.relative_to(recover_root).as_posix(): p.read_bytes() for p in recover_root.rglob("*") if p.is_file()})

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
        supplied_value["lifecycle"], supplied_value["lifecycleLabel"], supplied_value["selectedForExecution"] = "Active", "active", True
        supplied_value["nextAction"] = "scaffolded active checkpoint"
        supplied_record.write_text(ws.dump(supplied_value), encoding="utf-8")
        code, output = self.operation(supplied, "prepare", id="A", scaffold=True,
                                      checkpoint_id="custom-a", checkpoint_path="docs/work/custom-a")
        self.assertEqual(code, 0, output)
        stored = json.loads(supplied_record.read_text(encoding="utf-8"))
        self.assertEqual((stored["checkpointId"], stored["checkpointPath"]), ("custom-a", "docs/work/custom-a"))
        self.assertTrue((supplied / "docs/work/custom-a/checkpoint.md").exists())
        self.assertEqual(ws.execution(supplied, False), 0)
        execution = json.loads((supplied / "docs/project/execution.json").read_text(encoding="utf-8"))
        self.assertEqual(execution["activeCheckpointPath"], "docs/work/custom-a")
        with patch.object(ws, "repository_lock", wraps=ws.repository_lock) as lock:
            self.assertEqual(ws.execution(supplied, False), 0)
            self.assertTrue(lock.called)
        with patch.object(ws, "repository_lock", wraps=ws.repository_lock) as lock:
            self.assertEqual(ws.execution(supplied, True), 0)
            self.assertTrue(lock.called)
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
                                             final_receipt="final", attribution="external-author", pr="https://github.com/o/r/pull/1",
                                            head="a" * 40, observed_head="a" * 40, peer="reviewer",
                                            merge_sha="a" * 40)[0], 0)
        state = json.loads((root / "docs/project/work-items/B.json").read_text(encoding="utf-8"))
        self.assertEqual(state.get("executionId"), "b")
        self.assertTrue(state.get("selectedForExecution"))
        locked = self.synthetic()
        ready = Path(tempfile.mkdtemp(dir=self.d)); start = ready / "start"
        child = "import pathlib,sys,time; from types import SimpleNamespace; import tools.governance.work_state as w; r=pathlib.Path(sys.argv[1]); pathlib.Path(sys.argv[2]).write_text('ready'); s=pathlib.Path(sys.argv[3]);\nwhile not s.exists(): time.sleep(.005)\na=SimpleNamespace(id='A',state='Active',reason='parallel process',next_action='parallel process',select=True,select_id=None,check=False,dry_run=False); print(w.transition(r,a))"
        processes = [subprocess.Popen([__import__("sys").executable, "-B", "-c", child, str(locked), str(ready / f"ready-{n}"), str(start)], cwd=str(ROOT), stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True) for n in (1, 2)]
        deadline = __import__("time").time() + 10
        while not all((ready / f"ready-{n}").exists() for n in (1, 2)) and __import__("time").time() < deadline: __import__("time").sleep(.01)
        self.assertTrue(all((ready / f"ready-{n}").exists() for n in (1, 2)))
        start.write_text("go")
        results = [process.communicate(timeout=10)[0] for process in processes]
        self.assertEqual(sum("\n0\n" in ("\n" + output) for output in results), 1, results)
        compatible = self.synthetic(second=True)
        compatible_ready = Path(tempfile.mkdtemp(dir=self.d)); compatible_start = compatible_ready / "start"
        activation = "import pathlib,sys,time; import tools.governance.work_state as w; r=pathlib.Path(sys.argv[1]); item=sys.argv[2]; ready=pathlib.Path(sys.argv[3]); pathlib.Path(ready).write_text('ready'); s=pathlib.Path(sys.argv[4]);\nwhile not s.exists(): time.sleep(.005)\nargv=['work_state.py','activate','--root',str(r),'--id',item,'--execution-id','proc-'+item.lower(),'--expected-baseline','a'*40,'--current-head','a'*40,'--current-branch','feature/'+item.lower(),'--worktree-id','worktree-'+item.lower(),'--clean','--claim','src/'+item.lower()]; argv += ['--select'] if item == 'A' else []; sys.argv=argv; print(w.main())"
        workers = [subprocess.Popen([__import__("sys").executable, "-B", "-c", activation, str(compatible), item, str(compatible_ready / f"ready-{item}"), str(compatible_start)], cwd=str(ROOT), stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True) for item in ("A", "B")]
        deadline = __import__("time").time() + 10
        while not all((compatible_ready / f"ready-{item}").exists() for item in ("A", "B")) and __import__("time").time() < deadline: __import__("time").sleep(.01)
        self.assertTrue(all((compatible_ready / f"ready-{item}").exists() for item in ("A", "B"))); compatible_start.write_text("go")
        compatible_results = []
        for worker in workers:
            stdout, stderr = worker.communicate(timeout=10)
            compatible_results.append((worker.returncode, stdout, stderr))
        self.assertTrue(all(code == 0 and stdout.rstrip().endswith("0") for code, stdout, _ in compatible_results), compatible_results)
        final_items = {item: json.loads((compatible / "docs/project/work-items" / f"{item}.json").read_text()) for item in ("A", "B")}
        self.assertEqual({final_items[item]["executionId"] for item in final_items}, {"proc-a", "proc-b"}); self.assertEqual({final_items[item]["claims"][0]["value"] for item in final_items}, {"src/a", "src/b"}); self.assertEqual(sum(final_items[item]["selectedForExecution"] for item in final_items), 1)
        release = self.synthetic(second=True)
        self.assertNotEqual(self.activate(release, expected_baseline="b"*40)[0], 0)
        self.assertEqual(self.activate(release, execution_id="after-validation", claim="src/a")[0], 0)
        with patch("os.replace", side_effect=OSError("injected commit failure")):
            self.assertNotEqual(self.activate(release, item="B", execution_id="after-failure", claim="src/b")[0], 0)
        self.assertEqual(self.activate(release, item="B", execution_id="after-failure-retry", claim="src/b")[0], 0)
        projected = self.synthetic()
        projection_barrier = Path(tempfile.mkdtemp(dir=self.d)); projection_start = projection_barrier / "start"
        projection_child = "import pathlib,sys,time; import tools.governance.work_state as w; r=pathlib.Path(sys.argv[1]); pathlib.Path(sys.argv[2]).write_text('ready'); s=pathlib.Path(sys.argv[3]);\nwhile not s.exists(): time.sleep(.005)\nprint(w.execution(r,False))"
        transaction_child = "import pathlib,sys,time; from types import SimpleNamespace; import tools.governance.work_state as w; r=pathlib.Path(sys.argv[1]); pathlib.Path(sys.argv[2]).write_text('ready'); s=pathlib.Path(sys.argv[3]);\nwhile not s.exists(): time.sleep(.005)\na=SimpleNamespace(id='A',state='Active',reason='projection race',next_action='projection race',select=True,select_id=None,check=False,dry_run=False); print(w.transition(r,a))"
        projection_process = subprocess.Popen([__import__("sys").executable,"-B","-c",projection_child,str(projected),str(projection_barrier/"projection"),str(projection_start)],cwd=str(ROOT),stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
        transaction_process = subprocess.Popen([__import__("sys").executable,"-B","-c",transaction_child,str(projected),str(projection_barrier/"transaction"),str(projection_start)],cwd=str(ROOT),stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
        deadline = __import__("time").time() + 10
        while not all((projection_barrier / name).exists() for name in ("projection", "transaction")) and __import__("time").time() < deadline: __import__("time").sleep(.01)
        self.assertTrue(all((projection_barrier / name).exists() for name in ("projection", "transaction"))); projection_start.write_text("go")
        self.assertTrue(projection_process.communicate(timeout=10)[0].rstrip().endswith("0")); self.assertTrue(transaction_process.communicate(timeout=10)[0].rstrip().endswith("0"))
        self.assertEqual(json.loads((projected / "docs/project/execution.json").read_text()), json.loads(ws.execution_payload(ws.load(projected)))); self.assertEqual(ws.execution(projected, True), 0)

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
        def unresolved_journal_case(status, legacy=False):
            candidate = self.synthetic(); execution_bytes = (candidate / "docs/project/execution.json").read_bytes(); encoded = base64.b64encode(execution_bytes).decode("ascii"); digest = ws.file_hash(execution_bytes)
            journal = {"generation":"unresolved-generation", "status":status, "entries":[{"path":"docs/project/execution.json", "originalExists":True, "originalHash":digest, "targetHash":digest, "original":encoded, "target":encoded}]}
            journal_path = candidate / "docs/project/work-state.journal.json" if legacy else ws.runtime_journal(candidate); journal_path.parent.mkdir(parents=True, exist_ok=True); journal_path.write_text(json.dumps(journal), encoding="utf-8"); journal_bytes = journal_path.read_bytes(); before = {p.relative_to(candidate).as_posix(): p.read_bytes() for p in candidate.rglob("*") if p.is_file()}
            code, output = self.activate(candidate, execution_id="blocked-by-recovery", claim="src/recovery")
            self.assertNotEqual(code, 0, (status, legacy, output)); self.assertIn("recover", output.lower()); self.assertEqual(journal_bytes, journal_path.read_bytes()); self.assertEqual(before, {p.relative_to(candidate).as_posix(): p.read_bytes() for p in candidate.rglob("*") if p.is_file()})
            projection_before = {p.relative_to(candidate).as_posix(): p.read_bytes() for p in candidate.rglob("*") if p.is_file()}
            self.assertNotEqual(ws.execution(candidate, False), 0); self.assertEqual(projection_before, {p.relative_to(candidate).as_posix(): p.read_bytes() for p in candidate.rglob("*") if p.is_file()}); self.assertEqual(ws.execution(candidate, True), 0)
            journal_path.unlink(); self.assertEqual(self.activate(candidate, execution_id="after-recover", claim="src/recovery")[0], 0)
        for journal_status in ("prepared", "interrupted"):
            unresolved_journal_case(journal_status)
        unresolved_journal_case("interrupted", legacy=True)
        short_root = self.synthetic(second=True); short_before = {p.relative_to(short_root).as_posix(): p.read_bytes() for p in short_root.rglob("*") if p.is_file()}; real_write = __import__("os").write
        def short_write(fd, data):
            prefix = data[:max(1, len(data) // 2)]
            real_write(fd, prefix)
            return len(prefix)
        with patch("os.write", side_effect=short_write):
            short_code, short_output = self.activate(short_root, execution_id="short", claim="src/short", fixture="fixture-short")
        self.assertEqual(short_code, 0, short_output); self.assertEqual(ws.validate(short_root), 0); self.assertEqual(ws.execution(short_root, True), 0); self.assertFalse(list(short_root.rglob(".work-state-*")))
        zero_root = self.synthetic(second=True); zero_before = {p.relative_to(zero_root).as_posix(): p.read_bytes() for p in zero_root.rglob("*") if p.is_file()}; writes = [0]
        def partial_then_zero(fd, data):
            writes[0] += 1
            if writes[0] == 1:
                prefix = data[:max(1, len(data) // 2)]; real_write(fd, prefix); return len(prefix)
            return 0
        with patch("os.write", side_effect=partial_then_zero):
            zero_code, zero_output = self.activate(zero_root, execution_id="zero", claim="src/zero")
        self.assertNotEqual(zero_code, 0, zero_output); self.assertEqual(zero_before, {p.relative_to(zero_root).as_posix(): p.read_bytes() for p in zero_root.rglob("*") if p.is_file()}); self.assertFalse(list(zero_root.rglob(".work-state-*"))); self.assertTrue(not ws.runtime_journal(zero_root).exists() or ws.runtime_journal(zero_root).stat().st_size > 0)
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
        staged_sentinel = root.parent / "untrusted-stage-sentinel.txt"; staged_sentinel.write_text("must remain staged", encoding="utf-8")
        current_execution = (root / "docs/project/execution.json").read_bytes(); encoded = base64.b64encode(current_execution).decode("ascii"); digest = ws.file_hash(current_execution)
        staged_journal = {"generation": generation, "status":"interrupted", "entries":[{"path":"docs/project/execution.json", "originalExists":True, "originalHash":digest, "targetHash":digest, "original":encoded, "target":encoded, "targetStage":str(staged_sentinel), "originalStage":str(staged_sentinel)}]}
        (root / "docs/project/work-state.journal.json").write_text(json.dumps(staged_journal), encoding="utf-8")
        canonical_before = {p.relative_to(root).as_posix(): p.read_bytes() for p in root.rglob("*") if p.is_file()}
        staged_code, staged_output = self.operation(root, "recover")
        self.assertNotEqual(staged_code, 0); self.assertIn("refused", staged_output.lower()); self.assertEqual(staged_sentinel.read_text(encoding="utf-8"), "must remain staged"); self.assertEqual(canonical_before, {p.relative_to(root).as_posix(): p.read_bytes() for p in root.rglob("*") if p.is_file()})
        forged_stage = root / "docs/project/.work-state-forged"; forged_stage.write_text("wrong staged bytes", encoding="utf-8")
        forged_entry = {"path":"docs/project/execution.json", "originalExists":True, "originalHash":digest, "targetHash":digest, "original":encoded, "target":encoded, "targetStage":"docs/project/.work-state-forged"}
        forged_journal = root / "docs/project/work-state.journal.json"; forged_journal.write_text(json.dumps({"generation":generation,"status":"interrupted","entries":[forged_entry]}), encoding="utf-8")
        forged_before = {p.relative_to(root).as_posix(): p.read_bytes() for p in root.rglob("*") if p.is_file()}
        forged_code, forged_output = self.operation(root, "recover")
        self.assertNotEqual(forged_code, 0); self.assertIn("refused", forged_output.lower()); self.assertEqual(forged_stage.read_text(encoding="utf-8"), "wrong staged bytes"); self.assertEqual(forged_before, {p.relative_to(root).as_posix(): p.read_bytes() for p in root.rglob("*") if p.is_file()})
        legitimate_target_stage = root / "docs/project/.work-state-target-legitimate"; legitimate_original_stage = root / "docs/project/.work-state-original-legitimate"; legitimate_target = b"legitimate staged target"; legitimate_target_stage.write_bytes(legitimate_target); legitimate_original_stage.write_bytes(current_execution)
        forged_journal.write_text(json.dumps({"generation":generation,"status":"interrupted","entries":[{"path":"docs/project/execution.json", "originalExists":True, "originalHash":digest, "targetHash":ws.file_hash(legitimate_target), "original":encoded, "target":base64.b64encode(legitimate_target).decode("ascii"), "targetStage":"docs/project/.work-state-target-legitimate", "originalStage":"docs/project/.work-state-original-legitimate"}]}), encoding="utf-8")
        self.assertEqual(self.operation(root, "recover")[0], 0); self.assertFalse(legitimate_target_stage.exists()); self.assertFalse(legitimate_original_stage.exists())
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

    def test_symlink_and_reparse_boundaries_are_confined(self):
        link_root = self.synthetic(second=True)
        outside_dir = Path(tempfile.mkdtemp(dir=link_root.parent)); outside_file = outside_dir / "sentinel.txt"; outside_file.write_text("outside", encoding="utf-8")
        real_path, alias_path = link_root / "real", link_root / "links"; real_path.mkdir(); (real_path / "file.txt").write_text("inside", encoding="utf-8")
        try:
            alias_path.symlink_to(real_path, target_is_directory=True)
            parent_link = link_root / "docs/project/linked"; parent_link.symlink_to(outside_dir, target_is_directory=True)
            file_link = link_root / "docs/project/work-items/linked.txt"; file_link.symlink_to(outside_file)
        except (OSError, NotImplementedError) as error:
            self.skipTest("symlink/reparse capability unavailable: " + str(error))
        self.assertEqual(self.activate(link_root, execution_id="real", claim="real/file.txt")[0], 0)
        alias_code, _ = self.activate(link_root, item="B", execution_id="alias", claim="links/file.txt")
        self.assertNotEqual(alias_code, 0)
        for target in (parent_link / "sentinel.txt", file_link):
            payload = target.read_bytes(); journal = {"executionId":"safe", "entries":[{"path":str(target.relative_to(link_root)).replace("\\","/"), "originalExists":True, "originalHash":ws.file_hash(payload), "targetHash":ws.file_hash(b"replacement"), "original":base64.b64encode(payload).decode(), "target":base64.b64encode(b"replacement").decode()}]}
            journal_path = link_root / "docs/project/work-state.journal.json"; journal_path.write_text(json.dumps(journal), encoding="utf-8")
            code, output = self.operation(link_root, "recover", execution_id="safe")
            self.assertNotEqual(code, 0, output); self.assertEqual(outside_file.read_text(encoding="utf-8"), "outside")

    def test_persisted_checkpoint_paths_cannot_escape_or_follow_reparse_parents(self):
        outside = Path(tempfile.mkdtemp(dir=self.d))
        sentinel = outside / "checkpoint.md"
        sentinel.write_bytes(b"outside checkpoint bytes")
        root = self.synthetic()
        record_path = root / "docs/project/work-items/A.json"
        record = json.loads(record_path.read_text(encoding="utf-8"))
        record["checkpointPath"] = "../" + outside.name
        record_path.write_text(ws.dump(record), encoding="utf-8")
        before = {p.relative_to(root).as_posix(): p.read_bytes() for p in root.rglob("*") if p.is_file()}
        self.assertNotEqual(ws.validate(root), 0)
        self.assertEqual(before, {p.relative_to(root).as_posix(): p.read_bytes() for p in root.rglob("*") if p.is_file()})
        self.assertEqual(sentinel.read_bytes(), b"outside checkpoint bytes")
        action = type("A", (), {"id":"A", "state":"Active", "reason":"path boundary", "select":True,
                                 "select_id":None, "check":False, "dry_run":False, "checkpoint_path":"../" + outside.name,
                                 "checkpoint_id":"A", "next_action":"path boundary", "pr":None, "branch":None,
                                 "baseline":None, "contract_revision":None})()
        self.assertNotEqual(ws.transition(root, action), 0)
        self.assertEqual(before, {p.relative_to(root).as_posix(): p.read_bytes() for p in root.rglob("*") if p.is_file()})
        self.assertEqual(sentinel.read_bytes(), b"outside checkpoint bytes")

        close_root = self.synthetic()
        self.assertEqual(self.activate(close_root, claim="src/a")[0], 0)
        pr = type("R", (), {"stdout": json.dumps({"number":1, "url":"https://github.com/o/r/pull/1",
            "state":"OPEN", "isDraft":False, "author":{"login":"author", "id":"U_kgDODXRwzA", "is_bot":False},
            "headRefOid":"c" * 40, "mergeCommit":None, "reviewDecision":None})})()
        with patch("subprocess.run", return_value=pr):
            self.assertEqual(self.operation(close_root, "handoff", id="A", execution_id="exec-a",
                pr="https://github.com/o/r/pull/1", head="c" * 40, peer="reviewer", epoch="1",
                finding=["Fixed: boundary"])[0], 0)
        close_record_path = close_root / "docs/project/work-items/A.json"
        close_record = json.loads(close_record_path.read_text(encoding="utf-8"))
        close_record["checkpointPath"] = "../" + outside.name
        close_record_path.write_text(ws.dump(close_record), encoding="utf-8")
        close_before = {p.relative_to(close_root).as_posix(): p.read_bytes() for p in close_root.rglob("*") if p.is_file()}
        with patch("subprocess.run") as github:
            code, _ = self.operation(close_root, "closeout", id="A", execution_id="exec-a",
                findings="resolved", focused_receipt="focused", final_receipt="final", attribution="author",
                pr="https://github.com/o/r/pull/1", head="c" * 40, peer="reviewer", merge_sha="d" * 40)
        self.assertNotEqual(code, 0)
        github.assert_not_called()
        self.assertEqual(close_before, {p.relative_to(close_root).as_posix(): p.read_bytes() for p in close_root.rglob("*") if p.is_file()})
        self.assertEqual(sentinel.read_bytes(), b"outside checkpoint bytes")

        link_root = self.synthetic()
        link_outside = Path(tempfile.mkdtemp(dir=self.d.parent))
        link_sentinel = link_outside / "checkpoint.md"
        link_sentinel.write_bytes(b"reparse sentinel")
        try:
            (link_root / "docs/work/checkpoints/alias").symlink_to(link_outside, target_is_directory=True)
        except (OSError, NotImplementedError) as error:
            self.skipTest("symlink/reparse capability unavailable: " + str(error))
        linked_path = link_root / "docs/project/work-items/A.json"
        linked = json.loads(linked_path.read_text(encoding="utf-8"))
        linked["checkpointPath"] = "docs/work/checkpoints/alias"
        linked_path.write_text(ws.dump(linked), encoding="utf-8")
        linked_before = {p.relative_to(link_root).as_posix(): p.read_bytes() for p in link_root.rglob("*") if p.is_file()}
        self.assertNotEqual(ws.validate(link_root), 0)
        self.assertEqual(linked_before, {p.relative_to(link_root).as_posix(): p.read_bytes() for p in link_root.rglob("*") if p.is_file()})
        self.assertEqual(link_sentinel.read_bytes(), b"reparse sentinel")

    def test_file_attribute_reparse_components_block_claims_and_recovery_before_mutation(self):
        root = self.synthetic(second=True)
        component = root / "reparse-component"
        component.mkdir()
        target = component / "target.txt"
        target.write_bytes(b"must remain")
        real_stat = Path.stat

        class ReparseStat:
            st_file_attributes = 0x400

            def __init__(self, path):
                self.path = path

            def __getattr__(self, name):
                return getattr(real_stat(self.path), name)

        def mocked_stat(path, *args, **kwargs):
            if path == component:
                return ReparseStat(path)
            return real_stat(path, *args, **kwargs)

        failures = []
        before_claim = {p.relative_to(root).as_posix(): p.read_bytes() for p in root.rglob("*") if p.is_file()}
        with patch.object(Path, "stat", new=mocked_stat):
            admitted, output = self.activate(root, execution_id="reparse-claim", claim="reparse-component/file.txt")
        if admitted == 0:
            failures.append("claim was admitted: " + output)
        if "reparse" not in output.lower():
            failures.append("claim rejection omitted reparse diagnostic: " + output)
        if before_claim != {p.relative_to(root).as_posix(): p.read_bytes() for p in root.rglob("*") if p.is_file()}:
            failures.append("claim admission mutated the registry")

        journal = root / "docs/project/work-state.journal.json"
        journal.write_text(json.dumps({"executionId": "safe", "entries": [{
            "path": "reparse-component/target.txt", "original": "must remain"
        }]}), encoding="utf-8")
        journal_before = journal.read_bytes()
        target_before = target.read_bytes()
        with patch.object(Path, "stat", new=mocked_stat):
            recovered, output = self.operation(root, "recover", execution_id="safe")
        if recovered == 0:
            failures.append("recovery followed a reparse component: " + output)
        if "reparse" not in output.lower():
            failures.append("recovery rejection omitted reparse diagnostic: " + output)
        if not journal.exists() or journal.read_bytes() != journal_before or target.read_bytes() != target_before:
            failures.append("recovery mutated confined state")
        self.assertFalse(failures, "\n".join(failures))

    def test_invalid_registry_is_rejected_before_github_remote_observation(self):
        root = self.synthetic()
        item_path = root / "docs/project/work-items/A.json"
        item = json.loads(item_path.read_text(encoding="utf-8"))
        item["owner"] = None
        item_path.write_text(ws.dump(item), encoding="utf-8")
        with patch.object(ws, "read_remote", return_value={}) as remote, patch("subprocess.run") as run:
            code, output = self.operation(root, "sync-github", dry_run=True)
        self.assertNotEqual(code, 0, output)
        remote.assert_not_called()
        run.assert_not_called()

    def test_project_execution_write_fault_preserves_projection_or_journal_evidence(self):
        root = self.synthetic()
        self.assertEqual(self.activate(root, claim="src/a")[0], 0)
        projection = root / "docs/project/execution.json"
        old = projection.read_bytes()
        item_path = root / "docs/project/work-items/A.json"
        item = json.loads(item_path.read_text(encoding="utf-8"))
        item["nextAction"] = "changed projection"
        item_path.write_text(ws.dump(item), encoding="utf-8")
        expected = ws.execution_payload(ws.load(root))
        real_replace = __import__("os").replace
        def interrupted_replace(source, target):
            if Path(target) == projection:
                raise OSError("simulated projection replacement failure")
            return real_replace(source, target)

        with patch("os.replace", side_effect=interrupted_replace):
            self.assertNotEqual(ws.execution(root, False), 0)
        self.assertTrue(projection.read_bytes() == old or ws.runtime_journal(root).exists(),
                        "a failed projection write must preserve the old bytes or leave journal evidence")
        if ws.runtime_journal(root).exists():
            self.assertGreater(ws.runtime_journal(root).stat().st_size, 0)

    def test_deferred_finding_requires_final_peer_disposition_not_owner_approval(self):
        for finding, expected in (("Deferred: final non-author peer accepted disposition: bounded follow-up", 0),
                                  ("Deferred: pending owner decision", 1)):
            root = self.synthetic()
            self.assertEqual(self.activate(root, claim="src/a")[0], 0)
            response = type("R", (), {"stdout": json.dumps({"number":1, "url":"https://github.com/o/r/pull/1",
                "state":"OPEN", "isDraft":False, "author":{"login":"author", "id":"U_kgDODXRwzA", "is_bot":False},
                "headRefOid":"c" * 40, "mergeCommit":None, "reviewDecision":None})})()
            before = {p.relative_to(root).as_posix(): p.read_bytes() for p in root.rglob("*") if p.is_file()}
            with patch("subprocess.run", return_value=response):
                code, output = self.operation(root, "handoff", id="A", execution_id="exec-a",
                    pr="https://github.com/o/r/pull/1", head="c" * 40, peer="reviewer", epoch="1", finding=[finding])
            self.assertEqual(code, expected, output)
            if expected:
                self.assertEqual(before, {p.relative_to(root).as_posix(): p.read_bytes() for p in root.rglob("*") if p.is_file()})

    def test_peer_change_requires_durable_availability_and_replacement_eligibility_evidence(self):
        root = self.synthetic()
        self.assertEqual(self.activate(root, claim="src/a")[0], 0)
        def pr(head):
            return type("R", (), {"stdout": json.dumps({"number":1, "url":"https://github.com/o/r/pull/1",
                "state":"OPEN", "isDraft":False, "author":{"login":"author", "id":"U_kgDODXRwzA", "is_bot":False},
                "headRefOid":head, "mergeCommit":None, "reviewDecision":None})})()
        with patch("subprocess.run", return_value=pr("c" * 40)):
            self.assertEqual(self.operation(root, "handoff", id="A", execution_id="exec-a", pr="https://github.com/o/r/pull/1",
                head="c" * 40, peer="reserved", epoch="1", finding=["Fixed: check"])[0], 0)
        self.assertEqual(ws.transition(root, type("A", (), {"id":"A", "state":"ResolvingFindings", "reason":"repair",
            "select":False, "select_id":None, "check":False, "dry_run":False})()), 0)
        common = dict(id="A", execution_id="exec-a", pr="https://github.com/o/r/pull/1", head="d" * 40,
                      observed_head="spoofed", observed_author="spoofed", peer="replacement", epoch="2",
                      finding=["Deferred: final non-author peer accepted disposition: bounded follow-up"], allow_peer_change=True,
                      next_action=None)
        with patch("subprocess.run", return_value=pr("d" * 40)):
            rejected, _ = self.operation(root, "handoff", **common)
        self.assertNotEqual(rejected, 0)
        args = type("Args", (), dict(repository="o/r", **common,
            peer_change_reason="reserved reviewer factually unavailable",
            peer_change_evidence="recorded availability notice: reviewer unavailable",
            replacement_eligibility="replacement is untouched non-author human"))()
        with patch("subprocess.run", return_value=pr("d" * 40)):
            accepted = ws.handoff(root, args)
        self.assertEqual(accepted, 0)
        stored = json.loads((root / "docs/project/work-items/A.json").read_text(encoding="utf-8"))
        self.assertEqual(stored["review"]["peer"], "replacement")
        self.assertEqual(stored["review"].get("peerChangeReason"), "reserved reviewer factually unavailable")
        self.assertTrue(stored["review"].get("peerChangeEvidence"))
        self.assertTrue(stored["review"].get("replacementEligibility"))

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
        for malformed_head in (None, "", "not-a-sha", "c" * 39, "C" * 40):
            candidate = self.synthetic(); self.assertEqual(self.activate(candidate, execution_id="e", claim="src/a")[0], 0)
            before = {p.relative_to(candidate).as_posix(): p.read_bytes() for p in candidate.rglob("*") if p.is_file()}
            options = dict(base, head=malformed_head, pr="https://github.com/o/r/pull/1")
            with patch("subprocess.run") as run:
                rejected, _ = self.operation(candidate, "handoff", **options)
            self.assertNotEqual(rejected, 0, malformed_head); run.assert_not_called()
            self.assertEqual(before, {p.relative_to(candidate).as_posix(): p.read_bytes() for p in candidate.rglob("*") if p.is_file()})
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
        repaired_findings = ["Fixed: final verification", "Deferred: final non-author peer accepted disposition: bounded follow-up"]
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
        for persisted in ("SRC/A", "src\\a", "src/./a", "src//a"):
            candidate = __import__("copy").deepcopy(baseline); candidate["claims"] = [{"kind":"path", "value":persisted}]
            self.assertTrue(ws.validate_items([candidate], root), persisted)
        collision = __import__("copy").deepcopy(baseline); collision["claims"] = [{"kind":"path", "value":"src/a"}, {"kind":"path", "value":"SRC\\a"}]
        self.assertTrue(ws.validate_items([collision], root))
        canonical = __import__("copy").deepcopy(baseline); canonical["claims"] = [{"kind":"path", "value":"src/a"}]
        self.assertEqual(ws.validate_items([canonical], root), [])
        former = __import__("copy").deepcopy(baseline)
        former["lifecycle"], former["lifecycleLabel"], former["selectedForExecution"] = "Blocked", "blocked", False
        former["takeover"] = {"mode":"two-peer", "authorizationHead":"1"*40, "authorizationReceipts":[{"url":"https://github.com/o/r/issues/57#issuecomment-1", "authorizationDigest":"a"*64, "authorizedBy":"worker-one"}, {"url":"https://github.com/o/r/issues/57#issuecomment-2", "authorizationDigest":"b"*64, "authorizedBy":"worker-two"}], "reason":"recorded state", "startHead":"1"*40}
        former_capsule = (root / former["checkpointPath"] / "checkpoint.md").read_text(encoding="utf-8").replace("`NotStarted`", "`Blocked`")
        self.assertTrue(ws.validate_items([former], root, {former["checkpointPath"]: former_capsule}))
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
        real_items = ws.load(ROOT)
        canonical = next(x for x in real_items if x["id"] == "GH-57")
        disk_item = json.loads((ROOT / "docs/project/work-items/GH-57.json").read_text(encoding="utf-8"))
        self.assertEqual(disk_item["owner"], canonical["owner"])
        self.assertEqual(disk_item["branch"], canonical["branch"])
        self.assertEqual(disk_item["dependencies"], canonical["dependencies"])
        self.assertEqual(disk_item["lifecycleLabel"], ws.LABELS.get(disk_item["lifecycle"]))
        execution = json.loads((ROOT / "docs/project/execution.json").read_text(encoding="utf-8"))
        self.assertEqual(execution, ws.execution_object(real_items))
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
        capsule = root / "docs/work/checkpoints/A/checkpoint.md"
        capsule.write_text(capsule.read_text(encoding="utf-8").replace("`NotStarted`", "`Building`"), encoding="utf-8")
        capsule = root / "docs/work/checkpoints/A/checkpoint.md"
        capsule.write_text(capsule.read_text(encoding="utf-8").replace("`NotStarted`", "`Building`"), encoding="utf-8")
        with patch("subprocess.run", return_value=type("R", (), {"stdout": json.dumps([{"number": 57, "state": "OPEN", "labels": [{"name": "ready"}]}])})()):
            code, output = self.operation(root, "sync-github", dry_run=True)
        self.assertEqual(code, 0, output)
        self.assertIn("--remove-label ready --add-label active", output)
        item["lifecycle"], item["lifecycleLabel"], item["expectedGithubState"] = "Closed", None, "CLOSED"
        item_path.write_text(ws.dump(item), encoding="utf-8")
        capsule.write_text(capsule.read_text(encoding="utf-8").replace("`Building`", "`Closed`"), encoding="utf-8")
        with patch("subprocess.run", return_value=type("R", (), {"stdout": json.dumps([{"number": 57, "state": "CLOSED", "labels": [{"name": "active"}]}])})()):
            code, output = self.operation(root, "sync-github", dry_run=True)
        self.assertEqual(code, 0, output)
        self.assertIn("--remove-label active", output)

    def test_closeout_requires_receipts_identity_and_resolved_findings_and_promotes_only_ready_dependents(self):
        root = self.synthetic(second=True, dependency=True); old = root / "docs/project/work-items/A.json"; old.rename(root / "docs/project/work-items/GH-57.json"); (root / "docs/work/checkpoints/A").rename(root / "docs/work/checkpoints/G57-TPO"); path = root / "docs/project/work-items/GH-57.json"; item = json.loads(path.read_text()); item.update(id="GH-57", kind="github-issue", number=57, checkpointId="G57-TPO", checkpointPath="docs/work/checkpoints/G57-TPO", sourceUrl="https://github.com/o/r/issues/57", pr="https://github.com/o/r/pull/110", expectedGithubState="OPEN"); path.write_text(ws.dump(item)); dep = root / "docs/project/work-items/B.json"; value = json.loads(dep.read_text()); value["dependencies"]=["GH-57"]; dep.write_text(ws.dump(value)); self.assertEqual(ws.validate(root), 0)
        execution = "GH-57:G57-TPO"; h1, h2, merge = "d"*40, "e"*40, "f"*40; self.assertEqual(self.activate(root, item="GH-57", execution_id=execution, claim="src/a", current_branch="feature/a", worktree_id="worktree-gh57")[0], 0)
        pr = {"number":110,"url":"https://github.com/o/r/pull/110","state":"OPEN","isDraft":False,"author":{"login":"Actual-Author","id":"U_author","is_bot":False,"name":"Author"},"headRefOid":h1,"mergeCommit":None,"reviewDecision":"APPROVED"}; calls=[]
        def handoff_binding_rejection(item_changes, repository="other/r", supplied_pr="https://github.com/other/r/pull/110"):
            candidate = Path(tempfile.mkdtemp(dir=self.d)); shutil.copytree(root, candidate, dirs_exist_ok=True)
            item_path = candidate / "docs/project/work-items/GH-57.json"; item_value = json.loads(item_path.read_text())
            for key, value in item_changes.items():
                if value is None: item_value.pop(key, None)
                else: item_value[key] = value
            item_path.write_text(ws.dump(item_value))
            before = {q.relative_to(candidate).as_posix(): q.read_bytes() for q in candidate.rglob("*") if q.is_file()}
            with patch("subprocess.run") as api:
                rejected, _ = self.operation(candidate, "handoff", id="GH-57", execution_id=execution, pr=supplied_pr, head=h1, observed_head="x", observed_author="x", peer="pEeR", epoch="1", finding=["Fixed: check"], repository=repository)
            self.assertNotEqual(rejected, 0, item_changes); api.assert_not_called()
            self.assertEqual(before, {q.relative_to(candidate).as_posix(): q.read_bytes() for q in candidate.rglob("*") if q.is_file()})
        handoff_binding_rejection({"pr":"https://github.com/other/r/pull/110"})
        handoff_binding_rejection({"pr":"https://github.com/other/r/pull/110", "sourceUrl":"malformed"})
        handoff_binding_rejection({"pr":"https://github.com/other/r/pull/110", "sourceUrl":None})
        handoff_binding_rejection({"pr":"https://github.com/other/r/pull/110", "sourceUrl":"https://github.com/o/r/issues/58"})
        def run(command, *args, **kwargs):
            text=" ".join(command); calls.append(text); self.assertNotIn("issues/comments", text)
            if "reviews" in text: return type("R",(),{"stdout":json.dumps([[{"user":{"login":"Peer","type":"User"},"state":"APPROVED","commit_id":h1}]]),"returncode":0})()
            merged = len([x for x in calls if "pull/110" in x]) > 1
            return type("R",(),{"stdout":json.dumps(dict(pr, state="MERGED" if merged else "OPEN", headRefOid=h2 if merged else h1, mergeCommit={"oid":merge} if merged else None)),"returncode":0})()
        with patch("subprocess.run", side_effect=run):
            self_review_before = {q.relative_to(root).as_posix(): q.read_bytes() for q in root.rglob("*") if q.is_file()}
            self.assertNotEqual(self.operation(root, "handoff", id="GH-57", execution_id=execution, pr=pr["url"], head=h1, observed_head="x", observed_author="x", peer="actual-author", epoch="1", finding=["Fixed: check"])[0], 0)
            self.assertEqual(self_review_before, {q.relative_to(root).as_posix(): q.read_bytes() for q in root.rglob("*") if q.is_file()})
            self.assertEqual(self.operation(root, "handoff", id="GH-57", execution_id=execution, pr=pr["url"], head=h1, observed_head="x", observed_author="x", peer="pEeR", epoch="1", finding=["Fixed: check"], next_action="Obtain one latest-head non-author peer review.")[0], 0)
        reviewed = json.loads(path.read_text()); self.assertEqual(reviewed["review"]["author"], "Actual-Author"); self.assertEqual(reviewed["review"]["peer"], "pEeR"); reviewed["resume"]={"reason":"worker resume","startHead":h1}; path.write_text(ws.dump(reviewed))
        self.assertEqual(reviewed["nextAction"], "Obtain one latest-head non-author peer review.")
        casing_item = __import__("copy").deepcopy(reviewed); casing_item["reviewPeer"] = "PEER"; casing_item["review"]["peer"] = "pEeR"; self.assertEqual(ws.validate_items([casing_item], root), [])
        different_item = __import__("copy").deepcopy(reviewed); different_item["reviewPeer"] = "different"; different_item["review"]["peer"] = "pEeR"; self.assertTrue(ws.validate_items([different_item], root))
        self_review_item = __import__("copy").deepcopy(reviewed); self_review_item["review"]["author"] = "Peer"; self_review_item["review"]["peer"] = "pEeR"; self.assertTrue(ws.validate_items([self_review_item], root))
        base = dict(id="GH-57", execution_id=execution, findings="resolved", focused_receipt="focused", final_receipt="final", attribution="actual-author", repository="o/r", pr=pr["url"], head=h2, peer="PEER", merge_sha=merge)
        def fresh_rejection(changes=None, review_value=None, **arguments):
            candidate = Path(tempfile.mkdtemp(dir=self.d)); shutil.copytree(root, candidate, dirs_exist_ok=True)
            candidate_path = candidate / "docs/project/work-items/GH-57.json"
            before = {q.relative_to(candidate).as_posix(): q.read_bytes() for q in candidate.rglob("*") if q.is_file()}
            observation = dict(pr, state="MERGED", headRefOid=h2, mergeCommit={"oid":merge})
            if changes:
                for key, value in changes.items():
                    if value is None: observation.pop(key, None)
                    else: observation[key] = value
            def case_run(command, *args, **kwargs):
                text = " ".join(command); self.assertNotIn("issues/comments", text)
                if "reviews" in text:
                    value = review_value if review_value is not None else [[{"user":{"login":"Peer","type":"User"},"state":"APPROVED","commit_id":h2}]]
                else:
                    value = observation
                return type("R",(),{"stdout":json.dumps(value),"returncode":0})()
            with patch("subprocess.run", side_effect=case_run) as api:
                rejected, _ = self.operation(candidate, "closeout", **dict(base, **arguments))
            self.assertNotEqual(rejected, 0, arguments or changes or review_value)
            self.assertEqual(before, {q.relative_to(candidate).as_posix(): q.read_bytes() for q in candidate.rglob("*") if q.is_file()})
            self.assertTrue(all("issues/comments" not in str(call) for call in api.call_args_list))
        def canonical_binding_rejection(item_changes, repository="other/r", supplied_pr="https://github.com/other/r/pull/110"):
            candidate = Path(tempfile.mkdtemp(dir=self.d)); shutil.copytree(root, candidate, dirs_exist_ok=True)
            item_path = candidate / "docs/project/work-items/GH-57.json"; item_value = json.loads(item_path.read_text()); item_value.update(item_changes); item_path.write_text(ws.dump(item_value))
            before = {q.relative_to(candidate).as_posix(): q.read_bytes() for q in candidate.rglob("*") if q.is_file()}
            with patch("subprocess.run") as api:
                rejected, _ = self.operation(candidate, "closeout", **dict(base, repository=repository, pr=supplied_pr))
            self.assertNotEqual(rejected, 0, item_changes); api.assert_not_called()
            self.assertEqual(before, {q.relative_to(candidate).as_posix(): q.read_bytes() for q in candidate.rglob("*") if q.is_file()})
        canonical_binding_rejection({"pr":"https://github.com/other/r/pull/110"})
        canonical_binding_rejection({"pr":"https://github.com/other/r/pull/110", "sourceUrl":"malformed"})
        canonical_binding_rejection({"pr":"https://github.com/other/r/pull/110", "sourceUrl":None})
        canonical_binding_rejection({"pr":"https://github.com/other/r/pull/110", "sourceUrl":"https://github.com/o/r/issues/58"})
        for changes in ({"state":"OPEN"}, {"state":"CLOSED"}, {"headRefOid":h1}, {"mergeCommit":{"oid":"a"*40}}, {"author":{"login":"Other","id":"U_other","is_bot":False,"name":"Other"}}, {"number":None}, {"state":None}, {"isDraft":None}, {"author":None}, {"headRefOid":None}, {"mergeCommit":None}, {"reviewDecision":None}):
            fresh_rejection(changes)
        for review_value in ([{"bad":"outer"}], [["bad"]], [[{"user":{"login":"Peer","type":"User"},"state":"APPROVED","commit_id":h1}]], [[{"user":{"login":"Other","type":"User"},"state":"APPROVED","commit_id":h2}]], [[{"user":{"login":"Peer","type":"User"},"state":"COMMENTED","commit_id":h2}]], [[{"user":{"login":"Peer","type":"Bot"},"state":"APPROVED","commit_id":h2}]], [[{"user":{"login":"Peer","type":"App"},"state":"APPROVED","commit_id":h2}]], [[{"user":{"login":"Peer"},"state":"APPROVED","commit_id":h2}]], [[{"user":{"login":7,"type":"User"},"state":"APPROVED","commit_id":h2}]], [[{"user":{"login":"Peer","type":"User"},"state":"APPROVED","commit_id":7}]]):
            fresh_rejection(review_value=review_value)
        for field in ("number", "state", "isDraft", "author", "headRefOid", "mergeCommit", "reviewDecision"):
            fresh_rejection({field: None})
        fresh_rejection(repository="other/r")
        fresh_rejection(attribution="different-account")
        fresh_rejection(peer="different-account")
        def stored_findings_case(stored, requested, expected_code):
            candidate = Path(tempfile.mkdtemp(dir=self.d)); shutil.copytree(root, candidate, dirs_exist_ok=True)
            item_path = candidate / "docs/project/work-items/GH-57.json"; item_value = json.loads(item_path.read_text()); item_value["reviewFindings"] = [stored]; item_value["review"]["findings"] = [stored]; item_path.write_text(ws.dump(item_value))
            before = {q.relative_to(candidate).as_posix(): q.read_bytes() for q in candidate.rglob("*") if q.is_file()}; calls=[]
            def fixed_run(command, *args, **kwargs):
                calls.append(" ".join(command))
                if any("reviews" in part for part in command): value = [[{"user":{"login":"Peer","type":"User"},"state":"APPROVED","commit_id":h2}]]
                else: value = dict(pr, state="MERGED", headRefOid=h2, mergeCommit={"oid":merge})
                return type("R",(),{"stdout":json.dumps(value),"returncode":0})()
            with patch("subprocess.run", side_effect=fixed_run): code, output = self.operation(candidate, "closeout", **dict(base, findings=requested))
            self.assertEqual(code, expected_code, (stored, requested, output)); self.assertTrue(all("issues/comments" not in call for call in calls))
            if expected_code != 0: self.assertEqual(before, {q.relative_to(candidate).as_posix(): q.read_bytes() for q in candidate.rglob("*") if q.is_file()}); self.assertEqual(calls, [])
        stored_findings_case("Fixed: independently verified", "none", 1)
        stored_findings_case("Rejected: evidence retained", "none", 1)
        stored_findings_case("Deferred: final non-author peer accepted disposition: ledger entry", "none", 1)
        stored_findings_case("Rejected: evidence retained", "resolved", 0)
        stored_findings_case("Deferred: final non-author peer accepted disposition: ledger entry", "resolved", 0)
        for bad in (dict(findings="open"), dict(pr="https://github.com/o/r/pull/109"), dict(head=h1), dict(merge_sha="a"*40), dict(attribution="other")):
            before={q.relative_to(root).as_posix():q.read_bytes() for q in root.rglob("*") if q.is_file()}
            with patch("subprocess.run", side_effect=run): self.assertNotEqual(self.operation(root, "closeout", **dict(base, **bad))[0], 0)
            self.assertEqual(before, {q.relative_to(root).as_posix():q.read_bytes() for q in root.rglob("*") if q.is_file()})
        def final_run(command, *args, **kwargs):
            text = " ".join(command); calls.append(text); self.assertNotIn("issues/comments", text)
            if "reviews" in text:
                value = [[{"user":{"login":"Other","type":"User"},"state":"COMMENTED","commit_id":h1}], [{"user":{"login":"Peer","type":"User"},"state":"APPROVED","commit_id":h2}]]
            else:
                value = dict(pr, state="MERGED", headRefOid=h2, mergeCommit={"oid":merge})
            return type("R",(),{"stdout":json.dumps(value),"returncode":0})()
        with patch("subprocess.run", side_effect=final_run) as api:
            code, packet = self.operation(root, "closeout", **base)
        self.assertEqual(code, 0, packet); closed=json.loads(path.read_text()); self.assertEqual(closed["id"],"GH-57"); self.assertEqual(closed["closeout"]["attribution"],"Actual-Author"); self.assertEqual(closed["resume"], {"reason":"worker resume","startHead":h1}); self.assertNotIn("review", closed); self.assertNotIn("takeover", closed); self.assertNotIn("executionId", closed); self.assertNotIn("worktreeId", closed); self.assertEqual(closed["claims"], []); self.assertEqual(closed["closeout"]["head"],h2); self.assertEqual(closed["closeout"]["mergeSha"],merge); self.assertNotEqual(json.loads(dep.read_text())["lifecycle"], "Ready"); self.assertEqual(api.call_count, 2)
        self.assertEqual(self.operation(root, "promote", id="B")[0], 0); self.assertEqual(json.loads(dep.read_text())["lifecycle"], "Ready")
        for field in ("number", "state", "isDraft", "author", "headRefOid", "mergeCommit", "reviewDecision"):
            candidate=self.synthetic(second=True); self.assertNotEqual(self.operation(candidate, "closeout", id="A", execution_id="x", findings="resolved", focused_receipt="f", final_receipt="g", attribution="a", pr="https://github.com/o/r/pull/1", head="a"*40, peer="p", merge_sha="b"*40)[0], 0)
    def test_github_projection_is_dry_run_idempotent_permission_aware_and_preserves_unrelated_labels(self):
        root = self.synthetic()
        item_path = root / "docs/project/work-items/A.json"
        item = json.loads(item_path.read_text(encoding="utf-8"))
        item.update(kind="github-issue", number=1, sourceUrl="https://github.com/o/r/issues/1",
                    expectedGithubState="OPEN")
        item_path.write_text(ws.dump(item), encoding="utf-8")
        self.assertEqual(ws.validate(root), 0)
        drift = json.dumps([{"number": 1, "state": "OPEN", "labels": [{"name": "blocked"}, {"name": "keep"}], "comments": []}])
        with patch("subprocess.run", return_value=type("R", (), {"stdout": drift})()) as run:
            code, output = self.operation(root, "project", dry_run=True)
        self.assertEqual(code, 0, output)
        self.assertEqual(run.call_count, 1)
        self.assertNotIn("--remove-label keep", output)
        self.assertIn("--add-label ready", output)
        self.assertIn("DRY-RUN", output)
        remote = json.dumps([{"number": 1, "state": "OPEN", "labels": [{"name": "ready"}, {"name": "keep"}], "comments": []}])
        with patch("subprocess.run", return_value=type("R", (), {"stdout": remote})()) as run:
            self.assertEqual(self.operation(root, "project")[0], 0)
            self.assertEqual(run.call_count, 2)
        with patch("subprocess.run", side_effect=PermissionError("forbidden")):
            self.assertNotEqual(self.operation(root, "project")[0], 0)
        drift = json.dumps([{"number": 1, "state": "OPEN", "labels": [{"name": "blocked"}], "comments": []}])
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
        capsule = root / "docs/work/checkpoints/A/checkpoint.md"
        capsule.write_text(capsule.read_text(encoding="utf-8").replace("`NotStarted`", "`Building`"), encoding="utf-8")
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
        for comments in (None, {}, "comment", [{}], [{"body": 7}], [{"id": 1}]):
            malformed = json.dumps([{"number": 1, "state": "OPEN", "labels": [], "comments": comments}])
            with patch("subprocess.run", return_value=type("R", (), {"stdout": malformed})()) as run:
                code, output = self.operation(root, "project")
            self.assertNotEqual(code, 0, comments)
            self.assertEqual(run.call_count, 1, comments)
        ordering = self.synthetic()
        ordering_item = ordering / "docs/project/work-items/A.json"; ordering_value = json.loads(ordering_item.read_text()); ordering_value.update(kind="github-issue", number=1, sourceUrl="https://github.com/o/r/issues/1", expectedGithubState="OPEN"); ordering_item.write_text(ws.dump(ordering_value))
        remote = json.dumps([{"number":1,"state":"OPEN","labels":[],"comments":[]}]); calls=[]
        def ordered(command, *args, **kwargs):
            calls.append(command)
            if len(calls) == 1: return type("R",(),{"stdout":remote})()
            return type("R",(),{"stdout":""})()
        with patch("subprocess.run", side_effect=ordered):
            self.assertEqual(self.operation(ordering, "project")[0], 0)
        edits = [" ".join(command) for command in calls]
        self.assertLess(next(i for i, command in enumerate(edits) if "issue edit" in command), next(i for i, command in enumerate(edits) if "issue comment" in command))
        calls.clear()
        with patch("subprocess.run", side_effect=[type("R",(),{"stdout":remote})(), OSError("edit failed")]) as run:
            self.assertNotEqual(self.operation(ordering, "project")[0], 0)
        self.assertEqual(run.call_count, 2)

    def test_real_registry_and_checked_in_projection_are_read_only(self):
        registry = {p: p.read_bytes() for p in (ROOT / "docs/project/work-items").glob("*.json")}
        projection = (ROOT / "docs/project/execution.json").read_bytes()
        self.assertEqual(ws.validate(ROOT), 0)
        self.assertEqual(ws.execution(ROOT, True), 0)
        self.assertEqual(registry, {p: p.read_bytes() for p in registry})
        self.assertEqual(projection, (ROOT / "docs/project/execution.json").read_bytes())


if __name__ == "__main__": unittest.main()
