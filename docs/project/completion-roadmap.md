# SeqDoc completion roadmap

This is the owner-approved canonical staged v1 planning map. The registry owns current lifecycle, selection, and
registered assignments; issue bodies specify work, and checkpoint capsules contract implementation. This roadmap retains
planning IDs, dependencies, and strategy; the registry owns registered ownership and current state.

## Definition of v1 complete

SeqDoc v1 is complete when a clean, reproducible Release build can analyze the declared supported matrix and produce
deterministic, linked Markdown/Mermaid documentation for admitted roots in the supplied corpus, with every claim tied
to compiler evidence and certainty, explicit diagnostics for unsupported or ambiguous shapes, stable profile/fingerprint
isolation, cancellation, safe previous-valid-state activation, documented budgets, and a reproducible release mechanism
chosen through the applicable T3 release decision.
The whole-solution CreditTransfer audit must classify every executable entry as complete, partial, diagnostic-only, or
uncovered without application-specific production rules. The supported matrix, compatibility envelope, security/privacy
policy, chosen distribution and install/update path, and release artifacts must be published with the candidate. Signing,
SBOM, and attestations are conditional on the release decision and threat model.

### Explicit non-goals

- all .NET versions, languages, frameworks, hosting models, or third-party conventions;
- runtime truth, invocation counts, timing, scheduling, remote outcomes, database contents, or successful commits;
- an IDE, web UI, hosted service, or interactive explorer requirement;
- application-, route-, type-, method-, or business-name matching rules;
- natural-language perfection, unrestricted call-graph completeness, or a guarantee that every source construct is proved.

## Baseline snapshot (2026-09-03)

| Item | Current fact |
|---|---|
| Baseline | `main` `08cb735945a178e93458069d6c42da833e044a74` on 2026-09-02 |
| Current state | Read the live selected item and lifecycle from `work-items/`; this dated table is not execution authority |
| Parallel | GH-17/P17-R1 Abood: ResolvingFindings; reviewed PR #64 candidate `11e433f`, now integrated PR head `2c1283e8`; GH-53/QHTTP-B Qais Ready; GH-18 worker acceptance blocked on GH-17; GH-57 project operations Ready and contributor-claimable |
| Parents | GH-3, GH-4, GH-52 active |
| Reusable foundations | deterministic Program Index/Method Flow; configurable roots; depthless cycle/budget traversal; compatible loaded-source traversal; decomposition/budgets; ASP.NET controllers/minimal APIs; narrow CoreWCF/WCF and EF Core/EF6/EDMX; MediatR 13; hosted workers/schedulers/recovery controls; HttpClient GET/POST; SQLite atomic activation and previous-valid-state preservation |
| Corpus | CreditTransfer, FraudManagement, SMSGateway, TicketReservation, TelecomSimulator, CustomerManagement, DotNet eShop |
| Acceptance frontier | Dated 2026-09-03 baseline: I13 active; QHTTP-B next available; GH-18 follows GH-17; whole-solution CreditTransfer audit and cross-root/state-machine views remain |
| Release gaps | governance CI only; fixture restore outside the solution; no baseline manifest, supported matrix, required-corpus CI, package/release/signing/SBOM, compatibility/performance envelope, SECURITY/privacy policy, install/update path; `IsPackable=false` |

## Operating constraints

This snapshot is a planning-time read of the canonical registry plus live PR metadata recorded on 2026-09-03; it is
not a replacement for either source. In particular, the PR #64 reviewed candidate SHA and the currently integrated PR
head are separate facts.

The typed pipeline is **Program Index → Method Flow → Scenario Graph → Diagram Plan**. Evidence and certainty are
monotonic; identities, profile/fingerprint snapshots, chronology, guards, deterministic ordering, cancellation, Core
dependency purity, and previous-valid-state behavior are release invariants. High-contention areas are Core
identity/evidence contracts, ScenarioGraphBuilder topology, DocumentationPlanner ordering/fragments,
RoslynBehaviorExtractor shared operations, FrameworkModelHost applicability/provenance, PassAWorkflow/CLI composition,
and global release gates. Parallel work may use compiler/control-flow, service/outbound, persistence/state,
worker/recovery, narrow DI, outcomes, rendering/decomposition, cache, CLI/config, and corpus/release seams only when
target paths do not overlap.

## Buffered release-train stages

The dependency firewall favors independent workstream trains with planning buffers between shared joins. Stages 2, 5,
7, and 9 are planning-only snapshot gates, not product modules or GitHub issues. The full visual is in
[completion-roadmap-visual.md](completion-roadmap-visual.md).

| Stage | Work and entry/exit condition |
|---|---|
| 0. Current frontier A | Independent I13/A-1, P17/A-2 → GH-18/A-4 internal worker edge, and QHTTP-B/A-3 on its frozen GH-53 contract/baseline. |
| 1. Independent audits/readiness | G-0/G-5, Q-1/Q-4, M-1, C-1, S-1, P-1 after I13 closes, W-1 after P17/GH-18 close, D-1, O-1, and T-1; no cross-lane edges. |
| 2. Foundation Snapshot Gate | Freeze accepted typed contracts, baseline, gap briefs, and path leases; no implementation. |
| 3. Independent vertical slices | G-1/G-2 after GH-57 self-service readiness; Q-2; C/S/P/W/D/O/T implementation slices; L-1, F-1, and R-0. Each consumes its own prior issue and Stage 2 snapshot. |
| 4. Independent lane acceptance | G-3..G-7 as internally permitted, Q-3, C-6/S-4/P-5/W-4/D-4/O-4/T-4; semantic lanes emit accepted evidence inputs. Each consumes its own lane gate, not Q-3. |
| 5. Accepted Lane Snapshot Gate | Validate receipts, identities, evidence/certainty, profiles/fingerprints, and determinism; missing lanes stay explicit. |
| 6. Integration | X-1..X-5 owns the first cross-stream Scenario Graph joins; M-2..M-4 builds the support matrix from Stage 5 evidence. |
| 7. Composition Snapshot Gate | Freeze accepted M/X composition evidence, links, budgets, conservative boundaries, and R-0 provisional target constraints; no implementation. |
| 8. Independent consumers | U-1..U-4, L-2..L-4, and F-2..F-4 consume Stage 7 interfaces without calling semantic producers. K is outside v1 in the post-v1 parking lot. |
| 9. Release Input Snapshot Gate | Freeze finalized support/consumer outputs, release inputs, receipts, and R-0 constraints; no implementation. |
| 10. Release | R-1..R-5 consumes Stage 9 and the R-0 decision. |

