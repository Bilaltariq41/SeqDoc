# Issue 19 — Cycle-Safe Depthless Expansion

## Purpose

Remove the arbitrary configured-root direct-call depth stop and generate the largest deterministic evidence-backed
single diagram permitted by the finite Issue #20 work and output budgets. Exact call-site occurrences remain visible;
active-path cycles and exhausted budgets stop expansion explicitly without inventing behavior.

## Target paths

- `src/SeqDoc.Analysis.Scenarios/ScenarioGraphBuilder.cs`
- `src/SeqDoc.Core/ScenarioGraph/ScenarioGraphContracts.cs`
- `src/SeqDoc.Application/Documentation/DocumentationPlanner.cs`
- `src/SeqDoc.Rendering.Markdown/DocumentationSetBuilder.cs`
- `src/SeqDoc.Cli/AggregateAnalysisBuilder.cs`
- `src/SeqDoc.Cli/CliHost.cs`
- `tests/SeqDoc.Scenarios.Tests/DirectExactTraversalFixture.cs`
- `tests/SeqDoc.Scenarios.Tests/DirectExactTraversalTests.cs`
- focused `SeqDoc.Wording.Tests`, `SeqDoc.Rendering.Tests`, and `SeqDoc.Cli.Tests` budget tests when required at their
  owning boundary
- `docs/work/traversal/I19/**`

Changing another path requires Orchestrator approval and evidence that the accepted boundary cannot be implemented in
the listed owner.

## Accepted design

1. Pass the immutable `DiagramBudget` from resolved CLI configuration into Scenario analysis and documentation
   generation without introducing Configuration or CLI types into Core/Application contracts.
2. Remove `DirectCallMaxDepth` and `SC-DIRECT-DEPTH`. Traverse eligible `DirectExact` loaded-source calls in canonical
   depth-first source chronology until an active-path cycle or finite budget boundary.
3. Cycle detection is active-path based, not a global visited set. Emit the exact recursive call-site step once as an
   incomplete cycle boundary and never re-enter its target body. Repeated and diamond-shaped shared callees remain
   distinct call-site occurrences and may expand again on another non-cyclic path.
4. Count distinct method bodies admitted for expansion separately from projected call-site occurrences. The configured
   root is the first expanded method. A shared method identity consumes the distinct-method budget once, while every
   projected occurrence consumes the call budget. Budget checks happen before admitting work that would exceed a limit.
5. Preserve the already accepted deterministic DFS prefix and all existing identities when limits increase. Budget
   values never participate in entry, graph, step, node, edge, participant, message, fragment, or diagnostic identity.
6. Exhaustion emits one canonical evidence-backed diagnostic per omitted exact boundary site. Diagnostics identify the
   exhausted dimension and observed limit; no generic node/depth wording remains.
7. Apply material-message and participant limits to the deterministic diagram chronology while preserving a valid
   prefix, complete sequence-reference coverage, fragment validity, evidence/certainty, and an explicit Diagram Plan
   truncation diagnostic. Never retain a message whose participant was withheld.
8. Rendered Mermaid source for every accepted document must not exceed `MaxMermaidCharacters`. Character limiting must
   produce a valid deterministic plan/output prefix with an explicit truncation diagnostic; it must not silently fail,
   cut serialized text, or move semantic inference into a renderer.
9. Defaults remain finite and large. Existing shallow outputs and identities remain byte-stable when no budget is
   reached. Reversed input collections produce identical Scenario, Diagram Plan, and rendered Mermaid output.
10. Cancellation and previous-valid-state behavior remain unchanged; budget exhaustion is successful conservative
    documentation, not analysis failure.

## Non-goals

- No locally guarded callee composition (#22), CreditTransfer/FraudManagement/SMSGateway acceptance (#21), automatic
  decomposition (#23), new framework semantics, persistence schema changes, output activation changes, project-specific
  limits, runtime call-graph inference, global callee deduplication, or fragment-depth policy change.

## Risk inventory

1. Direct or mutual recursion becomes unbounded, or a global visited set incorrectly suppresses a later shared callee.
2. Distinct-method and call-site counters have off-by-one behavior or count rejected/withheld work.
3. A budget check admits a partial child after the prefix, produces duplicate diagnostics, or loses boundary evidence.
4. Input enumeration order changes the accepted prefix, diagnostics, identities, or rendered output.
5. Existing depth-1/depth-2 identities change merely because the fixed depth stop was removed.
6. Message/participant trimming leaves dangling references, empty invalid fragments, orphan participants, or missing
   evidence/certainty.
7. Mermaid character enforcement cuts text, exceeds the configured bound, or turns a conservative truncation into
   documentation-generation failure.
8. Configuration is visible in CLI output but not actually used by analysis/planning, or profile/root runs leak budget
   state into one another.
9. A synthetic deep chain overflows the process stack before a finite budget can stop it.
10. Existing unsupported body/source/flow, duplicate-anchor, local-guard, profile, and credential boundaries regress.

## Existing coverage and soft budget

`DirectExactTraversalTests` already pins DFS chronology, reversed-input determinism, duplicate anchors, the old
depth/node boundaries, direct and mutual cycles, repeated shared callees, evidence/certainty, source/body/flow stops,
profile isolation, guarded calls, safe arguments, and plan projection. Rendering tests pin exact sequence-reference and
fragment validity; CLI/configuration tests pin budget resolution and provenance. Replace obsolete depth/node claims and
add approximately 10–15 distinct risk claims: deep chain, method limit, call limit, shared-callee accounting, exact
diagnostics, output limits, Mermaid bound, and one end-to-end configuration-consumption proof. Do not duplicate existing
unsupported-boundary or YAML-validation assertions.

## Focused command

```powershell
dotnet build tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release && dotnet build tests/SeqDoc.Rendering.Tests/SeqDoc.Rendering.Tests.csproj -c Release && dotnet build tests/SeqDoc.Cli.Tests/SeqDoc.Cli.Tests.csproj -c Release && dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~DirectExactTraversal" && dotnet test tests/SeqDoc.Rendering.Tests/SeqDoc.Rendering.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~Budget|FullyQualifiedName~DiagramPlanRendering" && dotnet test tests/SeqDoc.Cli.Tests/SeqDoc.Cli.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~DiagramBudget"
```

## Final gate

```powershell
dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release --no-build --no-restore && dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release --no-build --no-restore && dotnet test tests/SeqDoc.Rendering.Tests/SeqDoc.Rendering.Tests.csproj -c Release --no-build --no-restore && dotnet test tests/SeqDoc.Cli.Tests/SeqDoc.Cli.Tests.csproj -c Release --no-build --no-restore
```
