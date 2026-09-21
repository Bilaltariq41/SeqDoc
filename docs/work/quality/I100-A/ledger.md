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

## Ahmad latest-head review findings

Ahmad reviewed PR head `a0d3ae175b6f14aa33cf2b6c28d7cac4b031287e` and returned `BLOCK`: construction
unwind can strand an uncontained suspended child; disposal can release family resources without successful
family-zero proof; and teardown evidence uses inconsistent synchronization. The prior WaitAsync/Dispose finding was
accepted as Fixed. Abood authorized one cohesive repair using authoritative Windows API evidence, one deterministic
regression per finding, focused verification, a complete Reviewer rerun, and a new latest-head Ahmad review. No final
gate is authorized before that approval.

## Owner platform-floor amendment

Authoritative Microsoft documentation for `UpdateProcThreadAttribute` identifies
`PROC_THREAD_ATTRIBUTE_JOB_LIST` as creation-time assignment of listed jobs and limits support to Windows 10 /
Windows Server 2016 and newer. The .NET 10 support matrix still includes older Windows Server releases, so adopting
this mechanism is a real platform contraction rather than an implied SDK floor. Abood explicitly selected the
recommended amendment: GH-106 now admits only Windows 10 / Windows Server 2016 x64 or newer, fails closed elsewhere,
and uses the job-list attribute to eliminate the uncontained suspended-child window. The three-handle inheritance
attribute remains exact and separate.

## Reviewer findings on Ahmad repair candidate

Independent review at `d964c76` marked Ahmad's construction-containment finding Fixed and teardown synchronization
finding Fixed. It retained one High finding: failed family proof kept process/job/completion handles but still marked
the object `Disposed`, making retained ownership unreachable and later cleanup impossible. It also found one Medium
attribute-list failure boundary: unconditional deletion after failed initialization. Both are accepted for one
deterministic regression and the smallest cohesive repair; the final gate remains withheld.

## Blocked after second repair review

The `7625165` repair passed 65/65 focused tests. Independent review marked retained-family lifecycle and
initialized-only attribute deletion Fixed, along with all three Ahmad findings and the earlier Wait/Dispose race. It
then found two Medium defects: tracked close failure discards recoverable handle ownership before reporting `Disposed`,
and a throwing attribute-delete test observer can interrupt native deletion and buffer release. Because this is the
second failed repair rerun, I100-A moved to Blocked. The worktree and branch are preserved; no final gate or Ahmad
re-review was requested.

## Owner-authorized additional repair round

The owner explicitly authorized one additional I100-A repair round on the same branch. The frozen repair covers only
recoverable ownership after tracked close failure and observer-safe attribute-list cleanup. It permits two focused
regressions, implementation, focused verification, and one complete independent review; it does not authorize the
final gate or an Ahmad review request before the candidate is clean.

## Additional repair review result

The `26191b8` candidate fixed retryable tracked-close ownership and isolated the attribute-delete observer, then passed
66/66 focused tests on an unchanged rerun. Complete independent review marked both targeted findings Fixed and all
earlier Ahmad findings still Fixed, but found one Medium observer boundary: `ResourceReleaseObserverForTests` remains
outside tracked buffer cleanup's exception boundary and can prevent deletion/free after ownership is cleared. The
authorized round is exhausted, so I100-A returned to Blocked without a final gate or Ahmad review request.

## Owner-authorized final observer repair

The owner authorized one final same-branch repair for the remaining `ResourceReleaseObserverForTests` cleanup finding.
The authorization permits one regression, the smallest observer-isolation repair, focused verification, and complete
independent review only. It does not authorize the final gate or an Ahmad review request before a clean review.

## Final observer repair review result

The `72016de` repair isolated tracked-buffer release observation and passed 67/67 focused tests. Independent review
marked that finding Fixed and all earlier findings still Fixed, but identified one Medium native-handle variant:
`ResourceReleaseObserverForTests` remains before `CloseHandle` in `CloseTracked`, allowing callback failure to skip the
close and become teardown evidence. Because `CloseTracked` was outside the final authorization, I100-A returned to
Blocked without a final gate or Ahmad review request.

