# I100-B planning decision ledger

This is a planning-only ledger. PR119 commits, pushes, reviews, and child issue creation/comment/closure occurred as
superseded planning history. No product/test command, implementation, or activation occurred during this replacement
amendment. The rejected candidate is preserved local-only and its old branch is not reused.

## Research sources

- Git worktree identity and removal: https://git-scm.com/docs/git-worktree
- Restart Manager admission/session: https://learn.microsoft.com/windows/win32/api/restartmanager/nf-restartmanager-rmstartsession
- Restart Manager resource registration: https://learn.microsoft.com/windows/win32/api/restartmanager/nf-restartmanager-rmregisterresources
- Restart Manager process listing: https://learn.microsoft.com/windows/win32/api/restartmanager/nf-restartmanager-rmgetlist
- Restart Manager session end: https://learn.microsoft.com/windows/win32/api/restartmanager/nf-restartmanager-rmendsession
- Microsoft CreateFileW: https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew
- Microsoft SetFileInformationByHandle: https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-setfileinformationbyhandle
- Microsoft FILE_RENAME_INFO: https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_rename_info
- Local accepted #106 public API boundary and cleanup pattern findings: `I100-A/checkpoint.md` and `I100-A/ledger.md`,
  including public `ContainedProcess` observations, active-zero proof, teardown evidence, and ordered secondary evidence.
- Reusable QHTTP/GH93 patterns were inspected as read-only risk input; neither contract supplies a sentinel, quarantine,
  or complete Restart Manager cleanup contract.
- Readiness and authority: Issue #107 historical issue/readiness context, authored by Ahmad and not owner authorization,
  https://github.com/Bilaltariq41/SeqDoc/issues/107#issuecomment-5696378047; parent split
  https://github.com/Bilaltariq41/SeqDoc/issues/100#issuecomment-5681943603; publication
  https://github.com/Bilaltariq41/SeqDoc/issues/100#issuecomment-5694678193.

## Decisions

| Decision | Frozen resolution |
|---|---|
| Ownership | Immutable receipt plus versioned sentinel is the sole destructive authority; all paths are canonical, non-reparse, listed descendants. |
| Process boundary | #106 public observations only; cleanup must prove `ACTIVE_PROCESS_ZERO` before any deletion, RM diagnostics, or quarantine. |
| Git boundary | Exact registration and captured admin path are removed through `git worktree remove --force`; no prune or manual admin deletion. |
| Diagnostics | RM is bounded attribution evidence only, never ownership or termination authority; stable receipts exclude raw paths and timestamps. |
| Failure | Existing primary failure wins; cleanup evidence is chronological and degradation cannot become success. |
| Lifecycle | Revalidate before every deletion attempt, use the fixed eight-attempt schedule, and quarantine only after all gates. |
| Concurrency | Serialize only same-common-dir metadata mutations; independent fixture roots and unrelated repository snapshots remain isolated. |
| Scope | Future implementation is limited to exactly two paths: `FixtureCleanup.cs` and `FixtureCleanupTests.cs`, plus this checkpoint/ledger and generated governance state. The csproj is excluded. |

## Inherited I100-B contract dispositions (not PR119 findings)

| Finding | Disposition |
|---|---|
| F1 — family-proof bound and cancellation | Fixed in the planning contract: finite `CleanupTimeout`, fixed 10-second `FamilyProofGrace`, 40-second default outer deadline, reserved grace before #106 `WaitAsync`, mandatory cleanup despite caller cancellation, overrun/finalization rules, and nested two-second deletion sub-budget are frozen. |
| F2 — Git residual safety | Fixed: only receipt-listed control-root descendants may be directly deleted; captured common-dir admin data is preserved on residual or quarantine paths, and success requires both admin and registration absence. |
| F3 — exact sentinel, receipt, and partial identity | Fixed: schema-v1 sentinel, cryptographic token, FILE_ID_INFO identities, reparse-safe revalidation, monotonic owner stages, and reconstructed-owner fail-closed behavior are frozen. |
| F4 — Restart Manager managed interop admission/state machine | Fixed: exact Unicode P/Invoke attributes, constants, layouts, signatures, 3-call count semantics, real admitted-platform empty/known-lock call, injected negatives, mandatory session end, and no shutdown/ownership inference are frozen. |
| F5 — claims and reviewer | Fixed: exact GH-107 claims exclude csproj; Qhatahet is reserved for Phase B and both Qhatahet/Abood-essa are required for Phase A. |
| F6 — lifecycle truth | Fixed: GH-107 is Blocked and unselected; B1/B2/B3 are internal phases with no child lifecycle, PR, merge, or gate; execution remains idle. |

