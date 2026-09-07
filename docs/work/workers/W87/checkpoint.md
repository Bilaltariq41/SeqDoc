# Issue 87 BD2020 local-withhold classification checkpoint

## State

`NotStarted`

## Authority and frozen state

- **Authority**: GitHub Issue #87.
- **Baseline**: `8f26c1fe21deec75c9bfd83ecb16cbd676ffc0d3` on `origin/main`.
- **Target branch**: `fix/issue-87-bd2020`.
- **Owner**: Bilal, confirmed by the explicit owner authorization for the exact PR #88 governance repair.
- **Blocked consumer**: Issue #18. Issue #13 remains blocked by its separately frozen dependency set.
- **Decision boundary**: This evidence/certainty and activation change requires two non-author peer decisions before `Ready`.
- **Frozen scope**: https://github.com/Bilaltariq41/SeqDoc/issues/87#issuecomment-5558523794.
- **Abood receipt**: https://github.com/Bilaltariq41/SeqDoc/issues/87#issuecomment-5558609657.
- **Qais receipt**: https://github.com/Bilaltariq41/SeqDoc/issues/87#issuecomment-5575040102.

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
- The ambiguous mapping emits no `CatchContinuations` entry; retaining `BD2020` must never admit or invent placement.
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
$ErrorActionPreference = "Stop"
$seqdocRoot = (Resolve-Path ".").Path
$corpusRoot = if ($env:SEQDOC_TEST_PROJECTS_ROOT) {
    (Resolve-Path $env:SEQDOC_TEST_PROJECTS_ROOT).Path
} else {
    (Resolve-Path (Join-Path $seqdocRoot "..\SeqDoc-TestProjects")).Path
}
$sourceRepository = Join-Path $corpusRoot "Provided\SMSGateway-om"
$runRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("seqdoc-gh87-" + [guid]::NewGuid().ToString("N"))
$worktree = Join-Path $runRoot "source"
$cache = Join-Path $runRoot "cache.db"
$output = Join-Path $runRoot "output"
$project = Join-Path $worktree "Source\LP.SMSGateway.Manager\LP.SMSGateway.Manager.csproj"
$cli = Join-Path $seqdocRoot "src\SeqDoc.Cli\bin\Release\net10.0\SeqDoc.Cli.dll"
$sourceStatusBefore = (& git -C $sourceRepository status --porcelain=v1 --untracked-files=all) -join "`n"
if ($LASTEXITCODE -ne 0) { throw "Failed to capture source repository status." }
$worktreeListBefore = (& git -C $sourceRepository worktree list --porcelain) -join "`n"
if ($LASTEXITCODE -ne 0) { throw "Failed to capture source worktree registrations." }
$localConfigBefore = (& git -C $sourceRepository config --local --list --show-origin) -join "`n"
if ($LASTEXITCODE -ne 0) { throw "Failed to capture source local configuration." }
if (Test-Path -LiteralPath $worktree) { throw "The disposable worktree path already exists." }

