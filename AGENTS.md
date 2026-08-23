# SeqDoc Contributor Agent Guide

SeqDoc is a .NET static-analysis CLI that produces evidence-backed Markdown and Mermaid. This file is the canonical
project instruction source for coding agents. Human contributors follow the same engineering and review standard.

## Start every task

1. Read the assigned GitHub issue completely, including dependencies and acceptance criteria.
2. Read `README.md`, `docs/architecture.md`, `docs/decisions.md`, `docs/contributing.md`, and
   `docs/project/testing-policy.md`.
3. Inspect `git status`, the target files, nearby tests, and recent commits before proposing changes.
4. Comment a short implementation plan on the issue or draft PR. Identify target paths, risks, tests, and blockers.
5. Stay inside the issue scope. Ask before changing architecture, public contracts, or unrelated files.

GitHub issues are execution authority for contributor work. Files under `docs/project/` are maintainer-owned durable
strategy and execution state; do not edit them unless the issue explicitly requires it.

## Product invariants

- Static/compiler evidence is authoritative. Never invent exact behavior when evidence is incomplete.
- Keep compilation profiles and target frameworks separate.
- Preserve the typed pipeline: Program Index, Method Flow, Scenario Graph, Diagram Plan.
- Every user-facing fact retains evidence and certainty.
- Failed analysis preserves the previous valid state.
- Output must not depend on checkout path, scheduling, timestamps, or unstable iteration.
- Keep `SeqDoc.Core` free of Roslyn, MSBuild, SQLite, CLI, and renderer dependencies.
- Propagate cancellation through long-running operations.
- Never use application, route, type, method, or business names as production matching rules.

## Semantic proof protocol

New compiler, framework, or intermediate-representation semantics need all five proof gates:

1. **Identity and admission:** name the accepted Roslyn `IOperation` shape, original symbol definition, containing type
   and assembly, supported overloads, and argument positions. Required identity fails closed when missing. A matching
   operation or type shape does not alone prove registration, root admission, or execution.
2. **Evidence chain:** prove that realistic source reaches the production extractor and typed stages. Completion means
   a user-visible or persisted assertion; an intermediate fact is not the first observable consumer. Every new fixture
   must participate in that production path.
3. **Monotonic claims:** each stage may preserve or weaken what evidence establishes, never strengthen it. Carry exact
   evidence and the least-confident contributing certainty; capability cannot become admission, registration cannot
   become execution, and syntax cannot become persistence without separate proof.
4. **Isolation and placement:** require exact profile and Program Index snapshot confinement at every join. Preserve
   proven guards, terminal arms, exception regions, and chronology; withhold placement that topology cannot prove.
5. **Boundaries:** ignore unrelated syntax, but retain an evidence-backed conservative boundary or diagnostic for
   recognized-but-unsupported behavior. Exercise supported overloads and a same-shaped negative through the producer.

For framework models, record the admission table in the implementation plan: operation shape, exact framework symbol,
supported overloads, registration requirement, callback mapping, unsupported forms, negative lookalikes, and the first
consumer. Stop and ask when the issue does not provide enough evidence to fill this table.

## Implementation workflow

1. Reproduce the problem or establish a focused red test before changing behavior.
2. Prefer the smallest generic contract that solves the issue. Do not implement later roadmap stages incidentally.
3. Reuse existing typed facts and helpers; do not rescan source in application or rendering layers.
4. Preserve stable identities, canonical ordering, evidence, certainty, and backward-compatible defaults.
5. Add risk-based tests at the least expensive reliable layer. Avoid duplicate assertions across layers.
6. Run focused tests during implementation. Run the issue's final gate once after self-review.
7. Inspect the complete diff, not only files you remember changing.

Treat the issue's target paths as an allowlist. Before changing an unlisted path, stop and obtain maintainer approval.
Build configuration, SDK selection, CLI behavior, public contracts, and maintainer-owned project files never count as
incidental cleanup. Record the approval link in the PR.

When blocked, stop and report the exact command, error, evidence, and smallest decision needed. Do not weaken tests,
remove conservative diagnostics, guess semantics, or expand scope to make the task appear complete.

## Self-review before opening a PR

- Re-read the issue and verify every acceptance criterion.
- Check `git diff --check`, `git status`, and the full diff from `main`.
- Look for false positives, profile leakage, unstable ordering, missing evidence/certainty, and previous-state regressions.
- Confirm negative and boundary cases, not only the happy path.
- Apply all five semantic proof gates to the complete diff. Account for every new fixture and unexpected changed path.
- Remove debug output, generated files, secrets, local paths, copied external source, and unrelated refactoring.
- Run the focused command and declared final gate; record exact counts and any unavailable external lanes.
- For generated diagrams, inspect the actual Markdown/Mermaid and use Mermaid CLI when layout behavior changed.

## External test projects

Supplied and open-source applications live in sibling `../SeqDoc-TestProjects`, or the directory named by
`SEQDOC_TEST_PROJECTS_ROOT`. Never commit their source, configuration, credentials, caches, build output, or generated
documentation to SeqDoc. See `docs/usage.md` for setup.

## Pull requests and review

- Work in a fork and a focused branch. Never push directly to SeqDoc `main`.
- Follow the parent workstream's approved delivery packages. One PR may close 1–3 cohesive sub-issues when they share a
  contract, target paths, and acceptance boundary; list every included issue with `Closes #<number>`.
- Do not combine unrelated issues or invent a package without maintainer approval. Shared contracts should include
  their first real consumer in the same package when that makes the design reviewable, but review-sensitive foundations
  may still be separated explicitly.
- Describe the problem, design, risks, changed paths, focused verification, final gate, and remaining boundaries.
- For a semantic package, one contributor or coding agent owns the complete vertical candidate from compiler producer
  through its first observable consumer and self-review. Independent review starts after that candidate is complete;
  avoid layer-by-layer handoffs unless the contract is already accepted and the paths are independent.
- Open a draft PR early for substantial work, but request review only after tests and self-review pass.
- The maintainer will batch findings. Fix every finding on the same PR branch, record the repair trace described in
  `docs/project/delegated-contribution-workflow.md`, re-review the complete candidate, rerun affected focused tests plus
  the required gate, and request review again.
- After two unsuccessful repair rounds, stop and wait for an explicit split, rejection, or bounded maintainer takeover.
  A takeover retains accepted contributor work and attribution, records its repair delta, and uses the original PR when
  practical.
- Do not rewrite canonical roadmap/status files to claim completion. The maintainer updates them after merge.

### While waiting for review

1. Finish and self-review the submitted PR before starting more implementation.
2. Prefer another independent issue or approved delivery package labeled `ready`, branched from current `main`. Keep
   at most two implementation PRs open per contributor.
3. For a blocked next issue, research and comment a plan, risks, fixtures, and tests without changing production code.
4. A dependent implementation may start only after the maintainer comments approval and applies `stack-approved`.
   Keep the GitHub dependency in place, branch from the pending PR, open a draft PR, state `Depends on PR #...`, and
   limit the stack to the base PR plus one dependent PR.
5. Never stack shared Core/IR, identity, persistence, profile, or other review-sensitive foundation changes unless the
   issue explicitly permits it.
6. After the base merges, rebase the dependent branch onto `main`, verify its diff contains only its issue, rerun its
   focused command and final gate, remove draft status, and request review.
7. If the base changes substantially or is rejected, pause and rework/discard dependent changes. If no independent or
   approved stacked work exists, comment on the parent workstream and wait rather than expanding scope.

The repository is licensed under MPL-2.0. By contributing, you agree to the terms in `docs/contributing.md`.
