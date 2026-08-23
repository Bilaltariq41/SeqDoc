# Delegated Contribution Workflow

Delegated changes remain candidates until the maintainer verifies their complete behavior. Preserve the submitted
branch, record its base revision, inspect the actual diff, and classify each area as accepted, repairable, or rejected.
Return bounded findings to human contributors with file/line evidence, risk, expected behavior, and one focused
verification command; review only the repair delta plus affected risks when they return. After two unsuccessful repair
rounds, reject, split, or explicitly take ownership of the repair.

Use the same return-and-repair loop for an available implementation agent: resume the same agent session with exact
findings, require it to fix its own delta, and verify only changed risks. Do not have the maintainer silently rewrite
repairable delegated work. The maintainer takes over only when the contributor/agent is unavailable, repeated repair
fails, or the required architectural decision exceeds the delegated scope.

Automated or unavailable-author candidates may be hardened on a local branch based on the submission. Retain correct
code and repair only demonstrated defects. Before publication, compare the full candidate against canonical `main`,
run risk-focused tests and one realistic acceptance scenario, then squash the verified tree onto a clean branch from
`main`. Scratch branches, worktrees, copied fixtures, misleading execution records, and intermediate commits never
enter public history.

Canonical documentation is rewritten from verified evidence. For each integrated candidate record what was reused,
what was repaired or rejected, why, the verification performed, and whether delegation reduced total work. Optimize
the workflow from recurring defect categories rather than weakening review standards.

## Recurring semantic review failures

Framework and persistence candidates have repeatedly implemented a plausible downstream model without proving that
real source can create its input. Common signatures are hand-built descriptors that bypass Roslyn extraction, fixtures
that compile but no test consumes, method-name shape treated as an exact framework slot, and facts rendered without
registration or control-placement proof. Another recurring failure is silent loss of recognized-but-unsupported forms.

Prevent these defects in the assignment, not only in review. Require an exact-symbol admission table, a realistic
source-to-first-consumer test, a same-shaped negative, weakest-certainty aggregation, and an explicit account of
profile/snapshot confinement and guard or terminal placement. Reviewers should search for new fixtures with no test
reference and tests that construct the new semantic fact directly without exercising its producer.

Scope drift is a separate recurring category. SDK/build files, CLI behavior, public contracts, and other paths outside
the issue allowlist require prior maintainer approval. A useful unrelated fix stays out of the candidate until it has
its own authority and coverage.

## Delivery package sizing

Use sub-issues to model dependencies and acceptance, but avoid requiring maintainer review after every small internal
step. A parent workstream should group 1–3 cohesive sub-issues into an approved delivery package when they share target
paths and one vertical outcome. The contributor and agent may use multiple ordered commits inside one package PR; the
PR closes every included issue and receives one complete self-review.

Keep a contract and its first consumer together when that makes the abstraction demonstrably useful. Separate a shared
foundation when its review could materially redirect dependent work, and keep supplied-project acceptance separate
when it would obscure the semantic implementation diff. Never enlarge a package with unrelated cleanup merely to
reduce review count.

## Review latency and dependent work

Maintain at least one independent `ready` issue per contributor where practical. A contributor with a review-ready PR
may start one independent branch from `main`, keeping a maximum of two open implementation PRs. Blocked work remains
planning-only unless the maintainer explicitly authorizes a two-level stack.

Stack authorization is workflow permission, not code approval: retain the GitHub `blocked by` relationship, add
`stack-approved`, branch the dependent issue from the pending PR, and keep its PR draft with an explicit dependency.
Do not stack review-sensitive shared contracts unless pre-approved. When the base merges, rebase, verify the isolated
diff, rerun affected verification, and remove the label/draft state. If the base direction changes or fails review,
stop dependent implementation rather than preserving sunk work on a rejected foundation.
