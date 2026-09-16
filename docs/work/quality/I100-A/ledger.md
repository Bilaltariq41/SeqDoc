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
| `Active` → `ReviewRequired` | PR #108 submitted for independent review |
| `ReviewRequired` → `ResolvingFindings` | Independent review at PR #108 found GH106-F1 (High severity); repair in progress |

## Independent review findings (PR #108)

| Finding | Severity | Disposition | Evidence |
|---|---|---|---|
| GH106-F1 — `WaitAsync`'s exited-process drain path has no real bound against a live descendant silently holding a pipe write handle open; `DrainPipe`'s synchronous `Read` (line ~755) is only checked for its deadline *between* reads, not during an in-flight blocked read, so a caller invoking `WaitAsync` directly (without first calling `Terminate()`) against such a descendant can hang indefinitely, contradicting the checkpoint's own "drains stdout and stderr concurrently within explicit bounds" objective and its native API admission table's "bounded by the same timeout token" claim. | High | Fixed (commit e1d1f0a) | Independent review at PR #108; confirmed by the orchestrator's own reading of `ProcessOwnership.cs:586-611,737-776`. |

Repair trace recorded in `docs/project/delegated-contribution-workflow.md` once verified.

## Final gate

Run once by the orchestrator after the post-repair review passed clean:

```
dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release
```

Result: Failed 5, Passed 70, Skipped 0, Total 75, Duration 20m15s. All 28 `ProcessOwnershipTests` are among the 70
passed (0 failures in that class). The 5 failures are pre-existing and unrelated to this checkpoint:

- `CorpusMediatRTests.OrderingDraftRouteReachesExactMediatRHandlerWithoutPipelineClaim` — `SD1102` multi-SDK
  MSBuildLocator conflict (MSBuild 10.0.302 already registered vs. SDK 10.0.400), the same pre-existing signature
  documented repeatedly in `docs/project/delegated-contribution-workflow.md` (QHTTP-B repair traces) and
  `docs/work/services/I54/checkpoint.md`.
- `ServiceClientExternalCorpusTests.PositiveLaneWordingIsEvidenceBoundedAndCredentialSafe`,
  `PositiveLanesRenderTheJoinedOutboundClientMessageExactlyOnce`,
  `ConfiguredRootsResolveAndProduceTheAcceptedDocumentSet` — external-corpus drift (`SD4011` malformed/unknown
  frozen `MethodId`, empty wording collections), the same pre-existing external-corpus-unavailable/drifted class
  documented in the same QHTTP-B history.
- `PersistenceAcceptanceTests.GetMeaningPersistenceFactsReachDiagramAndMarkdownDeterministically` — an MSBuild
  incremental-cache file collision inside the reused, non-isolated `tests/fixtures/BehaviorDocumentation/GetMeaning/obj-custom/`
  directory this test's own `BuildAsync` helper writes to; a local fixture-build artifact unrelated to this
  checkpoint's files.

Structural proof of non-causation: `git diff --name-only ab6e3e1..HEAD -- src/ tests/` touches zero `src/**` files
and only `tests/SeqDoc.AcceptanceTests.ProcessOwnershipStub/**`, `tests/SeqDoc.AcceptanceTests/ProcessOwnership.cs`,
`tests/SeqDoc.AcceptanceTests/ProcessOwnershipTests.cs`, and `tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj`
— none of the 3 failing test files this checkpoint could plausibly affect.

## Human peer review findings (Abood-essa, PR #108, head `3c87e867`)

Request-changes review against exact head `3c87e8673f66a9dd5a8c6448708719131bd050fc`. All 13 independently
confirmed by the orchestrator reading the actual code before repair began.