## Readiness and lifecycle

Readiness claims are frozen but intentionally unleased. Activation must supply exactly these canonical lowercase claims:
`path:tests/seqdoc.acceptancetests/fixturecleanup.cs`,
`path:tests/seqdoc.acceptancetests/fixturecleanuptests.cs`,
`path:docs/work/quality/i100-b`, `path:docs/project/work-items/gh-107.json`,
`fixture:fixturecleanup`, `governance-tool:tools/governance/work_state.py`, and
`exclusive:acceptance-git-worktree-metadata`. This exact set is acquired only by activation to avoid preemptive lease
blocking; no selection occurs now.

GH-106 is the closed dependency at the supplied accepted head and closeout baseline. GH-107 is `Blocked`, unselected,
and execution remains idle. The replacement branch contains only the three declared planning files relative to the
18aae5e baseline; no product/test/build verification, implementation, or activation occurred during this amendment.

The replacement state is instead `Blocked` and unselected. Issues #116–#118 are closed superseded history under
https://github.com/Bilaltariq41/SeqDoc/issues/107#issuecomment-5775101734. PR119 is superseded, unmerged, and not
activation ancestry. Historical PR119 commits/pushes/reviews and child issue create/comment/close are recorded as facts;
this amendment performed no product/test command, implementation, or activation.

## PR119 finding dispositions

Exactly four findings are recorded:

| Finding | Subject and disposition |
|---|---|
| F1 | B2/B3 ledgers misidentify reviewed findings — resolved by deleting child authority and accurately inheriting technical clauses in this one I100-B ledger. |
| F2 | Implementation deferral authority conflicts — Phase A cannot dispose or defer Phase B; no ownerless `Deferred` disposition. |
| F3 | B3 declares two final gates — the 86 result is internal affected verification; the full Acceptance project command is the sole final gate. |
| F4 | Independent review receipt not reproducible — prior agent summary is advisory only; a new review receipt will be posted on the replacement PR. |

## PR120 finding dispositions

| Finding | Subject | Disposition and proof |
|---|---|---|
| Qais I120-F1 | Independent human peer must invoke Reviewer agent at Phase B | **Fixed.** Under collaboration-model lines 37–39, the worker invokes its own Reviewer agent and self-reviews first; after green/dispositions at `ReviewRequired`, reserved independent human Qhatahet separately invokes Reviewer agent against the same complete latest SHA and posts the authenticated GitHub receipt before Gate Runner. |
| Abood I100-B-R2-F1 | Quarantine parent/source/target authority | **Fixed.** The parent is canonical local-drive, component-wise non-reparse, volume/FILE_ID authority, never destructive; source is its immediate child and target is the absent exact sibling. Group 8 and the terminal residual receipt prove this boundary. |
| Abood I100-B-R2-F2 | Terminal quarantine and residual handling | **Fixed.** State is `QuarantinedTerminal`; original destructive receipt is consumed, local terminal observation is report-only, stable evidence is sanitized, and no process or fixture cleanup may later delete it. |
| Abood I100-B-R2-F3 | Marker chronology and lock attribution | **Fixed.** B3 freezes marker/PID order, post-family-zero marker persistence, Git verification, first 32/33, RM list identity, host release barrier, retry, marker removal, and final root proof; marker is not required to disappear after child termination. |
| Abood I100-B-R2-F4 | Owner attribution and review receipt | **Fixed.** Historical Ahmad readiness context is not owner authorization. Phase B requires worker Reviewer evidence, then the reserved human independently invokes Reviewer agent and posts an authenticated same-SHA GitHub receipt before Gate Runner. |
| Abood I100-B-R2-F5 | Durable review evidence | **Fixed.** Raw session/task handles are excluded; unavailable reviewer metadata is explicitly unauthenticated, and the replacement PR must carry actor/SHA/agent/version/boundary/digest/outcome/findings/dispositions/evidence URL. |
| Abood I100-B-R3-F1 | High — path-validation-to-mutation TOCTOU | **Fixed.** Qais concurred at formal review `https://github.com/Bilaltariq41/SeqDoc/pull/120#pullrequestreview-5282276127` and follow-up `https://github.com/Bilaltariq41/SeqDoc/pull/120#issuecomment-5782126057`. The checkpoint now requires live identity-bound parent/source handles, the exact x64 `CreateFileW`/`SetFileInformationByHandle`/`FileRenameInfo` contract with parent `RootDirectory` and `ReplaceIfExists=false`, share-denied source races, one generic pre-call barrier, exact postchecks that classify but never authorize, and no path-based fallback. Group 8 proves every race, refusal, success identity, exception, reconstruction, layout/handle admission, and terminal partition. |
| Abood I100-B-R4-F1 | Medium — quarantine cannot truthfully follow mandatory RoleDeleted/state contradiction | **Fixed.** The disposition maps to `RoleCleanupInProgress`/`RoleCleanupComplete`/`DeletionBudgetExhausted`/`QuarantinedTerminal`/`FailedResidual`; groups 5 and 8 prove the transitions, prerequisites, root-only quarantine failure, and no `RoleDeleted` state. |
| Abood I100-B-R4-F2 | Low — ambiguous source-name wording | **Fixed.** The checkpoint now distinguishes destination-name collision before the native call from a new unrelated object at the vacated source name after successful rename; group 8 proves both races and preservation. |
| Abood I100-B-R4-F3 | Low — R3 disposition outside Markdown table | **Fixed.** R2, R3, and R4 rows are contiguous in this one valid PR120 table; review evidence: `https://github.com/Bilaltariq41/SeqDoc/pull/120#pullrequestreview-5288493974`. |

