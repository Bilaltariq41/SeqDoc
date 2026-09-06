# Issue 87 BD2020 local-withhold classification checkpoint

## State

`Blocked`

## Authority and frozen state

- **Authority**: GitHub Issue #87.
- **Baseline**: `8f26c1fe21deec75c9bfd83ecb16cbd676ffc0d3` on `origin/main`.
- **Target branch**: `fix/issue-87-bd2020`.
- **Owner**: Bilal.
- **Blocked consumer**: Issue #18. Issue #13 remains blocked by its separately frozen dependency set.
- **Decision boundary**: This evidence/certainty and activation change requires two non-author peer decisions before `Ready`.

## Objective and contract

Keep `BD2020` as a conservative, local-withhold warning. `MethodFlowBuilder` already withholds only the ambiguous catch continuation and returns the method-flow snapshot. Before `BehaviorAnalyzer` permits activation, the producer must attach deterministic contributing evidence and the least-confident contributing certainty.

The repair changes producer evidence/certainty and diagnostic classification only. It must not infer a catch continuation, strengthen placement, or weaken structural and unknown-diagnostic failures.

## Target paths

- `src/SeqDoc.Analysis.Behavior/MethodFlowBuilder.cs`
- `src/SeqDoc.Analysis.Behavior/BehaviorAnalyzer.cs`
- `tests/SeqDoc.Behavior.Tests/BehaviorAnalyzerTests.cs`

Every other product, test, fixture, configuration, build, workflow, and governance path is read-only during implementation.

## Non-goals

- No change to `MethodFlowBuilder` catch-continuation admission or topology beyond diagnostic evidence/certainty projection.
- No compiler extraction, Core/IR, Scenario Graph, wording, rendering, CLI, persistence, or external-source change.
- No new accepted diagnostic code except `BD2020`.
- No weakening of `BD1xxx`, `BD2004`, `BD2012`, unknown future `BD` codes, or other structural invariants.
- No incidental repair of the broader SMSGateway warning set.

## Risk inventory

1. `BD2020` remains blocking and prevents activation of an otherwise usable profile.
2. The repair accidentally admits unknown or structural diagnostics.
3. The warning has empty evidence or stronger certainty than one of its contributing facts.
4. A test injects `BD2020` directly and fails to prove the real `MethodFlowBuilder` producer path.
5. Classification is mistaken for proof of the withheld catch continuation.
6. A failed candidate overwrites the previous valid state.

## Existing coverage

- `BehaviorAnalyzerTests.AnalyzeAsyncAllowsOnlyTheRatifiedWithholdDiagnostics` freezes the current non-blocking set and retains diagnostic identity, evidence, certainty, and order.
- `BehaviorAnalyzerTests.AnalyzeAsyncTreatsUnknownFutureBehaviorDiagnosticsAsBlocking` protects the fail-closed default.
- `BehaviorAnalyzerTests.AnalyzeAsyncCompletesWhenFlowBuildingWithholdsANaturalLoop` proves the equivalent `BD2011` local-withhold behavior and deterministic fingerprint.
- `HostedWorkerProductionProjectionTests` proves the supported, unambiguous catch-to-loop continuation and rejects unexpected `BD2020` on that shape.
- The clean SMSGateway Manager production command at revision `7ca797356b1856eb815922ca977e9d85a569cb84` reproduces the current whole-profile `AnalysisFailure` with `BD2020`.

## Test Writer assignment and soft budget

Dispatch the Test Writer because this repair changes activation behavior, evidence retention, and a concrete compiler-derived regression signature.

Add 2–4 distinct claims in `tests/SeqDoc.Behavior.Tests/BehaviorAnalyzerTests.cs`:

- A producer-shaped extracted body makes `MethodFlowBuilder` emit `BD2020`; do not inject the diagnostic directly as the only proof.
- The warning carries deterministic contributing evidence and the least-confident contributing certainty.
- `BehaviorAnalyzer` succeeds, retains the method flow and warning, and computes a deterministic fingerprint.
- Existing unknown and structural diagnostics remain blocking.

Do not add fixtures, acceptance tests, or duplicate downstream assertions.

## Focused implementation command

```powershell
dotnet test tests/SeqDoc.Behavior.Tests/SeqDoc.Behavior.Tests.csproj -c Release --filter "FullyQualifiedName~BehaviorAnalyzerTests"
```

## Supplementary external verification

After focused tests pass and before `ReviewRequired`, run exactly:

```powershell
$root = <clean checkout of SMSGateway revision 7ca797356b1856eb815922ca977e9d85a569cb84>
dotnet restore "$root/Source/LP.SMSGateway.Manager/LP.SMSGateway.Manager.csproj"
dotnet build "$root/Source/LP.SMSGateway.Manager/LP.SMSGateway.Manager.csproj" -c Release -f net9.0 --no-restore
dotnet src/SeqDoc.Cli/bin/Release/net10.0/SeqDoc.Cli.dll analyze "$root/Source/LP.SMSGateway.Manager/LP.SMSGateway.Manager.csproj" --repository-root "$root" --configuration Release --framework net9.0 --cache "$env:TEMP/seqdoc-bd2020.db" --output "$env:TEMP/seqdoc-bd2020-output" --json
```

Use a clean cache and output directory. Require `Succeeded`, a retained evidence-backed `BD2020` warning, a non-null active profile, and no catch-continuation claim for the ambiguous mapping. This is external evidence, not permission to edit or commit supplied source.

## Review boundary

Stop implementation at `ReviewRequired`. The independent reviewer must inspect the complete candidate from the frozen baseline, confirm that `BD2020` is locally withheld by the producer, and confirm that all other fail-closed classifications remain unchanged. Record every finding as Fixed, Rejected with evidence, or Deferred with explicit owner approval.

## Final gate

Run once after all review findings are resolved:

```powershell
dotnet test tests/SeqDoc.Behavior.Tests/SeqDoc.Behavior.Tests.csproj -c Release
```

## Stop conditions

Stop if the repair requires a path outside the allowlist, if `BD2020` does not come from the local `continue` withholding path, if SMSGateway still fails because of `BD2020`, or if any structural/unknown diagnostic becomes non-blocking.
