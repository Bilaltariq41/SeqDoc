# Contributing

Thank you for considering a contribution to SeqDoc. This page describes how to build, test, and
submit focused changes.

## Build and test

```powershell
dotnet restore SeqDoc.slnx
dotnet build SeqDoc.slnx -c Release
dotnet test SeqDoc.slnx -c Release
```

The repository requires the .NET SDK declared in `global.json`. All warnings are treated as errors.

## Code style

- Follow `.editorconfig`; formatting and analyzer rules are enforced during build.
- Use file-scoped namespaces, explicit stable ordering, and immutable records where appropriate.
- Comments should explain intent, invariants, compatibility constraints, or non-obvious failure
  protection.

## Tests and fixtures

- Unit and component tests run through the solution. Compiler and CLI process integration tests live
  under `tests/` and run separately when changing those surfaces.
- Compiler fixtures live under `tests/fixtures/` and are referenced by relative path from tests.
- Semantic changes follow the proof gates in [`AGENTS.md`](../AGENTS.md) and the semantic test proofs in
  [`docs/project/testing-policy.md`](project/testing-policy.md). Use realistic producer fixtures and observable
  acceptance assertions; hand-built facts prove only their downstream consumer.
- Acceptance assertions should target observable wording, structure, and determinism.
- Supplied and open-source acceptance applications live in sibling `../SeqDoc-TestProjects`. See
  [Using SeqDoc](usage.md); never copy those repositories into SeqDoc.

## Submitting changes

1. Fork the public repository.
2. Create a focused branch from `main` in your fork.
3. Make focused changes with tests.
4. Run the build and relevant test commands above.
5. Open a pull request against SeqDoc's `main` branch describing the problem, change, and
   verification performed.

For assigned team work, the parent issue defines the workstream and sub-issues define mergeable slices. Use
`Closes #<issue>` in the PR body. Follow the planning, self-review, and repair loop in [`AGENTS.md`](../AGENTS.md)
whether the change is written manually or with a coding agent.

Sub-issues are planning and dependency units, not necessarily one-PR units. Parent workstreams define approved
delivery packages of 1–3 cohesive sub-issues. A package should deliver one complete vertical capability with production
code, tests, and realistic acceptance while avoiding unrelated scope. List every closed sub-issue in the PR body. Do
not create a new package without maintainer approval.

By submitting a contribution, you represent that you have the right to submit it and agree that it
is licensed under the [Mozilla Public License 2.0](../LICENSE), the same license as the project.

## Review process

- Changes are reviewed for correctness, evidence fidelity, and determinism.
- Behavior changes must be backed by tests and, where relevant, documentation updates.
- Keep unrelated refactoring out of a single pull request.
- Direct pushes to `main` are restricted; all external contributions use pull requests.
- Address review findings on the same PR branch and request review again. The maintainer updates canonical
  roadmap/status documentation after verified merges.

## Waiting for review and stacked work

When a completed PR is waiting, first choose another independent issue labeled `ready` and branch from current `main`.
Keep no more than two implementation PRs open at once. If the next issue is blocked by the pending PR, you may research
and plan it, but production implementation requires explicit maintainer approval.

Request approval in the blocked issue. If approved, the maintainer comments and adds `stack-approved`; the dependency
remains because the work still cannot merge independently. Branch from the pending PR, open a draft PR, link the base
PR, and limit the stack to two levels. After the base merges, rebase onto `main`, inspect the resulting issue-only diff,
rerun verification, and then mark the PR ready. Pause the dependent work if the base is rejected or substantially
redesigned. The PR **Approve** button is code-review approval and is not used to authorize starting stacked work.
