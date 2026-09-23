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
- Microsoft FILE_RENAME_INFORMATION: https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/ns-ntifs-_file_rename_information
- Microsoft WDK `FILE_INFORMATION_CLASS` / `FileRenameInformation=10` enum ordering, and the Wine project's
  `winternl.h`, independently confirmed against each other for the full native enum ordering 1–11: documented in the
  Issue #107 comment thread following the recovery decision.
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
| Baselines | GH-106 baseline `ab6e3e1cf16213ee5346506b16949fa32c4ddfa4`; accepted head `182ea35533482cebdfc070b368f3a7fa247a1735`; merge `227d2e9f8b49ce6a414795b16bb0408ed213012a`; GH-107 replacement baseline `18aae5e0364c0b13549bd0c5333ba48f8116bc9a`. |
| Concurrency | Serialize only same-common-dir metadata mutations; independent fixture roots and unrelated repository snapshots remain isolated. |
| Scope | Future implementation is limited to exactly two paths: `FixtureCleanup.cs` and `FixtureCleanupTests.cs`, plus this checkpoint/ledger and generated governance state. The csproj is excluded. |

## Inherited I100-B contract dispositions (not PR119 findings)

| Finding | Disposition |
|---|---|
| F1 — family-proof bound and cancellation | Fixed in the planning contract: finite `CleanupTimeout`, fixed 10-second `FamilyProofGrace`, 40-second default outer deadline, reserved grace before #106 `WaitAsync`, mandatory cleanup despite caller cancellation, overrun/finalization rules, and nested two-second deletion sub-budget are frozen. |
| F2 — Git residual safety | Fixed: only receipt-listed control-root descendants may be directly deleted; captured common-dir admin data is preserved on residual or quarantine paths, and success requires both admin and registration absence. |
| F3 — exact sentinel, receipt, and partial identity | Fixed: schema-v1 sentinel, cryptographic token, FILE_ID_INFO identities, reparse-safe revalidation, monotonic owner stages, and reconstructed-owner fail-closed behavior are frozen. |
| F4 — Restart Manager managed interop admission/state machine | Fixed: exact Unicode P/Invoke attributes, constants, layouts, signatures, 3-call count semantics, real admitted-platform empty/known-lock call, injected negatives, mandatory session end, and no shutdown/ownership inference are frozen. |
| F5 — claims and reviewer | Fixed: exact GH-107 claims exclude csproj; Qhatahet is reserved for Phase B and both Qhatahet/Abood-essa are required for Phase A. **Superseded for current authority by the 2026-09-23 recovery decision (see the dated amendment section below): Qhatahet became the implementer/candidate contributor and is no longer Phase-B reviewer-eligible; Abood-essa is the confirmed replacement reserved Phase-B reviewer. Preserved here as historical record only.** |
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

Historical record: the "Qais I120-F1" row below describes the reviewer-role assignment as it stood before the
2026-09-23 recovery decision (see the dated amendment section below), which made Qhatahet the implementer/candidate
contributor for GH-107/I100-B B1–B3 and named Abood-essa the confirmed replacement reserved Phase-B reviewer.
Preserved as historical record only, not current reviewer-eligibility authority.

