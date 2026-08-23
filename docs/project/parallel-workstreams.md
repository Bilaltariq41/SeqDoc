# Parallel Essential Workstreams

## Goal and operating model

Make SeqDoc fully useful for large real solutions as quickly as possible. CreditTransfer remains the primary visual
acceptance target; FraudManagement, SMSGateway, TicketReservation, and the training corpus protect generality. Project
names may appear publicly, but their source, configuration, and detailed behavioral findings remain external.

The original A–H plan is background architecture, not the execution target. Approximately 45–50% of its complete
scope and 60–65% of the current essential functional scope are implemented. Near-term work excludes CI, packaging,
release automation, broad platform matrices, and speculative performance infrastructure.

Each contributor owns one substantial parent workstream with several sub-issues and reviewable PRs. Parent issues may
run in parallel; dependencies are declared between sub-issues. Contributors work from forks. Canonical project docs
are updated by the maintainer after verified merges.

Public execution graph:

- depthless traversal: parent [#2](https://github.com/Bilaltariq41/SeqDoc/issues/2), sub-issues #19–#23;
- service contracts: parent [#1](https://github.com/Bilaltariq41/SeqDoc/issues/1), sub-issues #5–#8;
- persistence/state: parent [#3](https://github.com/Bilaltariq41/SeqDoc/issues/3), sub-issues #9–#13;
- worker/recovery: parent [#4](https://github.com/Bilaltariq41/SeqDoc/issues/4), sub-issues #14–#18.

Issues #14 and #15 merged through the original contributor PR after bounded maintainer integration. Issue #16 is the
next ready worker package boundary; #17 remains blocked by the persistence-state contract. Issues #5 and #9 remain
contributor-ready. GitHub sub-issue and `blocked by` relationships are authoritative for availability.

## Maintainer stream — Depthless traversal and large diagrams

**Owner:** maintainer

Remove the arbitrary call-depth stop without allowing cycles or renderer overflow.

1. Replace `MaxDepth=2` with cycle-safe traversal under deterministic method/call/message/participant budgets.
2. Make the large-diagram budget configurable. Keep rendered Mermaid below a conservative 45,000-character threshold
   because Mermaid's default `maxTextSize` is 50,000.
3. Compose locally guarded callee topology so deeper calls do not become unconditional.
4. Generate the largest safe single diagram first and report exact truncation/budget boundaries.
5. Regenerate CreditTransfer, FraudManagement, and SMSGateway and measure useful coverage.
6. After the single-diagram mode is proven, add optional linked overview/child decomposition for overflow.

This stream owns shared Scenario Graph/Diagram Plan traversal contracts and reviews cross-stream contract additions.

## Service-contract and outbound-boundary stream

**Workstream owner:** `@Qhatahet`

Deliver generic CoreWCF/WCF service, client, fault, and outbound-boundary interpretation.

Planned slices:

1. shared exact contract/operation/client compiler facts;
2. CoreWCF service root and dispatch admission;
3. generated WCF client and metadata/source boundary presentation;
4. exact request/response/fault and outbound HTTP/SOAP outcomes;
5. CreditTransfer and SMSGateway acceptance plus an unrelated negative fixture.

Final acceptance requires useful linked service and client diagrams without project-name matching.

## Persistence and state stream

**Workstream owner:** `@AhmadKrarha`

Deliver reusable persistence and state-transition behavior for EF Core and EF6/EDMX.

Planned slices:

1. shared query/mutation/save/state compiler contracts;
2. EF Core database-first and ordinary context behavior;
3. EF6/EDMX context and save behavior;
4. exact persisted assignments, status transitions, and caller-visible outcomes;
5. FraudManagement, CreditTransfer, SMSGateway, TicketReservation, and negative-fixture acceptance.

Do not infer database contents, transaction success, or stored-procedure internals.

## Worker, scheduler, and recovery stream

**Workstream owner:** `@Abood-essa`

Deliver generic worker lifecycle, scheduling, batch, retry, callback, and recovery presentation.

Planned slices:

1. hosted worker start/stop and executable-root admission — merged;
2. scheduler/timer registration and job invocation facts — merged;
3. polling, batch loops, retry, cancellation, and terminal boundaries;
4. recovery/state progression and callback/event boundaries;
5. FraudManagement, SMSGateway, CreditTransfer, and unrelated negative-fixture acceptance.

Runtime timing, concurrency order, eventual success, and configuration values remain unknown unless proven.

## Dependency graph

- External corpus resolution and contributor instructions precede all assigned work.
- Each stream's compiler/IR contract sub-issue precedes its framework/scenario projection sub-issues.
- Framework fact extraction may proceed while depthless traversal is built because target paths are separate.
- Whole-solution acceptance for each stream depends on the first depthless single-diagram milestone.
- Automatic decomposition depends on depthless traversal metrics and at least one accepted capability from every stream.
- Cross-root/state-machine overviews depend on persistence/state plus worker/recovery semantics.

## Integration and review

Every parent workstream has a public owner label and linked sub-issues. Each sub-issue specifies target paths, non-goals,
risks, focused verification, final gate, and supplied-app acceptance. PRs use `Closes #<sub-issue>`. The maintainer batches
findings; the contributor and their coding agent repair the same PR. After two unsuccessful repair rounds, the work is
split, rejected, or explicitly taken over. Only reviewed merges update parent progress and canonical docs.

### Approved contributor delivery packages

Sub-issues remain separate for dependencies and completion tracking, but the normal human/agent delivery unit is a
larger vertical PR:

- **Service package 1:** #5 exact facts + #7 CoreWCF roots/dispatch.
- **Service package 2:** #6 clients, outbound boundaries, faults, and responses.
- **Service package 3:** #8 supplied-project acceptance.
- **Persistence package 1:** #9 shared contracts + #10 EF Core behavior.
- **Persistence package 2:** #11 EF6/EDMX + #12 state transitions/outcomes.
- **Persistence package 3:** #13 supplied-project acceptance.
- **Worker package 1:** #14 worker lifecycle + #15 scheduler/job facts.
- **Worker package 2:** #16 polling/retry/cancellation + #17 recovery/callbacks.
- **Worker package 3:** #18 supplied-project acceptance.

The maintainer traversal issues #19, #22, #21, and #23 are already substantial and remain separate. Package grouping
does not remove GitHub dependencies: a PR can implement a contract and its approved first consumer together, but
merge order and external acceptance still follow the issue graph.

## Completion ladder

### Immediate

- shared external corpus resolver and reproducible usage;
- depthless high-budget single-diagram traversal;
- first compiler-fact slice from each contributor stream;
- regenerated acceptance metrics for all supplied projects.

### Medium

- guarded nested topology;
- complete first version of service, persistence, and worker streams;
- useful whole-solution suites and explicit coverage classification;
- configurable traversal/output budgets.

### Long

- optional automatic overview/child decomposition;
- cross-root and state-machine views;
- broader framework support driven only by measured supplied/open-source gaps.

### Very long, only when needed

- persisted later graph stages and incremental regeneration for repositories where full analysis is too costly;
- explanation/search surfaces when large suites make direct navigation insufficient;
- natural-language and visual refinement after semantic coverage is complete.