| Finding | Severity | Disposition |
|---|---|---|
| GH106-R2-F1 — post-construction failure unwind (StartCore lines ~303-324, 480-547) can double-close the 3 child-side pipe handles already closed at lines 498-500, and never cancels/awaits the drain tasks or completion monitor before returning, leaking background work on any assign/hook/resume failure. | High | Fixed (commit 1c23f9b) |
| GH106-R2-F2 — normal exit records no failure when `ACTIVE_PROCESS_ZERO` is never observed within its bound; the frozen failure table requires `ProcessFailed` when family exit cannot be proven. | High | Fixed (commit 1c23f9b) |
| GH106-R2-F3 — the timeout/cancellation branch calls `TerminateJobObject` but never awaits `ACTIVE_PROCESS_ZERO` before returning, so `WaitAsync` can return before the terminated family has actually finished exiting. | High | Fixed (commit 1c23f9b) |
| GH106-R2-F4 — `TerminateJobObject` failures are discarded in both `WaitAsync` and `Terminate()`; no `ProcessFailed` is recorded. | High | Fixed (commit 1c23f9b) |
| GH106-R2-F5 — `WaitForSingleObject` result `WAIT_FAILED` is mapped identically to `WAIT_TIMEOUT`, so a real wait failure is misreported as `TimedOut` instead of `ProcessFailed` with the captured `Win32Error`. | High | Fixed (commit 1c23f9b) |
| GH106-R2-F6 — the parent's stdin write handle is retained but never exposed or closed, so a child reading stdin to EOF cannot finish naturally. | High | Fixed (commit 1c23f9b) |
| GH106-R2-F7 — `Dispose()` can record `TeardownDegraded` internally, but only an internal test-only accessor can observe it; no real caller (including #107) has a way to learn teardown failed. | High | Fixed (commit 1c23f9b) |
| GH106-R2-F8 — embedded NUL and malformed environment-name entries are not rejected before native marshaling, which can silently truncate them while construction still reports success. | Medium | Fixed (commit 1c23f9b) |
| GH106-R2-F9 — `BuildEnvironmentBlock` dedups with `StringComparer.Ordinal`, so case-variant Windows environment names (e.g. `Path` vs `PATH`) are not deduplicated despite Windows treating them as the same variable. | Medium | Fixed (commit 1c23f9b) |
| GH106-R2-F10 — `DrainPipe` decodes each independent 64 KiB read chunk with `Encoding.UTF8.GetString`, so a multibyte UTF-8 sequence split across a read boundary can be corrupted while `Truncated` still reports `false`. | Medium | Fixed (commit 1c23f9b) |
| GH106-R2-F11 — `Dispose()`'s close order does not match true reverse-acquisition order (job/IOCP close before pipes; command-line/environment buffers close last instead of matching their early acquisition), despite the code comment claiming exact reverse order. | Medium | Fixed (commit 1c23f9b) |
| GH106-R2-F12 — the assign-before-resume chronology proof uses an in-process test hook plus `IsProcessInJob`, not the observable stub-receipt ordering the checkpoint originally specified. | Medium | Fixed (commit 1c23f9b) |
| GH106-R2-F13 — the checkpoint's declared invalid-NUL and environment-dedup vectors are not yet exercised by any test. | Medium | Fixed (commit 1c23f9b) |

### Repair trace (PR #108, head `3c87e867` → repair round GH106-R2)

All 13 findings repaired on the same branch in one coherent batch (Abood's explicit instruction 1 — no partial
candidate submitted). Changed paths: `tests/SeqDoc.AcceptanceTests.ProcessOwnershipStub/Program.cs`,
`tests/SeqDoc.AcceptanceTests/ProcessOwnership.cs`, `tests/SeqDoc.AcceptanceTests/ProcessOwnershipTests.cs`, this
ledger, and `checkpoint.md`.

- **F1**: the three manual `CloseHandle` calls (S4) and their corresponding unwind closures now share a
  `CloseIfOpen(ref nint)` guard (zero-after-close), eliminating the double-close hazard. A new unwind entry pushed
  immediately after `StartDrains`/`StartCompletionMonitor` terminates the process directly and best-effort awaits
  (5s bound each) the completion monitor and both drain tasks, replacing the two now-redundant inline
  `_completionMonitorCts?.Cancel()` calls. New `ConstructionFaultPoint.AfterDrainsStartedBeforeAssign` plus a
  `PostDrainsStartHookForTests` capture seam. Proof:
  `PostDrainsUnwindFaultCancelsAndAwaitsBackgroundDrainsAndCompletionMonitor`, plus the fault point added to
  `EachConstructionFaultPointUnwindsOnlyWhatWasAcquired`.
- **F2**: `WaitAsync`'s `exited == true` branch now records `ProcessFailed` when `!_activeProcessZeroObserved` after
  `WaitForActiveProcessZero`. New test-only `ActiveProcessZeroBoundForTests` setter and
  `StopCompletionMonitorForTests()` seam (never reachable from production — no public setter). Proof:
  `UnprovenActiveProcessZeroOnNormalExitRecordsProcessFailed`. Consequence: the pre-existing
  `WaitAsyncDirectlyAgainstLiveGrandchildDrainsWithinBoundInsteadOfHanging` test's expected failure class changed
  from `DrainIncomplete` to `ProcessFailed` — per the frozen precedence table `ProcessFailed` (3) legitimately
  outranks `DrainIncomplete` (4), and both now-simultaneous facts share the same root cause (the live descendant);
  first-write-wins correctly surfaces the more fundamental one.
- **F3**: the `!exited` branch now awaits `WaitForActiveProcessZero(CancellationToken.None)` after forced
  `TerminateJobObject` (a fresh token, since `linked` is already cancelled at that point) and records `ProcessFailed`
  if still unproven — never overwriting the already-recorded `TimedOut`/cancellation class. Proof:
  `TimeoutBranchAwaitsFamilyExitAfterForcedTermination`.
- **F4**: `TerminateJobObject`'s return value is now checked at all three call sites (`WaitAsync`'s timeout branch,
  its drain-deadline branch, and `Terminate()`), recording `ProcessFailed` with the captured `Win32Error` on
  failure. New `TerminateJobObjectOverrideForTests` seam (indirected through `InvokeTerminateJobObject`). Proof:
  `TerminateJobObjectFailureRecordsProcessFailedWithoutOverwritingEarlierTimedOut`.