| Finding | Subject | Disposition and proof |
|---|---|---|
| Qais I120-F1 | Independent human peer must invoke Reviewer agent at Phase B | **Fixed.** Under `docs/project/collaboration-model.md, Delivery procedure, item 3`, the worker invokes its own Reviewer agent and self-reviews first; after green/dispositions at `ReviewRequired`, reserved independent human Qhatahet separately invokes Reviewer agent against the same complete latest SHA and posts the authenticated GitHub receipt before Gate Runner. |
| Abood I100-B-R2-F1 | Quarantine parent/source/target authority | **Fixed.** The parent is canonical local-drive, component-wise non-reparse, volume/FILE_ID authority, never destructive; source is its immediate child and target is the absent exact sibling. Group 8 and the terminal residual receipt prove this boundary. |
| Abood I100-B-R2-F2 | Terminal quarantine and residual handling | **Fixed.** State is `QuarantinedTerminal`; original destructive receipt is consumed, local terminal observation is report-only, stable evidence is sanitized, and no process or fixture cleanup may later delete it. |
| Abood I100-B-R2-F3 | Marker chronology and lock attribution | **Fixed.** B3 freezes marker/PID order, post-family-zero marker persistence, Git verification, first 32/33, RM list identity, host release barrier, retry, marker removal, and final root proof; marker is not required to disappear after child termination. |
| Abood I100-B-R2-F4 | Owner attribution and review receipt | **Fixed.** Historical Ahmad readiness context is not owner authorization. Phase B requires worker Reviewer evidence, then the reserved human independently invokes Reviewer agent and posts an authenticated same-SHA GitHub receipt before Gate Runner. |
| Abood I100-B-R2-F5 | Durable review evidence | **Fixed.** Raw session/task handles are excluded; unavailable reviewer metadata is explicitly unauthenticated, and the replacement PR must carry actor/SHA/agent/version/boundary/digest/outcome/findings/dispositions/evidence URL. |
| Abood I100-B-R3-F1 | High — path-validation-to-mutation TOCTOU | **Fixed.** Qais concurred at formal review `https://github.com/Bilaltariq41/SeqDoc/pull/120#pullrequestreview-5282276127` and follow-up `https://github.com/Bilaltariq41/SeqDoc/pull/120#issuecomment-5782126057`. The checkpoint now requires live identity-bound parent/source handles, the exact x64 `CreateFileW`/`SetFileInformationByHandle`/`FileRenameInfo` contract with parent `RootDirectory` and `ReplaceIfExists=false`, share-denied source races, one generic pre-call barrier, exact postchecks that classify but never authorize, and no path-based fallback. Group 8 proves every race, refusal, success identity, exception, reconstruction, layout/handle admission, and terminal partition. |
| Abood I100-B-R4-F1 | Medium — quarantine cannot truthfully follow mandatory RoleDeleted/state contradiction | **Fixed.** The disposition maps to `RoleCleanupInProgress`/`RoleCleanupComplete`/`DeletionBudgetExhausted`/`QuarantinedTerminal`/`FailedResidual`; groups 5 and 8 prove the transitions, prerequisites, root-only quarantine failure, and no `RoleDeleted` state. |
| Abood I100-B-R4-F2 | Low — ambiguous source-name wording | **Fixed.** The checkpoint now distinguishes destination-name collision before the native call from a new unrelated object at the vacated source name after successful rename; group 8 proves both races and preservation. |
| Abood I100-B-R4-F3 | Low — R3 disposition outside Markdown table | **Fixed.** R2, R3, and R4 rows are contiguous in this one valid PR120 table; review evidence: `https://github.com/Bilaltariq41/SeqDoc/pull/120#pullrequestreview-5288493974`. |

## PR120 R5 and Qhatahet dispositions

Historical record: at the time of this section, Qhatahet still held the reserved independent Phase-B human reviewer
role referenced in rows below. The 2026-09-23 recovery decision (see the dated amendment section below) superseded
that role assignment: Qhatahet became the implementer/candidate contributor for GH-107/I100-B B1–B3, and Abood-essa is
the confirmed replacement reserved Phase-B reviewer. The rows below are preserved as historical record of PR120's
findings and are not current reviewer-eligibility authority.

| Finding | Subject | Disposition and proof |
|---|---|---|
| Abood I100-B-R5-F1 | Complete destructive ABI | **Fixed.** The checkpoint's one authoritative managed admission table freezes exact Kernel32 declarations, masks, safe-handle/error/finally semantics, x64 `FILE_RENAME_INFO` layout, name/buffer validation, no-call failures, and group-8 metadata/layout/disposal proofs. Primary sources are CreateFileW, SetFileInformationByHandle, FILE_RENAME_INFO, and ntifs FILE_RENAME_INFORMATION links above. |
| Abood I100-B-R5-F2 | Baseline identities | **Fixed.** GH-106 baseline/head/merge and GH-107 replacement baseline are frozen in the checkpoint and ledger; superseded `dfc28b0...` is historical GH-107 context only. |
| Abood I100-B-R5-F3 | Git working directories | **Fixed.** Explicit source/owned-worktree cwd table, scalar-output parsing, relative-output resolution, and process-cwd mismatch tests are frozen in the checkpoint. |
| Abood I100-B-R5-F4 | Retry completion/overrun | **Fixed.** Attempt admission, inclusive 1950/2000 boundary, overrun classifications, no ninth attempt, in-flight outer expiry, and retained physical postconditions are frozen in chronology and group 5. |
| Abood I100-B-R5-F5 | Baseline observation, not gate | **Fixed.** A clean-main full Acceptance Release observation with exact environment/count/signature receipt is required before promotion; it is not the focused or final gate, and unavailable evidence keeps GH-107 Blocked. |
| Abood I100-B-R5-F6 | Soft test budget | **Fixed.** The target is 10 grouped methods, with concrete-risk nonduplicate additions recorded with reason/group/total/expected discovery; current 86 is 76+10 and there is no hard cap. |
| Qhatahet latest F1 | Durable worker receipt | **Fixed.** The superseding receipt is linked above and must be updated with actor/invoker, exact SHA, agent/version, boundary, digest/outcome, findings/dispositions before Phase A authorization. |
| Qhatahet latest F2 | Collaboration citation and review sequence | **Fixed.** The ledger uses `docs/project/collaboration-model.md, Delivery procedure, item 3`; worker Reviewer/self-review precedes reserved human's independent Reviewer run and authenticated receipt before Gate Runner. |
| Qhatahet note | Admission observable | **Fixed.** Acceptance proof maps Group 1 admission failure to `AdmissionFailedNoOwnership` and admission success to `Provisioned` as first observed in Group 2. |

