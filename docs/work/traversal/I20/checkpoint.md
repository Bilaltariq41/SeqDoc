# Issue 20 — Depthless Budget and Identity Contract

## Purpose

Establish the finite, configurable, deterministic budget contract that Issue #19 will use to remove the arbitrary
depth limit. This issue changes configuration and observability only; current traversal and generated output remain
unchanged.

## Target paths

- `src/SeqDoc.Core/Configuration/**`
- `src/SeqDoc.Configuration/ConfigurationResolution.cs`
- `src/SeqDoc.Configuration/YamlConfigurationDocument.cs`
- `src/SeqDoc.Configuration/YamlConfigurationResolver.cs`
- `src/SeqDoc.Cli/CliHost.cs`
- `tests/SeqDoc.Core.Tests/**`
- `tests/SeqDoc.Configuration.Tests/**`
- `tests/SeqDoc.Cli.Tests/**`
- `docs/work/traversal/I20/**`

## Accepted design

1. Add one immutable configuration-neutral Core budget with finite positive limits for distinct expanded methods,
   call-site expansions, material Diagram messages, participants, and rendered Mermaid characters.
2. Defaults are intentionally large but bounded: 1,024 methods, 4,096 calls, 1,024 material messages, 256
   participants, and 45,000 Mermaid characters.
3. Reuse existing YAML `diagrams.maxParticipants` and `diagrams.maxMaterialMessages`. Add
   `maxExpandedMethods`, `maxExpandedCalls`, and `maxMermaidCharacters`; do not create duplicate message/participant
   settings or reinterpret `maxFragmentDepth`.
4. Resolve every field independently with Default or ConfigurationFile provenance. Command-line budget flags are not
   added in this issue.
5. Surface the resolved values and provenance in CLI human/JSON configuration output so later diagnostics and support
   can reproduce the policy.
6. Budget values do not participate in root, graph, call-step, participant, message, or plan identities. Applying a
   larger budget later must preserve the existing accepted prefix identities.
7. Existing YAML and no-config behavior remain valid, and this contract-only issue produces byte-identical diagrams.

## Non-goals

- No removal of `DirectCallMaxDepth`, traversal-loop change, method/call counting, Diagram Plan filtering, Mermaid
  truncation, fragment-depth behavior, decomposition, persistence, or project-specific defaults.

## Risk inventory

1. Unbounded defaults defeat the safety purpose.
2. Composite resolution loses per-field provenance.
3. Duplicate legacy/new message fields become contradictory.
4. Invalid zero/negative/overflow YAML values are accepted.
5. Adding the contract changes existing identities or generated output before Issue #19.
6. CLI JSON/human output becomes nondeterministic or omits provenance.

## Existing coverage and soft budget

Configuration tests already cover strict diagrams fields, positive integers, precedence, provenance, and schema-v1
compatibility. CLI tests cover deterministic configuration JSON/human output. Scenario tests pin traversal identities
and prefixes. Add at most ten distinct claims across Core, Configuration, and CLI; use existing Scenario coverage as
the no-behavior-change boundary rather than duplicating it.

## Focused command

```powershell
dotnet build tests/SeqDoc.Configuration.Tests/SeqDoc.Configuration.Tests.csproj -c Release && dotnet build tests/SeqDoc.Cli.Tests/SeqDoc.Cli.Tests.csproj -c Release && dotnet test tests/SeqDoc.Core.Tests/SeqDoc.Core.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~DiagramBudget" && dotnet test tests/SeqDoc.Configuration.Tests/SeqDoc.Configuration.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~DiagramBudget|FullyQualifiedName~YamlConfigurationResolver" && dotnet test tests/SeqDoc.Cli.Tests/SeqDoc.Cli.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~DiagramBudget|FullyQualifiedName~Configuration"
```

## Final gate

```powershell
dotnet test tests/SeqDoc.Core.Tests/SeqDoc.Core.Tests.csproj -c Release --no-build --no-restore && dotnet test tests/SeqDoc.Configuration.Tests/SeqDoc.Configuration.Tests.csproj -c Release --no-build --no-restore && dotnet test tests/SeqDoc.Cli.Tests/SeqDoc.Cli.Tests.csproj -c Release --no-build --no-restore
```