- **F5**: `WaitForSingleProcessExit` now returns `(Exited, WaitFailed, Win32Error)` instead of a bare `bool`,
  distinguishing `WAIT_OBJECT_0`/`WAIT_TIMEOUT`/`WAIT_FAILED`-or-`WAIT_ABANDONED`. New
  `WaitForSingleObjectOverrideForTests` seam (indirected through `InvokeWaitForSingleObject`). Proof:
  `WaitForSingleObjectFailureRecordsProcessFailedDistinctFromGenuineTimeout`.
- **F6**: the parent's stdin write handle now closes immediately after `CreateProcessW` succeeds (same site as the
  other unused parent-side handles), giving an immediate EOF to any stdin-reading child; `_parentStdInWrite` stays a
  Dispose-tracked field but is already zero by construction (no interactive-stdin support in this checkpoint's
  scope). New stub command `read-stdin-to-eof`. Proof: `StdinIsClosedImmediatelySoChildReadingToEofCompletesWithoutHanging`.
- **F7**: new public `ContainedProcess.FailureClass` and `TeardownFailures` properties proxy the same internal
  tracker/list the prior test-only accessors exposed; both old and new accessors are kept. Proof (genuine
  `CloseHandle` failure, no mock seam needed — the process handle is closed out from under `Dispose()` before it
  runs): `TeardownDegradedIsObservableThroughPublicSurfaceAfterDispose`.
- **F8/F13**: new `ValidateVectors` rejects an embedded NUL in the executable path, any argument, or any
  environment name/value, and rejects `=` in an environment-variable name — called from `Start()` before the
  rooted/exists check and before `StartCore` (so a rejected input never reaches any native call). Proof:
  `MalformedVectorsFailClosedBeforeReachingNativeConstruction` (5 vectors).
- **F9/F13**: `BuildEnvironmentBlock`'s dedup/sort comparer changed from `StringComparer.Ordinal` to
  `StringComparer.OrdinalIgnoreCase`. Proof (encoding-layer unit test, the least expensive reliable layer for a
  pure string-transform claim): `EnvironmentBlockDedupIsCaseInsensitiveLastWriteWinsAndWellFormed`.
