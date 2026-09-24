# I100-B fixture-owned worktree cleanup and lock attribution checkpoint

## State

`Blocked`

## Authority and frozen state

Issue [#107](https://github.com/Bilaltariq41/SeqDoc/issues/107) and its body are specification authority. The parent
split is [#100](https://github.com/Bilaltariq41/SeqDoc/issues/100#issuecomment-5681943603), published at
https://github.com/Bilaltariq41/SeqDoc/issues/100#issuecomment-5694678193. The historical issue/readiness context
https://github.com/Bilaltariq41/SeqDoc/issues/107#issuecomment-5696378047 was authored by Ahmad; it is not owner
authorization. No authenticated Bilaltariq41 owner decision is claimed or needed; ordinary T2 peer policy governs.
GH-106 baseline is `ab6e3e1cf16213ee5346506b16949fa32c4ddfa4`; its accepted PR head is
`182ea35533482cebdfc070b368f3a7fa247a1735` and merge is `227d2e9f8b49ce6a414795b16bb0408ed213012a`.
GH-107 replacement planning/activation baseline is `18aae5e0364c0b13549bd0c5333ba48f8116bc9a`. The former
`dfc28b0b227e544bda937229dd11e31619bb0f25` is superseded GH-107 historical context only, not current or GH-106 authority.
This package is planning-only. It does not select or activate execution.

### Same-issue amendment and clean-history boundary

Issues #116, #117, and #118 are closed superseded planning history, not dependencies, authority, lifecycle records, or
deliverables. The parent disposition is https://github.com/Bilaltariq41/SeqDoc/issues/107#issuecomment-5775101734.
There is one issue, one implementation branch `testing/issue-107-fixture-cleanup-v2`, one PR, and one outer checkpoint;
B1/B2/B3 are internal build order only, with no child lifecycle, Ready state, merge, PR, or final gate. The same worker
session should own the complete candidate when practical. The rejected candidate is preserved local-only and its old
branch is never reused; no owner bypass is claimed.

This replacement planning branch is clean-history: between baseline `18aae5e0364c0b13549bd0c5333ba48f8116bc9a` and
the planning head, only `docs/project/work-items/GH-107.json`, `docs/work/quality/I100-B/checkpoint.md`, and
`docs/work/quality/I100-B/ledger.md` may change. PR119 is superseded unmerged and is not activation ancestry. A reviewed
descendant may be admitted only from this three-file planning change.

## Objective

Prove Windows x64 acceptance-test fixture cleanup and lock attribution consuming #106 without redesign. The candidate
must establish safe ownership, exact Git administration cleanup, contained-process family-zero proof, bounded retry and
Restart Manager diagnostics, truthful failure precedence, quarantine, concurrency isolation, and a live disposable-repo
end-to-end result.

## Target paths (allowlist)

- `tests/SeqDoc.AcceptanceTests/FixtureCleanup.cs`
- `tests/SeqDoc.AcceptanceTests/FixtureCleanupTests.cs`
- `docs/work/quality/I100-B/checkpoint.md`
- `docs/work/quality/I100-B/ledger.md`
- generated canonical work-item and execution state only, through `tools/governance/work_state.py`

## Non-goals

No `ProcessOwnership` file edits; no GH93/GH18/PR99/PR103 files; no external corpus; no production source, SDK,
package, or repository configuration; no global/name process kill; no `RmShutdown`; no global Git worktree prune; no
unowned force removal; no unbounded waits; and no cross-platform claim.

## Frozen semantic contract

1. **Ownership authority.** Use an immutable in-memory receipt and versioned sentinel at one fixture control root.
    Its source basename is exactly `seqdoc-fixture-<token>` by ordinal equality, where `<token>` is the admitted 32
    cryptographically random bytes encoded as base64url without padding. The resulting basename is ASCII and is NUL-, dot-, slash-, backslash-,
    colon-, rooted-, device-, UNC-, and normalization-ambiguity-free; it is neither `.` nor `..`. Any different prefix,
    token, encoding, casing, padding, character, or normalized representation fails admission. Use exact logical roles
    `cache`, `output`, and `worktree`, sorted in that order. Quarantine is separate caller-parent and sibling move-target
    authority, never a role or child. The sentinel is UTF-8 schema v1: `schemaVersion=1`, the same admitted token, exact
    expected revision, source common-dir identity digest, and a sorted role-to-canonical-relative-path map; it contains no
    absolute paths or timestamps. The receipt additionally stores physical canonical paths and Windows `FILE_ID_INFO`
    volume serial and 128-bit file ID for the control root, sentinel, every existing role root, worktree root, and captured
    admin dir, plus exact registration identity. Every destructive target is a canonical non-reparse descendant listed in
    the receipt. Open directories with reparse-safe semantics. Before every destructive attempt revalidate token/content,
    containment, every non-reparse component, volume/file IDs, role, and Git identity. Missing, malformed, mismatched,
    escaped, replaced/recreated, reparse, role-mismatched, or changed-identity authority fails closed. The control root and
    sentinel survive child cleanup and the sentinel/root are removed last.
2. **Git identity.** Capture source before-state: status, exact refs, local config, and worktree porcelain. Create a
    detached worktree at the exact revision via the #106 process runner. Capture exact registration and
    `rev-parse --absolute-git-dir`; prove that admin path is under the source common-dir worktrees area. The source cwd
    is the canonical source repository root for common-dir, status, refs, config, worktree list, add, and remove. The
    owned-worktree cwd is the exact owned worktree path only for absolute-git-dir after add. Every adapter receives an
    explicit cwd and never uses process cwd. Trim exactly one terminal line ending for scalar output; reject embedded
    NUL, multiple lines, or empty output. Rooted output is canonicalized directly; relative output resolves with
    `Path.GetFullPath(Path.Combine(exactCommandWorkingDirectory, output))`, never process cwd or a guessed source, then
    undergoes path/reparse/volume/FILE_ID/digest/containment validation.
3. **Command adapter.** `FixtureCleanup.cs` may consume only public `ProcessOwnershipOptions`,
   `ContainedProcess.Start`, construction result, `WaitAsync` result, `Terminate`, `Dispose`, `ProcessId`,
   `HasObservedActiveProcessZero`, `FailureClass`, `TeardownFailures`, output/truncation/secondary evidence, and
   platform support. One private adapter translates immutable observations; no internal snapshots or seams.
4. **Chronology and deadline.** `CleanupTimeout` is required, finite, greater than zero and at most 30 seconds,
   defaulting to 30 seconds, for cleanup work. Accepted #106 `FamilyProofGrace` is fixed at 10 seconds. The injected
   monotonic outer deadline is cleanup start + `CleanupTimeout` + `FamilyProofGrace` (40 seconds at defaults). External
   cancellation or fixture timeout remains or becomes primary evidence but never cancels mandatory cleanup, which uses
   this independent deadline. Reserve `FamilyProofGrace` before starting #106 `WaitAsync`: ordinary process wait may
   request at most `max(0, remaining-to-outer - 10 seconds)`, and does not start when no positive work budget remains.
   #106 termination/family proof may use only the reserved grace. Dispose and an opened RM `EndSession` are finally
   obligations that may complete after the outer deadline; record overrun. Repository gate, Git wait, RM admission/calls,
   deletion, and quarantine receive remaining outer budget. Check remaining time before every stage and native call. No
   Git/RM/delete/quarantine stage starts after outer expiry. Strict wall duration may exceed the outer deadline only for
   a non-cancellable in-flight native call, Dispose, or `EndSession`; record degradation and start no subsequent stage.
   Deadline expiry is cleanup degradation, or primary if none, and never overwrites an earlier failure. The order is
   platform/Git/RM admission; unrelated-state snapshot; control root/sentinel; exact worktree registration; commands;
   primary outcome; terminate/wait/dispose every owned process; require `ACTIVE_PROCESS_ZERO` and inspect teardown;
    repository gate; ownership revalidation; exact `git worktree remove --force`; registration/admin verification;
    bounded residual deletion; sentinel/root last; unrelated-state equality. Without family-zero proof perform no
    Git/filesystem deletion, RM, or quarantine. Process teardown remains permitted before family zero; the prohibition
    before family zero is specifically Git/RM/delete/quarantine diagnostics and mutation.
5. **Git removal.** Never run global `git worktree prune` or manually delete Git admin data. If exact remove fails while
   registration remains, preserve state and fail. Residual direct deletion may target only receipt-listed descendants
   inside the owned control root (worktree, cache, and output), never the captured common-dir admin path. If registration
   is absent but the captured admin directory remains, report failure/degradation and preserve it; never delete it and
   never prune. Quarantine moves only the control root, never common-dir admin data. Success requires both registration
   absent and captured admin path absent.
6. **Retry.** Exactly eight attempt start offsets are `0, 50, 150, 350, 750, 1150, 1550, 1950 ms`, with inter-attempt
   delays `50, 100, 200, 400, 400, 400, 400 ms`, bounded by both a two-second deletion subdeadline and remaining
   overall cleanup deadline. Use an injectable monotonic clock/sleeper and stop earlier when either bound would be
    exceeded. The two-second budget is attempt-admission/start budget: an attempt starts only when its scheduled offset
    and actual monotonic time are <= the subdeadline and the outer deadline admits start. Synchronous admitted calls may
    complete after the subdeadline; capture completion and record stable `DeletionAttemptOverranSubdeadline` without raw
    duration. No ninth attempt. Success after overrun records the delete postcondition but preserves degradation and
    continues only if outer time remains. Retryable failure after overrun with outer time enters `DeletionBudgetExhausted`
    and may admit quarantine; outer expiry in flight records `OuterDeadlineExpiredInFlight`, starts no quarantine/root/new
    stage after return, and transitions `FailedResidual` except for already completed physical postconditions. Finally
    obligations remain permitted. Revalidate authority before every attempt. Retry only `IOException` with exact Win32 32
    or 33 and `UnauthorizedAccessException` while ownership still revalidates; stop for other failures.
7. **Restart Manager.** The native admission table is Unicode `Rstrtmgr.dll` entry points `RmStartSession`,
   `RmRegisterResources`, `RmGetList`, and `RmEndSession`; no `RmShutdown` import exists. Use a
   `CCH_RM_SESSION_KEY+1` session-key buffer, flags 0, and files-only registration of exact existing files. The
   `RM_UNIQUE_PROCESS` identity is PID plus start `FILETIME`; `RM_PROCESS_INFO` uses the required fixed Unicode fields
   and types; every API result is an exact DWORD return code. On the first exact 32/33 direct cleanup violation invoke
   once per logical target. `RmGetList` has at most three calls: first null/zero; `SUCCESS` plus needed zero means
   empty; `MORE_DATA` with needed 1..64 allocates; needed >64 is capped failure. Calls 2/3 accept `SUCCESS` only
   within the allocated count; one `MORE_DATA` resize within the cap is allowed, while a third `MORE_DATA` is an
   unstable-list degradation with no silent partial claim. Other codes are exact failures. Always `RmEndSession` in
   `finally` after successful start; record its result without erasing an earlier result. Record reboot reasons and
   sorted bounded PID/start-FILETIME identities only as local diagnostics; stable receipts expose role/count/class.
   Zero result, repeated growth, cap, malformed count, every API failure, and session-end failure are group-6 cases.
   Directory-only failures truthfully report attribution unavailable. RM cannot prove ownership or authorize termination.
8. **Failure precedence.** Preserve an existing fixture/command failure as primary. Append cleanup evidence in
   deterministic chronology; otherwise first cleanup failure becomes primary. RM/session-end failures and quarantine
   are degradations, never success. Preserve #106 ordered secondary evidence and truncation without strengthening it.
9. **Quarantine.** Only after family-zero, receipt/sentinel revalidation, absent Git registration/admin, and exhausted
    deletion budget. The immutable local authority receipt includes the caller-authorized canonical local-drive control
    parent, component-by-component non-reparse observations, volume serial, and FILE_ID_INFO. The parent authorizes one
    direct-child move target but is never owned or deletable. The source control root is its exact immediate child with
    captured identity/token/sentinel/roles. `sourceRootName` means only the receipt-captured source basename admitted by
    clause 1. Separate move-target authority is the exact absent sibling constructed solely as
    `sourceRootName + ".quarantine"`, necessarily `seqdoc-fixture-<token>.quarantine`, under the same exact parent and
    volume, with canonical boundary and non-reparse existing destination components. There is no alternate hard-coded or
    independently parsed target grammar.

    There is no `Directory.Move` and no path-based fallback. Open the authorized parent with `CreateFileW` using
    `OPEN_EXISTING`, `FILE_FLAG_BACKUP_SEMANTICS|FILE_FLAG_OPEN_REPARSE_POINT`, required traverse, read-attributes, and
    synchronization access, and sharing that prevents parent rename/deletion while permitting the required child
    operation. Open the exact source control-root directory with `CreateFileW` using `DELETE|FILE_READ_ATTRIBUTES|
    SYNCHRONIZE`, the same reparse-safe flags, and `FILE_SHARE_READ|FILE_SHARE_WRITE` while omitting
    `FILE_SHARE_DELETE`. Read `FILE_ID_INFO` from these same live handles and compare parent/source volume plus
    128-bit IDs to the immutable receipt; validate sentinel/roles/stage and exact authorized sibling target while the
    handles remain live. An injectable generic quarantine observer/barrier fires exactly after both handles and all
    authority are validated, immediately before the native call.

    The exact x64 Windows contract is mandatory. The native `FILE_RENAME_INFORMATION`-shaped manual
    buffer is DWORD union/ReplaceIfExists false at offset 0, zero padding 4–7, parent HANDLE at offset 8, filename
    length uint at offset 16, and exact UTF-16 relative sibling bytes at offset 20 with no required terminator and byte
    count excluding any terminator; total size is exactly `20 + FileNameLength`. This byte layout is unchanged from the
    prior frozen Win32-targeted layout and was empirically verified to work identically at the native layer. `IntPtr.Size==8`
    and explicit offsets/buffer length are mandatory group-8 admission checks. No `FileRenameInformationEx` or flags are allowed.
    The exact managed admission table is mandatory: parent/source handle opening remains `kernel32.dll`, Winapi,
    `ExactSpelling=true`, `SetLastError=true`; CreateFileW is Unicode with `BestFitMapping=false`,
    `ThrowOnUnmappableChar=true` and exact
    declaration `[DllImport("kernel32.dll", CharSet=CharSet.Unicode, ExactSpelling=true, SetLastError=true, CallingConvention=CallingConvention.Winapi, BestFitMapping=false, ThrowOnUnmappableChar=true)] static extern SafeFileHandle CreateFileW([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);`. The rename call itself moves to the native NT layer:
    `NtSetInformationFile` uses the exact frozen declaration `[DllImport("ntdll.dll", ExactSpelling=true, CallingConvention=CallingConvention.Winapi, SetLastError=false)] static extern int NtSetInformationFile(SafeFileHandle fileHandle, out IO_STATUS_BLOCK ioStatusBlock, IntPtr fileInformation, uint length, int fileInformationClass);`, called on the already-open source
    handle from the same handle-opening/validation sequence as before, with both handles held live via `SafeFileHandle`
    (never released or reconstructed) through the call and through postclassification. `ntdll.dll` sets no Win32 last
    error and its own return value is authoritative, so `SetLastError=false` is an exact declared contract, not an
    omission. `IO_STATUS_BLOCK` is the frozen x64 layout `[StructLayout(LayoutKind.Sequential)] struct IO_STATUS_BLOCK { public IntPtr Status; public IntPtr Information; }`:
    `Status` at offset 0, 8 bytes, holding the 32-bit `NTSTATUS` in its low 32 bits; `Information` at offset 8, 8 bytes;
    frozen total size 16 bytes. The native information-class parameter is signed `int fileInformationClass = 10`
    (`FileRenameInformation`), matching the native C `_FILE_INFORMATION_CLASS` enum's default signed 32-bit underlying
    type (deliberately not the `uint` used elsewhere in this document for Win32 DWORD-typed constants); never the Win32
    `FileRenameInfo=3`, the Win32 `FileRenameInfoEx=22`, or the native `FileRenameInformationEx=65`.
    `NtSetInformationFile`'s own return value is the authoritative `NTSTATUS`, captured as signed `int` local evidence
    immediately after the call and before evaluating it or the out `IO_STATUS_BLOCK`; success is exactly `0`
    (`STATUS_SUCCESS`) and any nonzero value is failure, with no NTSTATUS-to-Win32 translation attempted. Exact
    postclassification, none of which may trigger a fallback rename or a second destructive action: a nonzero returned
    `NTSTATUS` is failure, reobserved and classified per the existing `SourcePreserved`/`Collision`/`SourceNameReused`/
    `Indeterminate` rules below; `NTSTATUS==0` with `IO_STATUS_BLOCK.Status`'s low 32 bits also reporting success proceeds
    to the existing destination/`FILE_ID_INFO`/sentinel/registration postcondition proof; `NTSTATUS==0` with a
    contradictory nonzero `IO_STATUS_BLOCK.Status` is classified locally as `Indeterminate`, both raw values are
    retained as local diagnostic evidence, and the overall result remains non-success degradation; `NTSTATUS==0` with
    agreeing `IO_STATUS_BLOCK` but a failed destination `FILE_ID_INFO`/sentinel/registration postcondition check is
    likewise classified locally as `Indeterminate` and treated as non-success degradation, never as `MoveCompleted`;
    and any other partial or unrecognized observation (for example a thrown exception during postclassification, or a
    handle that becomes invalid mid-check) is classified locally as `Indeterminate` with all captured raw evidence
    preserved, and no further destructive action follows.

    **Accepted boundary.** This checkpoint depends on `NtSetInformationFile`, a native NT API exported by `ntdll.dll` that
    is not part of Microsoft's documented Win32 application surface; `FileRenameInformation` (class 10) has been stable
    since early Windows NT versions and this exact pattern is precedented in other language runtimes/tools for the same
    reason (Win32 has no public equivalent for a `RootDirectory`-relative rename), but Microsoft provides no compatibility
    guarantee across Windows updates for undocumented `ntdll.dll` exports. Because this is test-only developer tooling, not
    shipped product behavior, an incompatibility on a future Windows build is accepted to surface as a loud, fail-closed
    test failure, not a silent defect; this is recorded the same way the rooted-Git-path and RM-capability boundaries are
    already recorded elsewhere in this document.

    This boundary is explicit, not implicit: no version allowlist and no alternative-rename fallback are permitted.
    Every admitted Group-8 positive partition must execute the real `NtSetInformationFile` call on the actual running
    Windows build; a missing `ntdll.dll` export, an admission/declaration failure, a nonzero `NTSTATUS`, a contradictory
    `IO_STATUS_BLOCK`, or an unproven postcondition on any build is a fail-closed failure of that run, classified per
    clause 9's exact postclassification rules above, and never a skip. The focused-verification receipt (below) records
    the exact measured Windows build; an unsupported future build is a blocking non-pass for GH-107 promotion, not a
    silent defect and not cause for a second, different rename attempt.
    Parent access is FILE_TRAVERSE 0x20 | FILE_READ_ATTRIBUTES 0x80 | SYNCHRONIZE 0x00100000, never FILE_ADD_SUBDIRECTORY;
    source access is DELETE 0x00010000 | FILE_READ_ATTRIBUTES | SYNCHRONIZE. Both calls use share
    `FILE_SHARE_READ` 1 | `FILE_SHARE_WRITE` 2 while omitting `FILE_SHARE_DELETE` 4, disposition `OPEN_EXISTING` 3,
    flags `FILE_FLAG_BACKUP_SEMANTICS` 0x02000000 | `FILE_FLAG_OPEN_REPARSE_POINT` 0x00200000, null security attributes,
    and a null template. Successful SafeFileHandles own and close handles; null/invalid/closed handles fail before
    mutation. Immediately after each `CreateFileW`, capture `Marshal.GetLastWin32Error` before inspecting the returned
    handle; immediately after `NtSetInformationFile`, capture the returned `NTSTATUS` before evaluating it. Use the
    captured value only for a failed result. Dispose source then parent and free the unmanaged buffer in `finally`.
    Before unmanaged-buffer allocation, revalidate the source basename against clause 1 and construct the one target from
    that validated value. The target must equal the construction by exact ordinal equality, remain ASCII and normalization
    invariant, and contain exactly one dot: the separator introducing exactly one `.quarantine` suffix. Reject a missing or
    altered suffix, different token, extra dot or suffix, NUL, slash, backslash, colon, rooted/device/UNC form, `.` or `..`,
    non-ASCII input, or any normalization-changing representation before buffer allocation or native rename. Its checked
    even UTF-16 byte length is <= uint.MaxValue-20 and checked buffer length is `20+length`, with no terminator required.
    Any admission, handle, layout, name, or buffer failure makes no native rename call.
    Rename only with `NtSetInformationFile` on the already-open source handle, native class `FileRenameInformation=10`,
    pointer plus uint size, `ReplaceIfExists=false`, the parent handle as `RootDirectory`, and exact relative sibling name
    `sourceRootName + ".quarantine"`. Capture the returned `NTSTATUS` immediately as local failure evidence; treat exactly
    `0` (`STATUS_SUCCESS`) as success and any nonzero value as failure, and do not attempt to translate the NTSTATUS to a
    Win32 error code. If API, handle, share, layout, root-relative, or x64 admission fails, fail quarantine before
    mutation; never downgrade. Hold both handles from final
    identity validation through native rename completion and postclassification. Target creation at the atomic call
    refuses without overwrite/merge/retry; target remains unchanged and source retains its ID. Source rename/delete/
    replacement while paused after validation is denied by sharing. An object appearing at the destination name before the
    native call causes atomic collision refusal and remains unchanged. A new unrelated object may appear at the vacated
    source name only after successful rename; it is classified `SourceNameReused` and remains untouched.

    On success, open destination no-follow and prove exact pre-move source FILE_ID/volume under the same parent, unchanged
    sentinel bytes, destination/parent non-reparse, registration/admin absent, and source path does not resolve to the
    moved ID. A new unrelated source object is preserved and classified locally as `SourceNameReused`; source absence is
    not required in that case. On native failure reobserve by handle/path and classify only `SourcePreserved`,
    `MoveCompleted`, `Collision`, `SourceNameReused`, or `Indeterminate`; postchecks classify and never authorize a move,
    and no second destructive action occurs. Indeterminate stable evidence and local observations are retained; overall
    result remains degraded/non-success. Move-target authority never authorizes child deletion outside the source receipt,
    and the parent itself is never a destructive target.
10. **Concurrency.** A repository-scoped in-process async gate keyed by canonical common Git dir serializes only
    worktree metadata mutations, bounded by the cleanup deadline. Tokens/roots remain independent and Git locks remain
    authority. Prove concurrent fixtures cannot delete/corrupt each other and unrelated snapshots are byte-equivalent.
11. **Platform.** Windows 10/Server 2016 x64 per #106, with Git, RM, and NTFS capability. Missing Git or RM capability
    is blocking and non-passing, never skip/pass. The admitted filesystem boundary is exactly NTFS: independently
    confirmed for the control root's volume via `Get-Volume`/`FileSystemType` (or equivalent), matching the spike's
    measured environment (see the ledger's dated amendment sections); a non-NTFS control-root volume is likewise
    blocking and non-passing, never skip/pass.
12. **Determinism/security.** Stable receipts are ordered by stage/role/attempt and contain no credentials, raw checkout
   paths, wall timestamps, unstable dictionary order, or application vocabulary. Raw PID/start time is local RM
   diagnostic data only, never persisted or user output.

### Permitted test seams

This is a closed table: exactly these rows exist, and no other hook, hand-rolled seam, or injected override exists
anywhere in `FixtureCleanup.cs`/`FixtureCleanupTests.cs`. No hook may return or influence a stage transition,
authority/ownership decision, or final classification. Two distinct override domains exist, both closed:

- **Failure-only override** (clock/sleeper timing excepted): a hook may substitute only a caller-supplied value that a
  real OS/native call could itself have returned as failure/degradation for that call — it must never synthesize
  success, a `FILE_ID`, a path, sentinel bytes, Git command output, family-zero proof, RM ownership/attribution, a
  stage, a classification, or a postcondition.
- **RM diagnostic negative-tuple override** (`RmGetList` only): because a mandatory malformed/non-growing-count
  negative genuinely returns `SUCCESS`/`MORE_DATA` at the Win32 layer with an invalid accompanying `needed`/`count`
  value — the malformed *shape*, not the return code, is what production code must classify as failure/degradation —
  this one call site's hook may substitute the exact tuple `(result, needed, count)` only from the closed set listed
  in its row below. No tuple in that set may establish RM ownership, admission, attribution, stage success, or final
  success, regardless of whether its `result` field is nominally `SUCCESS`/`MORE_DATA` or an outright failure DWORD.

Every override, in either domain, must be inert (unset/no-op) during every group's positive/success partition. An
unlisted or unenumerated hook, or a tuple outside its row's closed set, fails review.

| Seam | Exact call site | Allowed override domain | Consuming group | Real positive-path proof (no seam active) |
|---|---|---|---|---|
| Injectable monotonic clock/sleeper | The retry loop's `IClock`/`ISleeper`-shaped seam (a hand-rolled `TimeProvider`-shaped test type, not a NuGet reference) wrapping every attempt-offset/delay wait | An OS monotonic-clock reading or a `Task.Delay`/`Thread.Sleep`-shaped timing observation only; never a stage/authority/outcome value | Group 5 | Group 5's successful-retry-then-delete positive case runs the real retry loop with the real clock; the seam is exercised only for the deterministic boundary subcases (1950/2000/2001 ms). |
| `NtSetInformationFile` return-code override hook | The one call site in the quarantine rename path, immediately after live parent/source handles and all authority are validated | Failure-only domain: only the returned `NTSTATUS`, to a caller-supplied nonzero failure value, for Group 8's single "unsupported native/layout/handle admission refusal" negative partition; never `0`/`STATUS_SUCCESS` | Group 8 | Group 8's `QuarantinedTerminal` positive case executes the real `NtSetInformationFile` call with the hook unset. |
| `RmStartSession`/`RmRegisterResources`/`RmEndSession` return-code override hook | The one call site for each of these three RM entry points (admission, registration, session-end) | Failure-only domain: only that call's own returned DWORD, to a caller-supplied nonzero failure code — never `ERROR_SUCCESS`/`0` | Group 6 | Group 6's first-violation-attribution positive case executes the real `RmStartSession`/`RmRegisterResources`/`RmEndSession` calls with every hook unset. |
| `RmGetList` negative-tuple override hook | The one call site for each of the up-to-three permitted `RmGetList` invocations | RM diagnostic negative-tuple domain: only the exact returned tuple `(result, needed, count)`, restricted to this closed set — `SUCCESS` with `count` greater than the currently allocated capacity (malformed count); `MORE_DATA` with `needed` <= the currently allocated `count` (non-growing count); a third successive `MORE_DATA` response after one prior resize (unstable-list degradation, cap violation); `needed` > 64 on the first call (capped failure); or any nonzero failure DWORD — never a tuple representing a genuine, capacity-consistent `SUCCESS`/`MORE_DATA` admission already provable on the real platform | Group 6 | Group 6's first-violation-attribution positive case executes the real `RmGetList` sequence with the hook unset. |
| Injectable generic quarantine observer/barrier | Fires exactly after both live handles are open and all parent/source/target authority is validated, immediately before `NtSetInformationFile` | A timing/synchronization observation only (signals that the native call is about to happen); never the call's outcome | Group 8 | Group 8's `QuarantinedTerminal` positive case fires the barrier with no competitor action taken. |

Every group's positive/success partition must execute with no test seam/hook active — real `git.exe`, real filesystem,
real native calls throughout; see the test-budget "no-override positive-path" rule below.

### Total physical state machine and per-role inventory

The total physical states and only allowed transitions are:

* `AdmissionFailedNoOwnership` is terminal and has no destructive authority.
* `Provisioned` -> `FamilyZero` or `FailedResidual`.
* `FamilyZero` -> `GitDeregisteredAdminVerified` or `FailedResidual`.
* `GitDeregisteredAdminVerified` -> `RoleCleanupInProgress` or `FailedResidual`.
* `RoleCleanupInProgress` carries sorted `cache`, `output`, `worktree` inventory, proven per-role delete
  postconditions, remaining roles, per-target attempts/offsets, and last classification. It may remain in progress within
  budget, then -> `RoleCleanupComplete`, `DeletionBudgetExhausted`, or `FailedResidual`. Missing path is accepted only
  when the same receipt recorded the prior delete postcondition.
* `RoleCleanupComplete` -> `RootDeleted` only on successful sentinel-last/root cleanup. Sentinel/root failure ->
  `FailedResidual`; quarantine is forbidden after `RoleCleanupComplete`.
* `DeletionBudgetExhausted` requires at least one remaining role, exact admitted attempt count/schedule (eight where a
  retryable failure persists), last retryable classification, full authority, family zero, registration/admin absence,
   exact sentinel, and enough outer budget to admit quarantine. It does not imply role completion or absence. It ->
   `QuarantinedTerminal` only on proven move completion, otherwise -> `FailedResidual`. If outer time expires before
   quarantine admission, transition directly to `FailedResidual` and start no new stage; process teardown remains
   permitted until family zero.
* `RootDeleted`, `QuarantinedTerminal`, and `FailedResidual` are terminal and reject all later automatic destructive
  actions. External disposition is outside this checkpoint and must establish separate authority. There is no generic
  `RoleDeleted` state.

After successful quarantine the original destructive receipt is consumed. A local terminal residual observation records
moved canonical path, parent/source/destination FILE_ID/volume, original token, sentinel bytes/hash, postconditions, and
local exception evidence for report only. Stable evidence contains only stage/role/classification/count/certainty. No
new, reconstructed, or same process may later delete via FixtureCleanup; the fixture grants no external removal authority
and only reports the local path. `QuarantinedTerminal` and `FailedResidual` are always degraded/non-success.

### Physical state and final outcome

Final outcome is separate from physical state. Success requires physical `RootDeleted` and no primary failure or
degradation. `RootDeleted` with RM, `EndSession`, or teardown degradation remains non-success; `QuarantinedTerminal`
and `FailedResidual` are always non-success. Primary evidence is set once: seed a pre-existing fixture/command failure;
otherwise use cancellation when it is the first observed failure; otherwise the first cleanup failure; otherwise deadline
if it is first. Later evidence is secondary in deterministic chronology. Finally obligations and degradations never
overwrite primary evidence or physical state.

### Restart Manager managed interop admission table

All declarations use DLL `Rstrtmgr.dll`, `CharSet.Unicode`, `ExactSpelling=true`, `CallingConvention.Winapi`,
`SetLastError=false`, and `uint` DWORD/UINT return values and counts. Constants are `ERROR_SUCCESS=0`,
`ERROR_MORE_DATA=234`, `CCH_RM_SESSION_KEY=32` (therefore `StringBuilder` capacity 33),
`CCH_RM_MAX_APP_NAME=255` (fixed 256 WCHAR), and `CCH_RM_MAX_SVC_NAME=63` (fixed 64 WCHAR).

| Entry point | Exact managed signature and boundary |
|---|---|
| `RmStartSession` | `uint RmStartSession(out uint handle, uint flags, StringBuilder key)`; flags are 0 and key capacity is 33. |
| `RmRegisterResources` | `uint RmRegisterResources(uint handle, uint nFiles, [In, MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.LPWStr, SizeParamIndex=1)] string[] files, uint nApps, IntPtr apps, uint nServices, IntPtr services)`; files-only, with apps/services zero and null. |
| `RmGetList` | `uint RmGetList(uint handle, out uint needed, ref uint count, [In, Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex=2)] RM_PROCESS_INFO[] apps, out uint rebootReasons)`; first call passes null/zero, then count is capacity. |
| `RmEndSession` | `uint RmEndSession(uint handle)`; called in `finally` after successful start. |

`FILETIME` is sequential with two `uint` fields (`dwLowDateTime`, `dwHighDateTime`). `RM_UNIQUE_PROCESS` is
sequential: `uint dwProcessId`, then `FILETIME ProcessStartTime`; admitted x64 expects offsets 0 and 4 and size 12.
`RM_APP_TYPE` is signed `int32` with values `0 Unknown`, `1 MainWindow`, `2 OtherWindow`, `3 Service`, `4 Explorer`,
`5 Console`, and `6 Critical`. `RM_PROCESS_INFO` is sequential Unicode with default native packing (no custom `Pack`):
`RM_UNIQUE_PROCESS`; `ByValTStr SizeConst=256` application name; `ByValTStr SizeConst=64` service short name;
`RM_APP_TYPE`; `uint AppStatus`; `uint TSSessionId`; and `bool` marshalled as `UnmanagedType.Bool`. Expected admitted
x64 offsets are 0, 12, 524, 652, 656, 660, 664 and size 668. Group 6 verifies these Marshal offsets and sizes.

The three-call state machine is exact: call 1 uses count 0 and apps null; `SUCCESS` with needed 0 is empty; `MORE_DATA`
with needed 1..64 allocates and sets count to capacity; each success requires count <= capacity and records exactly count;
`MORE_DATA` requires needed > current count and <=64 and permits one resize; non-growing or malformed counts fail; a
third `MORE_DATA` is unstable-list failure; no partial array is exposed on any failure. Every successful start ends with
`RmEndSession`, whose result is recorded without erasing prior evidence. No `RmShutdown` declaration exists.

B1 technical strengthening freezes rooted `GitExecutablePath` admission only from `%ProgramFiles%\Git\cmd\git.exe` or
`%ProgramFiles(x86)%\Git\cmd\git.exe`; zero or multiple distinct FILE_ID candidates fail. This is an accepted
supported-environment boundary: a machine (developer or CI) with Git installed elsewhere makes every group a blocking
non-pass, never a skip, and this is intentional, not an oversight to fix later. Capture and revalidate
source-root and common-dir canonical non-reparse chains, volume serials, and 128-bit FILE_ID_INFO before every
mutation. The sentinel is exact UTF-8 without BOM, one JSON line plus LF, ordered `schemaVersion`, `token`, `revision`,
`commonDirectoryDigest`, `roles`; token is 32 random bytes base64url without padding, revision is lowercase 40-hex,
    digest is local `sha256:` plus 64 lowercase hex, and roles are sorted `cache`, `output`, `worktree` with
ASCII relative values. Stable evidence contains only schema/stage/role/attempt/classification/count/certainty;
token, paths, PID/FILETIME, wall time, checkout data, and credentials remain local. Paths are drive-local, full,
separator-trimmed except roots, OrdinalIgnoreCase with boundary, and reject device/UNC/alternate streams, reparse,
replacement, escape, and cross-volume forms. Git vectors are exact and have no separator fallback:
`worktree add --detach <owned-absolute-path> <40-lowercase-revision>`, `rev-parse --git-common-dir`,
`rev-parse --absolute-git-dir`, `status --porcelain=v1 -z --untracked-files=all`,
`for-each-ref --format=%(refname)%00%(objectname)%00%(symref)%00 --sort=refname`, `config --local --null --list`,
`worktree list --porcelain`, and `worktree remove --force <owned-absolute-path>`. Parse registration strictly;
specific admin is a same-volume direct child of `<common>\worktrees`; never manually delete admin or prune.

B2 technical strengthening freezes retry starts from retry-start `[0,50,150,350,750,1150,1550,1950]` ms, delays
`[50,100,200,400,400,400,400]` ms, maximum eight attempts, nested monotonic 2-second deletion budget and remaining
outer deadline. Revalidate full B1 authority before every attempt and after every awaited delay; stop on success,
nonretryable failure, invalid authority, or deadline. A logical target is one exact receipt-listed regular file;
direct violations are IOException HResult low word 32/33, while directory-only failures are not attribution. RM is
`Rstrtmgr.dll`, Unicode, ExactSpelling=true, Winapi, SetLastError=false; constants 0/234, key 32+1, app 255+1,
service 63+1; exact Start/Register/GetList/End signatures use files LPArray and zero IntPtr apps/services. At most
three GetList calls permit one growth, cap 64, and fail malformed/non-growing/third MORE_DATA with no partial result;
End evidence is mandatory and no RmShutdown exists.

B3 technical strengthening freezes identity-bound native rename to the absent same-volume direct sibling produced only by
`sourceRootName + ".quarantine"`, whose resulting display is `seqdoc-fixture-<token>.quarantine`, plus collision refusal
and terminal `QuarantinedTerminal` only. The existing
assembly-staged rooted stub vector is exactly `sleep-with-marker <owned-marker-path> 30000`, marker under receipt-listed
output; bounded event/poll evidence parses marker bytes to public `ContainedProcess.ProcessId`. The exact live order is:
provision; start child; observe marker and PID; assert `.completed` absent; independently lock a separate exact
receipt-listed output file with `FileMode.Open`, `FileAccess.ReadWrite`, `FileShare.None`; terminate/wait/dispose child
and prove public active-family zero; immediately assert original marker still exists with identical PID bytes and
`.completed` absent; exact Git remove/registration/admin verification; attempt lock-file deletion and observe direct
IOException low word 32/33; RM Start/Register/GetList identifies exact testhost PID+start FILETIME locally while held;
observer signals host disposal and barrier confirms release; retry deletes lock, role cleanup removes original marker, and
final root/sentinel cleanup proves marker/output/root absent. No RM/delete before family zero, no Thread.Sleep/Task.Delay
test synchronization, no testhost termination, and RM registration alone is not attribution. Concurrency key is canonical
common-dir FILE_ID and serializes only metadata mutation. Group 8 uses the generic observer/barrier immediately before
`NtSetInformationFile` and proves competitor source rename/delete/replacement denial, destination race refusal,
exact moved identity, unrelated post-success source replacement preservation, unsupported native/layout/handle refusal,
no path-based fallback, and terminal/report-only semantics. No sleeps.

## Existing coverage

Accepted #106 ProcessOwnership coverage is 76/76; reusable QHTTP/GH93 patterns are read-only risk input. There is no
existing complete sentinel, quarantine, or Restart Manager contract.

## Risks

Risks are ownership confusion, Git admin damage, family-zero races, stale PIDs, RM leaks/caps, retry nondeterminism,
primary-failure masking, unsafe quarantine, concurrency, unavailable platform capability, and reliance on the undocumented
native `NtSetInformationFile` API for quarantine rename, with no Microsoft compatibility guarantee across Windows
updates — accepted because this is test-only tooling and any incompatibility fails closed and loudly.

## Test budget

Current soft target: ten `[Fact]` test methods in `FixtureCleanupTests.cs`, one per mandatory risk group below, with no
duplicated assertion across groups; current focused expectation: `86 passed/0 failed/0 skipped` (76 accepted
ProcessOwnership tests + 10 FixtureCleanup `[Fact]` methods). Ten is a soft target, not an immutable cap.
`[Theory]`/parameterized tests remain explicitly prohibited in this file, so the count stays row-for-row unambiguous
regardless of how many methods it settles at. A concrete, nonduplicate regression is permitted for a real finding or
risk newly discovered during implementation or review (including a High-severity safety finding surfaced by a B1/B2/B3
intermediate Reviewer-agent pass); record its reason, method/group, new total method count, and new focused expected
discovery count before `ReviewRequired`. Do not remove or combine mandatory groups to hide proof; do not add a method
to pad the count. Any change to the ten-method/86-count baseline amends the candidate's expected count through the
ordinary issue amendment/review path. Each group's substantive proof obligations (what each group must prove) are
unchanged regardless of the exact method count that satisfies them.

Every group's positive/success partition must execute with no test seam/hook active — real `git.exe`, real filesystem,
real native calls throughout — and, for each group, the checkpoint/ledger should be able to name the specific production
guard whose removal or inversion would make that group fail. Enumerating each of the ten groups' exact guards is
implementation-time/review-time work, not a planning-amendment obligation.

1. Windows/x64/rooted Git admission fails closed;
2. exact three receipt roles `cache`, `output`, `worktree`, v1 sentinel, stable sanitized receipt, FILE_ID_INFO
   replacement, containment, reparse, partial, and reconstructed-owner authority negatives;
3. real worktree registration/admin capture and exact successful cleanup, including absent registration and admin path,
   explicit source/owned-worktree cwd, relative/absolute/wrong-base output, and process-cwd mismatch partitions;
4. active-family or unproven family zero blocks Git mutation, RM, direct filesystem deletion, and quarantine; owned-process
   terminate/wait/dispose and evidence collection remain permitted and mandatory, with cancellation/deadline evidence
   recorded;
5. exact `CleanupTimeout`/10-second grace and nested deletion schedule, all-eight-fail/no-ninth, partial role success
   then later role failure, per-role inventory, `DeletionBudgetExhausted` evidence, no `RoleCleanupComplete`, and no
   premature quarantine. Boundary proofs include start at 1950/complete at 2000 inclusive without overrun, complete at
   2001 as overrun, wake/start after 2000 with no call, and outer expiry during an admitted call;
6. explicit missing/unloadable RM capability as blocking non-pass, plus RM scripts, first-violation attribution, real
   admitted-platform empty/known-lock calls, Unicode Marshal layout, injected state-machine negatives, caps/errors,
   session end, and no shutdown;
7. exact physical-state versus outcome and primary/cancellation/deadline/RM/quarantine precedence matrix;
8. exhaustion prerequisite, residual-role quarantine, no `RoleCleanupComplete`, wrong/replaced parent ID, source outside
    parent, target parent reparse/replacement, pre/post-open replacement, source live-handle rename/delete/replacement
    denial, destination race, unsupported native/layout/handle admission, exact barrier position, collision before/after
    check, cross-volume, source replacement, success identity/sentinel/terminal, SourcePreserved/MoveCompleted/
    Indeterminate, reconstructed observer no destruction, source-name reuse preserved, root-only quarantine after role
    completion fails closed, and concurrent sibling collision/isolation. The injectable generic quarantine
   observer/barrier fires exactly after both live handles are open and all parent/source/target authority is validated,
   immediately before `NtSetInformationFile`; subcases prove competitor source rename/delete denial, same-path
   replacement denial, destination creation race refusal without overwrite, exact moved FILE_ID, unrelated post-success
   source replacement unchanged, unsupported native/layout/handle refusal before mutation, no path-based fallback, and
    terminal/report-only semantics. Naming subcases prove the valid exact `seqdoc-fixture-<token>.quarantine` construction
    and reject missing/altered suffix, different token, extra dot/suffix, slash, backslash, colon, NUL, rooted/device/UNC
    form, `.`/`..`, non-ASCII or normalization-changing input, and a source basename that does not match the frozen token;
    every negative refuses before buffer allocation and native rename, and the positive encodes only the exact constructed
    UTF-16 sibling bytes. It also asserts `NtSetInformationFile`/`ntdll.dll` DllImport metadata/signatures, `IO_STATUS_BLOCK`
    layout, `NTSTATUS==0` success/failure classification (not BOOL marshalling, which no longer applies to the rename
    call itself), native information-class value, access masks, invalid-handle refusal, immediate NTSTATUS capture, reverse
    disposal, buffer free, and no-call-on-failure. It further covers the `NTSTATUS==0`-with-contradictory-`IO_STATUS_BLOCK`
    `Indeterminate` partition, the `NTSTATUS==0`-with-failed-postcondition `Indeterminate` partition, and — using only the
    closed per-native-call return-code override hook, never a real capability probe — one negative partition simulating an
    unsupported/failing native admission to prove the fail-closed, no-fallback rule required by the accepted
    `NtSetInformationFile` boundary. `CreateFileW` handle-opening calls are unaffected and remain
    `SafeFileHandle`/`GetLastWin32Error`-based. No sleeps;
9. concurrent fixtures and unrelated repo/ref/config/worktree isolation;
10. successful live cleanup only, with no residual registration/admin/root; quarantine is exclusive to group 8.

## Focused verification

Before GH-107 promotion/activation, on clean then-current main run the full Acceptance Release command once as a
baseline observation, not a focused/final gate and not a consumption of the candidate final gate. Record a public Issue
#107 receipt with exact SHA, relevant `dotnet --info` SDK version, Windows version/architecture, control-root volume
filesystem identity (must be NTFS per clause 11), rooted Git identity and capability, RM capability, discovered/pass/
fail/skip counts, and exact sorted failure signatures. If unavailable, GH-107 remains Blocked. Candidate comparison
requires all ProcessOwnership/FixtureCleanup tests pass with zero skips, no new failure signature beyond baseline, and
no baseline pass becoming fail; disappeared baseline failures are allowed and count changes require explanation.
Prefer the same environment and classify differences explicitly.

The one required focused implementation command, before `ReviewRequired`, is:
`dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter "FullyQualifiedName~FixtureCleanupAuthorityTests|FullyQualifiedName~FixtureCleanupProcessTests|FullyQualifiedName~FixtureCleanupIntegrationTests|FullyQualifiedName~ProcessOwnershipTests"`, exactly `86 passed/0 failed/0 skipped`.

B1 focused 3/0/0 and affected 79/0/0, B2 focused 3/0/0 and affected 82/0/0, and B3 focused 4/0/0 are optional
developer checks only; they are not required checkpoint commands, gates, or receipts. The sole final gate remains the
full Acceptance Release command once after Phase B human review and resolved findings. Planning validation only; no
product or dotnet tests are run while preparing the package.

After each of the internal build phases B1, B2, and B3, an independent Reviewer-agent pass must run against the same
draft PR/branch before the next phase starts. These are advisory worker containment checks only — not lifecycle
states, not human approvals, and not final gates. Every finding they raise must be recorded, before the next phase
starts, as exactly one of: `Fixed` (repaired on the same branch and reverified), `Rejected` with evidence (recorded
reason the finding does not apply), or `Carried` explicitly into the mandatory complete-candidate review at
`ReviewRequired` (only for a finding that genuinely cannot be resolved without work belonging to a later phase). A
High-severity safety or authority finding from a B1/B2/B3 pass stops the next phase from starting until it is
disposed as `Fixed` or `Rejected`; it may not be silently `Carried`. These passes do not replace, and their
dispositions do not substitute for, the worker's own complete-candidate Reviewer-agent pass, Abood-essa's authenticated
latest-head non-author human review, or the final gate. A hand-rolled `TimeProvider`-shaped test seam type (not a
`Microsoft.Extensions.TimeProvider`/`FakeTimeProvider` NuGet package reference, since that would require an out-of-scope
`csproj` change) is explicitly permitted inside `FixtureCleanupTests.cs` for deterministic control over the retry
schedule's clock/sleeper seam.

## Final gate

```powershell
dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release
```

Run once only after complete-candidate review findings are resolved. This planning package does not run the gate.

## Review boundary

Stop at `ReviewRequired` after a green focused lane. Require one latest-head, non-author human peer review of the
complete candidate; run the final gate once after findings are resolved. Stop if #106's public API cannot satisfy the
contract, exact ownership or family-zero proof is absent, Git admin cleanup risks unrelated state, RM would need
termination authority, Windows x64/Git/RM is unavailable, target paths expand, or GH93/GH18 files would be edited.
Following the 2026-09-23 recovery decision (https://github.com/Bilaltariq41/SeqDoc/issues/107#issuecomment-5795266589),
Qhatahet is the implementer/candidate contributor for GH-107/I100-B B1–B3 product implementation and, as of that
decision, is no longer eligible as an independent reviewer of that candidate. Abood-essa is the confirmed replacement
reserved eligible untouched non-author human reviewer for the latest candidate
(https://github.com/Bilaltariq41/SeqDoc/issues/107#issuecomment-5795300539). Any further replacement follows the
current reviewer-change evidence policy; no reviewer metadata is created at Ready.

### Review and activation contract

Readiness claims are frozen here and in the ledger but are not acquired while GH-107 is Ready and unselected, avoiding
preemptive lease blocking. Activation must supply exactly these canonical lowercase claims:

- `path:tests/seqdoc.acceptancetests/fixturecleanup.cs`
- `path:tests/seqdoc.acceptancetests/fixturecleanuptests.cs`
- `path:docs/work/quality/i100-b`
- `path:docs/project/work-items/gh-107.json`
- `fixture:fixturecleanup`
- `governance-tool:tools/governance/work_state.py`
- `exclusive:acceptance-git-worktree-metadata`

Activation must supply exactly this set; no selection occurs now.

The csproj remains outside the initial allowlist and claims. If concrete compiler/build evidence proves it necessary,
stop before editing; amend GH-107/I100-B target paths, claims, risks, and tests under T2, run worker readiness review,
and obtain latest-head non-author peer approval on the amended SHA before resuming. Target expansion without an accepted
amendment is a stop condition, not an automatic permanent block and not an undocumented bypass.

Phase A requires both reserved peers to review the same immutable replacement planning SHA and post authenticated T2
receipts; they approve only same-issue internal sequencing/spec/allowlists, not implementation findings or the final
gate. When one reserved peer authors a given planning/checkpoint amendment (as Qhatahet did for the 2026-09-23
quarantine-rename-ABI amendment, under the recovery decision's implementer authorization), that peer is disqualified
as its own reviewer for that exact SHA and only the surviving non-author reserved peer's authenticated T2 approval is
required to satisfy Phase A for it. Phase B requires the worker/Orchestrator to invoke an independent Reviewer agent on
the complete latest B1–B3 implementation candidate and record its invocation/output digest, then self-review/
dispositions, focused/affected green, `ReviewRequired`, and one authenticated latest-head non-author human GitHub
approval for the same implementation SHA reserved to Abood-essa, the confirmed replacement Phase-B reviewer under the
2026-09-23 recovery decision (Qhatahet, now the implementer, is disqualified from Phase B as of that decision), and
that human independently invokes their own Reviewer agent run against the same complete latest SHA before posting the
authenticated receipt. Phase A cannot defer or dispose Phase B findings; Phase B cannot amend the contract without a new
amendment. No Ready or owner bypass is claimed. Before implementation/promotion, capture clean current-main
complete-suite counts/signatures and the rule for unrelated known failures; fixture groups may not pass by skip.

## Acceptance proof

Group 1 owns `AdmissionFailedNoOwnership`; admission success first becomes `Provisioned` in group 2, which proves the
three-role sentinel/receipt and authority
negatives; group 3 proves Git identity and admin cleanup; group 4 proves family-zero/deadline gating; group 5 proves
per-role inventory, exact retry exhaustion, and no premature quarantine; group 6 proves blocking RM capability and ABI/
state machine; group 7 proves physical state versus outcome and primary/secondary precedence; group 8 proves every
identity-bound quarantine race, ABI admission, native outcome classification, terminal/report-only rule, and sibling
isolation; group 9 proves unrelated-state/concurrency isolation without duplicating group 8; group 10 proves successful
live cleanup only. All states `AdmissionFailedNoOwnership`, `Provisioned`, `FamilyZero`,
`GitDeregisteredAdminVerified`, `RoleCleanupInProgress`, `RoleCleanupComplete`, `DeletionBudgetExhausted`, `RootDeleted`,
`QuarantinedTerminal`, and `FailedResidual` have a first observable proof in these groups, and group 8 is the only
quarantine proof. No planning activity claims product or test verification.
