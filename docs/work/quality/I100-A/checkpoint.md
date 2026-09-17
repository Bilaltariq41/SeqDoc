# I100-A Windows contained-process ownership primitive checkpoint

## State

`ReviewRequired`

## Authority and frozen state

Issue [#106](https://github.com/Bilaltariq41/SeqDoc/issues/106) is the specification authority. Parent:
[#100](https://github.com/Bilaltariq41/SeqDoc/issues/100). This is Child A of the owner-approved #100 scope split;
Child B ([#107](https://github.com/Bilaltariq41/SeqDoc/issues/107)) remains blocked until this checkpoint closes with
an accepted contained-process contract.

This checkpoint freezes the contract at baseline `ab6e3e1cf16213ee5346506b16949fa32c4ddfa4` (`main`, current with the
`docs: simplify contributor workflow` update).

Readiness follows the simplified workflow confirmed on #106 (AhmadKrarha,
https://github.com/Bilaltariq41/SeqDoc/issues/106#issuecomment-5696377761) and `docs/project/issue-readiness.md`: an
eligible assigned contributor may freeze the checkpoint capsule and canonical record and move to `Ready` without a
separate readiness PR or human approval. Qais (`Qhatahet`) is the assignee of #106 and the author of the readiness
packet at https://github.com/Bilaltariq41/SeqDoc/issues/106#issuecomment-5694865856.

### Finalized contract decisions (supersede the readiness packet's "Open items" section)

1. **Windows/platform floor.** Supported platform is Windows **x64 on Windows 10 or Windows Server 2016 and newer**.
   ARM64, x86, Linux, macOS, older Windows clients, and older Windows Server releases fail closed. The original
   readiness freeze named Windows x64 without a separate version floor; Ahmad's latest-head construction-unwind
   finding required atomic creation-time job admission, and Abood approved this explicit amendment after Microsoft
   documentation established that `PROC_THREAD_ATTRIBUTE_JOB_LIST` is supported only on Windows 10 and Windows Server
   2016 or newer. The runtime admission guard checks Windows, x64, and the approved version capability before any
   filesystem or native construction work. This is a deliberate support contraction, not an inferred fallback.
2. **Executable-path resolution.** The primitive never performs a `SearchPathW`-equivalent lookup. The caller (test
   code) must supply an already-resolved, rooted, existing executable path; the primitive validates
   `Path.IsPathRooted` and file existence before construction and fails closed with `ProcessConstructionFailed`
   otherwise. `lpApplicationName` is always set explicitly from that validated path; `CreateProcessW`'s implicit
   PATH/cwd search is never exercised. This closes the fixture-controlled-cwd risk raised during the #100 review, as
   proposed in the readiness packet.
3. **`AllowUnsafeBlocks` placement.** Set `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` only on
   `tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj`, where the P/Invoke primitive class
   (`ProcessOwnership.cs`) needs unsafe pointer construction for the `PROC_THREAD_ATTRIBUTE_LIST` handle-inheritance
   restriction (`InitializeProcThreadAttributeList`/`UpdateProcThreadAttribute`) and the job-object completion-port
   association buffer. `tests/SeqDoc.AcceptanceTests.ProcessOwnershipStub/SeqDoc.AcceptanceTests.ProcessOwnershipStub.csproj`
   stays ordinary managed code (deterministic stdout/stderr markers, plus an ordinary `System.Diagnostics.Process`-spawned
   grandchild for the parent-exit-survival scenario) and does not set `AllowUnsafeBlocks`.

These three decisions are the readiness freeze itself, not a post-`Ready` T2 amendment: they resolve the readiness
packet's own "Open items, not yet frozen" section before the contract is frozen, consistent with the single-pass
readiness audit in `docs/project/issue-readiness.md`. No test, path, command, or non-goal from the issue body changes.
See `ledger.md` for the decision record.

## Objective

Implement and prove one test-only Windows process primitive that:

1. creates the child suspended with an explicit executable, Windows argument encoding, deterministic environment, and
   only the intended standard handles inheritable;
2. assigns the process to a kill-on-close Job Object before first resume and rejects breakaway;
3. owns descendants after the immediate parent exits;
4. drains stdout and stderr concurrently within explicit bounds;
5. exposes separate wait, query, terminate, and dispose phases with monotonic failure precedence;
6. terminates and awaits the contained process family during timeout, cancellation, or exceptional unwind;
7. releases every acquired native and managed resource in reverse acquisition order; and
8. fails closed outside supported Windows x64 (ARM64, x86, Linux, and macOS are unsupported in this checkpoint).

The candidate may use a different native construction only if it proves the same pre-execution containment and
inheritance guarantees. Do not add a package solely to avoid establishing those guarantees.

## Target-path allowlist

- `tests/SeqDoc.AcceptanceTests.ProcessOwnershipStub/SeqDoc.AcceptanceTests.ProcessOwnershipStub.csproj`
- `tests/SeqDoc.AcceptanceTests.ProcessOwnershipStub/Program.cs`
- `tests/SeqDoc.AcceptanceTests/ProcessOwnership.cs`
- `tests/SeqDoc.AcceptanceTests/ProcessOwnershipTests.cs`
- `tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj`
- `docs/work/quality/I100-A/checkpoint.md`
- `docs/work/quality/I100-A/ledger.md`
- the generated canonical work-item/execution projections, only through `tools/governance/work_state.py`

Any additional path requires a new owner decision before editing.

## Native API admission table

| Element | Exact identity |
|---|---|
| Process creation | `CreateProcessW` with `CREATE_SUSPENDED \| CREATE_UNICODE_ENVIRONMENT \| EXTENDED_STARTUPINFO_PRESENT`, explicit `lpApplicationName` from a caller-resolved rooted path (never PATH/cwd search), `STARTUPINFOEXW` |
| Containment | `CreateJobObjectW` then `SetInformationJobObject(JobObjectExtendedLimitInformation)` with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` and breakaway denied (silent-breakaway flags omitted), then `AssignProcessToJobObject` before `ResumeThread` |
| Handle policy | `bInheritHandles = TRUE` at `CreateProcessW`, but only the 3 std handles are inheritable (`SetHandleInformation(HANDLE_FLAG_INHERIT)` set only on the pipe ends actually passed); the job handle itself is non-inheritable |
| Completion/active-zero proof | IOCP associated to the job (`SetInformationJobObject(JobObjectAssociateCompletionPortInformation)`), observing `JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO`; inability to prove active-zero within its bound (production default 10s) is itself recorded as `ProcessFailed`, both on the normal-exit path and after a forced termination (GH106-R2-F2/F3) |
| Termination | `TerminateJobObject` (relies on `KILL_ON_JOB_CLOSE` for normal `Dispose`, explicit `TerminateJobObject` for timeout/cancellation/unwind, each call site's return value checked and a failure recorded as `ProcessFailed` — GH106-R2-F4) |
| Stream draining | Two synchronous, thread-pool-offloaded pipe reads (`FileStream.Read` off `Task.Run`, matching the in-repo IR-1 `RunProcess`/`RunGit` idiom — anonymous pipes from `CreatePipe` do not support `FILE_FLAG_OVERLAPPED`, so true overlapped/async I/O is not available here) started before `ResumeThread` returns control to the wait loop; bounded in practice by `WaitAsync` racing drain completion against its own timeout/cancellation token and forcing `TerminateJobObject` (closing every inherited handle, including a silent descendant's) to unblock an in-flight blocked read if the deadline is reached first; both must reach EOF or truncation is recorded |
| Supported platform | Windows x64 only (finalized decision 1 above). x86, ARM64, Linux, and macOS fail closed via an explicit runtime guard, never silently skipped |
| Executable resolution | Finalized decision 2 above: caller-resolved, rooted, existing path only; zero PATH/cwd search inside the primitive |

## Non-goals

- No Git command, worktree, ref, administration-directory, cache, quarantine, sentinel, or deletion behavior.
- No Restart Manager use.
- No external corpus or supplied-project execution.
- No changes to GH-93, GH-18/I18, PR #99, or PR #103 candidates.
- No production source, SDK, package-version, repository configuration, or external-source changes.
- No global or name-based process killing and no ownership inference from a reusable PID alone.
- No ARM64 claim without a separately authorized executable lane.

## Existing coverage and risks

Existing acceptance helpers use `System.Diagnostics.Process`/`ProcessStartInfo` and `Kill(entireProcessTree: true)`
across `tests/SeqDoc.AcceptanceTests/BehaviorDocumentationLevel2Tests.cs`,
`tests/SeqDoc.AcceptanceTests/EntityFramework6EdmxProductionTests.cs`,
`tests/SeqDoc.AcceptanceTests/OutboundHttpExternalCorpusTests.cs`, and
`tests/SeqDoc.AcceptanceTests/ServiceClientExternalCorpusTests.cs`, plus the async-stream-reads-started-before-wait
deadlock-prevention convention already documented in-repo as the IR-1 repair. None of them prove descendant-handle
release after the immediate parent exits — that is exactly the gap #106/I100-A exists to close.

There is zero existing P/Invoke, `DllImport`, `SafeHandle`, or `Marshal.*` usage anywhere in the repository
(`src/**` and `tests/**` both searched). This primitive is the first native-interop code in SeqDoc, and there is no
existing stub/helper test-support project naming convention to follow. Both facts mean the independent Windows-focused
review carries full weight; there is no existing in-repo pattern to defer to.

Primary risks are pre-containment execution, unintended handle inheritance, stranded suspended processes, PID reuse,
stdout/stderr deadlock, output overclaim after forced closure, timeout/error downgrading, Job Object breakaway,
partial-construction leaks, and killing unrelated processes. The historical B1-B8 and L1-L29 findings on #100 remain
risk input; only findings relevant to this primitive belong here.

## Failure-class precedence (Child A scope only; no CleanupDegraded/Restart Manager class — that belongs to #107)

1. `ProcessConstructionFailed` — any S0/S1 failure before/at resume (create, job create/assign, pipe/attribute-list/
   environment-block construction, unresolved/non-rooted/missing executable path, `ResumeThread` failure)
2. `TimedOut` — wait-for-exit or readiness wait exceeds its bound
3. `ProcessFailed` — nonzero exit, `WAIT_FAILED`, `TerminateProcess`/`TerminateJobObject` failure, inability to prove
   active-zero
4. `DrainIncomplete` — either stream fails to reach EOF inside the bound; forced closure truncates output (captured
   prefix preserved, explicitly marked truncated, never claimed lossless)
5. `TeardownDegraded` — an individual handle/resource close fails during reverse-order unwind after a primary phase
   already succeeded; teardown continues through all remaining resources and aggregates every failure

A later class never overwrites an earlier one already recorded; successful cleanup never erases a real primary
failure.

## Soft test budget

Approximately 10-12 grouped claims (the issue's declared budget) at the least expensive reliable layer, covering:

1. platform admission (x64 pass; x86/ARM64/other fail closed, never skipped)
2. executable/argument/environment vectors (quoting: empty arg, embedded quote, trailing backslash, invalid NUL; env
   determinism/dedup; non-rooted or missing executable path fails closed per decision 2)
3. exact 3-handle inheritance and rejection of an unrelated handle leaking
4. assign-before-resume chronology (proven via an observable stub receipt — the stub's own
   `report-job-membership` command calls `IsProcessInJob` against its own process handle as the very first action
   it takes and prints `IN-JOB:True`/`False` as its first stdout line; the prior in-process
   `AssignBeforeResumeHookForTests` proof is kept as a supplementary claim, not the sole proof — GH106-R2-F12)
5. parent-exit-survival: parent exits normally, grandchild still owned/contained, later explicitly terminated
6. complete concurrent stdout/stderr drain to EOF (no deadlock under load)
7. bounded forced termination with explicit `DrainIncomplete` truncation marking
8. timeout vs. cancellation vs. body-failure precedence ordering
9. S0-S3 partial-construction unwind (each construction failure point unwinds only what was acquired, in reverse)
10. active-zero-gated normal disposal (no `TerminateJobObject` call on the clean exit path)
11. unrelated-process isolation, deterministic stub receipt, repeated-run idempotent cleanup

Parameterized vectors count as grouped claims, not one claim per row. Expand only when an observed risk requires it.

## Focused verification

```powershell
dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter FullyQualifiedName~ProcessOwnershipTests
```

The receipt must report nonzero discovery and exact pass/fail/skip counts. Unsupported platforms must fail the
admission claim rather than silently pass or skip it.

## Final gate

Run once, only after all independent-review findings are resolved:

```powershell
dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release
```

## Independent review

Follow `docs/project/collaboration-model.md`'s current delivery procedure: implement, run focused verification,
self-review the complete candidate, run the worker's own Reviewer agent, then stop at `ReviewRequired` for one
independent, non-author, Windows-focused human peer to inspect the complete compiled candidate and all historical
relevant risks (author cannot be that peer). Resolve every finding as `Fixed`, `Rejected` with evidence, or
`Deferred` with explicit accepted disposition before the final gate. After two failed repair rounds, preserve the
worktree, transition GH-106 to `Blocked`, and stop for a separate authorized decision.

## Repair round GH106-R2 (PR #108 human peer review, 13 findings — full detail in `ledger.md`)

The following contract text changed as a direct result of that repair round; see `ledger.md`'s repair trace for the
finding-by-finding root cause and proof:

- **Public failure surface (GH106-R2-F7).** `ContainedProcess` now exposes public `FailureClass` and
  `TeardownFailures` properties (proxying the same tracker/list the prior internal test-only accessors exposed,
  which are kept alongside). A real caller — including a future #107 consumer — can now observe a degraded teardown
  after `Dispose()` without any test-only seam.
- **Family-exit-proof failure recording (GH106-R2-F2/F3).** Inability to observe `ACTIVE_PROCESS_ZERO` within its
  bound (production default 10s) is now itself recorded as `ProcessFailed` on both the normal-exit path and the
  timeout/cancellation path (which now awaits family exit after forced termination rather than only terminating).
  Contract note: because the timeout/cancellation path now also awaits `WaitForActiveProcessZero` after forced
  termination, `WaitAsync(timeout: X, ...)` can legitimately take up to roughly `X + 10s` (the production-default
  `_activeProcessZeroBound`) in the worst case — e.g. `TerminateJobObject` succeeds but IOCP's
  `JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO` notification is delayed. This is an intentional trade-off (terminate *and*
  await proof of family exit, not terminate-and-hope), not a bug; a future caller (e.g. #107) should budget for this
  ceiling rather than assume `WaitAsync` returns at or immediately after `X`.
- **`TerminateJobObject`/`WaitForSingleObject` failure recording (GH106-R2-F4/F5).** Both native call families now
  have their return values checked at every call site; a genuine failure records `ProcessFailed` with the captured
  `Win32Error`, distinct from a benign timeout.
- **Stdin scope (GH106-R2-F6).** The parent's stdin write handle now closes immediately after `CreateProcessW`
  succeeds (this checkpoint has no interactive-stdin-input scope), giving any stdin-reading child immediate EOF
  instead of an unusable, silently-retained handle.
- **Input validation (GH106-R2-F8/F13).** An embedded NUL in the executable path, any argument, or any environment
  name/value, and `=` in an environment-variable name, now fail closed (`ProcessConstructionFailed`) before any
  native call.
- **Environment dedup (GH106-R2-F9/F13).** `BuildEnvironmentBlock` dedups/sorts with `StringComparer.OrdinalIgnoreCase`
  (was `Ordinal`), matching Windows environment-variable case-insensitivity.
- **Stream decoding (GH106-R2-F10).** `DrainPipe` now holds one `Decoder` across its whole read loop instead of
  decoding each 64 KiB chunk independently, so a multibyte UTF-8 character split across a read boundary decodes
  correctly. The fix is unconditionally correct regardless of where any given split lands; the proof test forces the
  split to a deterministic offset via a test-only `PipeBufferSizeOverrideForTests` seam on `CreatePipePair` (the
  default anonymous-pipe buffer is far smaller than 64 KiB and a non-overlapped `ReadFile` can return well before it
  fills, so without this seam the split's actual offset would be governed by uncontrolled OS pipe
  buffering/scheduling, not by the stub's chosen byte offset).
- **Exact teardown order (GH106-R2-F11).** `Dispose()`'s close order is now true exact reverse acquisition order:
  process handle, thread handle, environment-block buffer, command-line buffer, completion port handle, job handle,
  attribute-list buffer, handle-list buffer, stderr pipe, stdout pipe, stdin pipe (the last a no-op given the stdin
  fix above). The native API admission table and stream-draining row above reflect the F4/F2/F3 recording changes;
  this bullet is the authoritative description of Dispose's own order (superseding the prior inline code comment,
  which described an order that did not actually match true reverse acquisition).
- **New stub commands.** `report-job-membership` (F12), `read-stdin-to-eof` (F6), and `utf8-boundary` (F10) were
  added to `SeqDoc.AcceptanceTests.ProcessOwnershipStub/Program.cs`, alongside the existing command vocabulary. The
  stub remains ordinary managed code with one plain (`AllowUnsafeBlocks`-free) P/Invoke signature for
  `report-job-membership`'s `IsProcessInJob` call — finalized decision 3 is unaffected (that decision concerns the
  unsafe-pointer flag specifically, not P/Invoke generally).

## Stop conditions

Stop rather than broaden scope if safe assignment-before-resume cannot be proved, output cannot be drained without
deadlock, containment would affect unrelated processes, required behavior needs production/build/configuration
changes, or the supported Windows x64 lane is unavailable.

## Takeover focused evidence

The declared focused command completed with literal result: `Failed 0, Passed 52, Skipped 0, Total 52` on resolved
SDK `10.0.302`. The isolated-runtime test uses only explicit `DOTNET_ROOT_X64`/`DOTNET_ROOT` values and excludes
`PATH` from the child environment. The evidence shell reported `DOTNET_ROOT=` and `DOTNET_ROOT_X64=`; no machine
path is recorded. The final unfiltered gate was not run.

## Takeover review result

The complete candidate received a `BLOCK` verdict. Four High findings remain: unsynchronized `Terminate()` versus
resource release, drain handles closed beneath live tasks, missing bounded family-zero proof in explicit termination
and disposal, and ignored false timed waits during construction unwind. Three Medium findings cover `PeekNamedPipe`
error overclaim, unsynchronized mutable secondary evidence, and an isolated-runtime test/evidence contradiction.
Under the frozen takeover stop rule, no further automatic repair or final gate is permitted. See `ledger.md` for the
finding table.

## Owner recovery R3

Authority: [PR #109 owner-recovery decision](https://github.com/Bilaltariq41/SeqDoc/pull/109#issuecomment-5703147045)
and Ahmad's lease-release acknowledgment. Frozen repair base:
`952e681d2d60eecc53dfd40d81eec11b053c9f3c`. Abood owns and coordinates this one bounded recovery;
Ahmad remains the latest-head independent human reviewer and does not implement it. Qais's and Ahmad's commits,
authorship, evidence, PR #108, and PR #109 remain preserved.

Writable implementation/test paths are `tests/SeqDoc.AcceptanceTests/ProcessOwnership.cs`,
`tests/SeqDoc.AcceptanceTests/ProcessOwnershipTests.cs`, and, only when an observable child scenario requires it,
`tests/SeqDoc.AcceptanceTests.ProcessOwnershipStub/Program.cs`. This checkpoint, its ledger, the GH-106 work-item
record, generated execution projection, and the existing delegated-contribution trace may change only as needed for
truthful lifecycle and repair evidence. Any other path requires a recorded scope amendment before editing.

Non-goals are GH-107 implementation, I18/PR #103, `src/**`, packages, build/SDK/repository configuration, external
corpus work, ARM64, global/name-based or PID-only killing, unrelated refactoring, and new process-management
capability.

The repair must resolve all seven accepted findings together: serialized `Terminate`/`Dispose` handle ownership;
drain completion before pipe release; bounded family-zero proof for explicit termination and disposal; checked and
retained construction-unwind termination/wait failures; fail-closed `PeekNamedPipe` evidence; synchronized immutable
secondary evidence; and a real explicit isolated-SDK launch proof. Primary risks are handle reuse, deadlock,
background reads against released handles, descendant escape, failure-evidence loss, output-completeness overclaim,
test seam leakage, and hidden machine-registration dependence.

Existing focused coverage is 52 passing tests at the frozen base, but the prior review found the seven gaps above and
rejected its isolated-runtime proof. Add or materially strengthen approximately seven grouped tests, one per distinct
observable, reusing existing tests rather than mirroring implementation branches. The focused command remains:

```powershell
dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter FullyQualifiedName~ProcessOwnershipTests
```

Run the worker Reviewer only after the complete focused candidate passes, then stop at `ReviewRequired` for Ahmad.
The unfiltered Acceptance final gate remains the command at lines 177-179 and must not run until Ahmad's findings are
resolved. If this candidate retains any High ownership, cleanup, family-exit, handle-isolation, or unrelated-process
safety defect, preserve the branch, return GH-106 to `Blocked`, and stop without another automatic repair.

### Owner recovery R3 result

The Test Writer established the intended red baseline under isolated SDK `10.0.302`: `Failed 6, Passed 54, Skipped
0, Total 60`; F1-F6 failed for their declared lifecycle/evidence gaps and the explicit isolated-runtime F7 passed. The
single implementation candidate then reported `Failed 18, Passed 42, Skipped 0, Total 60`. Its self-review retained
High drain-lifecycle and construction-cleanup risks. The frozen stop condition therefore fired: I100-A is `Blocked`,
the candidate is preserved, and no worker Reviewer, final gate, additional repair, or GH-107 work is authorized.

## Owner redesign v2

On 2026-09-16, Abood separately authorized one bounded lifecycle redesign while Qais and Ahmad are unavailable. The
redesign continues forward on PR #109 after blocked head `f5ceff4`; every existing commit, author, review receipt,
PR #108, and PR #109 remains preserved. No force-push, rewrite, squash, deletion, or history removal is authorized.

The committed owner-recovery regressions at `2e6d3d2` are the acceptance contract. The only writable implementation
path is `tests/SeqDoc.AcceptanceTests/ProcessOwnership.cs`, plus this checkpoint, its ledger, the GH-106 record, and
generated execution projection for truthful lifecycle evidence. Tests may not be weakened or removed. The redesign
must replace the failed terminal/cleanup implementation with one explicit serialized lifecycle state machine covering
terminal-operation ownership, drain completion before handle release, bounded family-zero proof, construction-unwind
completion, fail-closed `PeekNamedPipe` evidence, synchronized immutable secondary evidence, and the already-passing
isolated-runtime behavior.

Non-goals remain GH-107, I18/PR #103, `src/**`, packages, build/SDK/repository configuration, external corpus work,
ARM64, global/name-based or PID-only killing, unrelated refactoring, and new capability. Existing focused evidence is
the 60-test lane: red-contract baseline `6 failed / 54 passed`, followed by the preserved failed R3 candidate at
`18 failed / 42 passed`. No additional tests are budgeted unless independent review identifies a concrete uncovered
regression within this exact outcome.

Run the existing focused command under isolated SDK `10.0.302`, then run one independent Reviewer-agent review. Stop
at `ReviewRequired` for a future non-author human review; do not run the unfiltered final gate, merge, close GH-106,
or begin GH-107 without that human review. If focused verification remains red or the Reviewer finds another High
lifecycle defect, return GH-106 to `Blocked` without another automatic repair.

### Owner redesign v2 result

The forward redesign changed only `ProcessOwnership.cs` and reduced the preserved R3 candidate's focused failures from
18 to 3. Its isolated-SDK result was `Failed 3, Passed 57, Skipped 0, Total 60`. The remaining failures cover
live-grandchild failure classification, fail-closed `PeekNamedPipe` prefix preservation, and required terminal-call
evidence after descendant pipe closure. The implementation also exhausted its work budget before complete self-review.
The explicit stop condition therefore fired: I100-A is `Blocked`, no Reviewer or final gate ran, and no additional
repair or GH-107 work is authorized without a new owner disposition and available non-author human review.

## Owner targeted repair v3

Abood separately authorized one final targeted repair from blocked head `839dd8e`. All commits, authorship, review
evidence, PR #108, and PR #109 remain preserved; no rewrite, force-push, squash, deletion, or test weakening is
authorized. The only implementation outcomes are: retain `ProcessFailed` for the live-grandchild wait; preserve the
evidenced prefix while treating `PeekNamedPipe` failure as incomplete with exact error evidence; restore required
terminal-operation evidence for descendant pipe closure; and remove dead/incomplete lifecycle code before complete
self-review.

Only `tests/SeqDoc.AcceptanceTests/ProcessOwnership.cs` and truthful I100-A lifecycle records may change. Tests, stub,
build/package/configuration, `src/**`, GH-107, I18/PR #103, ARM64, external corpus, and unrelated changes remain out of
scope. The existing 60-test focused lane under isolated SDK `10.0.302` must pass 60/60, followed by one independent
Reviewer-agent review and only concrete in-scope repairs. Stop at `ReviewRequired` for Ahmad; do not run the final
gate, merge, close GH-106, or begin GH-107. Any remaining focused failure or unresolved Reviewer High finding returns
GH-106 to `Blocked` permanently pending human technical direction.

### Owner targeted repair v3 result

The targeted candidate changed only `ProcessOwnership.cs`, removed the dead disposal block and obsolete helpers, fixed
live-grandchild failure classification, and preserved prefix/error evidence after injected `PeekNamedPipe` failure.
Its isolated-SDK result was `Failed 1, Passed 59, Skipped 0, Total 60`. The descendant pipe-closure scenario still did
not retain the required terminal-operation evidence. The permanent stop condition therefore fired: I100-A is
`Blocked`, no Reviewer or final gate ran, and no additional automatic repair is authorized pending human technical
direction and non-author review availability.

## Owner-directed final race fix v4

After reviewing the remaining failure, Abood authorized one human-directed fix from blocked head `beb5c3a`. Scope is
only the descendant pipe-closure race: replace timestamp/fire-and-forget inference with one atomic lifecycle decision
that permanently requires and awaits the shared terminal task when drains finish before family-zero is proven. Tests
remain frozen; all history and attribution remain preserved.

Only `tests/SeqDoc.AcceptanceTests/ProcessOwnership.cs` and truthful I100-A lifecycle records may change. No other
behavior, test, stub, build/configuration, product source, issue, or corpus work is authorized. The focused lane must
pass 60/60 under isolated SDK `10.0.302`, followed by complete diff inspection and one independent Reviewer-agent
review. Stop at `ReviewRequired` for Ahmad; do not run the final gate or merge.

### v4 independent review finding

The independent Reviewer found one High handle-lifetime gap at head `08733dd`: `DisposeCoreAsync` may release process
and job handles while an already-started `WaitAsync` still uses them for process exit or job accounting. This concrete
regression expands the v4 test allowance by exactly one deterministic barrier-based Wait/Dispose case in
`ProcessOwnershipTests.cs`; implementation remains limited to `ProcessOwnership.cs`. The repair must quiesce or retain
the in-flight wait before releasing its native handles, without holding the lifecycle lock across waits/native calls
or deadlocking terminal/drain coordination. Focused verification and a new complete Reviewer pass are required before
`ReviewRequired`.

### v4 repair result

The final race fix at `08733dd` replaced timing inference with lifecycle-gated drain completion plus authoritative
`QueryInformationJobObject(JobObjectBasicAccountingInformation)` evidence. The isolated-SDK focused lane passed
60/60. Independent review then found one High Wait/Dispose handle-lifetime gap. A deterministic barrier regression
failed 1/61 at `11267b9`; repair `37b7f71` synchronously claims disposal, blocks new waits, and quiesces or safely
retains handles for an existing wait before release. The isolated-SDK focused lane passed 61/61, and the complete
Reviewer rerun reported `PASS - NO ISSUES DETECTED`, marking the prior High finding Fixed. The checkpoint is ready for
Ahmad's latest-head human review at exact head `37b7f71597c2a4bbd4effc87df70fad18519cc89`; the final gate remains withheld.

## Ahmad latest-head findings repair

Ahmad reviewed latest PR head `a0d3ae175b6f14aa33cf2b6c28d7cac4b031287e` and blocked approval with two High
findings and one Medium finding. The accepted repair outcomes are: eliminate the pre-containment suspended-child
window with evidence-backed creation-time job admission; prevent disposal from releasing family resources without
successful family-zero proof; and synchronize teardown evidence through immutable snapshots. One deterministic
regression per observable is authorized in `ProcessOwnershipTests.cs`; implementation remains in `ProcessOwnership.cs`
and the existing stub may change only if an observable child receipt is indispensable.

Non-goals remain GH-107, I18/PR #103, `src/**`, package/build/repository configuration, ARM64, external corpus,
global/name/PID killing, and unrelated process capabilities. Research must use authoritative Microsoft Windows API
documentation. The focused lane must pass under isolated SDK `10.0.302`, followed by a complete independent Reviewer
rerun and Ahmad's latest-head formal review. The final gate remains withheld until Ahmad approves the repaired head.

### Reviewer findings on Ahmad repair candidate

Independent review of `d964c76` accepted creation-time containment and synchronized teardown snapshots as Fixed, but
found two remaining defects: retained family handles were followed by a false terminal `Disposed` state that made
ownership unreachable, and failed second-stage attribute-list initialization could call
`DeleteProcThreadAttributeList` on an uninitialized buffer. One deterministic regression per signature is authorized.
The retained-family path must use a truthful non-disposed retained state with observable ownership and retry-safe
behavior; attribute-list deletion must occur only after successful initialization.

### Blocked after second repair review

Head `7625165` passed the 65-test focused lane and fixed both preceding findings. The next complete independent review
found two remaining Medium defects: failed tracked closes zero their fields and can falsely finish as `Disposed`, and
the test-only attribute-list deletion observer can throw before native deletion and buffer release. This was the second
failed repair rerun. I100-A is therefore blocked with the branch preserved; no further implementation, final gate, or
Ahmad re-review is authorized without a separate continuation decision.

### Owner-authorized additional repair round

The owner authorized one additional repair round on the same branch. Its scope is limited to preserving tracked handle
ownership when close fails, enabling deterministic retry, and making the test-only attribute-list deletion observer
unable to interrupt native deletion or buffer release. Two focused regressions, the focused lane, and one complete
independent review are required. The final gate and Ahmad re-review remain withheld until that review is clean.

### Additional repair review result

Head `26191b8` passed the 66-test focused lane on an unchanged rerun and fixed both findings authorized for this round.
The required complete review found one further Medium defect: `ResourceReleaseObserverForTests` can throw before tracked
buffer deletion/free, fault disposal after ownership is cleared, and prevent later resource cleanup. The authorization
is exhausted, so I100-A is blocked again with the branch preserved. The earlier single drain-test failure was assessed
as fail-closed verification noise rather than a targeted-delta defect; no final gate or Ahmad request was made.

### Owner-authorized final observer repair

The owner authorized one final repair limited to preventing `ResourceReleaseObserverForTests` from interrupting tracked
buffer deletion/free. One deterministic regression, focused SDK 10.0.302 verification, and one complete independent
review are required. No other lifecycle or cleanup semantics may change. The final gate and Ahmad request remain
withheld until the review is clean.

### Final observer repair review result

Head `72016de` passed 67/67 focused tests and fixed the authorized tracked-buffer observer finding. Complete review found
one remaining Medium boundary in the excluded native-handle path: `CloseTracked` still invokes
`ResourceReleaseObserverForTests` before `CloseHandle`, so a throwing observer can skip the native close and contaminate
teardown evidence. The final authorization is exhausted; I100-A is blocked with the branch preserved and no final gate
or Ahmad request.

### Owner-authorized CloseTracked observer repair

The owner authorized one same-branch repair limited to isolating `ResourceReleaseObserverForTests` from native-handle
cleanup in `CloseTracked`. One deterministic regression, focused SDK 10.0.302 verification, and one complete
independent review are required. The final gate and Ahmad request remain withheld until that review is clean.

### CloseTracked observer repair result

Head `d09d236` passed 68/68 focused tests under isolated SDK 10.0.302. Complete independent review found no issues and
marked the final `CloseTracked` observer finding Fixed; all Ahmad and earlier reviewer findings remain Fixed. I100-A is
ready for Ahmad's latest-head formal review. The final gate remains withheld until Ahmad approves.

### Owner platform-floor amendment

Abood selected the evidence-backed repair: require Windows 10 / Windows Server 2016 x64 or newer and use
`PROC_THREAD_ATTRIBUTE_JOB_LIST` for creation-time job admission. Microsoft documents that the job-list attribute
assigns the listed jobs to the child during process creation, that its payload must remain valid until the attribute
list is destroyed, and that support begins with Windows 10 / Windows Server 2016. The attribute list therefore owns
both the exact three-handle inheritance payload and the one-job payload through `CreateProcessW`; post-creation
`AssignProcessToJobObject` is no longer the admission mechanism.