- **F10**: `DrainPipe` now holds one `Decoder` (`Encoding.UTF8.GetDecoder()`) across the whole read loop instead of
  calling `Encoding.UTF8.GetString` independently per 64 KiB chunk, flushing the decoder at EOF. New stub command
  `utf8-boundary` writes exactly 65535 filler bytes then a 3-byte UTF-8 character, so its first byte lands at offset
  65535. The proof test also sets a new test-only `ContainedProcess.PipeBufferSizeOverrideForTests` seam (consumed
  by `CreatePipePair`, defaulting to `0`/system-default in every other caller) large enough to hold the whole
  payload, so the pipe buffers it atomically before the parent's first read — guaranteeing by construction, not just
  usually in practice, that the multibyte character is actually split exactly across `DrainPipe`'s real 64 KiB read
  boundary. Proof: `Utf8MultibyteCharacterStraddling64KiBReadBoundaryDecodesCorrectly`.
- **F11**: `Dispose()`'s close order corrected to true exact reverse acquisition order: process handle, thread
  handle, environment-block buffer, command-line buffer, completion port handle, job handle, attribute-list buffer,
  handle-list buffer, stderr pipe, stdout pipe, stdin pipe (see `checkpoint.md` for the full derivation).
  `CloseTracked`/`FreeTracked` now record an ordered label trace; a new `UnwindStepObserverForTests` seam records
  the same trace for the partial-construction unwind path. Proof: `DisposeClosesResourcesInExactTrueReverseAcquisitionOrder`
  (full disposal) and `PartialConstructionUnwindClosesResourcesInExactTrueReverseAcquisitionOrder` (one S0-S3 fault
  point, per Abood's explicit requirement to cover partial teardown order too).
- **F12**: new stub command `report-job-membership` calls a plain (`AllowUnsafeBlocks`-free) `IsProcessInJob`
  P/Invoke against its own process handle as the very first action `Main` takes, printing `IN-JOB:True`/`False` as
  its first stdout line — a genuine, external, production-code-path receipt. This supplements (does not replace)
  the prior in-process `AssignBeforeResumeHookForTests` proof; both are kept. Proof:
  `AssignPrecedesResumeProvenByObservableChildReceiptFromItsOwnFirstInstruction`.

**Budget exception**: this repair round added 13 new/extended tests (one per finding, `Utf8...`/`MalformedVectors...`
etc.) against the checkpoint's original ~10-12 grouped-claim soft budget for the whole checkpoint. Reason: Abood's
review explicitly requested a dedicated proof per finding ("Prove with...") for nearly every item, and each finding
is a distinct observable failure mode (not a branch/overload/row-count variant of an existing test), so each earns
its own test under the "risk-based tests... by distinct observable failure mode" rule.

Full finding text: https://github.com/Bilaltariq41/SeqDoc/pull/108 (Abood-essa review comment). Disposition note:
Abood also flagged that the prior "final gate passed" wording overstated a 5-failures-out-of-75 result without a
frozen accepted-failure baseline or independent peer disposition; going forward the orchestrator will record the
final gate's literal outcome (exact counts) without characterizing a nonzero-failure run as "passed."

## Post-repair independent review rounds

Three additional independent review passes ran after the F1-F13 repair, each independently reconfirmed by the
orchestrator (not just trusted from the delegate report):

1. **`reviewer-medium` against complete repaired head `1c23f9b`.** Verdict: substantively sound. Independently
   re-derived (not merely re-read) the reverse-acquisition order (F11), the unwind pop-order reasoning (F1), the
   failure-precedence first-write-wins semantics (F2/F3), and test-seam isolation (F4/F5/F7) directly from the code.
   One finding: the new F10 boundary test's "exactly at the 64 KiB boundary" claim was not actually guaranteed by
   construction (`CreatePipe` used the Win32 default buffer size, far smaller than 64 KiB, so the real split point
   was governed by uncontrolled OS pipe buffering/scheduling) — not a production defect, a test-precision gap.
2. **Follow-up repair, commit `9192152`.** Added a test-only `PipeBufferSizeOverrideForTests` seam on
   `CreatePipePair` (default unchanged, `internal static`, same-assembly-only) so the boundary test can force a
   large enough pipe buffer to make the split land deterministically at the intended offset; documented the new
   `WaitAsync` worst-case `~X + 10s` latency ceiling introduced by the F3 repair in `checkpoint.md`.
3. **`reviewer-medium` against commit `9192152`.** Verdict: fix correct, documentation accurate, but the stub's
   `RunUtf8Boundary` still issued three *separate* `stdout.Write` calls (filler, then the multibyte character, then
   a marker) — not jointly atomic, leaving a narrow (though very low-probability) window where a concurrent reader
   could still observe a partial write and silently miss the boundary condition the test exists to prove. The
   underlying production fix (persistent `Decoder` in `DrainPipe`) remained correct regardless.
4. **Follow-up repair, commit `bd6440c`** (mechanical-tier, since fully specified): combined the three writes into
   one `byte[]` payload issued via a single `stdout.Write` call, making a partial-payload read structurally
   impossible rather than merely unlikely.
5. **`reviewer-low` against commit `bd6440c`.** Verdict: PASS, no issues. Independently checked the `Buffer.BlockCopy`
   offset arithmetic and confirmed payload content/ordering unchanged.

Final verification after all repairs (independently rerun by the orchestrator, not just reported by any delegate):
`dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter "FullyQualifiedName~ProcessOwnershipTests"` — **46 passed, 0 failed, 0 skipped**. The `Utf8MultibyteCharacterStraddling64KiBReadBoundaryDecodesCorrectly` test specifically reconfirmed stable across 5+3+1 = 9 total runs across the repair/review chain.

No test-only seam (`...ForTests`, `internal static Action/Func` override) is reachable from any production call path;
`ContainedProcess` has no `src/**` consumer in this checkpoint. Scope confirmed clean throughout: no `src/**`,
GH-93, GH-18/I18, PR #99, or PR #103 paths touched by any commit in this repair chain.

## Final gate, post-repair (literal result, not characterized)

Run once by the orchestrator after all 13 findings plus two follow-up precision fixes were repaired and
independently reverified across four review rounds:

```
dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release
```

**Failed 4, Passed 89, Skipped 0, Total 93, Duration 4m21s.** All 46 `ProcessOwnershipTests` are among the 89
passed; the failure list contains no `ProcessOwnershipTests` entries. The 4 failures are the same pre-existing,
structurally-unrelated signatures recorded on the earlier final-gate run in this file:
`CorpusMediatRTests.OrderingDraftRouteReachesExactMediatRHandlerWithoutPipelineClaim` (`SD1102` multi-SDK
MSBuildLocator conflict) and 3 `ServiceClientExternalCorpusTests` tests (external-corpus `SD4011`/empty-wording
drift). The structural proof recorded earlier in this file (`git diff --name-only ab6e3e1..HEAD -- src/ tests/`
touching zero `src/**` files and none of these failing tests' own files) still applies unchanged — this repair
chain only ever touched `ProcessOwnership.cs`, `ProcessOwnershipTests.cs`, and the stub `Program.cs`.

Observation, not a claim either way: the earlier final-gate run on this branch also showed a 5th failure,
`PersistenceAcceptanceTests.GetMeaningPersistenceFactsReachDiagramAndMarkdownDeterministically` (an MSBuild
incremental-cache collision inside a reused local fixture-build directory this checkpoint's files do not touch).
That failure did not recur on this run — consistent with a transient local-build artifact rather than a
deterministic regression, but recorded plainly rather than asserted with certainty.

Total test count in this project rose from 75 (prior final-gate run) to 93 (this run), a difference of exactly 18 —
matching `ProcessOwnershipTests` growing from 28 to 46 (also +18) through the F1-F13 repair. No other test in the
project changed count.

This result is not characterized as "passed" — 4 of 93 tests failed. The disposition is that those 4 failures are
pre-existing and unrelated to this checkpoint, evidenced structurally and by matching documented precedent, not
that the gate command exited cleanly.

## GH-106 takeover focused evidence

The declared focused command completed with literal result: `Failed 0, Passed 52, Skipped 0, Total 52` on resolved
SDK `10.0.302`. The isolated-runtime test uses only explicit `DOTNET_ROOT_X64`/`DOTNET_ROOT` values and excludes
`PATH` from the child environment. The evidence shell reported `DOTNET_ROOT=` and `DOTNET_ROOT_X64=`. No final
unfiltered gate, commit, push, lifecycle transition, or independent-review claim was made.

## GH-106 takeover review and block

The worker Reviewer inspected the complete local takeover candidate after the 52/52 focused result. Verdict:
`BLOCK` under the frozen takeover stop rule. No final gate ran and no further automatic repair is authorized.

| Finding | Severity | Disposition |
|---|---|---|
| I100-A-F1 — `Terminate()` is outside the lifecycle lock and can race `Dispose()` while the job handle is released or reused. | High | Unresolved; blocked. |
| I100-A-F2 — failed-termination and disposal paths close drain handles while managed drain tasks may still use them. | High | Unresolved; blocked. |
| I100-A-F3 — explicit `Terminate()` and disposal without `WaitAsync` do not establish bounded `ACTIVE_PROCESS_ZERO` evidence before returning or releasing resources. | High | Unresolved; blocked. |
| I100-A-F4 — construction unwind ignores false timed `Task.Wait` results and can continue releasing resources while background work remains live. | High | Unresolved; blocked. |
| I100-A-F5 — `PeekNamedPipe` failure is represented as clean EOF rather than incomplete output with native evidence. | Medium | Unresolved. |
| I100-A-F6 — failure tracking and the returned secondary-evidence list are mutable and unsynchronized across wait, terminate, and dispose races. | Medium | Unresolved. |
| I100-A-F7 — the runtime test does not prove an isolated official SDK/runtime launch, and its nonempty-root precondition conflicts with the recorded empty shell variables. | Medium | Unresolved; the 52/52 result is not accepted as proof of outcome 10. |

Scope remained inside the takeover allowlist. The worktree and both PR histories are preserved; PR #108 and Qais's
branch remain untouched. GH-107 stays blocked on an accepted contained-process contract.

## Owner recovery R3 activation

Abood's [owner-recovery decision](https://github.com/Bilaltariq41/SeqDoc/pull/109#issuecomment-5703147045)
authorized one bounded seven-finding recovery from frozen head
`952e681d2d60eecc53dfd40d81eec11b053c9f3c`. Ahmad released the implementation/path lease and accepted the
latest-head independent-review role. Canonical state transitioned `Blocked` → `Ready` → `Active`; ownership moved to
`abood`, PR #109 remained the integration branch, and I100-A was selected for tests-first execution. The transition
tool does not expose an owner-field option, so the canonical owner field was updated directly before lifecycle and
execution projections were generated and validated through `work_state.py`.

## Owner recovery R3 result and block

The Test Writer added seven grouped owner-recovery observables without editing implementation. Under isolated SDK
`10.0.302`, the resulting baseline was `Failed 6, Passed 54, Skipped 0, Total 60`: F1-F6 failed for the intended
declared gaps, F7's explicit `dotnet.exe --version` launch passed, and existing focused coverage was green.

One checkpoint-builder then changed only `tests/SeqDoc.AcceptanceTests/ProcessOwnership.cs`. Its focused run reported
`Failed 18, Passed 42, Skipped 0, Total 60`; its self-review identified unresolved High drain-lifecycle and
construction-cleanup risks. Orchestrator diff inspection confirmed the candidate was not safe to advance. No worker
Reviewer or final gate ran. Under the frozen one-attempt stop condition, canonical state returned to `Blocked` and
the tests, implementation candidate, both PR histories, and all attribution were preserved without another repair.

## Owner redesign v2 authorization

On 2026-09-16, Abood separately authorized one bounded forward redesign on PR #109 while Qais and Ahmad are
unavailable. The decision preserves blocked head `f5ceff4` and all earlier history; it does not authorize a revert,
force-push, rewrite, squash, deletion, test weakening, or scope beyond `ProcessOwnership.cs` and truthful lifecycle
evidence. Commit `2e6d3d2` remains the executable acceptance contract. The candidate must pass the isolated-SDK
focused lane and independent Reviewer-agent review, then stop at `ReviewRequired` without the final gate.

## Owner redesign v2 result and block

Two independent read-only designs first converged on a single lifecycle coordinator, one shared terminal operation,
one shared disposal operation, immutable failure snapshots, drain leases, and construction-specific cleanup. One
checkpoint-builder then changed only `tests/SeqDoc.AcceptanceTests/ProcessOwnership.cs`.

The isolated-SDK focused lane reported `Failed 3, Passed 57, Skipped 0, Total 60`, improving the preserved R3
candidate's `18/42` result but not satisfying the frozen green requirement. Remaining failures were live-grandchild
classification (`None` instead of `ProcessFailed`), missing `SLOW` prefix after injected `PeekNamedPipe` failure, and
missing expected terminal-call evidence in the descendant pipe-closure scenario. The builder also reached its work
limit before complete self-review. Orchestrator diff inspection found incomplete/dead implementation structure, so no
Reviewer or final gate ran. The v2 stop condition fired and canonical state returned to `Blocked`; candidate, history,
tests, both PRs, and all attribution remain preserved.

## Owner targeted repair v3 authorization

Abood separately authorized one final targeted repair from blocked head `839dd8e`. Scope is limited to the three
remaining focused behaviors—live-grandchild failure classification, fail-closed prefix preservation after
`PeekNamedPipe` failure, and descendant pipe-closure terminal evidence—plus removal of dead/incomplete lifecycle code
and complete self-review. Tests remain frozen. The candidate must pass 60/60 under isolated SDK `10.0.302`, pass one
independent Reviewer-agent review, and stop at `ReviewRequired` without the final gate.

## Owner targeted repair v3 result and permanent block

A read-only diagnosis isolated the three remaining control-flow defects before one checkpoint-builder changed only
`tests/SeqDoc.AcceptanceTests/ProcessOwnership.cs`. The candidate removed the dead `#if false` disposal block and
obsolete helpers, repaired live-grandchild classification, and preserved fail-closed `PeekNamedPipe` prefix/error
evidence. Its isolated-SDK focused lane reported `Failed 1, Passed 59, Skipped 0, Total 60`; the descendant pipe-closure
case still lacked required terminal-operation evidence. Orchestrator diff inspection found timing-derived lifecycle
coordination that was not ready for independent review. Under the frozen v3 condition, canonical state returned to
`Blocked` permanently pending human technical direction. No Reviewer or final gate ran; candidate, tests, history,
both PRs, and attribution remain preserved.

## Owner-directed final race fix v4 authorization

Abood reviewed the remaining descendant pipe-closure failure and authorized one exact human-directed repair from
`beb5c3a`: remove timing/fire-and-forget inference and atomically require plus await the shared terminal operation when
drains complete before family-zero proof. Tests remain frozen and scope remains `ProcessOwnership.cs` plus truthful
lifecycle evidence. The required handoff result is focused 60/60 and an independent Reviewer-agent pass, followed by
`ReviewRequired` for Ahmad without the final gate.

## v4 independent review finding

At exact head `08733dd`, independent review found one unresolved High Wait/Dispose interleaving: disposal can close
the process and job handles while the shared wait task still calls `GetExitCodeProcess` or
`QueryInformationJobObject`. The focused lane was 60/60 but did not cover this handle-lifetime signature. The finding
is accepted for one deterministic barrier-based regression and the smallest lifecycle repair; no final gate is
authorized.

## v4 repair and Reviewer result

Head `08733dd` passed the isolated-SDK focused lane 60/60 after replacing drain/completion timing heuristics with
atomic drain state and authoritative Job Object `ActiveProcesses` evidence. Independent review found one High
WaitAsync/Dispose handle-lifetime race. One deterministic barrier regression was added and produced the intended red
result: `Failed 1, Passed 60, Skipped 0, Total 61` at `11267b9`.

Repair `37b7f71` publishes `DisposalInProgress` synchronously, rejects new waits, joins an existing shared wait before
native handle release, observes wait faults, and retains process/job handles on bounded quiescence failure. The
isolated-SDK focused lane then passed `61/61`, and the complete Reviewer rerun reported `PASS - NO ISSUES DETECTED`;
the prior High finding is Fixed. No final gate ran. Exact head
`37b7f71597c2a4bbd4effc87df70fad18519cc89` is ready for Ahmad's latest-head human review.
