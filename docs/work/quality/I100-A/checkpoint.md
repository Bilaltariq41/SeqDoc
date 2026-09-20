# I100-A Windows contained-process ownership primitive checkpoint

## State

`ResolvingFindings`

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
- `tests/SeqDoc.AcceptanceTests/ProcessOwnership.Resources.cs`
- `tests/SeqDoc.AcceptanceTests/ProcessOwnership.Lifecycle.cs`
- `tests/SeqDoc.AcceptanceTests/ProcessOwnership.Native.cs`
- `tests/SeqDoc.AcceptanceTests/ProcessOwnershipTests.cs`
- `tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj`
- `tests/SeqDoc.AcceptanceTests.ProcessOwnershipStub/packages.lock.json`
- `SeqDoc.slnx`
- `docs/project/delegated-contribution-workflow.md`
- `docs/work/quality/I100-A/checkpoint.md`
- `docs/work/quality/I100-A/ledger.md`
- the generated canonical work-item/execution projections, only through `tools/governance/work_state.py`

Any additional path requires a new owner decision before editing.

The three added paths are an owner-approved reconciliation of historical PR changes, not authority for unrelated
modification. `SeqDoc.slnx` admits the stub project, the stub lock file pins its restored dependency graph, and the
delegated-contribution record preserves the required implementation and repair trace.

## Native API admission table