## Owner-authorized CloseTracked observer repair

The owner authorized one repair limited to the remaining native-handle observer boundary. It permits one regression,
the smallest callback-isolation change in `CloseTracked`, focused verification, and complete independent review. It
does not authorize the final gate or an Ahmad review request before a clean review.

## CloseTracked observer repair result

The `d09d236` repair runs native close and ownership/evidence handling before isolated release observation. The focused
SDK 10.0.302 lane passed 68/68, and complete independent review returned PASS with no findings. All Ahmad and reviewer
findings are Fixed. I100-A moved to ReviewRequired for Ahmad's latest-head formal review; the final gate remains
withheld.

## Ahmad latest-head re-review result

Ahmad reviewed exact head `6fee7f5271a4ee7ce5d4626af02e631f6d36f51d` and returned `BLOCK`. One High finding
remains: construction unwind can continue after failed termination/wait without proving the contained family reached
zero; the current regression proves only managed-task quiescence. One Medium finding records three changed paths
outside the frozen allowlist, and one Low finding records stale `AssignProcessToJobObject` wording/seams and private
test coupling. I100-A returned to Blocked under its frozen High-severity stop condition. No final gate or GH-107 work
ran.

## Owner-authorized Ahmad findings repair

The owner authorized one bounded repair covering Ahmad's High construction-unwind family-exit finding, the exact
historical-path allowlist discrepancy, and the Low stale assignment documentation/seam finding. The allowlist now names
`SeqDoc.slnx`, `docs/project/delegated-contribution-workflow.md`, and the stub `packages.lock.json` for their existing
solution-membership, trace, and lock-file purposes. The implementation must prove family zero after failed direct
termination/wait or retain reachable cleanup ownership; managed-task quiescence alone is not acceptance evidence.
Focused verification and complete independent review precede a new Ahmad request; the final gate and GH-107 remain
withheld.

### Repair candidate result

The candidate transfers all post-create resources to one `ContainedProcess` owner before any injectable fault, invokes
job-level termination, requires bounded `ACTIVE_PROCESS_ZERO` plus managed-task quiescence before releasing anything,
and returns a reachable retryable cleanup owner when either proof fails. External duplicate-job accounting prevents
kill-on-close from masking the proof. Stale post-create assignment seams and wording were removed, private lifecycle
reflection was replaced by typed evidence, and the three historical paths remain unchanged except for the precise
delegated-trace wording amendment. After the final typed-test cleanup, the first focused SDK 10.0.302 run reproduced
the previously seen fail-closed drain signature (`DrainIncomplete` expected, `ProcessFailed` actual, 25 seconds); an
unchanged rerun passed 69/69. This is disclosed for independent review rather than silently treated as stable evidence.

### Independent review finding on the repair candidate

Independent review at `a38fb31` marked Ahmad's allowlist and stale-seam findings Fixed but retained the construction
finding as High: field population preceded allocation/push of the transferred cleanup action, leaving a theoretical
post-create no-owner interval. The repair remains within the authorized outcome: preallocate the owner/action/token
before native acquisition, gate old unwind through one ownership token, and add one deterministic transfer-boundary
regression. No final gate or GH-107 work is authorized.

### Atomic transfer repair result

The `495208f` repair installs the owner, token, and transferred cleanup action before any native resource, gates every
legacy unwind entry through that token, and adds a deterministic fault immediately after transfer. Focused verification
passed 70/70 under isolated SDK 10.0.302. Complete independent review returned PASS with no findings, marked I100-A-F5
Fixed, and marked all three Ahmad findings Fixed. I100-A moved to ReviewRequired; no final gate or GH-107 work ran.

### Ahmad latest-head review at `cd7d103`

Ahmad returned `BLOCK`. Local code verification confirmed both High findings: failed pre-transfer native closes can be
zeroed out of ownership, including parent pipe writers whose survival prevents EOF; and the exited-child drain/family
race records `ProcessFailed` before the bounded family-proof task finishes, explaining the previously disclosed
25-second instability. The Medium finding is also accurate: runtime stub discovery depends on a literal repository
`tests/bin/<Config>/<Tfm>` layout instead of staged output or MSBuild metadata. I100-A returned to Blocked with no final
gate or GH-107 work.

