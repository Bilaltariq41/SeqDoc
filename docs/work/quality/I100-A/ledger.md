# I100-A decision ledger

Additive-only record of governance decisions made while establishing this checkpoint. See `checkpoint.md` for the
full frozen contract.

## Readiness freeze

Actor: root Orchestrator, acting on Qais's (`Qhatahet`, assignee of #106) explicit direction.

| # | Decision | Resolution | Evidence |
|---|---|---|---|
| 1 | Windows/platform floor | Windows x64 only; ARM64 out of scope for this checkpoint; no separate SKU/version floor beyond the pinned `net10.0` SDK's own Windows minimum. | Issue #106 body point 8 (frozen — contradicts the readiness packet's provisional x64+ARM64 table); `CreateProcessW`/`CreateJobObjectW`/job-object completion-port association/`TerminateJobObject` have been available since Windows XP/Server 2003. |
| 2 | Executable-path resolution | Caller supplies a fully resolved, rooted, existing path; the primitive performs zero PATH/cwd search; fails closed (`ProcessConstructionFailed`) otherwise. | Readiness packet open item 2 (https://github.com/Bilaltariq41/SeqDoc/issues/106#issuecomment-5694865856); closes the fixture-controlled-cwd risk raised during the #100 review. |
| 3 | `AllowUnsafeBlocks` placement | Set only on `tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj`; not on the `ProcessOwnershipStub` executable project. | Readiness packet open item 3; the stub project needs no native interop. |

No test, path, command, or non-goal in the frozen issue body changed. This freeze is a readiness-audit action under
`docs/project/issue-readiness.md` (no separate readiness PR/human approval required to reach `Ready`), not a
post-`Ready` T2 amendment.

## Lifecycle transitions

| Transition | Result |
|---|---|
| (created) → `Draft` | `docs/project/work-items/GH-106.json` created |
| `Draft` → `Ready` | Contract frozen at baseline `ab6e3e1cf16213ee5346506b16949fa32c4ddfa4`; `checkpointId=I100-A` |
| `Ready` → `Active` | Selected for root-Orchestrator execution; implementation begins |