## PR120 R6 disposition

| Finding | Subject | Disposition and proof |
|---|---|---|
| Abood I100-B-R6-F1 | High — impossible and competing quarantine target grammars | **Fixed.** Clause 1 freezes the source basename as exact ordinal `seqdoc-fixture-<token>` using the admitted unpadded base64url token. Quarantine constructs its sole target as `sourceRootName + ".quarantine"`; source-only grammar validation is separate from exact target equality and the single allowed suffix dot. Group 8 covers the valid construction and every requested token, suffix, character, path-form, and normalization negative before allocation or native rename. Review evidence: `https://github.com/Bilaltariq41/SeqDoc/pull/120#pullrequestreview-5289552311`. |
| Qhatahet same-head approval | R2–R5 and prior Qhatahet repairs | **Preserved as historical evidence.** Qhatahet approved `5ad33e85638b5ba2297a03351fe86e6b55709a66` at `https://github.com/Bilaltariq41/SeqDoc/pull/120#pullrequestreview-5289582563`; this R6 amendment makes that approval stale for authorization, so both peers must review the new exact SHA. |

## Worker review finding dispositions

Historical record: row I100-B-F2 below describes the reviewer-role assignment as it stood before the 2026-09-23
recovery decision (see the dated amendment section below), which made Qhatahet the implementer/candidate contributor
and named Abood-essa the confirmed replacement reserved Phase-B reviewer. Preserved as historical record only.

| Finding | Disposition and governing evidence |
|---|---|
| I100-B-F1 | **Fixed.** The csproj remains outside the initial allowlist and claims. If concrete compiler/build evidence proves it necessary, stop before edit; amend GH-107/I100-B target paths, claims, risks, and tests under T2, run worker readiness review, and obtain latest-head non-author peer approval on the amended SHA before resuming. Unamended target expansion remains a stop condition, not an automatic permanent block or undocumented bypass. |
| I100-B-F2 | **Fixed.** Qais's controlling rule is accepted under `docs/project/collaboration-model.md, Delivery procedure, item 3`: the worker invokes its own Reviewer agent and self-reviews first; after green/dispositions at `ReviewRequired`, reserved independent human Qhatahet separately invokes Reviewer agent against the same complete latest SHA and posts an authenticated GitHub receipt before Gate Runner. The receipt records actor, exact SHA, reviewer agent name/version, invocation boundary, output digest/outcome, findings/dispositions, and evidence URL. |

## Global audit worker advisory dispositions

| Advisory ID | Disposition |
|---|---|
| I100-B-AUDIT-QUARANTINE-ROLE | **Fixed.** Removed quarantine from the receipt role map; quarantine is only separate parent/sibling move-target authority. |
| I100-B-AUDIT-FOCUSED-COMMAND | **Fixed.** One required cumulative 86/0/0 focused command is declared; earlier phase counts are optional developer checks. |
| I100-B-AUDIT-RM-CAPABILITY | **Fixed.** Missing or unloadable RM capability is an explicit blocking non-pass in group 6. |
| I100-B-AUDIT-STATE-OUTCOME | **Fixed.** Total physical state transitions, separate final outcome, primary chronology, degradation precedence, and terminal residual rules are explicit. |