### Frozen redesign direction

Ahmad approved the table-driven ownership ledger with one lifecycle coordinator as design direction, subject to an
amended frozen plan before implementation authorization. The checkpoint now enumerates all 15 native and 8 managed
slots, failed-release ownership/error/retry semantics, operation IDs that reject stale asynchronous results, monotonic
retryable family proof, one in-flight terminal attempt with later failed-attempt retry, the exact
`StageProcessOwnershipStub` MSBuild contract, a six-group test cap retaining all 70 tests, three new partial-file paths,
and the required Blocked stop rule. This publication does not itself authorize implementation, the final gate, merge,
or GH-107.

The owner subsequently authorized implementation of the exact frozen plan at `50b70e4`; I100-A returned to Active
under its six-group test cap and mandatory Blocked stop rule.

#### Frozen redesign implementation stop

The Test Writer produced exactly six red groups with 70 prior tests green. The single implementation owner added
partial ledger/coordinator/native files and staging changes but exhausted its bounded implementation run while focused
verification remained red and timed out. Explicit failures remained in failed-close ownership/snapshots, real
drain-family classification, and relocated output text. No final count was emitted. The frozen stop rule returned
I100-A to Blocked; no final gate or GH-107 work ran, and the worktree was preserved without committing the incomplete
candidate or generated staging output.

The owner authorized one bounded same-owner continuation for the four recorded failures plus generated staging-artifact
cleanup. The continuation may proceed to independent review only after a green focused lane and must return to Blocked
on remaining red or a new High safety finding. No final gate or GH-107 work is authorized.

#### Bounded continuation stop

The last verified continuation result was 72/76. Four failures remained: two drain/family `ProcessFailed`
classifications and two failed-close ownership/snapshot assertions. The owner then made unverified ledger retry edits
before exhausting the continuation. Generated source-tree staging directories were removed and no matching live
process remained at inspection. The mandatory stop rule returned I100-A to Blocked with the partial worktree preserved;
no independent review or final gate ran.

The owner authorized a fresh checkpoint-builder takeover limited to the four verified failures on the preserved
worktree. The takeover is bound to focused verification first and the same red/new-High Blocked stop rule; no final
gate or GH-107 work is authorized.

#### Fresh takeover stop

The fresh takeover reached a verified 74/76 before two descendant-family classification failures. Its later
drain-before-proof tracking edits are unverified and currently fail IDE0011 at `ProcessOwnership.cs:1605-1606`. No
matching live test/stub process or source-tree staging output remained at inspection. I100-A returned to Blocked; no
independent review or final gate ran.

The owner authorized one final bounded takeover for the brace errors and two remaining descendant-family failures,
with independent review permitted only after 76/76 focused verification. No final gate or GH-107 work is authorized.

#### Final bounded takeover stop

Focused verification under isolated SDK 10.0.302 reached 75/76. The only remaining failure is the descendant-closes-
pipes scenario, where EOF still prevents required family enforcement. The no-red stop rule returned I100-A to Blocked;
no independent review or final gate ran, and the worktree remains preserved.

The owner authorized one surgical repair limited to descendant pipe EOF versus family proof. Review is permitted only
after 76/76 focused verification; the final gate and GH-107 remain prohibited.

#### Independent redesign review stop

Focused verification passed 76/76 at `7f0b4a1`. Complete independent review nevertheless found three High structural
defects: the coordinator is test-only rather than production authority, raw pre-transfer unwind can lose ownership
without publishing a cleanup owner, and ledger acquire/release rules can hide duplicate or synthesize stale ownership.
I100-A returned to Blocked under the frozen stop rule; no Ahmad request or final gate ran.

The owner authorized repair of I100-A-F6, F7, and F8 on the existing PR. The six grouped tests may be strengthened
without increasing the 76-test total. Production coordinator authority, sole ledger ownership, and strict slot
identity must all pass focused verification and complete independent review before Ahmad is requested again.

#### F6/F7/F8 repair-limit stop

