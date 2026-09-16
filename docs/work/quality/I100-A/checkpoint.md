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

1. **Windows/platform floor.** Supported platform is Windows **x64 only**, matching the frozen issue body verbatim
   ("fails closed outside supported Windows x64. ARM64, x86, Linux, and macOS are unsupported in this checkpoint.").
   This corrects the readiness packet's provisional "x64 and ARM64" table entry, which conflicted with the frozen
   issue body: ARM64 is out of scope for I100-A, not merely undecided. No separate Windows version/SKU floor is
   introduced: every native API this primitive uses (`CreateProcessW`, `CreateJobObjectW`, `SetInformationJobObject`
   with `JobObjectExtendedLimitInformation`/`JobObjectAssociateCompletionPortInformation`,
   `AssignProcessToJobObject`, `ResumeThread`, `TerminateJobObject`, `SetHandleInformation`) has been available since
   Windows XP/Server 2003, well below the floor already implied by the pinned `net10.0` SDK's own minimum supported
   Windows version. The runtime admission guard is `OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture
   == Architecture.X64`; anything else fails closed through the platform-admission claim rather than skipping it.
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