## Superseded worker review history and durable receipt

The earlier worker review summary returned `REQUEST CHANGES` with I100-B-F1 and I100-B-F2; both findings are Fixed above.
It is superseded review history, not current status or formal human completion. The durable reserved receipt is
https://github.com/Bilaltariq41/SeqDoc/pull/120#issuecomment-5792443353. That public receipt must be updated after the
exact-SHA worker Reviewer run and show actor/invoker, exact SHA, agent name/version when available, invocation boundary,
output digest/outcome, findings/dispositions. Until that update is an authenticated approval, it does not authorize
Phase A. The PR body claim is subordinate to this ledger and linked receipt; no approval is claimed here.

## Abood finding inheritance map

Child issue authority is deleted; these original technical findings are inherited by the corresponding internal clauses
and proofs. Original issue URLs remain historical sources; the exact supplied Abood finding comment is retained for #116.
Any "Qhatahet Phase B" mention in the tables below reflects the reviewer-role assignment as it stood at that historical
point; the 2026-09-23 recovery decision (see the dated amendment section below) superseded it — Qhatahet is now the
implementer/candidate contributor and Abood-essa is the confirmed replacement reserved Phase-B reviewer. Preserved as
historical record only.

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

Phase A requires both reserved peers to review the same immutable replacement planning SHA and post authenticated T2
receipts; these approve only same-issue sequencing/spec/allowlists. When one reserved peer authors the amendment
under review, only the non-author peer's authenticated T2 approval is required for that exact SHA (see the Review and
activation contract in `checkpoint.md`). Phase B requires the worker/Orchestrator to invoke its own Reviewer agent and
self-review first, then after focused/affected green and dispositions at `ReviewRequired`, reserved independent human
Abood-essa — the confirmed replacement Phase-B reviewer under the 2026-09-23 recovery decision, since implementer
Qhatahet is disqualified from Phase B as of that decision — separately invokes Reviewer agent against the same complete
latest SHA and posts an authenticated GitHub receipt containing actor, exact SHA, agent name/version, invocation
boundary, output digest/outcome, findings/dispositions, and evidence URL. Only then does Gate Runner run the final
gate. Prior agent summaries are advisory and not formal independent-review completion. No Ready now.

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

The checkpoint's managed/native ABI table is authoritative and incorporates R5-F1, as amended for the native rename ABI
(see the dated amendment section below): exact Kernel32 `CreateFileW` handle-opening declarations plus `ntdll.dll`
`NtSetInformationFile` for the rename call itself, metadata, access/share masks, safe-handle/`NTSTATUS`/finally behavior,
x64 native `FileRenameInformation`-shaped buffer offsets and checks, identity-bound live handles, and no path fallback.
The Microsoft and ntifs primary links above are the durable references; the ledger does not restate a second ABI variant.

The checkpoint's technical strengthening is authoritative: rooted GitExecutablePath and Program Files admission; exact
sentinel/stable-vs-local receipt and source/common FILE_ID chains; exact Git vectors without `--` fallback; four
unrelated vectors; outer deadline, retry starts/delays/2-second budget and revalidation; exact RM ABI/state machine;
identity-bound CreateFileW parent/source handles, FILE_ID checks, `NtSetInformationFile`/native `FileRenameInformation=10`
sibling rename with no `Directory.Move` fallback, collision/race classifications, and terminal report-only residual rules;
`sleep-with-marker <owned-marker-path> 30000`, marker PID equal to public
ProcessId, independent test-host FileStream `FileShare.None`, deterministic 32/33+RM observer barrier, no sleeps or
testhost termination; and common-dir FILE_ID concurrency. Exact Git vectors are `worktree add --detach
<owned-absolute-path> <40-lowercase-revision>`, `rev-parse --git-common-dir`, `rev-parse --absolute-git-dir`,
`status --porcelain=v1 -z --untracked-files=all`, `for-each-ref --format=%(refname)%00%(objectname)%00%(symref)%00
--sort=refname`, `config --local --null --list`, `worktree list --porcelain`, and `worktree remove --force
<owned-absolute-path>`.

## 2026-09-23 quarantine rename ABI amendment (Issue #107 spike)

### Recovery decision and role change

