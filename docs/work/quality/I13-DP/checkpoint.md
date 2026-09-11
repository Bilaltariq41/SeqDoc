# I13 diagnostic-path confinement checkpoint

## State

`NotStarted`

## Authority and frozen state

Issue [#93](https://github.com/Bilaltariq41/SeqDoc/issues/93) is the specification authority. This checkpoint freezes
its T3 contract at baseline `ac1a41f25357b32fbf2e0b2bff82ffc6cb1fe586`.

Matching prospective approvals:

- Qais: https://github.com/Bilaltariq41/SeqDoc/issues/93#issuecomment-5620523305
- Abood: https://github.com/Bilaltariq41/SeqDoc/issues/93#issuecomment-5633736360

Those approvals authorize implementation only after this governance-only readiness transaction merges and GH-93 is
selected and activated through `tools/governance/work_state.py`. This checkpoint does not reactivate GH-13/I13.

## Objective

Confine checkout-specific paths in workspace and compiler diagnostics before those values reach public or persisted
output. Keep raw compiler and workspace values authoritative for classification, deterministic ordering, ordinal
assignment, and diagnostic identity.

## Target paths

Production:

- `src/SeqDoc.Analysis.Roslyn/Diagnostics/CompilerDiagnosticFactory.cs`
- `src/SeqDoc.Analysis.Roslyn/Workspace/CompilationWorkspaceLoader.cs`

Tests:

- `tests/SeqDoc.Analysis.Tests/CompilerDiagnosticFactoryTests.cs` (new)
- `tests/SeqDoc.AcceptanceTests/CompilerDiagnosticPathConfinementTests.cs` (new)

Checkpoint evidence:

- `docs/work/quality/I13-DP/**`

Generated canonical state:

- `docs/project/work-items/GH-93.json`
- `docs/project/execution.json`

Canonical state changes must use `tools/governance/work_state.py`. Treat this list as an allowlist. Stop before editing
any other path.

## Required behavior

1. `CompilationWorkspaceLoader` supplies the analyzed repository root to both
   `CompilerDiagnosticFactory.CreateWorkspace` and `CompilerDiagnosticFactory.CreateCompiler`.
2. Workspace failure/warning classification reads the original raw `WorkspaceDiagnostic.Message`.
3. Compiler ordering and ordinal assignment read the original diagnostic ID, source path, source span, message, and
   project identity.
4. Diagnostic identity inputs remain unchanged.
5. Path confinement occurs only after classification, ordering, ordinal assignment, and identity decisions.
6. `CreateWorkspace` confines path text before it enters `AnalysisDiagnostic.TechnicalCause`.
7. `CreateCompiler` confines both `DiagnosticLocation.Description` and `AnalysisDiagnostic.TechnicalCause`; the
   path-free compiler message summary remains unchanged.
8. A path proven inside the analyzed repository root becomes a canonical repository-relative path with `/` separators.
9. An absolute path outside the analyzed repository root becomes `<external-path>`.
10. URLs, package identities, package versions, ordinary prose, severity, evidence, and certainty remain unchanged.
11. Console output, `--json`, the complete build-diagnostics artifact, and persisted diagnostic values receive the same
    confined fields.
12. Two differently located clean checkouts of the same failing project produce byte-identical public diagnostic values
    with stable diagnostic IDs and order.

## Evidence chain and first observable consumers

- `CompilationWorkspaceLoader.LoadAsync` collects raw `WorkspaceDiagnostic` values and Roslyn compiler errors.
- `CompilerDiagnosticFactory.CreateWorkspace` performs workspace classification before public-value confinement.
- `CompilerDiagnosticFactory.CreateCompiler` performs raw-value ordering before public-value confinement.
- Both producers create the single `AnalysisDiagnostic` value consumed by console output, CLI JSON, the complete
  build-diagnostics artifact, and persistence.
- Factory tests prove exact boundary parsing, classification, identity, and ordering. The acceptance fixture must prove
  the production producer-to-public/persisted path from two differently located clean checkouts.

An intermediate helper result is not completion evidence.

## Non-goals

- No change to workspace or compiler failure classification, warning promotion, diagnostic identity, ordinal assignment,
  ordering, severity, evidence, or certainty.
- No broad drive-letter, slash, substring, or regular-expression replacement over arbitrary diagnostic prose.
- No change to `CliHost.DisplayPath`.
- No normalization in `CompilerDiagnosticFactory.CreateInput`, `CreateProfileResolution`, `CreateInfrastructure`, or
  `CreateIndexFailure` without a separate reproduction and matching amended T3 approval.
- No application, repository, package, route, type, method, business, or drive-letter-case matching rule.
- No public contract, persistence schema, SDK, package, build, workflow, configuration, external-source, or generated
  artifact change.
- No GH-13/I13 acceptance implementation or reactivation.

## Risk inventory

1. Confinement before classification changes a workspace failure into a warning or prevents warning promotion.
2. Confined values replace raw sort keys and change ordinal assignment or `DiagnosticId` values.
3. Two distinct raw diagnostics collapse into one public value and one is lost or reordered.
4. Repository containment accepts traversal, a sibling prefix, a different volume, UNC ambiguity, or mixed separators.
5. A URL, package identity/version, or ordinary prose is mistaken for a path.
6. Workspace and compiler producers apply different confinement rules.
7. Console, JSON, build-diagnostics artifact, and persistence expose different values.
8. An internal path remains absolute or an external absolute path remains machine-specific.
9. Diagnostic evidence or certainty is invented, removed, strengthened, or weakened.
10. Relocated clean checkouts produce different diagnostic bytes, IDs, or order.
11. The production acceptance fixture does not exercise both workspace and compiler producer paths.
12. Acceptance pressure expands the change to unrelated diagnostic factories or output paths.

## Existing relevant coverage

- `WorkspaceDiagnosticClassifierTests` covers raw audit warnings, fatal lookalikes, existing warning preservation, and
  target-framework support warnings.
- `CompilationWorkspaceLoaderTests` covers project and solution selection but not producer-to-public path confinement.
- `AnalysisDiagnosticTests` covers required fields, certainty, and validation.
- `SqliteProgramIndexStoreTests` covers diagnostic round-trip shape.
- `CliProcessTests` covers build-failure JSON and complete build-diagnostics artifacts.

None of this coverage proves identical confined workspace/compiler diagnostics from two relocated clean checkouts.

## Test Writer assignment and soft budget

A Test Writer is required because this checkpoint changes security-sensitive public diagnostics and has exact
classification, identity, ordering, false-positive, persistence, and relocated-output risks.

Uncovered risks are the twelve items above. Add approximately 4 to 8 distinct claims across exactly:

- `tests/SeqDoc.Analysis.Tests/CompilerDiagnosticFactoryTests.cs`
- `tests/SeqDoc.AcceptanceTests/CompilerDiagnosticPathConfinementTests.cs`

At the factory layer, cover repository-internal, external absolute, no-path, URL, package, raw classification,
identity/order, Windows/UNC, and portable or mixed-separator boundaries with shared cases where practical. At the
acceptance layer, use the production workspace/compiler producers and prove two relocated clean checkouts, public
console/JSON/build-diagnostics parity, persisted-value parity, stable IDs/order, and byte equality. Avoid duplicate
assertions across layers.

The Test Writer must not edit production, existing tests, configuration, external source, build files, or any path
outside the allowlist.

Focused Test Writer and implementation command:

```powershell
dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --filter "FullyQualifiedName~CompilerDiagnosticFactoryTests"; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; $env:SEQDOC_TEST_PROJECTS_ROOT = (Resolve-Path "../SeqDoc-TestProjects").Path; dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter "FullyQualifiedName~CompilerDiagnosticPathConfinementTests"
```

Record nonzero discovery and exact passed, failed, skipped, and total counts. A missing corpus, restore/build failure,
producer-path skip, or zero discovery is not an accepted pass.

## Implementation boundary

Start with a failing producer-path test. Make the smallest generic change within the two production files. Preserve the
raw values used for classification and ordering. Do not rescan or rewrite diagnostics in CLI or persistence layers; all
public consumers must inherit the same confined `AnalysisDiagnostic` fields from the producer.

Inspect the complete diff and generated diagnostic artifacts after focused verification. Stop at `ReviewRequired`.

## Independent review

Run one independent complete-candidate review after focused verification. Review the full diff for raw classification,
stable identity/order, path false positives, public/persisted parity, relocated byte equality, scope, and all stop
conditions. Record each finding as `Fixed`, `Rejected` with evidence, or `Deferred` with explicit owner approval.

After a changed repair candidate has green focused verification, re-review the complete candidate. After two failed
repair rounds, preserve the worktree, transition GH-93 to `Blocked`, and obtain a separate authorized decision.

## Final gate

Run once only after every review finding is resolved:

```powershell
dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Cli.Tests/SeqDoc.Cli.Tests.csproj -c Release; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Persistence.Tests/SeqDoc.Persistence.Tests.csproj -c Release; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; $env:SEQDOC_TEST_PROJECTS_ROOT = (Resolve-Path "../SeqDoc-TestProjects").Path; dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter "FullyQualifiedName~CompilerDiagnosticPathConfinementTests"
```

The final receipt must include exact counts, both relocated-checkout artifact hashes, complete diagnostic records, stable
IDs/order, public/persisted parity, and cleanup equality. Do not repeat a successful final gate on an unchanged candidate.

## Stop conditions

Stop and report the exact command, error, evidence, and smallest decision needed if:

- any required change falls outside the allowlist;
- classification, warning promotion, diagnostic identity, ordinal assignment, raw ordering, severity, evidence, or
  certainty changes;
- a path false positive changes a URL, package identity/version, or ordinary prose;
- a repository-internal path remains absolute or an external absolute path remains machine-specific;
- console, JSON, build-diagnostics artifact, and persisted values disagree;
- relocated output, IDs, or order differ;
- both workspace and compiler producer paths cannot reach the first observable consumer;
- a required external lane or tool is unavailable;
- external source or generated output would need to be committed;
- a public contract, architecture, schema, SDK, package, build, workflow, or unrelated diagnostic factory must change;
- two repair rounds fail.
