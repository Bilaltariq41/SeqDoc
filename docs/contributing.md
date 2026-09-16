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

For assigned team work, the parent issue defines the workstream and an implementation issue defines the outcome. Use
`Closes #<issue>` in the PR body. Follow the planning, self-review, and continuation loop in [`AGENTS.md`](../AGENTS.md)
whether the change is written manually or with a coding agent.

An implementation issue may contain the production, tests, fixtures, documentation, configuration, scripts, refactors,
and acceptance work reasonably needed for its outcome. Do not create child issues. Absorb discoveries or return them to
the parent backlog; create a new implementation issue only for a genuinely independent deliverable.

By submitting a contribution, you represent that you have the right to submit it and agree that it
is licensed under the [Mozilla Public License 2.0](../LICENSE), the same license as the project.

## Review process

- Changes are reviewed for correctness, evidence fidelity, and determinism.
- Behavior changes must be backed by tests and, where relevant, documentation updates.
- Keep unrelated refactoring out of a single pull request.
- Direct pushes to `main` are restricted; all external contributions use pull requests.
- Address review findings on the same PR branch, run the Reviewer agent again, and request one latest-head non-author
  human peer approval. Agent self-review is not another human approval.

## Waiting for review and stacked work

When a completed PR is waiting, choose another independent Ready issue. Do not create child issues or stack a new
implementation issue merely to route a discovery; return it to the parent backlog. Continue repairing the same PR unless
the change is a separate outcome, conflicts with active work/path leases, or needs T4 owner administration.
