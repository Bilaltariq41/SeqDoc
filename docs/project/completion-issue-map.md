# Completion issue map

This is the canonical planning map for the staged completion roadmap. The identifiers below are local planning IDs
until a real GitHub issue and work-item record exist. The registry owns current lifecycle, selection, and registered
assignments; issue bodies own specifications; checkpoints own implementation contracts. This owner-approved planning
map retains planning IDs, dependencies, and cross-references; the registry owns registered ownership and current state.
Suggested pickups are not reservations. Do not treat unregistered future assignments as registry state.

The completion-program workstream is [GH-69](https://github.com/Bilaltariq41/SeqDoc/issues/69). Its implementation issues
[GH-70](https://github.com/Bilaltariq41/SeqDoc/issues/70) through [GH-81](https://github.com/Bilaltariq41/SeqDoc/issues/81)
are independently claimable when their own readiness and dependencies pass; an implementation issue cannot create child issues.

Every future contract must state outcome, producer/identity, first observable, exact allowlist, non-goals, risks and
negatives, existing coverage, test budget, focused command, final gate, dependencies, and review receipt. Semantic work
also needs the closed-world brief. Acceptance work pins revision/profile/config/root and repeat-byte evidence.

## Stages and current frontier

Stage 0 is the independent frontier: A-1 I13, A-2 P17-R1 -> A-4 GH-18 (one worker-lane edge), and A-3 QHTTP-B.
Stage 1 runs independent audits/readiness. Stage 2 freezes the Foundation Snapshot. Stage 3 builds independent slices.
Stage 4 accepts each lane. Stage 5 freezes accepted lane evidence. Stage 6 is the first X/M integration. Stage 7
freezes composition. Stage 8 delivers independent consumers. Stage 9 freezes release inputs. Stage 10 releases.
Stages 2, 5, 7, and 9 are planning gates, not product modules or issues. Direct sibling-in-progress dependencies are
forbidden; shared paths use one lease; missing capability is diagnosed, withheld, or deferred.

| ID | Deliverable and first observable | Dependencies | Owner/status and boundary |
|---|---|---|---|
| A-1 | I13 four-lane persistence artifacts and diagnostics | none | Ahmad; acceptance-only, exact revisions and repeat-byte gate |
| A-2 | P17-R1 callback repair through wording/diagram | GH-17 baseline | Abood; worker paths |
| A-3 | QHTTP-B FraudManagement GET/POST acceptance | GH-53 frozen contract/baseline | Qais; acceptance-only |
| A-4 | GH-18 worker acceptance/frontier ledger | A-2 | Abood; no duplicated implementation |
| G-0 ([GH-70](https://github.com/Bilaltariq41/SeqDoc/issues/70)) | governance rollout and readiness verification | Stage 1 | unassigned; verifies publication adoption without rewriting history |
| G-1 | GH-57 transactional multi-execution lifecycle/lease model | Stage 2 | unassigned; Ready at its recorded baseline; claimant creates/freezes its capsule during self-service readiness |
| G-2 | one latest-head peer receipt pinned to candidate SHA | G-1; Stage 2 | unassigned; governance |
| G-3 | one-peer receipt validator/tests | G-2 | unassigned; CI seam |
| G-4 | ruleset/emergency bypass plan | G-3 | unassigned; owner required for T4 settings |
| G-5 ([GH-71](https://github.com/Bilaltariq41/SeqDoc/issues/71)) | collaborator access and manual receipt pilot | Stage 1 | unassigned; disposable fork, read-only checks |
| G-6 | grandfathered-record migration without fabricated receipts | G-1 | unassigned; preserve legacy evidence |
| G-7 | negative rehearsals for tiers, bypass, owners, leases, forks, containment, repair | G-4/G-6 | unassigned; fail closed |
| Q-1 ([GH-72](https://github.com/Bilaltariq41/SeqDoc/issues/72)) | clean baseline manifest and supported test matrix | Stage 1 | unassigned; reproducible identity |
| Q-2 | restore/build fixtures outside solution | Q-1; Stage 2 | unassigned; infrastructure only |
| Q-3 | required corpus CI and artifact validator | Q-2 | unassigned; critical |
| Q-4 ([GH-73](https://github.com/Bilaltariq41/SeqDoc/issues/73)) | compatibility/resource measurement protocol | Q-1 | unassigned; measure before optimization |

## Capability lanes

| ID | Deliverable | Dependencies | Owner/status and acceptance boundary |
|---|---|---|---|
| C-1 ([GH-75](https://github.com/Bilaltariq41/SeqDoc/issues/75)) | measured control-flow gap inventory/readiness | Stage 1 | Abood; assigned, no producer recreation |
| C-2 | predicate/branch projection | C-1; Stage 2 | Abood; guarded diagram and negative |
| C-3 | catch/filter/finally topology | C-2 | Abood; exception placement |
| C-4 | switch/loop/continue boundaries | C-3 | Abood; bounded diagnostic |
| C-5 | control-flow evidence/certainty propagation | C-4; Stage 2 | Abood; weakest certainty |
| C-6 | control-flow corpus acceptance | C-5 | Abood; deterministic negative |
| S-1 ([GH-76](https://github.com/Bilaltariq41/SeqDoc/issues/76)) | service/outbound gap inventory/readiness | Stage 1 | Qais; assigned |
| S-2 | named CoreWCF/WCF gap slice | S-1; Stage 2 | Qais; exact admission/callback |
| S-3 | client/fault/source-boundary presentation | S-2 | Qais; no invented outcomes |
| S-4 | service/outbound corpus acceptance | S-3 | Qais; assigned, repeat-byte gate |
| P-1 ([GH-77](https://github.com/Bilaltariq41/SeqDoc/issues/77)) | post-I13 persistence/state gap inventory | Stage 1 after GH-13 | Ahmad; assigned |
| P-2 | named persistence expansion | P-1; Stage 2 | Ahmad; exact producer-to-doc proof |
| P-3 | EF6/EDMX residual expansion | P-2 | Ahmad; declaration never execution |
| P-4 | assignments/transitions/caller-visible results | P-3 | Ahmad; chronology/guard isolation |
| P-5 | persistence/state corpus classification | P-4 | Ahmad; four-application matrix |
| W-1 ([GH-78](https://github.com/Bilaltariq41/SeqDoc/issues/78)) | post-P17/GH-18 worker gap inventory | Stage 1 after closure | unassigned, suggested Abood; may change |
| W-2 | scheduler/poll/retry/batch gap | W-1; Stage 2 | unassigned, suggested Abood; may change |
| W-3 | recovery progression/callback boundaries | W-2 | unassigned, suggested Abood; may change |
| W-4 | worker corpus acceptance | W-3 | unassigned; prior evidence read-only |
| D-1 ([GH-79](https://github.com/Bilaltariq41/SeqDoc/issues/79)) | Microsoft DI gap inventory/readiness | Stage 1 | unassigned, suggested Qais; may change |
| D-2 | lifetime/factory/constructor gap | D-1; Stage 2 | unassigned; no runtime inference |
| D-3 | DI fact-to-consumer observable | D-2 | unassigned; exact evidence |
| D-4 | DI corpus acceptance/diagnostics | D-3 | unassigned; ambiguity diagnostic |
| O-1 ([GH-80](https://github.com/Bilaltariq41/SeqDoc/issues/80)) | outcome/error/result gap inventory | Stage 1 | unassigned, suggested Abood; may change; serial shared IR |
| O-2 | generic exception/error slice | O-1; Stage 2 | unassigned; placement retained |
| O-3 | generic result/outcome slice | O-2 | unassigned; no runtime success claim |
| O-4 | outcome acceptance/forbidden-claim scan | O-3 | unassigned; conservative artifacts |
| T-1 ([GH-81](https://github.com/Bilaltariq41/SeqDoc/issues/81)) | traversal/coverage gap inventory | Stage 1 | unassigned; measured boundary |
| T-2 | root/callee coverage accounting | T-1; Stage 2 | unassigned; no invented caller |
| T-3 | cycle/budget/unavailable/cross-project boundaries | T-2 | unassigned; deterministic limits |
| T-4 | traversal corpus regression baseline | T-3 | unassigned; useful coverage only |

## Integration and consumers

| ID | Deliverable | Dependencies | Owner/status |
|---|---|---|---|
| X-1 | root identity and participant hygiene | Stage 5 snapshot | critical, unassigned, integration/serial |
| X-2 | linked context/adapter/facade/orchestration views | X-1 | unassigned |
| X-3 | state/recovery and cross-root transitions | X-2 | unassigned; suggested Ahmad/Abood may change |
| X-4 | whole-solution CreditTransfer audit | X-3 | unassigned; complete/partial/diagnostic/uncovered |
| X-5 | broad linked-view acceptance | X-4 | unassigned; four primary plus corpus |
| U-1 | signal/participant/argument presentation | Stage 7 | unassigned |
| U-2 | technical predicate/outcome/limitation wording | U-1 | unassigned |
| U-3 | linked Markdown/accessibility conventions | U-2 | unassigned |
| U-4 | rendering regression/corpus inspection | U-3 | unassigned |
| L-1 | config schema/provenance display | Stage 2 | unassigned |
| L-2 | diagnostics/order/exit behavior | L-1; Stage 7 | unassigned, serial |
| L-3 | machine-readable output compatibility | L-2 | unassigned, serial |
| L-4 | CLI corpus/release acceptance | L-3 | unassigned |
| M-1 ([GH-74](https://github.com/Bilaltariq41/SeqDoc/issues/74)) | supported framework inventory | Stage 1 | unassigned |
| M-2 | unsupported/version/profile boundaries | M-1; Stage 5 | unassigned, serial integration |
| M-3 | corpus-to-matrix evidence register | M-2 | unassigned |
| M-4 | published support matrix | M-3 | critical, unassigned |

## Post-v1, performance, and release

| ID | Deliverable | Dependencies | Owner/status |
|---|---|---|---|
| K-1 | measure need for incremental reuse | evidence trigger | unassigned, suggested Ahmad; decision only |
| K-2 | persisted later-stage identity/invalidation | K-1 | unassigned; paths deferred |
| K-3 | incremental analysis/safe activation | K-2 | unassigned |
| K-4 | cold/warm equivalence/corruption acceptance | K-3 | unassigned |
| F-1 | representative time/memory/output measurement | Stage 2 | unassigned |
| F-2 | cancellation and bounded cleanup | F-1; Stage 7 | unassigned |
| F-3 | deterministic scaling at budgets | F-2 | unassigned |
| F-4 | platform/resource compatibility envelope | F-3 | unassigned |
| R-0 | provisional distribution/signing/update decision | Q-4/M-1 after Stage 2 | unassigned; T3 decision |
| R-1 | package/version/install/update path | Stage 9; R-0 | unassigned |
| R-2 | conditional signing/SBOM/attestation/manifest | R-1 | unassigned |
| R-3 | security/privacy and secret/path hygiene | R-2 | unassigned |
| R-4 | user docs and support matrix | R-3 | unassigned |
| R-5 | release candidate and rollback gate | R-4 | critical, unassigned, serial |

K is outside v1 and cannot start without repeatable evidence that full analysis is too slow plus readiness covering
stale evidence, isolation, corruption, and safe activation. G-1/G-2 follow GH-57's readiness and dependencies;
G-3/G-4/G-6/G-7 follow G-1. P-1 waits for GH-13 closure; W-1 waits for P17-R1 and GH-18 closure. X/M begin sibling integration only in
Stage 6. No suggested pickup is an assignment.

## Creation, readiness, and retirement

Create a real issue only after the local plan identifies a measured gap, owner/status, dependency edges, exact allowlist,
non-goals, risks/negative, producer and first observable, test budget, focused command, final gate, and reviewer plan.
Run the readiness audit before assignment; freeze the baseline and contract at assignment. A parent closes only when
committed children close or are explicitly retired, receipts and final-gate evidence exist, and remaining boundaries link
to a decision ticket. Retire work only when superseded, unsupported by evidence, or merged elsewhere; record the reason
and preserve historical receipts. Never retire a conservative negative because it reduces coverage.

See [completion-roadmap.md](completion-roadmap.md), [collaboration-model.md](collaboration-model.md), and the
[work-item registry](work-items/) for canonical strategy, governance, and current lifecycle respectively.
