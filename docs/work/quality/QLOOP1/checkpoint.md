# QLOOP1 compiler-backed loop fixture repair

## State

`Verifying`

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
- `docs/project/work-items/GH-13.json` only for `selectedForExecution: true` to `false` while QLOOP1 is selected; restore
  I13 selection when QLOOP1 closes. No other GH-13 field may change.
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

## Repair trace

### Pre-review implementation repair

- Attempt 1 produced a changed candidate with three focused passes and one failure. The remaining
  `BackEdgeProducesNaturalLoopRegion` case reported `BD2011`. Root cause: the latch predecessor was accidentally added
  to an unrelated earlier conditional fixture instead of the while-loop header. The candidate also contained avoidable
  invocation-payload and formatting churn. This is one failed repair round.
- Attempt 2 reverted the unrelated predecessor change, added predecessor `2` to the actual while-loop header, removed
  the unnecessary invocation payload and formatting churn, and preserved the compiler-backed evidence and topology in
  all four fixtures. The exact focused command then discovered four tests and passed 4/4 with zero failures and skips.
- Applicable failed-round count before independent review: one. The successful second attempt did not trigger the
  two-failed-round blocking boundary.

### Independent review of `d6fb5c80212704fbd357f0b3a2004271d98fe17d`

- `QLOOP1-F1` — **Fixed**: both changed consumer tests now assert the exact projected `LoopKind`, header block `1`,
  latch block `[2]`, body block `[2]`, and the single exit node projected from block `3`. The original loop-presence,
  region, ordering, and body-invocation assertions remain. The exact focused command passed 4/4 after this repair.
- `QLOOP1-F2` — **Fixed**: this repair trace records the failed attempt, root cause, repair delta, focused 4/4 evidence,
  affected assertions, and applicable failed-round count.
- `QLOOP1-F3` — **Fixed by bounded amendment**: the original capsule omitted the mechanically required GH-13
  selection-only record from the allowlist. The independent peer issued `PEER-AMENDMENT v1` at
  https://github.com/Bilaltariq41/SeqDoc/issues/85#issuecomment-5541770163. The amended allowlist permits only
  `selectedForExecution: true` to `false` while QLOOP1 is selected, requires restoration when QLOOP1 closes, and leaves
  every I13 contract, lifecycle, ownership, branch, next action, acceptance boundary, and implementation path unchanged.

### Post-amendment readiness re-audit

- **PASS**: Issue #85 is open, assigned to Bilaltariq41, and its latest comment is the authenticated, non-conflicting
  `PEER-AMENDMENT v1` receipt above.
- **PASS**: baseline, branch, objective, exact three-test implementation allowlist, four-test regression signature,
  non-goals, risks, existing coverage, soft budget, focused command, review boundary, and final gate remain frozen.
- **PASS**: the complete candidate changes only the three test files and four governance paths declared by this amended
  capsule. `GH-13.json` changes only `selectedForExecution: true` to `false`.
- **PASS**: canonical work-state validation and `git diff --check` succeed. The focused test candidate is unchanged from
  the post-repair 4/4 pass. A new independent post-amendment complete-candidate review is still required before the final
  Behavior gate.

### Post-amendment review

- **PASS — no findings** at exact SHA `7d7b459f223d47f89ec7723cb61e176ce4f13873`.
- The reviewer confirmed `QLOOP1-F1`, `QLOOP1-F2`, and `QLOOP1-F3` are resolved; all changed paths are allowlisted; the
  amended GH-13 delta is selection-only; and the test candidate is unchanged from focused 4/4 evidence.
- Residual obligations are the final 71-test Behavior gate and restoration of I13 selection when QLOOP1 closes.

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

Final gate result at exact SHA `7f7ba6404a5bb84e45d080709ba530fe56c82532`: **PASS** — 71 passed, 0 failed,
0 skipped, 71 total; exit code 0 and nonzero discovery. The worktree was clean before and after the gate.

PR #86 may leave draft state. QLOOP1 remains `Verifying` until authorized merge/closure; closure must restore I13 as
the selected root-Orchestrator work item as required by `PEER-AMENDMENT v1`.
