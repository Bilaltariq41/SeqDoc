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
- Local accepted #106 public API boundary and cleanup pattern findings: `I100-A/checkpoint.md` and `I100-A/ledger.md`,
  including public `ContainedProcess` observations, active-zero proof, teardown evidence, and ordered secondary evidence.
- Reusable QHTTP/GH93 patterns were inspected as read-only risk input; neither contract supplies a sentinel, quarantine,
  or complete Restart Manager cleanup contract.
- Readiness and authority: Issue #107, owner authorization
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

## Worker review finding dispositions

| Finding | Disposition and governing evidence |
|---|---|
| I100-B-F1 | **Fixed.** The csproj remains outside the initial allowlist and claims. If concrete compiler/build evidence proves it necessary, stop before edit; amend GH-107/I100-B target paths, claims, risks, and tests under T2, run worker readiness review, and obtain latest-head non-author peer approval on the amended SHA before resuming. Unamended target expansion remains a stop condition, not an automatic permanent block or undocumented bypass. |
| I100-B-F2 | **Rejected.** A human peer is not required to invoke the Reviewer agent. `AGENTS.md` lines 113–117 require the worker to fix, run the Reviewer agent again, rerun focused verification, then request one human approval; `docs/project/collaboration-model.md` lines 57–59 require worker readiness/spec and complete-candidate self-review, the worker's Reviewer agent, and one human latest-head review. Phase B therefore has the worker/Orchestrator invoke the independent Reviewer agent against the complete latest candidate and record invocation/output digest; after green/dispositions, the reserved latest-head non-author human reviews and gives authenticated GitHub approval for that same SHA. The human may independently run tools but need not invoke the agent. |

## Advisory worker review receipt

Role: `review`; task: `ses_f373cbfaaffeEh4jo3YbUfJl31`; result: `REQUEST CHANGES`; findings: I100-B-F1 and I100-B-F2. This is advisory worker-review evidence, not formal human completion. Reviewer version and output digest are unknown and intentionally not fabricated; the reproducible output digest will be posted on the replacement PR after final rereview.

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
| F2 | Quarantine destination authority ([comment](https://github.com/Bilaltariq41/SeqDoc/issues/118#issuecomment-5774337587)) | Same-volume direct sibling, collision refusal, sentinel retention, and same-owner eventual cleanup. |
| F3 | Live executable/synchronization ([comment](https://github.com/Bilaltariq41/SeqDoc/issues/118#issuecomment-5774337587)) | Rooted marker stub, marker PID proof, independent FileStream lock, deterministic observer/barrier, no sleeps or termination. |
| F4 | Exact paths/receipts/review route ([comment](https://github.com/Bilaltariq41/SeqDoc/issues/118#issuecomment-5774337587)) | Two implementation paths, excluded csproj, B3 focused/affected route, and Qhatahet Phase B review. |
| F5 | Isolation inheritance ([comment](https://github.com/Bilaltariq41/SeqDoc/issues/118#issuecomment-5774337587)) | Four unrelated vectors and common-dir FILE_ID concurrency inherit B1/B2 authority. |

## Internal commands and review receipts

B1 focused `dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter FullyQualifiedName~FixtureCleanupAuthorityTests` is exactly 3/0/0; affected `dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter "FullyQualifiedName~FixtureCleanupAuthorityTests|FullyQualifiedName~ProcessOwnershipTests"` is exactly 79/0/0. B2 focused `dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter FullyQualifiedName~FixtureCleanupProcessTests` is exactly 3/0/0; affected `dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter "FullyQualifiedName~FixtureCleanupAuthorityTests|FullyQualifiedName~FixtureCleanupProcessTests|FullyQualifiedName~ProcessOwnershipTests"` is exactly 82/0/0. B3 focused `dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter FullyQualifiedName~FixtureCleanupIntegrationTests` is exactly 4/0/0; complete affected `dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter "FullyQualifiedName~FixtureCleanupAuthorityTests|FullyQualifiedName~FixtureCleanupProcessTests|FullyQualifiedName~FixtureCleanupIntegrationTests|FullyQualifiedName~ProcessOwnershipTests"` is exactly 86/0/0 before ReviewRequired. These are internal verification, not final gates. The sole final gate is `dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release`, once after human review and resolved findings.

Phase A requires both Qhatahet and Abood-essa to review the same immutable replacement planning SHA and post
authenticated T2 receipts; these approve only same-issue sequencing/spec/allowlists. Phase B requires the
worker/Orchestrator to invoke an independent Reviewer agent against the complete latest candidate and record its
invocation/output digest, then implementer self-review, Fixed/Rejected dispositions, green internal verification,
ReviewRequired, and one authenticated latest-head non-author human GitHub approval for the same implementation SHA
reserved to Qhatahet. A human may independently run tools but need not invoke the agent. Prior agent summaries are
advisory and not formal independent-review completion. The new reproducible receipt will be posted on the replacement
PR after final rereview; until then the summary above is explicitly unauthenticated. No Ready now.

## Technical inheritance

The checkpoint's technical strengthening is authoritative: rooted GitExecutablePath and Program Files admission; exact
sentinel/stable-vs-local receipt and source/common FILE_ID chains; exact Git vectors without `--` fallback; four
unrelated vectors; outer deadline, retry starts/delays/2-second budget and revalidation; exact RM ABI/state machine;
quarantine sibling/collision/owner rules; `sleep-with-marker <owned-marker-path> 30000`, marker PID equal to public
ProcessId, independent test-host FileStream `FileShare.None`, deterministic 32/33+RM observer barrier, no sleeps or
testhost termination; and common-dir FILE_ID concurrency. Exact Git vectors are `worktree add --detach
<owned-absolute-path> <40-lowercase-revision>`, `rev-parse --git-common-dir`, `rev-parse --absolute-git-dir`,
`status --porcelain=v1 -z --untracked-files=all`, `for-each-ref --format=%(refname)%00%(objectname)%00%(symref)%00
--sort=refname`, `config --local --null --list`, `worktree list --porcelain`, and `worktree remove --force
<owned-absolute-path>`.
