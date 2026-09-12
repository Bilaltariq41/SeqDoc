# I13 diagnostic-path confinement checkpoint

## State

`Verifying`

## Authority and frozen state

Issue [#93](https://github.com/Bilaltariq41/SeqDoc/issues/93) is the specification authority. This checkpoint freezes
its T3 contract at baseline `ac1a41f25357b32fbf2e0b2bff82ffc6cb1fe586`.

Matching prospective approvals:

- Qais: https://github.com/Bilaltariq41/SeqDoc/issues/93#issuecomment-5620523305
- Abood: https://github.com/Bilaltariq41/SeqDoc/issues/93#issuecomment-5633736360

Those approvals authorize implementation only after this governance-only readiness transaction merges and GH-93 is
selected and activated through `tools/governance/work_state.py`. This checkpoint does not reactivate GH-13/I13.

A command-only amendment to the focused command, final gate, and one stop-condition wording (removing the forced `SEQDOC_TEST_PROJECTS_ROOT` override, since `CompilerDiagnosticPathConfinementTests` is self-contained and does not read the external corpus) was approved by both peers: Abood at https://github.com/Bilaltariq41/SeqDoc/issues/93#issuecomment-5634435042 and Qais at https://github.com/Bilaltariq41/SeqDoc/issues/93#issuecomment-5637036478. No other contract term changed.

A producer-specific acceptance amendment was approved by Qais at
https://github.com/Bilaltariq41/SeqDoc/issues/93#issuecomment-5638482622 and matched by Abood at
https://github.com/Bilaltariq41/SeqDoc/issues/93#issuecomment-5646026918. It replaces the impossible same-project
dual-producer fixture with two independent lanes and restores the conditional external-corpus fallback in the focused
command. Relocated equality is scoped to an identical toolchain, normalized restored-package set, target
project/profile, and Program Index snapshot. Cross-profile diagnostic-ID equality is not required and must not be
asserted. No other contract term changed.

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
11. The workspace lane uses two clean, relocated, disposable worktrees of supplied CreditTransfer revision
    `02b82a5115ef6e2d138c70670f28b959fb646f6e`, targets `CreditTransferWeb/CreditTransfer.csproj` at `Release/net9.0`,
    and proves matching confined console, `--json`, and persisted active-snapshot values. Its successful warning-only
    analysis produces no `build-diagnostics.json`.
12. The compiler lane uses the self-contained broken project in two relocated temporary roots and proves matching
    confined console, `--json`, and complete build-diagnostics values. Failed analysis must not invent a persisted valid
    state.
13. Relocated diagnostic value, ID, and order equality is required only when toolchain, normalized restored-package set,
    target project/profile, and Program Index snapshot are identical. Cross-profile diagnostic-ID equality is not
    required and must not be asserted.

## Evidence chain and first observable consumers

- `CompilationWorkspaceLoader.LoadAsync` collects raw `WorkspaceDiagnostic` values and Roslyn compiler errors.
- `CompilerDiagnosticFactory.CreateWorkspace` performs workspace classification before public-value confinement.
- `CompilerDiagnosticFactory.CreateCompiler` performs raw-value ordering before public-value confinement.
- Each producer independently creates authoritative `AnalysisDiagnostic` values. One project need not trigger both.
- The successful workspace lane reaches console output, CLI JSON, and the persisted active Program Index snapshot; no
  build-diagnostics artifact is expected.
- The failing compiler lane reaches console output, CLI JSON, and the complete build-diagnostics artifact; it does not
  activate or invent persisted failed state.
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
10. Relocated clean checkouts with identical toolchain, package set, project/profile, and Program Index snapshot produce
    different diagnostic bytes, IDs, or order, or a test incorrectly requires cross-profile identity equality.
11. The two production acceptance lanes do not independently exercise the workspace and compiler producer paths.
12. Acceptance pressure expands the change to unrelated diagnostic factories or output paths.
13. Fixture cleanup removes unrelated state, leaves fixture-owned temporary directories or worktree registrations, or
    treats incomplete cleanup as success.

## Existing relevant coverage

- `WorkspaceDiagnosticClassifierTests` covers raw audit warnings, fatal lookalikes, existing warning preservation, and
  target-framework support warnings.
- `CompilationWorkspaceLoaderTests` covers project and solution selection but not producer-to-public path confinement.
- `AnalysisDiagnosticTests` covers required fields, certainty, and validation.
- `SqliteProgramIndexStoreTests` covers diagnostic round-trip shape.
- `CliProcessTests` covers build-failure JSON and complete build-diagnostics artifacts.

None of this coverage proves identical confined workspace/compiler diagnostics through their applicable consumers from
two relocated clean checkouts.

## Test Writer assignment and soft budget

A Test Writer is required because this checkpoint changes security-sensitive public diagnostics and has exact
classification, identity, ordering, false-positive, persistence, and relocated-output risks.

Uncovered risks are the twelve items above. Add approximately 4 to 8 distinct claims across exactly:

- `tests/SeqDoc.Analysis.Tests/CompilerDiagnosticFactoryTests.cs`
- `tests/SeqDoc.AcceptanceTests/CompilerDiagnosticPathConfinementTests.cs`

At the factory layer, cover repository-internal, external absolute, no-path, URL, package, raw classification,
identity/order, Windows/UNC, and portable or mixed-separator boundaries with shared cases where practical. At the
acceptance layer, use the supplied CreditTransfer workspace producer and self-contained compiler producer as separate
lanes. Prove each lane across two relocated clean roots, applicable public/persisted parity, stable IDs/order within one
exact profile, and byte equality. The workspace lane must resolve the corpus conditionally from an existing
`SEQDOC_TEST_PROJECTS_ROOT` or the sibling fallback, stop rather than skip when the exact revision or producer is
unavailable, and leave external source unchanged. Avoid duplicate assertions across layers.

The Test Writer must not edit production, existing tests, configuration, external source, build files, or any path
outside the allowlist.

Focused Test Writer and implementation command:

```powershell
if (-not $env:SEQDOC_TEST_PROJECTS_ROOT) { $env:SEQDOC_TEST_PROJECTS_ROOT = (Resolve-Path ../SeqDoc-TestProjects).Path }; dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --filter FullyQualifiedName~CompilerDiagnosticFactoryTests; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter FullyQualifiedName~CompilerDiagnosticPathConfinementTests
```

Record nonzero discovery and exact passed, failed, skipped, and total counts. A missing corpus, restore/build failure,
producer-path skip, or zero discovery is not an accepted pass.

## Implementation boundary

Start with a failing producer-path test. Make the smallest generic change within the two production files. Preserve the
raw values used for classification and ordering. Do not rescan or rewrite diagnostics in CLI or persistence layers; all
public consumers must inherit the same confined `AnalysisDiagnostic` fields from the producer.

Inspect the complete diff and generated diagnostic artifacts after focused verification. Stop at `ReviewRequired`.
Acceptance cleanup must use bounded retries, directly verify that every fixture-owned temporary directory and worktree
registration is absent, preserve unrelated pre-existing state and grandfathered stale metadata, and fail closed if
cleanup remains incomplete.

## Independent review

Run one independent complete-candidate review after focused verification. Review the full diff for raw classification,
stable identity/order, path false positives, public/persisted parity, relocated byte equality, scope, and all stop
conditions. Record each finding as `Fixed`, `Rejected` with evidence, or `Deferred` with explicit owner approval.

After a changed repair candidate has green focused verification, re-review the complete candidate. After two failed
repair rounds, preserve the worktree, transition GH-93 to `Blocked`, and obtain a separate authorized decision.

Review epoch 1 found five issues. `I13-DP-F1` through `I13-DP-F4` were fixed by spaced-path parsing, fixture-scoped
cleanup, byte-exact output checks, and positive UNC coverage. Post-repair review found `I13-DP-F5`; requiring the real
compiler `):` location terminator fixed that ordinary-prose false positive. The owner-authorized verification retry
passed 15/15 factory tests and 2/2 acceptance tests, and the independent reviewer approved the complete changed
candidate with no remaining findings.

## Final gate

Run once only after every review finding is resolved:

```powershell
dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Cli.Tests/SeqDoc.Cli.Tests.csproj -c Release; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Persistence.Tests/SeqDoc.Persistence.Tests.csproj -c Release; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter "FullyQualifiedName~CompilerDiagnosticPathConfinementTests"
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
- relocated output, IDs, or order differ within the same toolchain, normalized package set, target project/profile, and
  Program Index snapshot;
- either workspace or compiler producer path cannot reach its applicable first observable consumers;
- a required acceptance fixture or tool is unavailable;
- fixture-owned cleanup remains incomplete after bounded retries, or cleanup changes unrelated source status,
  registrations, local Git configuration, caches, outputs, or grandfathered stale metadata;
- external source or generated output would need to be committed;
- a public contract, architecture, schema, SDK, package, build, workflow, or unrelated diagnostic factory must change;
- two repair rounds fail.
