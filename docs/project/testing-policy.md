# Testing Policy

SeqDoc selects tests from observable risks rather than raw branch, row, or coverage counts.

## Principles

1. Start each product checkpoint with a risk inventory and identify existing coverage first.
2. Use the smallest reliable layer: pure contract tests, compiler-boundary tests, then real-project acceptance.
3. Test semantic outcomes, evidence/certainty, false-positive boundaries, deterministic identity/output, failure
   preservation, and concrete regressions rather than implementation shape.
4. Avoid duplicating the same assertion across layers unless each layer has a distinct failure mode.
5. Keep tests deterministic, isolated, order-independent, and self-checking.
6. Real applications prove integration and breadth; they never justify project-specific production rules.

## Semantic test proofs

For new compiler, framework, persistence, worker, or IR semantics, select the proofs required by the risk inventory:

1. **Producer proof:** realistic source reaches exact production extraction.
2. **Propagation proof:** evidence, weakest certainty, identity, profile, ordering, placement, and claim strength survive
   each changed typed boundary.
3. **Observable proof:** wording, diagram, persistence, or CLI behavior proves the result; an intermediate fact does not.
4. **Boundary proof:** a relevant lookalike, overload, unsupported shape, missing identity, or foreign profile fails
   conservatively through the same producer.

Add contract tests when immutable shape or invariant risk requires them. Hand-constructed facts prove only their
consumer. A fixture that merely builds, or a large unrelated pass count, is not semantic producer evidence.

Map every acceptance criterion to a named test and layer before requesting review. If a proof is unnecessary or
unavailable, record the reason and the residual risk. Exact-symbol work must test original definitions, supported
overloads, and a same-shaped negative. Control-sensitive work must test guard or terminal placement rather than only
fact presence.

## Soft budget

A routine checkpoint should normally add approximately 5–12 distinct claims. More than 15 requires a written
risk-by-risk justification. The budget is soft and never suppresses a genuinely distinct high-impact risk.

## Dedicated test pass

Use a dedicated test-design pass for compiler or IR semantics, exact-symbol or false-positive risk, evidence/certainty
degradation, persistence or previous-valid-state behavior, deterministic identity/output, concrete regressions, and
acceptance-critical wording or diagrams. This may be a fresh pass by the same coding agent or a separate agent when the
tool supports one. Routine mechanical and documentation work does not need this pass.

## Verification lanes

- **Focused:** the smallest changed test surface or affected project build.
- **Checkpoint:** affected small tests plus one relevant boundary proof and the declared final gate.
- **Milestone:** Release build/tests, applicable deterministic and repository checks, named real applications, and
  manual output inspection.
- **Corpus/release:** broad applications, performance/resources, platform lanes, and persistence equivalence when
  explicitly required.

Build an affected test project once, then use `--no-build --no-restore` for unchanged reruns. Expensive Roslyn/MSBuild
classes are selected by fully qualified test filter during checkpoints. A complete Analysis or solution sweep is a
major compiler/release milestone gate, not a routine PR or documentation gate.

No completion claim may rely on stale evidence. A failed command may be rerun after a relevant repair; do not rerun
a successful command against an unchanged candidate.