### Critical path and integration points

The staged critical path is **frontier → independent audits → Foundation Snapshot Gate → vertical slices → lane
acceptance → Accepted Lane Snapshot Gate → X/M integration → Composition Snapshot Gate → independent consumers → Release
Input Snapshot Gate → release**. Core identity, ScenarioGraphBuilder, DocumentationPlanner, shared extractor/model host,
and CLI composition changes use one exact path lease at a time. Sibling composition begins only in Stage 6.

### Ownership and collaboration plan

The current frontier remains Ahmad on I13/A-1, Abood on P17/A-2 then GH-18/A-4, and Qais on QHTTP-B/A-3. The only
firm future assignments are Ahmad on P-1..P-5, Qais on S-1..S-4, and Abood on C-1..C-6. Everything else is
unassigned. A free qualified collaborator may claim a Ready item only when its dependencies, readiness self-review, and path
lease permit it; suggested pickups are not reservations and may change.

Bilaltariq41 is unavailable for routine coordination or implementation. He remains required only for provider-restricted
T4 actions: repository access, settings, rulesets, secrets, and bypass. T3 cross-stream, public, and release decisions
are peer decisions. Any qualified collaborator may coordinate integration or merge when independence and receipt rules
permit it.

Parallel delivery uses one exact lease per path set and preserves author/reviewer independence. T0 permits work inside a
frozen Ready contract; T1 records reversible risk; T2/T3 use one latest-head non-author human peer approval. T4 remains
owner-only. I13/P17/QHTTP-B retain their frozen policy through closure.

Immediate governance work is G-0/G-5: this is critical unassigned work that any qualified collaborator may claim when its
dependencies, readiness self-review, and path lease permit it. Verify rollout/readiness of the published model; invite collaborators and configure the
manual receipt pilot, and run the disposable smoke test. G-6 migrates grandfathered records and G-7 exercises negative
cases. G-1 is specifically GH-57's transactional multi-execution work, Ready at its recorded baseline; its claimant
creates and freezes the capsule during self-service readiness. G-2/G-3/G-4/G-6/G-7 are not automation-executable before
G-1, while manual G-5 is separate. G, Q,
T, X, U, L, F, M, and R remain unassigned flexible work, subject to the dependency firewall.

## Milestone gates

1. **Frontier gate:** exact revisions/configs, observable artifacts, and dispositioned findings for all active work.
2. **Governance gate:** candidate SHA, review receipt, lease/path-conflict checks, locked build, and required-corpus receipt.
3. **Semantic gate:** producer, propagation, observable, and boundary proofs for each supported matrix row.
4. **Composition gate:** complete CreditTransfer classification, linked views, budgets, links, Mermaid, and byte equality.
5. **Release gate:** Release build/tests, supported matrix, performance/security/reliability evidence, the R-0-selected
   distribution mechanism and install/update rehearsal, applicable signing/SBOM/attestation, docs, rollback, and receipt.

## Committed requirements versus later horizons

Committed v1 requirements are the supported measured framework matrix, including its truthful published support list, the current typed invariants, useful linked
whole-solution technical output, conservative diagnostics, deterministic/cancellable bounded analysis, safe persistence,
CLI reproducibility, security/privacy and compatibility documentation, and a reproducible release mechanism selected by
R-0. Release infrastructure is intentionally deprioritized until diagrams are useful, but a product release decision is
eventually required.

The following are evidence-triggered horizons, not promises: ongoing framework additions or upkeep, broader frameworks or language constructs, persisted later
graph stages, incremental invalidation, search/explanation surfaces, richer state-machine inference, and natural-language
polish beyond understandable evidence-backed wording. Each requires a decision ticket with corpus measurements,
cost/benefit, identity contract, negative boundary, and the applicable peer/cross-stream decision tier.

### K — post-v1 parking lot

SeqDoc already has SQLite persistence in `src/SeqDoc.Persistence.Sqlite`, and the CLI already uses it. K means persisted
later graph stages and incremental invalidation so unchanged analysis can be reused; it does not mean adding a cache. It
would likely involve `SeqDoc.Persistence.Sqlite` and `SeqDoc.Application`, with stable identity contracts kept
dependency-safe and exact paths deferred until measurement/readiness.

K-1 is a measurement and decision ticket only. K-2..K-4 must not begin without repeatable evidence that full analysis
is too slow and a readiness decision covering stale evidence, profile/configuration isolation, corruption, and safe
activation. Existing SQLite storage, atomic activation, and previous-valid-state behavior remain required. K is not
currently justified and is not required for v1. All K work is unassigned; Ahmad is only a suggested next person and that
suggestion may change. Ongoing framework additions and maintenance are likewise unassigned post-v1 work.
