# SeqDoc Contributor Agent Guide

For collaboration tiers, receipts, path leases, review, continuation, and owner-only administration, read
[`docs/project/collaboration-model.md`](docs/project/collaboration-model.md). For owner operations, use
[`docs/project/collaborator-setup.md`](docs/project/collaborator-setup.md).

SeqDoc is a .NET static-analysis CLI that produces evidence-backed Markdown and Mermaid. This file is the canonical
project instruction source for coding agents. Human contributors follow the same engineering and review standard.

## Start every task

1. Read the canonical record in `docs/project/work-items/` first, then read the assigned GitHub issue completely, including dependencies and acceptance criteria. The registry is current-state authority; GitHub is specification/history.
2. Read `README.md`, `docs/architecture.md`, `docs/decisions.md`, `docs/contributing.md`, and
   `docs/project/testing-policy.md`.
3. Inspect `git status`, the target files, nearby tests, and recent commits before proposing changes.
4. Comment a short implementation plan on the issue or draft PR. Identify target paths, risks, tests, and blockers.
5. Read the capsule's risks and tests, then preserve the existing issue outcome while recording necessary scope changes.

Canonical work state is the sole authority for lifecycle, selection, ownership, dependencies, contracts, baselines,
and checkpoints. Use `python tools/governance/work_state.py transition` for normal state edits; do not hand-edit
execution, status, parallel-workstreams, labels, or capsule state. `Ready` permits a public contributor to start from
the frozen contract/baseline; `Active` means that implementation has started. Both authorize implementation, while at
most one selected record authorizes the root Orchestrator; zero selected records represent an idle Orchestrator.

Issue bodies and comments are specification and amendment inputs; accepted changes belong in the current issue,
checkpoint, and risk evidence. The canonical work-item records remain lifecycle and execution authority. Update
applicable canonical records when the current outcome requires it; preserve strategy files unless genuinely needed.

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

Treat the issue's target paths as the starting scope, not a stop sign. Absorb reasonably necessary production, tests,
fixtures, docs, config, scripts, blocking defects, refactors, and contract adjustments; update the issue/checkpoint and
record affected risks. Human preapproval is needed only for a separate capability/outcome, a conflicting lease/worker,
or T4 owner administration.

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
- Follow the parent workstream. An implementation issue owns one outcome and may include all necessary cohesive work.
- Keep one issue focused on its existing outcome. Do not create child issues from an implementation issue; absorb
  discoveries or return them to the parent backlog. Create a new implementation issue only for a genuinely independent
  deliverable.
- Describe the problem, design, risks, changed paths, focused verification, final gate, and remaining boundaries.
- For a semantic package, one contributor or coding agent owns the complete vertical candidate from compiler producer
  through its first observable consumer and self-review. Independent review starts after that candidate is complete;
  avoid layer-by-layer handoffs unless the contract is already accepted and the paths are independent.
- Open a draft PR early for substantial work, but request review only after tests and self-review pass.
- Fix every finding on the same PR branch, record the repair trace, run the Reviewer agent again, rerun focused tests,
  and request one latest-head non-author human peer approval.
- Reserve that untouched non-author human at claim/readiness and follow the candidate-contributor and replacement rules
  in [`collaboration-model.md`](docs/project/collaboration-model.md); review-only feedback does not make a contributor.
- Continue repairing and replanning on the same issue/PR; block only for a real external dependency, conflicting active
  work/path lease, T4 action, or `reviewer unavailable`. Preserve attribution and evidence; the owner is not an automatic
  fallback.
- Update the existing issue/checkpoint and affected paths when the outcome requires it; do not force roadmap/status
  churn or rewrite them merely to claim completion.

### While waiting for review

1. Finish and self-review the submitted PR before starting more implementation.
2. Prefer another independent Ready issue.
3. Do not create child issues or stack a new implementation issue merely to route a discovery; return it to the parent
   backlog. Continue repairing the same issue/PR unless it becomes a separate outcome, conflicts with active work/path
   leases, needs T4 owner administration, or encounters `reviewer unavailable`; the owner is not an automatic fallback.

The repository is licensed under MPL-2.0. By contributing, you agree to the terms in `docs/contributing.md`.