New-Item -ItemType Directory -Path $runRoot | Out-Null
$bodyError = $null
$cleanupError = $null
try {
    git -C $sourceRepository worktree add --detach $worktree 7ca797356b1856eb815922ca977e9d85a569cb84
    if ($LASTEXITCODE -ne 0) { throw "Failed to create the pinned SMSGateway worktree." }
    if ((git -C $worktree rev-parse HEAD).Trim() -ne "7ca797356b1856eb815922ca977e9d85a569cb84") {
        throw "The SMSGateway revision is not pinned."
    }

    dotnet build (Join-Path $seqdocRoot "src\SeqDoc.Cli\SeqDoc.Cli.csproj") -c Release
    if ($LASTEXITCODE -ne 0) { throw "SeqDoc CLI build failed." }
    dotnet restore $project
    if ($LASTEXITCODE -ne 0) { throw "SMSGateway restore failed." }
    dotnet build $project -c Release -f net9.0 --no-restore
    if ($LASTEXITCODE -ne 0) { throw "SMSGateway build failed." }

    $json = (& dotnet $cli analyze $project --repository-root $worktree --configuration Release --framework net9.0 --cache $cache --output $output --json) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "SeqDoc analysis failed: $json" }
    $result = $json | ConvertFrom-Json
    if ($result.outcome -ne "Succeeded") { throw "Expected Succeeded, got $($result.outcome)." }
    if (@($result.data.runs).Count -ne 1) { throw "Expected one active profile." }
    if ([string]::IsNullOrWhiteSpace($result.data.runs[0].profileId)) { throw "Missing active profile ID." }
    if ($result.data.runs[0].indexFingerprint.Length -ne 64) { throw "Missing Program Index fingerprint." }
    $bd2020 = @($result.diagnostics | Where-Object { $_.code -eq "BD2020" })
    if ($bd2020.Count -lt 1) { throw "The retained BD2020 warning is missing." }
    if (@($bd2020 | Where-Object { $_.severity -ne "Warning" -or $_.stage -ne "BaselineIndex" }).Count -ne 0) {
        throw "BD2020 severity or stage changed."
    }
} catch {
    $bodyError = $_
} finally {
    if (Test-Path -LiteralPath $worktree) {
        git -C $sourceRepository worktree remove --force $worktree
        if ($LASTEXITCODE -ne 0) { $cleanupError = "Failed to remove the disposable worktree registration." }
    }
    if (Test-Path -LiteralPath $worktree) {
        $cleanupError = "The disposable worktree directory remains after cleanup."
    }

    $sourceStatusAfter = (& git -C $sourceRepository status --porcelain=v1 --untracked-files=all) -join "`n"
    if ($LASTEXITCODE -ne 0) { $cleanupError = "Failed to verify source repository status after cleanup." }
    $worktreeListAfter = (& git -C $sourceRepository worktree list --porcelain) -join "`n"
    if ($LASTEXITCODE -ne 0) { $cleanupError = "Failed to verify source worktree registrations after cleanup." }
    $localConfigAfter = (& git -C $sourceRepository config --local --list --show-origin) -join "`n"
    if ($LASTEXITCODE -ne 0) { $cleanupError = "Failed to verify source local configuration after cleanup." }
    if ($sourceStatusAfter -cne $sourceStatusBefore) { $cleanupError = "Source repository status changed." }
    if ($worktreeListAfter -cne $worktreeListBefore) { $cleanupError = "Source worktree registrations changed." }
    if ($localConfigAfter -cne $localConfigBefore) { $cleanupError = "Source local configuration changed." }

    if (-not (Test-Path -LiteralPath $worktree) -and (Test-Path -LiteralPath $runRoot)) {
        try { Remove-Item -LiteralPath $runRoot -Recurse -Force -ErrorAction Stop }
        catch { $cleanupError = "Failed to remove the disposable run directory." }
    }
}
if ($cleanupError) { throw $cleanupError }
if ($bodyError) { throw $bodyError }
```

The unique run root provides a clean cache and output directory. The producer-shaped focused test proves retained evidence, least-confident certainty, and the absence of an invented catch continuation because CLI JSON does not expose the complete internal evidence collection. This external command proves successful activation, a fresh profile/fingerprint, and retention of the warning on the exact supplied target. It is external evidence, not permission to edit or commit supplied source.

## Review boundary

Stop implementation at `ReviewRequired`. The independent reviewer must inspect the complete candidate from the frozen baseline, confirm that `BD2020` is locally withheld by the producer, and confirm that all other fail-closed classifications remain unchanged. Record every finding as Fixed, Rejected with evidence, or Deferred with explicit owner approval.

The prospective review epochs are mandatory:

1. Readiness/specification review must pass before `Ready`.
2. After focused verification and the external check pass, stop at `ReviewRequired` and request complete-candidate review pinned to the exact implementation SHA.
3. If findings change the candidate, rerun focused verification before requesting post-repair review pinned to the new SHA.
4. Permit at most two implementation repair rounds. After two failed rounds, set the checkpoint to `Blocked`, preserve the worktree, and obtain a separately authorized split, transfer, or takeover decision.
5. Run the final gate once, only after every finding is Fixed, Rejected with evidence, or Deferred with explicit owner approval.

## Final gate

Run once after all review findings are resolved:

```powershell
dotnet test tests/SeqDoc.Behavior.Tests/SeqDoc.Behavior.Tests.csproj -c Release
```

## Stop conditions

Stop if the repair requires a path outside the allowlist, if `BD2020` does not come from the local `continue` withholding path, if SMSGateway still fails because of `BD2020`, or if any structural/unknown diagnostic becomes non-blocking.