## Worker review finding dispositions

| Finding | Disposition and governing evidence |
|---|---|
| I100-B-F1 | **Fixed.** The csproj remains outside the initial allowlist and claims. If concrete compiler/build evidence proves it necessary, stop before edit; amend GH-107/I100-B target paths, claims, risks, and tests under T2, run worker readiness review, and obtain latest-head non-author peer approval on the amended SHA before resuming. Unamended target expansion remains a stop condition, not an automatic permanent block or undocumented bypass. |
| I100-B-F2 | **Fixed.** Qais's controlling rule is accepted under `docs/project/collaboration-model.md` lines 37–39: the worker invokes its own Reviewer agent and self-reviews first; after green/dispositions at `ReviewRequired`, reserved independent human Qhatahet separately invokes Reviewer agent against the same complete latest SHA and posts an authenticated GitHub receipt before Gate Runner. The receipt records actor, exact SHA, reviewer agent name/version, invocation boundary, output digest/outcome, findings/dispositions, and evidence URL. |

## Global audit worker advisory dispositions

| Advisory ID | Disposition |
|---|---|
| I100-B-AUDIT-QUARANTINE-ROLE | **Fixed.** Removed quarantine from the receipt role map; quarantine is only separate parent/sibling move-target authority. |
| I100-B-AUDIT-FOCUSED-COMMAND | **Fixed.** One required cumulative 86/0/0 focused command is declared; earlier phase counts are optional developer checks. |
| I100-B-AUDIT-RM-CAPABILITY | **Fixed.** Missing or unloadable RM capability is an explicit blocking non-pass in group 6. |
| I100-B-AUDIT-STATE-OUTCOME | **Fixed.** Total physical state transitions, separate final outcome, primary chronology, degradation precedence, and terminal residual rules are explicit. |

## Advisory worker review receipt

Role: `review`; result: `REQUEST CHANGES`; findings: I100-B-F1 and I100-B-F2. This is advisory worker-review evidence,
not formal human completion. Reviewer name/version, invocation boundary, exact target SHA, output digest, and public
evidence URL are unavailable and intentionally not fabricated; no raw task/session identifier is persisted. The durable
review receipt will be posted on the replacement PR after final rereview.

## Abood finding inheritance map

Child issue authority is deleted; these original technical findings are inherited by the corresponding internal clauses
and proofs. Original issue URLs remain historical sources; the exact supplied Abood finding comment is retained for #116.

### Issue #116 — authority/Git phase