| Element | Exact identity |
|---|---|
| Process creation | `CreateProcessW` with `CREATE_SUSPENDED \| CREATE_UNICODE_ENVIRONMENT \| EXTENDED_STARTUPINFO_PRESENT`, explicit `lpApplicationName` from a caller-resolved rooted path (never PATH/cwd search), `STARTUPINFOEXW` |
| Containment | `CreateJobObjectW` then `SetInformationJobObject(JobObjectExtendedLimitInformation)` with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` and breakaway denied (silent-breakaway flags omitted); `PROC_THREAD_ATTRIBUTE_JOB_LIST` admits the job atomically during suspended `CreateProcessW`, before any child instruction or `ResumeThread` |
| Handle policy | `bInheritHandles = TRUE` at `CreateProcessW`, but only the 3 std handles are inheritable (`SetHandleInformation(HANDLE_FLAG_INHERIT)` set only on the pipe ends actually passed); the job handle itself is non-inheritable |
| Completion/active-zero proof | IOCP associated to the job (`SetInformationJobObject(JobObjectAssociateCompletionPortInformation)`), observing `JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO`; inability to prove active-zero within its bound (production default 10s) is itself recorded as `ProcessFailed`, both on the normal-exit path and after a forced termination (GH106-R2-F2/F3) |
| Termination | `TerminateJobObject` (relies on `KILL_ON_JOB_CLOSE` for normal `Dispose`, explicit `TerminateJobObject` for timeout/cancellation/unwind, each call site's return value checked and a failure recorded as `ProcessFailed` — GH106-R2-F4) |
| Stream draining | Two synchronous, thread-pool-offloaded pipe reads (`FileStream.Read` off `Task.Run`, matching the in-repo IR-1 `RunProcess`/`RunGit` idiom — anonymous pipes from `CreatePipe` do not support `FILE_FLAG_OVERLAPPED`, so true overlapped/async I/O is not available here) started before `ResumeThread` returns control to the wait loop; bounded in practice by `WaitAsync` racing drain completion against its own timeout/cancellation token and forcing `TerminateJobObject` (closing every inherited handle, including a silent descendant's) to unblock an in-flight blocked read if the deadline is reached first; both must reach EOF or truncation is recorded |
| Supported platform | Windows 10 / Windows Server 2016 x64 or newer. Older Windows, x86, ARM64, Linux, and macOS fail closed via an explicit runtime guard, never silently skipped |
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

1. `ProcessConstructionFailed` — any S0/S1 failure before/at resume (create, job creation/admission, pipe/attribute-list/
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
4. creation-time job admission before resume (proven via an observable stub receipt — the stub's own
   `report-job-membership` command calls `IsProcessInJob` against its own process handle as the very first action
   it takes and prints `IN-JOB:True`/`False` as its first stdout line; the suspended post-create observer separately
   proves the process is already in the exact job before resume — GH106-R2-F12)
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

### Ahmad latest-head re-review result

Ahmad reviewed exact PR head `6fee7f5271a4ee7ce5d4626af02e631f6d36f51d` and returned `BLOCK`. The High finding
is that exceptional construction unwind can record failed termination/wait, then close or retain resources without a
bounded process-family-zero proof. The focused regression checks managed-task quiescence but not family exit. Ahmad
also found a Medium frozen-allowlist discrepancy for `SeqDoc.slnx`,
`docs/project/delegated-contribution-workflow.md`, and the stub `packages.lock.json`, plus Low stale post-create
assignment wording/seams. The frozen High-severity stop condition fired: I100-A is Blocked, the branch and attribution
are preserved, and no final gate or GH-107 work is authorized.

### Owner-authorized Ahmad findings repair

The owner authorized one bounded same-branch repair for Ahmad's latest-head findings. Construction failure after child
creation must not return until the process family is proven at zero, or must preserve a reachable owner of every native
and managed resource required to finish containment and cleanup; closing a process handle or relying on asynchronous
job close without bounded proof is insufficient. The repair must exercise failed direct termination and failed process
wait while proving job-level termination and family-zero evidence, with no unrelated-process action and no premature
resource release. The exact three historical paths above are now admitted for their existing checkpoint purposes.
Stale post-create assignment wording and the unused native assignment seam must be removed; tests should prefer typed
ownership and evidence observables over new private-field coupling. Run the focused SDK 10.0.302 lane and a complete
independent review, then stop for Ahmad. The final gate and GH-107 remain prohibited until Ahmad approves.

#### Independent review finding on the repair candidate

Review of `a38fb31` found one High transfer-atomicity gap: owner fields were populated before the transferred cleanup
delegate was allocated and pushed, so an exception in that interval could leave the old unwind path releasing a family
without proof while the populated owner was unreachable. The accepted repair must allocate the owner, ownership token,
and transferred cleanup action before native acquisition; all old unwind actions must consult the same atomic token;
and one token transition must select exactly one owner. A deterministic fault immediately after transfer and before
monitor startup must prove the preinstalled cleanup owner handles the family. Ahmad's Medium and Low findings remain
Fixed. The final gate and GH-107 remain prohibited.

#### Atomic transfer repair result

Head `495208f` preallocates the empty owner, transfer token, and bottom cleanup action before native acquisition. Every
old unwind action consults that token, and a single atomic transition selects the transferred owner without rebuilding
the stack. A deterministic post-transfer/pre-monitor fault proves explicit job termination, bounded family-zero proof,
and release chronology while a duplicated job handle defeats kill-on-close. The focused SDK 10.0.302 lane passed 70/70.
Complete independent review returned PASS with no findings and marked Ahmad's High, Medium, and Low findings Fixed.
I100-A is ReviewRequired for Ahmad; the final gate and GH-107 remain withheld.

#### Ahmad latest-head review at `cd7d103`

Ahmad returned `BLOCK` with two High findings and one Medium finding; local inspection confirmed all three. First,
pre-transfer `CloseHandle` results are ignored and locals are zeroed, so a failed parent pipe-write close loses
ownership and can prevent drain EOF even after job termination. Second, the exited-child race records `ProcessFailed`
when drains win just before `ACTIVE_PROCESS_ZERO` publication, before awaiting the already-started bounded family-proof
task; this is a concrete cause of the disclosed intermittent `DrainIncomplete` versus `ProcessFailed` result. Third,
stub resolution walks from `AppContext.BaseDirectory` to a literal `tests` ancestor, so relocated or shadow-copied test
output fails independently of the primitive. I100-A is Blocked; preserve the branch and do not run the final gate or
GH-107 without owner disposition.

### Frozen ownership-ledger redesign plan

Ahmad approved option 2—the table-driven ownership ledger with one lifecycle coordinator—as the design direction at
https://github.com/Bilaltariq41/SeqDoc/pull/109#issuecomment-5731669533. This section incorporates every condition of
that approval and is the frozen implementation plan. The existing public `ContainedProcess`, construction result,
wait result, options, and failure-class interfaces remain compatible; the redesign is internal.

#### Exact resource inventory

The coordinator preallocates typed slots before the first native acquisition. Native slots are: stdin child-read,
stdin parent-write, stdout parent-read, stdout child-write, stderr parent-read, stderr child-write, handle-list buffer,
job handle, completion-port handle, job-list buffer, initialized attribute-list buffer, command-line buffer,
environment-block buffer, process handle, and primary-thread handle. Managed slots are: completion-monitor CTS/task,
drain CTS, stdout-drain task, stderr-drain task, process-wait task, terminal-operation task, family-proof task, and
disposal task.

Each native slot records typed kind, stable acquisition sequence, native value, ownership state, release prerequisite,
attempt count, and exact error/evidence. A failed release remains `Owned`, retains the unchanged native value, records
the exact failed attempt, and is eligible for deterministic retry. `Disposed` is legal only when every native slot is
released and every required managed slot is quiescent. Attribute-list deletion precedes its job-list and handle-list
payload release. No label string selects native behavior.

#### Lifecycle and operation epochs

One lifecycle coordinator is the only writer of lifecycle and failure classification. It creates monotonically
increasing operation IDs for wait, terminal, family-proof, drain, and disposal epochs. Every asynchronous completion
carries its operation ID; a completion is ignored when its ID no longer matches the active slot, preventing stale work
from mutating a later lifecycle epoch.

Family proof is monotonic and retryable: `Unknown` may become `Proven`; timeout is `TimedOutUnproven`, never proof of
activity and never `Proven`. Later proof epochs remain legal until zero is proven. Drain completion and immediate child
exit are facts only and cannot classify family state. Wait classification awaits the shared bounded family-proof result
before recording `ProcessFailed`; terminal enforcement follows only a bounded unproven result or exact job accounting
that still reports activity.

Only one terminal attempt may be in flight. Concurrent callers join it. A failed native terminal attempt permits a
later terminal epoch. A successful native termination is not repeated merely because family proof is delayed; later
epochs retry proof only. Construction failure, normal disposal, and retained cleanup all use the same ledger release
engine and immutable ordered evidence snapshots.

#### Exact staged-stub contract

`SeqDoc.AcceptanceTests.csproj` defines `ProcessOwnershipStubStageDir` as
`$(TargetDir)process-ownership-stub\` and an exact `StageProcessOwnershipStub` target running after `Build` and before
`VSTest`. The target removes the prior stage directory, invokes MSBuild `Build` on
`SeqDoc.AcceptanceTests.ProcessOwnershipStub.csproj` with the current `Configuration` and `TargetFramework`,
`OutputPath=$(ProcessOwnershipStubStageDir)`, `AppendTargetFrameworkToOutputPath=false`, and
`AppendRuntimeIdentifierToOutputPath=false`, then fails when the staged apphost executable is absent. Building directly
into that directory stages the executable, DLL, deps/runtimeconfig files, and runtime dependencies together.

Tests resolve only
`Path.Combine(AppContext.BaseDirectory, "process-ownership-stub", "SeqDoc.AcceptanceTests.ProcessOwnershipStub.exe")`.
They do not inspect repository, configuration, TFM, `bin`, or `tests` directory names. A relocation test copies the
staged directory and proves resolution/launch independent of checkout layout. `ReferenceOutputAssembly=false` remains;
the stub contributes no compile-time types.

#### Frozen tests and stop rule

All existing 70 focused tests remain; no assertion may be weakened or removed. Add at most six grouped claims:

1. every pre-resume parent-copy close failure remains owned and retries without resume;
2. every construction fault leaves each acquired slot with exactly one reachable owner;
3. drain-first/family-proof-later ordering cannot produce a false `ProcessFailed`;
4. bounded unproven family state and terminal retry/concurrency are deterministic across operation epochs;
5. retained release failures preserve value, error, retry evidence, reverse order, and immutable snapshots;
6. staged stub resolution and launch survive relocated output.

One Test Writer establishes red evidence, then one implementation owner delivers the complete candidate across the
typed pipeline and removes the old raw-field/unwind/classification paths. Focused verification remains:

```powershell
dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter FullyQualifiedName~ProcessOwnershipTests
```

If focused verification remains red after the bounded repair candidate, or independent review finds a new High defect
in ownership, release retry, lifecycle epochs, family proof, terminal serialization, or staging, return I100-A to
`Blocked` with the branch preserved. If green and independently clean, stop at `ReviewRequired` for Ahmad. Do not run
the final gate, merge, close GH-106, or begin GH-107 before Ahmad approves.

The owner authorized implementation of this exact frozen plan at `50b70e4`. No broader architecture, path, test
budget, final gate, merge, or GH-107 work is authorized.

#### Frozen redesign implementation stop

The single bounded implementation candidate did not reach green before its implementation budget ended. The focused
command exceeded 120 seconds and reported four explicit failures: synchronized teardown did not retain failed-close
ownership; the typed ownership snapshot exposed only a fallback entry instead of the retained resource; the real
drain/family barrier still classified `ProcessFailed`; and relocated stub output differed from the asserted payload.
The ledger, coordinator, and staging target are partial. No matching test process remained running when checked, but
the timeout path may have retained tasks/resources during the run. The frozen stop rule fired: I100-A is Blocked with
the complete worktree, untracked partial files, and generated staging directories preserved. No transfer, further
repair, final gate, merge, or GH-107 work is authorized without a new owner decision.

The owner authorized one bounded continuation on the preserved worktree for only the four recorded failures: checked
close ownership, real ledger snapshot publication/retry, drain-family classification, and relocated output assertion.
The same implementation owner must remove generated staging directories from repository paths and run focused
verification. Continue to independent review only if green; return to Blocked if red or if review finds a High safety
defect. The final gate and GH-107 remain prohibited.

#### Bounded continuation stop

The same implementation owner removed both generated source-tree staging directories and continued the four named
repairs. The last verified focused run was 72 passed, 4 failed, 0 skipped: the known drain test and deterministic
barrier still classified `ProcessFailed`, synchronized teardown did not retain typed failed-close ownership, and the
failed-release snapshot contained no matching retained entry. Later `HasOwned`/`RetryOwned`, deferred parent-close
retry, disposal retry, and owned-resource detection edits were not verified before the bounded continuation ended.
No matching test/stub process remained running when checked, and both generated source-tree staging directories are
absent. I100-A is Blocked with the unverified worktree preserved; no further command, transfer, final gate, merge, or
GH-107 work is authorized without a new owner decision.

The owner authorized a fresh checkpoint-builder takeover of the preserved unverified worktree. Scope is limited to the
two drain/family classification failures and the two failed-close ledger ownership/snapshot failures. The fresh owner
must inspect rather than trust prior edits, run focused verification, and proceed to independent review only if 76/76
is green. Remaining red or a new High safety finding returns I100-A to Blocked. The final gate and GH-107 remain
prohibited.

#### Fresh takeover stop

The fresh checkpoint-builder improved the last completed focused result to 74 passed, 2 failed, 0 skipped. Both
remaining failures were descendant-family semantics: direct wait against a live grandchild returned `None` instead of
`ProcessFailed`, and descendant pipe EOF was mistaken for family exit. The owner then added unverified drain-completion
tracking and post-proof classification changes; the current tree does not compile because IDE0011 requires braces at
`ProcessOwnership.cs` lines 1605-1606. No matching SeqDoc test/stub process remained at inspection, and generated
source-tree staging directories remain absent. The takeover stop rule returned I100-A to Blocked with the worktree
preserved. No further repair, verification, transfer, final gate, merge, or GH-107 work is authorized without a new
owner decision.

The owner authorized one final bounded takeover limited to the current IDE0011 brace errors and the two remaining
descendant-family focused failures. Focused verification must reach 76/76 before independent review; otherwise I100-A
returns to Blocked. The final gate and GH-107 remain prohibited.

#### Final bounded takeover stop

The isolated SDK 10.0.302 focused command completed without timeout at 75 passed, 1 failed, 0 skipped. The direct live
grandchild case is now green. `ParentExitWithDescendantPipeClosureStillTerminatesFamilyAndProvesActiveZero` remains red:
descendant pipe EOF still suppresses the required family-active classification and terminal enforcement. No matching
test/stub process remained at inspection, generated source-tree staging directories remain absent, and `git diff
--check` passed. The explicit stop condition returned I100-A to Blocked with the complete worktree preserved. No
independent review, final gate, merge, or GH-107 work ran.

The owner authorized one surgical repair solely for the remaining descendant-closes-pipes failure. Pipe EOF must not
substitute for bounded family proof; an unproven active family must retain `ProcessFailed`, invoke terminal cleanup, and
prove `ACTIVE_PROCESS_ZERO`. Focused verification must reach 76/76 before independent review; otherwise I100-A returns
to Blocked. The final gate and GH-107 remain prohibited.

#### Independent redesign review stop

Head `7f0b4a1` passed 76/76 focused tests, but mandatory complete review found three new High defects. I100-A-F6: the
new lifecycle coordinator is exercised only by reflection tests; production still classifies through the legacy
lifecycle gate/tasks/failure tracker. I100-A-F7: pre-transfer raw unwind remains a parallel owner, and a failed raw
release can be swallowed while the transferred action no-ops, returning no `CleanupOwner`. I100-A-F8: duplicate
acquisition is silently ignored and a failed release of an already released slot can synthesize new ownership, breaking
stable slot identity. The frozen High stop rule returned I100-A to Blocked. No Ahmad request, final gate, merge, or
GH-107 work is authorized.

The owner authorized repair of all three independent High redesign findings on PR #109. Scope is limited to making
the lifecycle coordinator the actual production authority, making the typed ledger the sole owner before and after
transfer, and rejecting duplicate acquisition or stale release without synthesizing ownership. Existing six grouped
tests may be strengthened but the 76-test cap remains. Focused verification and complete independent review are
required before Ahmad; the final gate and GH-107 remain prohibited.

#### F6/F7/F8 repair-limit stop

The strengthened existing groups produced 73/76 with exact red evidence for production coordinator use, reachable
pre-transfer ownership, and strict slot identity. Two implementation attempts then exhausted their execution limits
without a compilable candidate. The preserved tree partially removes legacy lifecycle fields and unwind scaffolding,
but coordinator compatibility/authority, typed construction cleanup, raw mirror handling, and a malformed ownership
predicate remain incomplete. No focused command ran after these edits. No matching SeqDoc test/stub process or generated
source-tree staging directory was present at inspection, and `git diff --check` passed. The two-repair limit returned
I100-A to Blocked; no continuation, transfer, revert, compile, gate, Ahmad request, merge, or GH-107 work is authorized
without a new owner decision.

The owner authorized preserving the incomplete three-file diff externally, restoring only those files to committed
head, and implementing F8, F7, and F6 as three bounded sequential stages. The recovery artifact identifier is
`I100-A-f6-f7-f8-incomplete-20260918.patch`, 51,590 bytes, SHA-256
`210A193A515F94805A6C06697301D10B7EA96A8BB5C3F2AA81F9C5C8D900732B`; it is external recovery evidence, not repository
content, and no machine-local location is authoritative. The exact restore completed with a clean worktree while retaining committed red tests. Each stage gets focused
subset verification; the full 76-test lane and independent review run only after integration. The final gate and GH-107
remain prohibited.

#### Sequential F8/F7/F6 integration stop

F8 strict slot identity, F7 sole ledger ownership, and F6 production coordinator authority were integrated in the
preserved worktree. The isolated SDK 10.0.302 focused command compiled the candidate and reported `Failed 7, Passed 69,
Skipped 0, Total 76`. All seven failures share one regression signature: the four construction-time parent-copy
releases (`stdin child`, `stdout child`, `stderr child`, and `stdin parent`) are published through the disposal/unwind
observation streams, which contaminates the frozen exact release-order assertions; the transfer-boundary test also
rejects those observations because they are not retained-owner cleanup steps. The frozen red stop rule returned I100-A
to Blocked with the six modified files preserved. No independent review, final gate, Ahmad request, merge, or GH-107
work ran.

The owner authorized one bounded repair of this single regression signature. Successful-construction parent-copy
releases must remain exact ledger release events but must not enter the teardown/unwind observer streams reserved for
construction-failure cleanup and disposal. All 76 tests are frozen; no assertion may be weakened or removed. Run the
complete focused lane and continue to independent review only at 76/76. The final gate, Ahmad request, merge, and
GH-107 remain withheld.

The first bounded repair removed the four early parent-copy observations and improved focused verification to 74/76.
The two remaining failures are deeper assertions from the same original seven: failed cleanup attempts are absent from
the immutable teardown chronology, and the process/thread ledger acquisition sequence reverses their frozen release
order during construction unwind. One final repair round may change only `ProcessOwnership.cs` and
`ProcessOwnership.Resources.cs` to preserve attempted-release chronology and acquisition-ordered snapshots while
restoring dependency-safe reverse release. Remaining red returns I100-A to Blocked.

The final repair restored failed-attempt chronology and process-before-thread reverse release, improving the focused
result to `Failed 1, Passed 75, Skipped 0, Total 76`. The sole remaining failure is
`PartialConstructionUnwindClosesResourcesInExactTrueReverseAcquisitionOrder`: it expects a final `stdin pipe handle`
unwind observation, but successful construction already released the stdin parent copy and the new boundary correctly
withheld that early event, leaving no owned stdin slot during later fault cleanup. Resolving whether the immutable
chronology should defer that real earlier release or revise its phase interpretation requires a new owner decision. The
two-round stop rule returned I100-A to Blocked; no independent review or final gate ran.

The owner authorized one frozen-test contract amendment for this contradiction. The partial-unwind test must remove the
already released stdin parent handle from its cleanup-order expectation and replace that stale event with typed snapshot
proof that `StdinParentWrite` was acquired, released exactly once during construction, and is no longer owned. Production
implementation is frozen. The suite remains 76 tests; focused verification and independent review are required before
any later gate or Ahmad request.

The authorized amendment changed only `ProcessOwnershipTests.cs`: the stale stdin unwind entry was replaced by exact
typed snapshot assertions for positive acquisition sequence, `Released` state, one release attempt, and zero current
value. The isolated SDK 10.0.302 focused lane passed 76/76 with zero skipped. Implementation is complete and moves to
independent review; the final gate remains withheld until findings are resolved.

Independent complete-candidate review returned five findings. F1 High: a faulted first wait task is cached permanently.
F2 High: production lacks the frozen wait, drain, and disposal operation epochs and stale-completion admission. F3
Medium: several native lifecycle decisions still use raw mirrors rather than ledger-owned values. F4 Medium: production
coverage does not prove proof-only terminal retry after an initially unproven family epoch. F5 Medium: the amended stdin
assertion proves final release state but not that release preceded later unwind operations. The frozen rule at lines
603-605 requires Blocked when independent review finds a new High ownership/lifecycle defect. The branch is preserved;
no finding repair, final gate, Ahmad request, merge, or GH-107 work is authorized without a new owner decision.

The owner authorized one cohesive repair epoch for I100-A-F1 through F5 on the preserved branch. Existing tests may be
strengthened but the suite remains 76 total. The repair must add production wait, drain, and disposal epochs with stale
completion rejection; prevent permanent caching of faulted waits; use ledger-owned native values for lifecycle calls;
prove production proof-only terminal retry after native success; and prove stdin release chronology. Focused verification
and a new independent complete-candidate review are required. The final gate remains withheld.

The Test Writer retained 76 tests and established a 74/76 red baseline for F1/F2 while strengthened F4/F5 production
claims passed. The implementation repair added exact per-kind production epochs and stale completion admission, made
faulted wait and terminal reservations retryable, sourced lifecycle native values from ledger-owned snapshots, and
preserved proof-only terminal retry plus typed stdin chronology. Orchestrator inspection corrected cross-kind completion
coupling before review. The final focused SDK 10.0.302 run passed 76/76 with zero skipped and `git diff --check` passed.
F1-F5 are Fixed pending independent confirmation; no final gate has run.

The new independent complete-candidate review returned PASS with no findings and confirmed F1-F5 Fixed. Residual risk
is limited to the intentionally unrun final gate and later real Windows-native verification outside the focused seams.
I100-A remains ReviewRequired and stops for Ahmad. Do not run the final gate, merge, close GH-106, or begin GH-107
before Ahmad approval.

### Ahmad latest-head review at `d52df4e`

Ahmad accepted the canonical T2/T3 authorization path, reviewed the complete range
`ab6e3e1cf16213ee5346506b16949fa32c4ddfa4..d52df4e9b02d0450e9b4f43e474b740ae93972ae`, and returned BLOCK at
https://github.com/Bilaltariq41/SeqDoc/pull/109#issuecomment-5742808303. High: the eight frozen managed-resource slots
are not represented in the typed ledger or immutable snapshot, leaving raw task/CTS fields as a second ownership
authority. Medium: redesign acceptance uses string reflection and fallback member names rather than direct typed
evidence. Low: the native close adapter is unused, and the recovery record exposed a machine-local path. Earlier
native ownership, family proof, transfer, retry, and synchronization findings were confirmed Fixed. Repair all four
findings together, keep 76 focused tests, and request another latest-head review only after focused and independent
review pass. The final gate and GH-107 remain prohibited.

The Test Writer retained 76 tests, removed redesign fallback reflection, and established compile-red evidence for the
missing managed types and snapshot. The implementation added the eight typed slots, direct coordinator evidence,
managed epoch/state/evidence transitions, and adapter-routed native close behavior. The machine-local recovery path was
replaced by its repository-neutral artifact identifier and digest. Focused verification under isolated SDK 10.0.302
passed 76/76 with zero skipped; `git diff --check` passed. The candidate now requires independent adversarial review of
sole managed ownership, stable identity, stale completion, and Disposed legality before publication or Ahmad rereview.

Independent review returned FINDINGS. F1 High: managed slots contain metadata only; actual CTS/task leases remain in raw
fields and still drive cleanup, so the ledger is not sole managed owner. F2 High: drain/monitor epochs can publish
synthetic Active slots before resource installation without rollback, and `CompleteDispose` can enter Disposed without
rejecting Active/Retained managed slots. The direct typed evidence Medium and both Low findings are Fixed. Repair F1/F2
with ledger-owned leases, atomic installation/rollback, ledger-driven cleanup, and deterministic existing-test coverage
for startup failure and Disposed legality, then rerun focused verification and independent review.

The second Test Writer pass added deterministic existing-test coverage for actual lease presence, drain-install rollback,
stable retry identity, and active-monitor disposal blocking without increasing the 76-test total. The repair moved exact
CTS/task leases into managed slots, made drain/monitor publication atomic, drove cancellation/await/release through typed
leases, rejected Disposed while Active/Retained slots remain, disposed detached leases outside locks, and closed the
concurrent-disposal finalization window. One observability regression was repaired by retaining completed task mirrors
without consulting them for ownership decisions. Focused SDK 10.0.302 verification passed 76/76 with zero skipped and
`git diff --check` passed. F1/F2 are Fixed pending independent confirmation.

The new independent review returned one High finding. Although slots now own concrete leases, `Task.Run` schedules drain
and monitor delegates before typed lease publication; the delegates begin and block on a publication task while still
temporarily unowned. Atomic rollback and Disposed legality are otherwise fixed, as are Ahmad's eight-slot High, direct
evidence Medium, and both Low findings. Two repair rounds are exhausted and the frozen High-review stop rule applies.
I100-A returns to Blocked with the worktree preserved; no further repair, publication, Ahmad request, final gate, or
GH-107 work is authorized without a separate owner decision.

The owner separately authorized one exact continuation: replace pre-publication `Task.Run` workers with non-running
reservation tasks installed in the typed drain/monitor slots first, then schedule real workers and bridge their exact
success, fault, and cancellation into those reservations. Existing tests must prove no worker delegate begins before
slot publication; the suite remains 76 tests. Focused verification and a clean independent rereview are required before
publishing a new head or requesting Ahmad. The final gate and GH-107 remain prohibited.

The existing construction group now proves monitor/stdout/stderr worker delegates have not begun at each install
boundary and later do begin after publication, with stable lease identities. Production installs non-running reservation
tasks first, then schedules workers and bridges exact result, fault, or cancellation into those registered reservations;
epoch completion observes the reservations. Install/scheduling rollback is typed and operation-ID scoped. Focused SDK
10.0.302 verification passed 76/76 with zero skipped and `git diff --check` passed. The exact High is Fixed pending a new
independent review; no publication or final gate has run.

Independent rereview accepted normal reservation-first ordering but found one High partial-scheduling hole: if stdout
scheduling succeeds and stderr scheduling fails, rollback cancels/detaches reservation leases without awaiting the
already-running stdout worker, allowing it to race native pipe release. Add deterministic existing-test scheduler
failure evidence and retain/quiesce every successfully scheduled worker before rollback or native release. This is the
second repair round under the separate authorization; remaining High returns I100-A to Blocked.

The existing construction group now injects a scheduler that starts stdout and throws before scheduling stderr, then
proves `Start` does not return until stdout finishes. Production retains every returned worker task, cancels and waits it
outside locks before exact-ID rollback, and retains typed cleanup ownership if bounded quiescence fails. Focused
verification under isolated SDK 10.0.302 passed 76/76 with zero skipped; `git diff --check` passed. The partial-scheduler
High is Fixed pending the final independent rereview.

Final independent rereview returned one High. Rollback now retains and quiesces returned worker tasks, but successful
startup discards the scheduler-returned worker `Task` after binding only the reservation task into the ledger. The
reservation tracks result/fault, yet the actual scheduled task itself is not reachable from typed ownership for its full
lifetime. The second separately authorized repair round is exhausted. I100-A returns to Blocked with the worktree
preserved; no publication, Ahmad request, final gate, or GH-107 work is authorized without another owner decision.

The owner authorized the preferred smallest redesign: remove separate scheduler-returned worker Tasks entirely. Install
typed reservation Tasks first, queue worker callbacks directly, and bridge every callback's result/fault/cancellation
into the exact reservation, which remains the sole task authority. Existing deterministic partial-queue failure coverage
must be adapted without increasing the 76-test total. Focused verification and independent rereview remain mandatory;
publication, final gate, and GH-107 remain prohibited until clean.

The existing partial-scheduler scenario now uses direct queue admission: stdout is admitted, stderr returns false, and
`Start` proves stdout's exact reservation settles before rollback. Production creates no drain/monitor worker Tasks;
typed reservation Tasks are installed first and are the sole completion authority for directly queued callbacks.
Success, fault, and cancellation bridge into reservations; completed faulted/canceled reservations count as quiescent
only after exact observation, while timeout retains ownership. Focused SDK 10.0.302 verification passed 76/76 with zero
skipped and `git diff --check` passed. The successful-path worker ownership High is Fixed pending independent rereview.

Independent rereview confirmed the successful-path and partial-admission High findings Fixed and accepted every Ahmad
High/Medium/Low disposition. One Medium coverage finding remains: the focused suite deterministically rejects stderr
after stdout admission but does not force CompletionMonitor queue admission false. Add that assertion to the same
construction/admission group, keep 76 tests, and rerun focused verification plus independent review.

The existing group now rejects CompletionMonitor queue admission, proves the callback never begins, and proves the
installed typed reservation rolls back to Released/no lease while native cleanup remains reachable. Focused SDK 10.0.302
verification passed 76/76 with zero skipped; `git diff --check` passed. F9 is Fixed pending independent confirmation.

Independent review proposed a High throw-after-queue scenario for the internal queue seam. Disposition: Rejected pending
independent confirmation. The exact seam contract is atomic admission: `true` means queued, `false` means not queued,
and an exception occurs before queue admission. The production implementation calls the bool-returning
`ThreadPool.QueueUserWorkItem` directly; it has no scheduler object that can return control by throwing after a successful
queue. A test hook that deliberately queues and then throws violates its contract just as a native-call seam that reports
failure after an undocumented successful side effect would. False admission and partial admission are both deterministic
focused cases. No code or test change is warranted for a contract-breaking test implementation.

Independent review accepted the F9 Rejected disposition and returned PASS with no other findings. All Ahmad High,
Medium, and Low findings and all accepted independent findings are Fixed. Focused evidence remains 76/76 under isolated
SDK 10.0.302. The candidate remains ReviewRequired; explicit commit/push authorization is required before publishing the
eight-file diff and requesting Ahmad's latest-head review. The final gate remains unrun and prohibited.

### Bilal one-time review reservation and preparatory findings

Bilal accepted one complete latest-head independent human review after technical repair, current-main integration, and
the governance-only handoff correction. His preparatory inspection of `6de3f74` found: High, infinite/non-positive wait
or drain bounds violate finite cleanup; Medium, the ambient-environment test does not restore prior process state;
Medium, strict native-ledger proof still uses reflection/string lookup; Medium, reservation ordering is asserted before
installation rather than at actual queue admission; and a residual evidence gap, `GetExitCodeProcess` failure omits the
captured Win32 error. Repair these without weakening the 76 existing tests, run the focused lane and complete-candidate
independent review, then stop before commit/current-main integration/governance handoff/final gate. Bilal's review-only
findings do not make him a candidate contributor or consume his reserved independence.

The Test Writer retained all 76 tests and added red evidence inside existing groups for finite positive wait/drain
bounds, exact ambient-state restoration with nonparallel isolation, direct typed ledger strictness, lease publication at
the actual queue-admission boundary, and exact `GetExitCodeProcess` error evidence. Production now validates deadlines
before native acquisition/use, invokes the queue observer after typed lease installation and immediately before queueing,
and captures the native exit-code error at the call boundary. Focused SDK 10.0.302 verification passed 76/76 with zero
skipped; `git diff --check` passed. Bilal's preparatory findings are Fixed pending independent review.

Independent review accepted the direct typed ledger, queue-admission, and exact exit-code evidence findings Fixed. Two
Medium proof gaps remain: the finite-bound tests omit the first rejected value above the supported maximum
(`uint.MaxValue` milliseconds), and ambient restoration is not asserted after cleanup for both absent and pre-existing
host values. Strengthen those two existing groups without increasing the 76-test count; production behavior is unchanged.

The two existing groups now prove the first rejected timeout above the supported maximum for wait and drain, valid wait
retry after all rejected reservations, deterministic absent and pre-existing ambient states, per-state restoration, and
final restoration of the host's original value. The initial Test Writer command exceeded its 120-second tool allowance
without a test result; the same focused command under isolated SDK 10.0.302 completed in 1 minute 16 seconds with 76/76
passing and zero skipped. `git diff --check` passed. Both independent-review proof gaps are Fixed pending rereview.

Independent rereview passed with no findings. It accepted I100-A-F1 and I100-A-F2 Fixed, accepted all five Bilal
preparatory findings Fixed, preserved all 76 prior claims, and accepted the exact technical scope. Stop before commit,
current-main integration, governance-only handoff correction, Bilal request, or final gate.

The reviewed repair was preserved in `13c20c3`, then current `main` at `b66b0db` was integrated cleanly by merge commit
`580353d`. Post-integration focused verification under isolated SDK 10.0.302 passed 76/76 with zero skipped in 1 minute
16 seconds. Governance validation passed for all 51 work items, `git diff --check` passed, and `b66b0db` is an ancestor
of the latest candidate. Return the candidate to `ReviewRequired` for Bilal's reserved latest-head non-author human
review. The final gate remains prohibited until that review is resolved.

Current `main` advanced again through PR #110. Merge commit `26ee63e` integrates `fa20535`; its only conflict was the
generated `docs/project/execution.json`, which was regenerated from authoritative GH-106 state under schema v2.
Post-integration verification passed the focused ProcessOwnership lane 76/76 with zero skipped in 1 minute 16 seconds,
validated all 51 work items, confirmed the execution projection current, passed `git diff --check`, and proved current
`origin/main` is an ancestor. Independent post-integration review returned PASS with no findings and preserved all 76
claims. An optional governance-suite run reached its repository snapshot test but raised `MemoryError` while retaining
every non-`.git` file in memory; it reproduced after `dotnet clean`, is outside I100-A, and is non-blocking current-main
governance resource-exhaustion evidence.

Bilal's authenticated `OWNER-BYPASS v1` equivalent for PR #109 is
https://github.com/Bilaltariq41/SeqDoc/pull/109#issuecomment-5752397694. It acknowledges reviewer exhaustion, authorizes
Qais or Ahmad to perform the final latest-head technical review despite prior candidate contribution, requires that the
final pusher not approve that push, prohibits describing the review as independent, and supersedes Bilal's reservation.
Abood will push the final candidate; Ahmad did not push it and is the requested final technical reviewer. The
transactional `handoff` command cannot represent this PR-specific exception because it mechanically rejects the PR author
as peer, so the authenticated bypass and exact role separation are recorded here and in the ledger without inventing an
independent reviewer. Ahmad must post exact-head `PASS` or `BLOCK`; only PASS permits transition to `Verifying` and the
single final gate.

Ahmad returned exact-head `BLOCK` on `1af4eddb03812cdb23342494f60a14133b6b55d5` at
https://github.com/Bilaltariq41/SeqDoc/pull/109#issuecomment-5752921086. He accepted the five Bilal repair outcomes and
raised two bounded findings: F10, required CI exposes that the real-repository projection assertion derives its expected
idle state from normalized synthetic fixtures instead of `ws.load(ROOT)`; F11, the existing xUnit 2.9.3
`DisableParallelization` guarantee needs a concise adjacent comment and durable evidence. Repair only those findings,
preserve the existing non-mutation assertions and 76 tests, require green CI and rereview, and keep the final gate unrun.

### Owner platform-floor amendment

Abood selected the evidence-backed repair: require Windows 10 / Windows Server 2016 x64 or newer and use
`PROC_THREAD_ATTRIBUTE_JOB_LIST` for creation-time job admission. Microsoft documents that the job-list attribute
assigns the listed jobs to the child during process creation, that its payload must remain valid until the attribute
list is destroyed, and that support begins with Windows 10 / Windows Server 2016. The attribute list therefore owns
both the exact three-handle inheritance payload and the one-job payload through `CreateProcessW`; post-creation
`AssignProcessToJobObject` is no longer the admission mechanism.
