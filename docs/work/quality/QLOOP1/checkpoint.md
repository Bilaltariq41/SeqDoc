# QLOOP1 compiler-backed loop fixture repair

## State

`Building`

## Authority and baseline

Owner-directed test-only repair for GitHub Issue #85:
https://github.com/Bilaltariq41/SeqDoc/issues/85

Baseline: `e6dfc038f304f8265f6228052ebb326d882c5157`.

The baseline Behavior gate reports 67 passed, 4 failed, 0 skipped. The exact red cases are:

- `MethodFlowBuilderTests.BackEdgeProducesNaturalLoopRegion`
- `MethodFlowBuilderTests.ConditionalBackEdgeProducesDoWhileNaturalLoop`
- `MutationGateTests.LoopBackEdgeMutationChangesLoopPresence`
- `ControlDependenceCompletenessTests.EntryExitLoopAndUnknownNodesStayExcluded`

I13 remains active and owned by Ahmad. Selecting QLOOP1 changes only the root Orchestrator execution lane; it does not
change I13 ownership, contract, branch, lifecycle, or allowlist.

## Objective

Repair the four stale hand-built Behavior fixtures so they provide the compiler-backed natural-loop descriptors,
anchors, admitted ordinary back-edge branches, evidence, certainty, and loop kind now required by
`MethodFlowBuilder`. Preserve production fail-closed behavior and every existing test intent.

## Target paths

Implementation and tests:

- `tests/SeqDoc.Behavior.Tests/MethodFlowBuilderTests.cs`
- `tests/SeqDoc.Behavior.Tests/MutationGateTests.cs`
- `tests/SeqDoc.Behavior.Tests/ControlDependenceCompletenessTests.cs`

Checkpoint and canonical execution evidence:

- `docs/work/quality/QLOOP1/**`
- `docs/project/work-items/GH-85.json`
- `docs/project/execution.json`

## Non-goals

- Any edit under `src/**`.
- Core contract, Roslyn extractor, Method Flow, renderer, CLI, configuration, fixture-project, or build changes.
- Heuristic loop inference or weakened loop validation.
- New semantic behavior, unrelated test cleanup, or broad helper refactoring.
- Any change to I13 acceptance semantics, branch, ownership, lifecycle, or allowlist.

## Risk inventory

1. A hand-built descriptor could pass validation despite disagreeing with the fixture blocks.
2. While and do-while fixtures could use the wrong header, latch, anchor, or loop kind.
3. Both sides of the mutation pair could receive equivalent compiler topology and stop killing the mutant.
4. Empty, duplicate, or mismatched evidence could bypass the intended compiler-backed admission boundary.
5. A shared helper could silently alter unrelated Behavior cases.

## Existing relevant coverage

- `NaturalLoopProjectionTests` exercises the production Roslyn producer, exact loop kinds, anchors, ordinary branches,
  deterministic ordering/fingerprints, and malformed boundaries.
- `BehaviorAnalyzerTests` proves malformed natural-loop withholding and invalid-anchor failure.
- The four red cases are the downstream Method Flow, mutation-sensitivity, and control-dependence regression signature.

## Test Writer assignment and soft budget

A Test Writer is required because this is a concrete compiler/IR regression signature involving deterministic topology,
evidence admission, and false-positive risk. Repair the four existing cases with the smallest local fixture helpers.
Do not add production behavior or duplicate producer coverage. Add no new test unless a distinct uncovered risk cannot be
proved by the repaired assertions. Soft budget: four repaired claims and at most one focused helper per affected file.

Focused command:

```powershell
dotnet test tests/SeqDoc.Behavior.Tests/SeqDoc.Behavior.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~MethodFlowBuilderTests.BackEdgeProducesNaturalLoopRegion|FullyQualifiedName~MethodFlowBuilderTests.ConditionalBackEdgeProducesDoWhileNaturalLoop|FullyQualifiedName~MutationGateTests.LoopBackEdgeMutationChangesLoopPresence|FullyQualifiedName~ControlDependenceCompletenessTests.EntryExitLoopAndUnknownNodesStayExcluded"
```

Expected focused discovery is exactly four tests.

## Review boundary

Implementation stops at `ReviewRequired`. The Orchestrator inspects the complete diff and invokes one independent
complete-candidate review. Every finding must be Fixed, Rejected with evidence, or Deferred with explicit owner approval.
The final gate runs only after all findings are resolved. After two failed repair rounds, preserve the worktree, mark the
checkpoint `Blocked`, and stop.

## Final gate

```powershell
dotnet test tests/SeqDoc.Behavior.Tests/SeqDoc.Behavior.Tests.csproj -c Release --no-build --no-restore
```

Expected discovery is 71 tests with zero failures and zero skips. Also run `git diff --check` and verify that every
changed path is in the checkpoint allowlist.