The strengthened six-group package remained at 76 total and went red 3/73 for F6/F7/F8. Two implementation attempts
expired after partial edits to `ProcessOwnership.cs`, `ProcessOwnership.Lifecycle.cs`, and
`ProcessOwnership.Resources.cs`; the current worktree is not known to compile and was not focused-tested. The repair
limit returned I100-A to Blocked with the worktree preserved. No independent review or final gate ran.

The owner authorized external preservation plus exact restoration of the three incomplete implementation files. The
patch was saved outside the repository with SHA-256
`210A193A515F94805A6C06697301D10B7EA96A8BB5C3F2AA81F9C5C8D900732B`; the repository returned clean at committed
red head. F8, F7, and F6 are now three bounded sequential implementation stages, followed by one integrated 76-test
lane and independent review. No final gate or GH-107 work is authorized.

#### Sequential F8/F7/F6 integration stop

The three stages produced an integrated candidate in `ProcessOwnership.cs`, `ProcessOwnership.Resources.cs`, and
`ProcessOwnership.Lifecycle.cs`. Orchestrator inspection repaired idempotent disposal, proof-only terminal retry after
successful native termination, ledger-authoritative ownership checks, disposed-terminal admission, and initialized-only
attribute-list deletion before the declared focused run. Under isolated SDK 10.0.302, the command reported `Failed 7,
Passed 69, Skipped 0, Total 76`. Every failure contains the same early-observation signature: four parent-copy releases
performed during successful construction appear before the expected cleanup sequence; the transfer-fault proof likewise
rejects those four labels as non-cleanup observations. The mandatory red stop rule returned I100-A to Blocked. The
worktree is preserved; independent review, the final gate, Ahmad request, merge, and GH-107 remain withheld.

The owner subsequently authorized one bounded repair for the shared early-observation regression only. The repair may
distinguish successful-construction parent-copy release from cleanup observation, but it must retain typed ledger
ownership, strict release identity, and all 69 passing behaviors. Tests remain frozen. Full 76-test focused verification
and a clean independent review are required before any later Ahmad request; the final gate and GH-107 remain withheld.

The first repair reached 74/76. Five of the seven failures were fixed by suppressing cleanup-only observations for the
four successful-construction parent-copy releases. The remaining two are the same release-order outcome: failed release
attempts are not added to teardown chronology, and process/thread acquisition sequence currently makes generic reverse
unwind publish thread before process. A second and final repair may adjust those two implementation boundaries without
test changes; any remaining red returns the checkpoint to Blocked.

The second repair added failed release attempts to cleanup chronology and aligned process/thread acquisition sequence
with dependency-safe reverse release. Focused verification reached 75/76. Only the partial-construction unwind order
test remains red because its expected final stdin observation represents a parent copy that was actually released
earlier during successful construction setup; suppressing early parent-copy observations removed it entirely. The
repair limit is exhausted. I100-A returned to Blocked with the candidate preserved and without independent review,
final gate, Ahmad request, merge, or GH-107 work.

The owner authorized a single existing-test contract correction: omit the already released stdin parent handle from the
later partial-unwind chronology and assert its exact acquired/released-once typed-ledger state instead. No production
edit or test-count increase is authorized. The full 76-test focused lane must pass before independent review.

The Test Writer amended only the existing partial-unwind test and retained 76 total tests. Focused verification under
isolated SDK 10.0.302 passed 76/76 with zero skipped; `git diff --check` passed. The candidate is ready for independent
review. No final gate has run.

Independent review verdict: FINDINGS (2 High, 3 Medium). I100-A-F1: first-wait task faults can remain permanently cached.
I100-A-F2: production has no operation IDs/completion admission for wait, drain, and disposal despite the frozen epoch
contract. I100-A-F3: raw native mirrors still participate in lifecycle native-call decisions. I100-A-F4: the suite lacks
a production-path proof-only terminal retry after native success plus an initially unproven family epoch. I100-A-F5:
the stdin amendment proves final slot state but not chronology before later unwind. Per the frozen independent-review
High stop rule, the candidate returned to Blocked without repairs or final gate.

The owner authorized repair of all five independent-review findings without increasing the 76-test total. A Test Writer
will strengthen existing grouped claims for F1, F2, F4, and F5; F3 retains direct review evidence. One cohesive
implementation candidate may modify the three frozen implementation files, followed by focused verification and a new
independent review. No final gate, Ahmad request, merge, or GH-107 work is authorized yet.

