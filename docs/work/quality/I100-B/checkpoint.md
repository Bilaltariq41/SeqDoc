# I100-B fixture-owned worktree cleanup and lock attribution checkpoint

## State

`Blocked`

## Authority and frozen state

Issue [#107](https://github.com/Bilaltariq41/SeqDoc/issues/107) and its body are specification authority. The parent
split is [#100](https://github.com/Bilaltariq41/SeqDoc/issues/100#issuecomment-5681943603), published at
https://github.com/Bilaltariq41/SeqDoc/issues/100#issuecomment-5694678193. Owner readiness authorization is
https://github.com/Bilaltariq41/SeqDoc/issues/107#issuecomment-5696378047; it permits `Ready` after #106 closes.
GH-106 is closed and accepted at PR head `182ea35533482cebdfc070b368f3a7fa247a1735`, merge
`227d2e9f8b49ce6a414795b16bb0408ed213012a`, and baseline `dfc28b0b227e544bda937229dd11e31619bb0f25`.
This package is planning-only. It does not select or activate execution.

## Same-issue amendment and clean-history boundary

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

1. **Ownership authority.** Use an immutable in-memory receipt and versioned sentinel at one fixture control root,
   with an unpredictable token and exact logical roles for worktree, cache, output, and quarantine. The sentinel is
   UTF-8 schema v1: `schemaVersion=1`, a cryptographic 128-bit-or-longer base64url token, exact expected revision,
   source common-dir identity digest, and a sorted role-to-canonical-relative-path map; it contains no absolute paths
   or timestamps. The receipt additionally stores physical canonical paths and Windows `FILE_ID_INFO` volume serial
   and 128-bit file ID for the control root, sentinel, every existing role root, worktree root, and captured admin dir,
   plus exact registration identity. Every destructive target is a canonical non-reparse descendant listed in the
   receipt. Open directories with reparse-safe semantics. Before every destructive attempt revalidate token/content,
   containment, every non-reparse component, volume/file IDs, role, and Git identity. Missing, malformed, mismatched,
   escaped, replaced/recreated, reparse, role-mismatched, or changed-identity authority fails closed. The control root
   and sentinel survive child cleanup and the sentinel/root are removed last.
2. **Git identity.** Capture source before-state: status, exact refs, local config, and worktree porcelain. Create a
   detached worktree at the exact revision via the #106 process runner. Capture exact registration and
   `rev-parse --absolute-git-dir`; prove that admin path is under the source common-dir worktrees area. Never infer
   ownership from business or name vocabulary.
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
   Git/filesystem deletion, RM, or quarantine.
5. **Git removal.** Never run global `git worktree prune` or manually delete Git admin data. If exact remove fails while
   registration remains, preserve state and fail. Residual direct deletion may target only receipt-listed descendants
   inside the owned control root (worktree, cache, and output), never the captured common-dir admin path. If registration
   is absent but the captured admin directory remains, report failure/degradation and preserve it; never delete it and
   never prune. Quarantine moves only the control root, never common-dir admin data. Success requires both registration
   absent and captured admin path absent.
6. **Retry.** Exactly eight attempt start offsets are `0, 50, 150, 350, 750, 1150, 1550, 1950 ms`, with inter-attempt
   delays `50, 100, 200, 400, 400, 400, 400 ms`, bounded by both a two-second deletion subdeadline and remaining
   overall cleanup deadline. Use an injectable monotonic clock/sleeper and stop earlier when either bound would be
   exceeded. Revalidate authority before every attempt. Retry only `IOException` with exact Win32 32 or 33 and
   `UnauthorizedAccessException` while ownership still revalidates; stop for other failures.
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
   deletion budget. Atomically move the whole owned control root on the same volume to a token-derived collision-safe
   quarantine parent; the moved sentinel remains. Never quarantine registered, unowned, escaped, or reparse paths.
   A failed move leaves the root and reports a failing/degraded outcome; path is local diagnostic only.
10. **Concurrency.** A repository-scoped in-process async gate keyed by canonical common Git dir serializes only
    worktree metadata mutations, bounded by the cleanup deadline. Tokens/roots remain independent and Git locks remain
    authority. Prove concurrent fixtures cannot delete/corrupt each other and unrelated snapshots are byte-equivalent.
11. **Platform.** Windows 10/Server 2016 x64 per #106, with Git and RM capability. Missing capability is blocking and
    non-passing, never skip/pass.
12. **Determinism/security.** Stable receipts are ordered by stage/role/attempt and contain no credentials, raw checkout
   paths, wall timestamps, unstable dictionary order, or application vocabulary. Raw PID/start time is local RM
   diagnostic data only, never persisted or user output.

### Cleanup receipt state machine

The same cleanup owner holds an immutable receipt and monotonic in-memory stage: `Provisioned`, `FamilyZero`,
`GitDeregisteredAdminVerified`, `RoleDeleted`, then either `RootDeleted` or `Quarantined`. A missing path is accepted
only after this receipt observed and recorded the prior stage postcondition. The sentinel remains until root completion or
quarantine. A reconstructed process/receipt may report an orphan from the sentinel but cannot resume destructive cleanup
from the sentinel alone and fails closed. Partial failures retain sentinel, root, and receipt for same-owner bounded
retry.

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
`%ProgramFiles(x86)%\Git\cmd\git.exe`; zero or multiple distinct FILE_ID candidates fail. Capture and revalidate
source-root and common-dir canonical non-reparse chains, volume serials, and 128-bit FILE_ID_INFO before every
mutation. The sentinel is exact UTF-8 without BOM, one JSON line plus LF, ordered `schemaVersion`, `token`, `revision`,
`commonDirectoryDigest`, `roles`; token is 32 random bytes base64url without padding, revision is lowercase 40-hex,
digest is local `sha256:` plus 64 lowercase hex, and roles are sorted `cache`, `output`, `quarantine`, `worktree` with
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

B3 technical strengthening freezes quarantine as atomic same-volume move to absent direct sibling
`seqdoc-fixture-<token>.quarantine`, collision refusal, and same in-memory-owner eventual cleanup only. The existing
assembly-staged rooted stub vector is exactly `sleep-with-marker <owned-marker-path> 30000`, marker under receipt-listed
output; bounded event/poll evidence parses marker bytes to public `ContainedProcess.ProcessId`, and marker is absent
after forced termination. The test host independently opens the exact receipt-listed lock target with
`FileMode.Open`, `FileAccess.ReadWrite`, `FileShare.None`; an observer/barrier records first 32/33 and RM registration,
signals host disposal, then releases. No Thread.Sleep/Task.Delay timing synchronization and no testhost termination;
family zero uses public #106 API. Concurrency key is canonical common-dir FILE_ID and serializes only metadata mutation.

## Existing coverage

Accepted #106 ProcessOwnership coverage is 76/76; reusable QHTTP/GH93 patterns are read-only risk input. There is no
existing complete sentinel, quarantine, or Restart Manager contract.

## Risks

Risks are ownership confusion, Git admin damage, family-zero races, stale PIDs, RM leaks/caps, retry nondeterminism,
primary-failure masking, unsafe quarantine, concurrency, and unavailable platform capability.

## Test budget

Exactly 10 grouped test methods, with theories/subcases permitted and no duplicated assertion across groups:

1. Windows/x64/rooted Git admission fails closed;
2. receipt, v1 sentinel, FILE_ID_INFO replacement, containment, reparse, mismatch, and reconstructed-owner negatives;
3. real worktree registration/admin capture and exact successful cleanup, including absent registration and admin path;
4. active-family or unproven-zero blocks every destructive and diagnostic action, including outer deadline/cancellation;
5. exact `CleanupTimeout`/10-second grace and nested deletion schedule, transient success, and nonretryable boundaries;
6. RM first-violation attribution, real admitted-platform empty/known-lock calls, Unicode Marshal layout, injected state-machine negatives, caps/errors/session end, and no shutdown;
7. primary failure plus ordered cleanup degradation and #106 secondary evidence;
8. quarantine success and refusal partitions;
9. concurrent fixtures and unrelated repo/ref/config/worktree isolation;
10. live Windows disposable-repo lock-release end-to-end with no residual registration/admin/root.

## Internal phase verification

B1 focused: `dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter FullyQualifiedName~FixtureCleanupAuthorityTests`, exactly `3 passed/0 failed/0 skipped`; then internal affected `dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter "FullyQualifiedName~FixtureCleanupAuthorityTests|FullyQualifiedName~ProcessOwnershipTests"`, exactly `79 passed/0 failed/0 skipped`.

B2 focused: `dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter FullyQualifiedName~FixtureCleanupProcessTests`, exactly `3 passed/0 failed/0 skipped`; then internal affected `dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter "FullyQualifiedName~FixtureCleanupAuthorityTests|FullyQualifiedName~FixtureCleanupProcessTests|FullyQualifiedName~ProcessOwnershipTests"`, exactly `82 passed/0 failed/0 skipped`.

B3 focused: `dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter FullyQualifiedName~FixtureCleanupIntegrationTests`, exactly `4 passed/0 failed/0 skipped`; before ReviewRequired, internal complete affected `dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter "FullyQualifiedName~FixtureCleanupAuthorityTests|FullyQualifiedName~FixtureCleanupProcessTests|FullyQualifiedName~FixtureCleanupIntegrationTests|FullyQualifiedName~ProcessOwnershipTests"`, exactly `86 passed/0 failed/0 skipped`. All 79/82/86 commands are internal verification, never final gates. No zero-discovery `FixtureCleanupTests` command exists.

Planning validation only; these commands are not run while preparing the package.

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
Qhatahet is the reserved eligible untouched non-author human reviewer for the latest candidate. Any replacement follows
the current reviewer-change evidence policy; no reviewer metadata is created at Ready.

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

Phase A requires both Qhatahet and Abood-essa to review the same immutable replacement planning SHA and post
authenticated T2 receipts. They approve only same-issue internal sequencing/spec/allowlists, not implementation findings
or the final gate. Phase B requires the worker/Orchestrator to invoke an independent Reviewer agent on the complete latest
candidate and record its invocation/output digest, then self-review/dispositions, focused/affected green,
`ReviewRequired`, and one authenticated latest-head non-author human GitHub approval for the same implementation SHA
reserved to Qhatahet (replacement only under policy evidence). A human may independently run tools but need not invoke
the Reviewer agent. Phase A cannot defer or dispose Phase B findings; Phase B cannot amend the contract without a new
amendment. No Ready or owner bypass is claimed. Before implementation/promotion, capture clean current-main
complete-suite counts/signatures and the rule for unrelated known failures; fixture groups may not pass by skip.

## Acceptance proof

Groups 1–2 prove admission and receipt authority before mutation; groups 3–4 prove exact Git identity, chronology, and
family-zero gating; groups 5–6 prove deterministic retry and bounded RM attribution; groups 7–8 prove precedence and
quarantine refusal/success; group 9 proves concurrency and byte-equivalent unrelated state; group 10 is the first
observable live Windows disposable-repository path and proves no residual registration, admin directory, or root. Each
semantic contract above must map to its first observable grouped test and, where applicable, that live path. No planning
activity claims product or test verification.