| Finding | Subject | Internal proof |
|---|---|---|
| F1 | Split authorization provenance ([comment](https://github.com/Bilaltariq41/SeqDoc/issues/116#issuecomment-5774337600)) | Phase A same-SHA dual-peer receipts; no owner bypass. |
| F2 | Immutable canonical candidate ([comment](https://github.com/Bilaltariq41/SeqDoc/issues/116#issuecomment-5774337600)) | Exactly three planning files between baseline and head; PR119 not ancestry. |
| F3 | Source common-directory ownership ([comment](https://github.com/Bilaltariq41/SeqDoc/issues/116#issuecomment-5774337600)) | B1 authority section: canonical non-reparse chains and FILE_ID before every mutation. |
| F4 | Final gate ([comment](https://github.com/Bilaltariq41/SeqDoc/issues/116#issuecomment-5774337600)) | Sole complete Acceptance gate after Phase B human review. |
| F5 | Exact authority/scope details ([comment](https://github.com/Bilaltariq41/SeqDoc/issues/116#issuecomment-5774337600)) | Two implementation paths, excluded csproj, claims, Qhatahet Phase B route. |

### Issue #117 — process/RM phase

| Finding | Subject | Internal proof |
|---|---|---|
| F1 | Split authority/canonical identity ([comment](https://github.com/Bilaltariq41/SeqDoc/issues/117#issuecomment-5774337571)) | Inherited B2 internal phase; same planning SHA and no child lifecycle or dependency. |
| F2 | Deadline equation/chronology ([comment](https://github.com/Bilaltariq41/SeqDoc/issues/117#issuecomment-5774337571)) | Outer deadline, reserved family grace, no post-expiry stage, and required finally evidence. |
| F3 | Retry/RM admission terms ([comment](https://github.com/Bilaltariq41/SeqDoc/issues/117#issuecomment-5774337571)) | Exact eight-attempt schedule, 2-second budget, full revalidation, RM table/state machine. |
| F4 | Exact paths/completion route ([comment](https://github.com/Bilaltariq41/SeqDoc/issues/117#issuecomment-5774337571)) | Two implementation paths, no csproj, B2 focused/affected route and no final gate. |
| F5 | Pin #106 dependency/receipts ([comment](https://github.com/Bilaltariq41/SeqDoc/issues/117#issuecomment-5774337571)) | Accepted #106 head/merge, public API boundary, and receipts are pinned before implementation. |

### Issue #118 — integration phase

| Finding | Subject | Internal proof |
|---|---|---|
| F1 | Split authority/canonical identity ([comment](https://github.com/Bilaltariq41/SeqDoc/issues/118#issuecomment-5774337587)) | Inherited B3 internal phase; same planning SHA and child issue is historical only. |
| F2 | Quarantine destination authority ([comment](https://github.com/Bilaltariq41/SeqDoc/issues/118#issuecomment-5774337587)) | Same-volume direct sibling, collision refusal, sentinel retention, and terminal report-only residual; FixtureCleanup grants no later removal authority and any external disposition requires separate authority. |
| F3 | Live executable/synchronization ([comment](https://github.com/Bilaltariq41/SeqDoc/issues/118#issuecomment-5774337587)) | Rooted marker stub, marker PID proof, independent FileStream lock, deterministic observer/barrier, no sleeps or termination. |
| F4 | Exact paths/receipts/review route ([comment](https://github.com/Bilaltariq41/SeqDoc/issues/118#issuecomment-5774337587)) | Two implementation paths, excluded csproj, B3 focused/affected route, and Qhatahet Phase B review. |
| F5 | Isolation inheritance ([comment](https://github.com/Bilaltariq41/SeqDoc/issues/118#issuecomment-5774337587)) | Four unrelated vectors and common-dir FILE_ID concurrency inherit B1/B2 authority. |

## Internal commands and review receipts

Required focused implementation command, before `ReviewRequired`: `dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter "FullyQualifiedName~FixtureCleanupAuthorityTests|FullyQualifiedName~FixtureCleanupProcessTests|FullyQualifiedName~FixtureCleanupIntegrationTests|FullyQualifiedName~ProcessOwnershipTests"`, exactly `86 passed/0 failed/0 skipped`. B1 3/0/0 and 79/0/0, B2 3/0/0 and 82/0/0, and B3 focused 4/0/0 remain optional developer checks only, not checkpoint commands, gates, or receipts. The sole final gate is `dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release`, once after human review and resolved findings.

Phase A requires both Qhatahet and Abood-essa to review the same immutable replacement planning SHA and post
authenticated T2 receipts; these approve only same-issue sequencing/spec/allowlists. Phase B requires the worker/
Orchestrator to invoke its own Reviewer agent and self-review first, then after focused/affected green and dispositions
at `ReviewRequired`, reserved independent human Qhatahet separately invokes Reviewer agent against the same complete latest
SHA and posts an authenticated GitHub receipt containing actor, exact SHA, agent name/version, invocation boundary,
output digest/outcome, findings/dispositions, and evidence URL. Only then does Gate Runner run the final gate. Prior
agent summaries are advisory and not formal independent-review completion. No Ready now.

## State, roles, and outcome proof

The sentinel role map is exactly sorted `cache`, `output`, `worktree`; quarantine is not a role or receipt-listed child.
The total state transitions are `AdmissionFailedNoOwnership` terminal; `Provisioned` -> `FamilyZero` or `FailedResidual`;
`FamilyZero` -> `GitDeregisteredAdminVerified` or `FailedResidual`; `GitDeregisteredAdminVerified` ->
`RoleCleanupInProgress` or `FailedResidual`; `RoleCleanupInProgress` -> `RoleCleanupComplete`,
`DeletionBudgetExhausted`, or `FailedResidual`; `RoleCleanupComplete` -> `RootDeleted` only, with root/sentinel failure
to `FailedResidual`; `DeletionBudgetExhausted` -> `QuarantinedTerminal` only when native identity proves completion,
otherwise `FailedResidual`. Terminal states reject later automatic destruction and there is no `RoleDeleted` state.
The in-progress inventory is sorted by role and records proven postconditions, remaining roles, attempts/offsets, and last
classification. Exhaustion requires a remaining role, admitted retry schedule, last retryable class, valid authority,
family zero, registration/admin absence, exact sentinel, and quarantine budget; it never implies role completion.

Final outcome is separate: only `RootDeleted` without primary failure/degradation is success. RM, EndSession, teardown,
quarantine, and residual degradations preserve non-success. Primary is set once in order of pre-existing failure,
first-observed cancellation, first cleanup failure, then deadline; later evidence is deterministic secondary chronology.
Groups 3/8/10 do not duplicate proof: group 3 owns Git identity, group 8 owns all quarantine, and group 10 owns live
successful cleanup only.

## Technical inheritance

The R3 native contract is exact, not an implementation choice: Kernel32.dll `CreateFileW` and
`SetFileInformationByHandle`, Unicode where applicable, `ExactSpelling=true`, `SetLastError=true`, Winapi. Parent
access is 0x80|0x00100000, share 1|2 (DELETE share omitted), disposition 3, flags 0x02000000|0x00200000, with no
inheritance/template; source access is 0x00010000|0x80|0x00100000 with the same share/disposition/flags. Class
`FileRenameInfo` is 3. On admitted x64, `FILE_RENAME_INFO` is manually packed as ReplaceIfExists DWORD 0 at offset 0,
zero padding 4–7, parent HANDLE offset 8, filename length offset 16, UTF-16 relative sibling bytes offset 20, exact
size `20 + FileNameLength`, no required terminator; `IntPtr.Size==8`, offsets, and buffer size are mandatory group-8
checks. SetFileInformationByHandle receives the live source handle, class 3, pointer and uint size, and false records
exact GetLastWin32Error locally. Parent/source handles remain live through the call and postclassification; omitted
FILE_SHARE_DELETE prevents competing delete-access opens until handle close. No FileRenameInfoEx/flags or path fallback.

The checkpoint's technical strengthening is authoritative: rooted GitExecutablePath and Program Files admission; exact
sentinel/stable-vs-local receipt and source/common FILE_ID chains; exact Git vectors without `--` fallback; four
unrelated vectors; outer deadline, retry starts/delays/2-second budget and revalidation; exact RM ABI/state machine;
identity-bound CreateFileW parent/source handles, FILE_ID checks, SetFileInformationByHandle/FileRenameInfo sibling
rename with no `Directory.Move` fallback, collision/race classifications, and terminal report-only residual rules;
`sleep-with-marker <owned-marker-path> 30000`, marker PID equal to public
ProcessId, independent test-host FileStream `FileShare.None`, deterministic 32/33+RM observer barrier, no sleeps or
testhost termination; and common-dir FILE_ID concurrency. Exact Git vectors are `worktree add --detach
<owned-absolute-path> <40-lowercase-revision>`, `rev-parse --git-common-dir`, `rev-parse --absolute-git-dir`,
`status --porcelain=v1 -z --untracked-files=all`, `for-each-ref --format=%(refname)%00%(objectname)%00%(symref)%00
--sort=refname`, `config --local --null --list`, `worktree list --porcelain`, and `worktree remove --force
<owned-absolute-path>`.