The strengthened existing suite first reported 74/76: F1 reproduced the poisoned second wait and F2 rejected the current
generic Dispose epoch; F4 proof-only production retry and F5 typed stdin chronology passed. The cohesive repair then
implemented per-kind wait/drain/dispose completion admission, retryable faulted reservations, deterministic epoch
receipts, and ledger-sourced native lifecycle values. A final pre-review correction removed global cross-kind coupling
from production completion checks and made faulted Terminal reservations retryable. Focused verification passed 76/76
with zero skipped; `git diff --check` passed. All five findings are Fixed pending independent review.

New independent complete-candidate review verdict: PASS, no findings. F1, F2, F3, F4, and F5 are all independently
confirmed Fixed. The candidate remains at ReviewRequired for Ahmad; the final gate has not run and remains prohibited
until Ahmad approves.

Ahmad then performed the required human latest-head review at `d52df4e` and returned BLOCK. Accepted findings: High,
implement all eight frozen managed slots with typed ownership, epoch, quiescence, retention/retry, and immutable snapshot
evidence; Medium, replace redesign reflection/fallback probes with direct typed evidence; Low, use or remove the native
close adapter and replace the machine-local recovery path with an artifact identifier and digest. The authorization
question is resolved under canonical T2/T3 policy. I100-A moved to ResolvingFindings; final gate and GH-107 remain
withheld.

Managed-authority repair evidence: existing grouped tests were converted to direct typed access and compile-red on the
missing eight-slot contract without increasing the 76-test total. Production then added typed managed snapshots and
coordinator-mediated transitions, routed native close through the existing adapter, and neutralized the machine-local
recovery record. Focused SDK 10.0.302 verification passed 76/76 with zero skipped and `git diff --check` passed. An
independent review is required before publishing a new head or requesting Ahmad's next review.

Independent review found two High managed-authority defects. F1: typed managed entries mirror metadata but do not own or
reach the actual task/CTS leases, leaving raw fields authoritative. F2: startup can strand synthetic Active entries and
Disposed has no structural Active/Retained-slot rejection. Medium direct evidence, Low adapter routing, and Low neutral
artifact evidence are Fixed. The checkpoint returned to ResolvingFindings for lease ownership and legality repair.

Existing tests then established red evidence for real lease presence, atomic startup rollback, stable disposal-slot
identity, and Disposed blocking on an active monitor. Production now stores exact task/CTS leases in the typed ledger,
uses ledger leases for cleanup, rolls back unpublished workers, releases/disposes outside locks, and serializes disposal
through final state publication. A one-test completed-task observability regression was fixed without restoring raw-field
authority. Focused verification passed 76/76; F1/F2 are Fixed pending independent review.

Independent rereview found one remaining High: drain and completion-monitor `Task.Run` delegates are scheduled before
lease publication, so they begin by waiting on a publication barrier while not yet owned by a typed slot. Concrete lease
ownership, rollback, Disposed legality, stable retries, direct evidence, adapter routing, and neutral recovery evidence
were accepted. The two repair rounds are exhausted; I100-A returns to Blocked with no final gate or publication.

The owner separately authorized the one remaining High repair: reservation-task leases must be installed before any
drain/monitor worker is scheduled, and real worker completion must bridge into the exact registered reservation. No new
tests are permitted; focused verification and independent rereview remain mandatory before publication.

Reservation-first repair evidence: existing tests now block each install observer for 500 ms and prove no corresponding
worker delegate has begun, then prove all workers begin after slot installation and preserve acquisition identity.
Production binds non-running reservations to managed leases before `Task.Run`, bridges success/fault/cancellation, and
completes epochs from reservation tasks. Focused verification passed 76/76; the High is Fixed pending independent review.

Independent rereview found one High partial-scheduling path: a first drain worker may be scheduled before the second
scheduler throws, but current rollback detaches leases without awaiting that worker. Normal ordering and all Ahmad
findings were accepted. One final repair round may add deterministic scheduler-failure coverage and quiesce every
successfully scheduled worker before rollback; remaining High returns the checkpoint to Blocked.

