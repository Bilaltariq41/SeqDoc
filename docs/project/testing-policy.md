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

## Semantic proof ladder

For new compiler, framework, persistence, worker, or IR semantics, select coverage from this ladder according to the
failure risks:

1. a contract test for immutable shape and invariants;
2. a compiler-bound fixture test proving real syntax reaches exact production extraction;
3. a cross-stage projection test proving evidence, certainty, profile isolation, ordering, and control placement;
4. a wording, diagram, persistence, or CLI test at the first observable consumer;
5. a lookalike, overload, unsupported-shape, or foreign-profile negative through the same production path.

Hand-constructed descriptors and facts are valid contract or consumer tests, but they cannot replace compiler-boundary
coverage. Every added fixture must be referenced by a test through the production path it claims to exercise. A fixture
that merely builds, or a large unrelated pass count, is not evidence for a new admission rule.

Map every acceptance criterion to a named test and layer before requesting review. If a ladder level is unnecessary or
unavailable, record the reason and the residual risk. Exact-symbol work must test original definitions, supported
overloads, and a same-shaped negative. Control-sensitive work must test guard or terminal placement rather than only
fact presence.

## Soft budget

A routine checkpoint should normally add approximately 5–12 distinct claims. More than 15 requires a written
risk-by-risk justification. The budget is soft and never suppresses a genuinely distinct high-impact risk.

## Test Writer trigger

Dispatch the Test Writer for new compiler or intermediate-representation semantics; exact-symbol, overload, or
false-positive risk; evidence/certainty degradation; persistence or previous-valid-state behavior; deterministic
identity/output; a concrete regression signature; or acceptance-critical scenario, wording, or diagram behavior.
Routine mechanical and documentation work does not justify a separate Test Writer.

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
