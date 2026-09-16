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
| GH106-R2-F1 — post-construction failure unwind (StartCore lines ~303-324, 480-547) can double-close the 3 child-side pipe handles already closed at lines 498-500, and never cancels/awaits the drain tasks or completion monitor before returning, leaking background work on any assign/hook/resume failure. | High | Repair in progress |
| GH106-R2-F2 — normal exit records no failure when `ACTIVE_PROCESS_ZERO` is never observed within its bound; the frozen failure table requires `ProcessFailed` when family exit cannot be proven. | High | Repair in progress |
| GH106-R2-F3 — the timeout/cancellation branch calls `TerminateJobObject` but never awaits `ACTIVE_PROCESS_ZERO` before returning, so `WaitAsync` can return before the terminated family has actually finished exiting. | High | Repair in progress |
| GH106-R2-F4 — `TerminateJobObject` failures are discarded in both `WaitAsync` and `Terminate()`; no `ProcessFailed` is recorded. | High | Repair in progress |
| GH106-R2-F5 — `WaitForSingleObject` result `WAIT_FAILED` is mapped identically to `WAIT_TIMEOUT`, so a real wait failure is misreported as `TimedOut` instead of `ProcessFailed` with the captured `Win32Error`. | High | Repair in progress |
| GH106-R2-F6 — the parent's stdin write handle is retained but never exposed or closed, so a child reading stdin to EOF cannot finish naturally. | High | Repair in progress |
| GH106-R2-F7 — `Dispose()` can record `TeardownDegraded` internally, but only an internal test-only accessor can observe it; no real caller (including #107) has a way to learn teardown failed. | High | Repair in progress |
| GH106-R2-F8 — embedded NUL and malformed environment-name entries are not rejected before native marshaling, which can silently truncate them while construction still reports success. | Medium | Repair in progress |
| GH106-R2-F9 — `BuildEnvironmentBlock` dedups with `StringComparer.Ordinal`, so case-variant Windows environment names (e.g. `Path` vs `PATH`) are not deduplicated despite Windows treating them as the same variable. | Medium | Repair in progress |
| GH106-R2-F10 — `DrainPipe` decodes each independent 64 KiB read chunk with `Encoding.UTF8.GetString`, so a multibyte UTF-8 sequence split across a read boundary can be corrupted while `Truncated` still reports `false`. | Medium | Repair in progress |
| GH106-R2-F11 — `Dispose()`'s close order does not match true reverse-acquisition order (job/IOCP close before pipes; command-line/environment buffers close last instead of matching their early acquisition), despite the code comment claiming exact reverse order. | Medium | Repair in progress |
| GH106-R2-F12 — the assign-before-resume chronology proof uses an in-process test hook plus `IsProcessInJob`, not the observable stub-receipt ordering the checkpoint originally specified. | Medium | Repair in progress |
| GH106-R2-F13 — the checkpoint's declared invalid-NUL and environment-dedup vectors are not yet exercised by any test. | Medium | Repair in progress (folded into F8/F9 repair) |

Full finding text: https://github.com/Bilaltariq41/SeqDoc/pull/108 (Abood-essa review comment). Disposition note:
Abood also flagged that the prior "final gate passed" wording overstated a 5-failures-out-of-75 result without a
frozen accepted-failure baseline or independent peer disposition; going forward the orchestrator will record the
final gate's literal outcome (exact counts) without characterizing a nonzero-failure run as "passed."
