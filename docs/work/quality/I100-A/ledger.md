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
| GH106-F1 — `WaitAsync`'s exited-process drain path has no real bound against a live descendant silently holding a pipe write handle open; `DrainPipe`'s synchronous `Read` (line ~755) is only checked for its deadline *between* reads, not during an in-flight blocked read, so a caller invoking `WaitAsync` directly (without first calling `Terminate()`) against such a descendant can hang indefinitely, contradicting the checkpoint's own "drains stdout and stderr concurrently within explicit bounds" objective and its native API admission table's "bounded by the same timeout token" claim. | High | Repair in progress | Independent review at PR #108; confirmed by the orchestrator's own reading of `ProcessOwnership.cs:586-611,737-776`. |

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
| GH106-R2-F1 — post-construction failure unwind (StartCore lines ~303-324, 480-547) can double-close the 3 child-side pipe handles already closed at lines 498-500, and never cancels/awaits the drain tasks or completion monitor before returning, leaking background work on any assign/hook/resume failure. | High | Fixed |
| GH106-R2-F2 — normal exit records no failure when `ACTIVE_PROCESS_ZERO` is never observed within its bound; the frozen failure table requires `ProcessFailed` when family exit cannot be proven. | High | Fixed |
| GH106-R2-F3 — the timeout/cancellation branch calls `TerminateJobObject` but never awaits `ACTIVE_PROCESS_ZERO` before returning, so `WaitAsync` can return before the terminated family has actually finished exiting. | High | Fixed |
| GH106-R2-F4 — `TerminateJobObject` failures are discarded in both `WaitAsync` and `Terminate()`; no `ProcessFailed` is recorded. | High | Fixed |
| GH106-R2-F5 — `WaitForSingleObject` result `WAIT_FAILED` is mapped identically to `WAIT_TIMEOUT`, so a real wait failure is misreported as `TimedOut` instead of `ProcessFailed` with the captured `Win32Error`. | High | Fixed |
| GH106-R2-F6 — the parent's stdin write handle is retained but never exposed or closed, so a child reading stdin to EOF cannot finish naturally. | High | Fixed |
| GH106-R2-F7 — `Dispose()` can record `TeardownDegraded` internally, but only an internal test-only accessor can observe it; no real caller (including #107) has a way to learn teardown failed. | High | Fixed |
| GH106-R2-F8 — embedded NUL and malformed environment-name entries are not rejected before native marshaling, which can silently truncate them while construction still reports success. | Medium | Fixed |
| GH106-R2-F9 — `BuildEnvironmentBlock` dedups with `StringComparer.Ordinal`, so case-variant Windows environment names (e.g. `Path` vs `PATH`) are not deduplicated despite Windows treating them as the same variable. | Medium | Fixed |
| GH106-R2-F10 — `DrainPipe` decodes each independent 64 KiB read chunk with `Encoding.UTF8.GetString`, so a multibyte UTF-8 sequence split across a read boundary can be corrupted while `Truncated` still reports `false`. | Medium | Fixed |
| GH106-R2-F11 — `Dispose()`'s close order does not match true reverse-acquisition order (job/IOCP close before pipes; command-line/environment buffers close last instead of matching their early acquisition), despite the code comment claiming exact reverse order. | Medium | Fixed |
| GH106-R2-F12 — the assign-before-resume chronology proof uses an in-process test hook plus `IsProcessInJob`, not the observable stub-receipt ordering the checkpoint originally specified. | Medium | Fixed |
| GH106-R2-F13 — the checkpoint's declared invalid-NUL and environment-dedup vectors are not yet exercised by any test. | Medium | Fixed (folded into F8/F9 repair) |

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