The 2026-09-23 recovery decision
(https://github.com/Bilaltariq41/SeqDoc/issues/107#issuecomment-5795266589) named Qhatahet as implementer/candidate
contributor for GH-107/I100-B B1–B3 product implementation, AhmadKrarha as coordinator/canonical-record custodian, and
Abood-essa as the proposed replacement independent Phase-B reviewer, subsequently confirmed by Abood-essa
(https://github.com/Bilaltariq41/SeqDoc/issues/107#issuecomment-5795300539). Because Qhatahet's own investigation
design is incorporated into this amendment and he authored it, he is disqualified as its reviewer and as GH-107's
Phase-B reviewer; every "reserved independent human Qhatahet" reference elsewhere in this ledger and in
`checkpoint.md` that predates this decision is preserved as historical record only and does not authorize current
review of this or any later B1–B3 candidate. Abood-essa's T2 approval of this exact amendment SHA is independent
approval of the amendment, not authorship of its design or its own acceptance of the underlying native-API boundary;
Abood-essa explicitly reserved his own judgment on the replacement design when acknowledging the measured blocker
(https://github.com/Bilaltariq41/SeqDoc/issues/107#issuecomment-5795782960: "This is not approval of a replacement
contract... I will review the proposed amendment and evidence but will not author or repair its design.").

Ahmad's coordinator selection of the specific recommended option (native `NtSetInformationFile`, Option 2 of the two
compared in the option-comparison comment below) is delegated in his step-2/step-3 instructions
(https://github.com/Bilaltariq41/SeqDoc/issues/107#issuecomment-5795896275) but Ahmad has not yet posted a separate
coordinator-selection confirmation naming Option 2 specifically. That confirmation remains outstanding and is required,
as coordinator/canonical-record custodian authority separate from Abood-essa's independent T2 approval, before this
amendment is treated as fully authorized; Abood-essa's approval of this SHA does not substitute for it.

### Spike evidence and selected replacement

A throwaway, read-only spike (not in this repo; full harness source, build/run commands, and results table published at
https://github.com/Bilaltariq41/SeqDoc/issues/107#issuecomment-5796090920) measured, on one machine — Windows build
`10.0.26200.0` (a Windows 11 24H2/25H2-era build), x64, .NET runtime `10.0.11` (SDK `10.0.302`), assumed-NTFS volume
(filesystem name not independently confirmed on that machine; treated as lower-confidence evidence) — that
`SetFileInformationByHandle` with `FileRenameInfo` (class 3) and a non-NULL `RootDirectory` handle repeatedly fails
with Win32 error 87 (`ERROR_INVALID_PARAMETER`) across three development runs, with the source object's `FILE_ID_INFO`
provably unchanged after each failure (a true no-op, not a partial rename). This is a measured result on that one
environment, not a universal claim across every supported Windows build. It also observably conflicts with current
Microsoft `FILE_RENAME_INFO` documentation, which describes a `RootDirectory`-relative name as a supported form of the
structure; the conflict is between that public documentation and this empirical Win32-layer observation, not a
confirmation by the documentation that the form is unsupported.

The same spike measured, on the same one machine, that the native alternative succeeds: `NtSetInformationFile`
(`ntdll.dll`) with native `FileRenameInformation` (class 10, independently confirmed against Microsoft's WDK docs and
the Wine project's `winternl.h`, both agreeing on the full enum ordering 1–11) returns `NTSTATUS = 0`, preserving file
identity (proven via `FILE_ID_INFO` before/after). Abood-essa acknowledged this measured blocker but did not approve or
author the replacement design (see the Recovery decision and role change subsection above); this PR's amendment review
is the first authenticated T2 review of the native proposal. The full option comparison (path-based narrowed-TOCTOU
rename versus native `NtSetInformationFile`, with the native option recommended as the smaller change against the
already-reviewed contract) is published at
https://github.com/Bilaltariq41/SeqDoc/issues/107#issuecomment-5796097951.

Selected replacement: native `NtSetInformationFile`, `FileRenameInformation=10`, replacing the Win32
`SetFileInformationByHandle`/`FileRenameInfo=3` rename call only; the `CreateFileW` handle-opening sequence, buffer byte
layout, and every other frozen quarantine design element are unchanged.

| Change | Disposition |
|---|---|
| 1. Native rename ABI throughout `checkpoint.md` | **Applied.** The Frozen semantic contract quarantine clause, the B3 technical-strengthening paragraph, and the Technical inheritance sections here now consistently describe `NtSetInformationFile`/`ntdll.dll`, `IO_STATUS_BLOCK`, `FileRenameInformation=10`, and `NTSTATUS==0` success; `CreateFileW` handle-opening stays on `kernel32.dll` and is unaffected. |
| 2. Risks | **Applied.** Added reliance on the undocumented native `NtSetInformationFile` API, with no Microsoft compatibility guarantee across Windows updates, accepted because this is test-only tooling that fails closed and loudly. |
| 3. Group 8 test description | **Applied.** Updated to `NtSetInformationFile`/`ntdll.dll`, `IO_STATUS_BLOCK` layout, and `NTSTATUS==0` success check in place of `SetFileInformationByHandle`/BOOL marshalling; `CreateFileW` handle-opening assertions are unaffected. |
| 4. Permitted test seams table | **Applied.** Added a closed "Permitted test seams" table (clock/sleeper, per-native-call return-code override hook, generic quarantine observer/barrier), each row limited to an OS return value or timing observation, never a stage/authority/outcome decision. **Superseded by the I100-B-A3 repair below: the table now has four exact rows (clock/sleeper; the one `NtSetInformationFile` hook; each of the four RM entry-point hooks; the barrier), not the three summarized here.** |
| 5. No-override positive-path rule | **Applied.** Added the rule that every group's positive/success partition runs with no test seam/hook active, and that each group's checkpoint/ledger can name the specific production guard whose removal/inversion would fail that group. |
| 6. Exactly ten `[Fact]` methods, no theories | **Applied.** The test-budget section now requires exactly ten `[Fact]` methods in `FixtureCleanupTests.cs`, explicitly prohibits `[Theory]`/parameterized tests in that file, and withdraws the prior "theories/subcases permitted" flexibility, to keep the 86-count exact. **Superseded by the I100-B-A5 repair below: ten is now a soft target, not an immutable cap, while `[Theory]` remains prohibited.** |
| 7. Rooted-Git-path boundary | **Applied.** Added an explicit sentence next to the existing `%ProgramFiles%\Git\cmd\git.exe`/x86 admission rule confirming a differently located Git installation is an accepted supported-environment boundary and a blocking non-pass, never a skip. |
| 8. B1/B2/B3 intermediate Reviewer-agent checks and `TimeProvider` seam permission | **Applied.** Added a paragraph requiring an independent Reviewer-agent pass after each of B1, B2, and B3 (advisory containment checks only, not lifecycle states/approvals/gates), and explicitly permitted a hand-rolled `TimeProvider`-shaped test seam type inside `FixtureCleanupTests.cs` (not a NuGet package reference, to stay out of `csproj` scope) for the retry-schedule clock/sleeper seam. |

This amendment changed only `docs/work/quality/I100-B/checkpoint.md` and this ledger; `docs/project/work-items/GH-107.json`
is untouched, and no product/test command, implementation, or activation occurred.

## 2026-09-23 I100-B-A1–A7 repair (Abood-essa PR121 amendment review)

Abood-essa reviewed exact head `141e3a06f6c5cbead035c6aab444f17b013c3859` (this file's prior amendment) and requested
changes at https://github.com/Bilaltariq41/SeqDoc/pull/121#pullrequestreview-5292257454 (T2 amendment review, changes
requested, 2026-09-23T14:22:15Z) with 7 findings (5 High, 2 Medium). All seven are repaired in this same round.

| Finding | Disposition and proof |
|---|---|
| I100-B-A1 (High) — evidence and authorization overstated | **Fixed.** The "Spike evidence and selected replacement" subsection above now states the exact measured one-machine environment (Windows `10.0.26200.0`, x64, .NET `10.0.11`/SDK `10.0.302`, assumed-NTFS) without universalizing the result, records the observed conflict between current Microsoft `FILE_RENAME_INFO` documentation and the empirical Win32-layer failure instead of claiming documentation confirmation, removes the false "two human reviewers accepted" claim, links the exact spike-packet (`...5796090920`) and option-comparison (`...5796097951`) comments, and the new "Recovery decision and role change" subsection records that Ahmad's own coordinator-selection confirmation of the specific native option remains outstanding and that Abood-essa's T2 approval is independent approval, not design authorship. |
| I100-B-A2 (High) — reviewer contract still names the implementer as reviewer | **Fixed.** `checkpoint.md`'s Review boundary and Phase A/Phase B paragraphs now name Qhatahet as implementer and Abood-essa as the reserved independent Phase-B reviewer, with an exact link to the recovery decision and Abood-essa's confirmation. This ledger's "Internal commands and review receipts" section is corrected to match. Older "reserved independent human Qhatahet" rows in the PR120 R5/Qhatahet dispositions, Worker review finding dispositions, and Abood finding inheritance map sections are explicitly labeled historical record only, pointing to this section, rather than rewritten out of the historical trace. |
| I100-B-A3 (High) — open-ended native-hook table | **Fixed.** `checkpoint.md`'s "Permitted test seams" table now enumerates every permitted hook by exact call site (the one `NtSetInformationFile` rename call; each of the four `RmStartSession`/`RmRegisterResources`/`RmGetList`/`RmEndSession` entry points; the clock/sleeper wait; the quarantine barrier), states each hook's allowed override domain is failure-only and never synthesizes success/identity/path/sentinel/Git output/family-zero/RM ownership/stage/classification/postcondition, states each hook must be inert during every positive partition, names the consuming test group for each row, and states an unlisted hook fails review. |
| I100-B-A4 (High) — native ABI declaration not exact enough | **Fixed.** The Quarantine clause's `NtSetInformationFile` declaration now freezes `ExactSpelling=true`, `CallingConvention=Winapi`, `SetLastError=false` (with the reason `SetLastError=false` is exact, not an omission), a signed 32-bit `int fileInformationClass=10` matching the native C enum's default underlying type, frozen `IO_STATUS_BLOCK` x64 field offsets (`Status` at 0, `Information` at 8, size 16) and `SafeFileHandle` lifetime through postclassification, and an exact postclassification rule for nonzero `NTSTATUS`, `NTSTATUS==0` with contradictory `IO_STATUS_BLOCK`, `NTSTATUS==0` with a failed postcondition check, and any other partial/unrecognized observation — none of which may trigger a fallback rename or a second destructive action. |
| I100-B-A5 (Medium) — self-contradictory test budget | **Fixed.** The Test budget section now states "current soft target: ten `[Fact]` methods, current focused expectation: 86" explicitly, describes ten as a soft target rather than an immutable cap, keeps `[Theory]`/parameterized tests prohibited, and permits a concrete nonduplicate regression for a real finding or risk (including one surfaced by a B1/B2/B3 intermediate Reviewer-agent pass) through the existing amendment/count-recording process. |
| I100-B-A6 (Medium) — no disposition rule for intermediate reviews | **Fixed.** The B1/B2/B3 paragraph now classifies these passes as advisory worker containment checks, requires every finding to be recorded as `Fixed`, `Rejected` with evidence, or explicitly `Carried` into the complete-candidate review before the next phase starts, and requires a High-severity safety/authority finding to stop the next phase until disposed as `Fixed` or `Rejected` (never silently `Carried`). It also states these checks do not replace the worker's complete-candidate Reviewer pass, Abood-essa's authenticated human review, or the final gate. |
| I100-B-A7 (Medium) — future-build capability judgment left implicit | **Fixed.** The Accepted boundary paragraph now states explicitly: no version allowlist and no alternative-rename fallback; every admitted Group-8 positive partition must execute the real native call; a missing export, admission/declaration failure, nonzero `NTSTATUS`, contradictory `IO_STATUS_BLOCK`, or unproven postcondition is a fail-closed failure classified per the exact postclassification rule, never a skip; the focused-verification receipt records the exact measured Windows build; and an unsupported future build is a blocking non-pass for GH-107 promotion. |

### Outstanding item not resolved by this repair

The Ahmad coordinator-selection confirmation referenced in I100-B-A1's disposition above and in the "Recovery decision
and role change" subsection remains outstanding: AhmadKrarha has not yet posted a GitHub comment on Issue #107
explicitly selecting native `NtSetInformationFile` (Option 2) as the coordinator-approved replacement boundary,
distinct from his earlier delegation of the option-comparison/selection task and distinct from Abood-essa's
independent T2 review of this amendment. This repair records the gap accurately rather than fabricating or implying
that confirmation; per Abood-essa's own rereview checklist, it should be posted before requesting rereview of this
amendment.