The final repair added a typed scheduler seam and deterministic partial-scheduling regression. Production retains each
successfully returned worker task, cancels and quiesces it before rollback, and retains ledger ownership when bounded
quiescence cannot be established. Focused SDK 10.0.302 verification passed 76/76; independent rereview remains required.

Final independent rereview found one High: normal successful startup discards the scheduler-returned worker Tasks after
storing only reservation Tasks in typed leases. Partial-failure rollback is fixed, but actual scheduled-task ownership is
not durable on the success path. The second repair round is exhausted and the checkpoint returns to Blocked.

The owner authorized replacing scheduler-returned Tasks with direct queue admission so the preinstalled typed reservation
is the only Task representing each worker. The existing partial-scheduling regression will become a partial-queue
admission regression; no test-count increase is allowed. Focused and independent verification remain required.

Direct-queue repair evidence: worker callbacks are admitted through a bool-returning queue with no returned worker Task;
the preinstalled typed reservation is the sole task. The partial-admission regression proves admitted stdout settles
before rollback when stderr is rejected. Faulted/canceled reservations are observed and treated as quiescent; only a
timeout retains ownership. Focused verification passed 76/76; independent rereview remains required.

Independent rereview accepted all ownership and Ahmad findings as Fixed, with one Medium coverage gap: no deterministic
CompletionMonitor queue-rejection scenario. Repair is test-only within the existing grouped claim; total remains 76.

The grouped construction test now deterministically rejects CompletionMonitor queue admission and verifies non-start,
typed rollback, and cleanup reachability. Focused verification passed 76/76; F9 is Fixed pending independent review.

Review finding F9 throw-after-queue is Rejected pending independent confirmation. The internal queue seam's exact
contract is atomic bool admission or pre-admission throw; production uses `ThreadPool.QueueUserWorkItem` and consumes its
bool directly. Queue-then-throw is outside the accepted producer contract. No candidate change or focused rerun occurred.

Independent review verdict: PASS, no findings. F9 Rejected is accepted; all Ahmad and prior independent findings are
Fixed. Focused evidence remains 76/76. Publication and Ahmad rereview await explicit commit/push authority; no final gate
has run.

Bilal reserved one latest-head independent human review and performed a preparatory inspection at `6de3f74`. Accepted
findings: finite positive wait/drain admission; exact restoration and nonparallel isolation for ambient environment
mutation; direct typed strict-ledger proof; lease-publication assertion at the actual queue-admission boundary for drain
and monitor workers; and exact Win32 error evidence for `GetExitCodeProcess` failure. Technical repairs precede focused
verification, self/independent review, current-main integration, and a separate governance-only handoff commit. No final
gate is authorized.

Bilal preparatory repair evidence: existing tests now cover zero/infinite/negative wait and drain bounds, exact ambient
environment restoration and collection isolation, direct typed strict-ledger operations, actual queue-admission lease
publication for monitor/stdout/stderr, and injected `GetExitCodeProcess` error 2468. Production implements the exact
finite-bound, queue-boundary, and native-error contracts. Focused verification passed 76/76; findings are Fixed pending
independent review.

Independent review returned two Medium proof gaps only: add the rejected `uint.MaxValue`-millisecond wait/drain boundary
and assert exact ambient restoration for deterministic absent and pre-existing states. All other Bilal preparatory
findings are Fixed. No production repair is required.

The two existing groups were strengthened with the `uint.MaxValue`-millisecond rejected boundary and exact restoration
assertions for absent, pre-existing, and original host environment states. The Test Writer's first focused process hit
its 120-second harness allowance before emitting a result; an Orchestrator timeout-recovery rerun of the same command
under isolated SDK 10.0.302 passed 76/76 in 1 minute 16 seconds with zero skipped. `git diff --check` passed. The two
Medium review findings are Fixed pending independent rereview.

Independent rereview verdict: PASS, no findings. I100-A-F1/F2 and all five Bilal preparatory findings are Fixed; all 76
prior claims and the exact two-file technical scope remain accepted. The candidate is still uncommitted and must stop
before current-main integration, governance-only handoff correction, Bilal request, or final gate.

Publication preparation preserved the reviewed repair as `13c20c3` and integrated current `main` at `b66b0db` through
merge commit `580353d` without conflicts. Post-integration focused verification under isolated SDK 10.0.302 passed
76/76 with zero skipped in 1 minute 16 seconds. Governance validation passed for 51 work items, `git diff --check`
passed, and ancestry verification confirmed `b66b0db` in the candidate. Bilal's reserved latest-head non-author human
review is now the next gate; the final gate remains intentionally unrun.

Latest-main integration receipt: `26ee63e` merged `fa20535` after regenerating the sole conflicted derived projection
from canonical GH-106 state. Focused SDK 10.0.302 verification passed 76/76 with zero skipped in 1 minute 16 seconds;
51-item validation, projection check, `git diff --check`, and ancestry check passed. Independent post-integration review
returned PASS with no findings and accepted the optional Python governance-suite `MemoryError` as unrelated,
non-blocking resource exhaustion in its whole-repository snapshot test.

PR-specific owner-bypass receipt:
https://github.com/Bilaltariq41/SeqDoc/pull/109#issuecomment-5752397694. Bilal authorizes Ahmad or Qais to conduct the
final technical review despite non-independence, forbids the final pusher from approving that push, and withdraws his
earlier reservation. Abood is the final pusher; Ahmad is the non-independent final technical reviewer and must return
exact-head `PASS` or `BLOCK`. The transactional handoff's non-author invariant cannot encode this explicit exception, so
the bypass is recorded verbatim rather than misidentifying an independent peer. The final gate remains unrun.

Ahmad exact-head review receipt:
https://github.com/Bilaltariq41/SeqDoc/pull/109#issuecomment-5752921086 — `BLOCK` on
`1af4eddb03812cdb23342494f60a14133b6b55d5`. F10 requires the real-repository projection expectation to come from
`ws.load(ROOT)`/`ws.execution_object(real_items)` rather than synthetic `self.items`. F11 requires an adjacent comment
recording the xUnit 2.9.3 opted-out collection guarantee from
https://xunit.net/docs/running-tests-in-parallel#opting-a-test-collection-out-of-parallelism. No accepted
ProcessOwnership behavior may change; required CI and Ahmad rereview precede the final gate.

F10 repair derives the complete real projection from `ws.load(ROOT)` and `ws.execution_object(real_items)` without
hard-coding GH-106, active state, or execution count; existing byte-snapshot and synthetic fixture assertions remain.
F11 adds only the xUnit 2.9.3 collection-isolation contract comment beside `ProcessOwnershipGroup`; its attribute and
behavior are unchanged. Fresh-worktree receipts: targeted governance 1/1 in 9.7 seconds; complete governance 35/35 with
two skipped in 52.3 seconds. The clean-worktree Windows lane did not produce a stable verdict (`Fatal error.` on its
first attempt; the second was manually aborted), so the required ProcessOwnership command ran in the established primary
Windows worktree and passed 76/76 with zero skipped in 1 minute 17 seconds. Validation passed for all 51 work items,
projection check reported current, and `git diff --check` passed. F10 is Fixed and F11 is
Verified-by-framework-contract pending independent rereview and required CI.

Independent rereview verdict: PASS, no findings. F10 and F11 dispositions, verification anomalies, exact bounded scope,
and all 76 prior ProcessOwnership claims are accepted. Required GitHub `validate` and Ahmad exact-head rereview remain;
the final gate is still prohibited.

Ahmad rereview receipt https://github.com/Bilaltariq41/SeqDoc/pull/109#issuecomment-5753171524: technical acceptance of
F10/F11, the complete candidate, 76/76 with zero skipped, and green `validate`; governance-only `BLOCK` on exact head
`1007d6faaebd2270dee9dbd2452be9059d1a202b` for I100-A-F12 stale canonical handoff. F12 is Fixed through the required
`ReviewRequired` → `ResolvingFindings` → `ReviewRequired` transition and exact Ahmad review next action. Product source,
tests, build files, and F10/F11 technical evidence are unchanged. No approval or final gate is claimed.
