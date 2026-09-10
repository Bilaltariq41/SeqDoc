# Issue 87 BD2020 local-withhold classification checkpoint

## State

`Building`

## Authority and frozen state

- **Authority**: GitHub Issue #87.
- **Baseline**: `8f26c1fe21deec75c9bfd83ecb16cbd676ffc0d3` on `origin/main`.
- **Target branch**: `fix/issue-87-bd2020`.
- **Owner**: Ahmad, agreed by the matching bounded takeover and owner decisions below.
- **Blocked consumer**: Issue #18. Issue #13 remains blocked by its separately frozen dependency set.
- **Decision boundary**: This evidence/certainty and activation change requires two non-author peer decisions before `Ready`.
- **Frozen scope**: https://github.com/Bilaltariq41/SeqDoc/issues/87#issuecomment-5558523794.
- **Abood receipt**: https://github.com/Bilaltariq41/SeqDoc/issues/87#issuecomment-5558609657.
- **Qais receipt**: https://github.com/Bilaltariq41/SeqDoc/issues/87#issuecomment-5575040102.
- **Abood takeover/owner receipt**: https://github.com/Bilaltariq41/SeqDoc/issues/87#issuecomment-5576067024.
- **Qais takeover/owner receipt**: https://github.com/Bilaltariq41/SeqDoc/issues/87#issuecomment-5584549024.

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

After focused tests pass and before `ReviewRequired`, run exactly. This amended observable is authorized by the
matching decisions from [Qais](https://github.com/Bilaltariq41/SeqDoc/issues/87#issuecomment-5603914756) and
[Abood](https://github.com/Bilaltariq41/SeqDoc/issues/87#issuecomment-5617767409):

```powershell
$ErrorActionPreference = "Stop"
$seqdocRoot = (Resolve-Path ".").Path
$corpusRoot = if ($env:SEQDOC_TEST_PROJECTS_ROOT) {
    (Resolve-Path $env:SEQDOC_TEST_PROJECTS_ROOT).Path
} else {
    (Resolve-Path (Join-Path $seqdocRoot "..\SeqDoc-TestProjects")).Path
}
$sourceRepository = Join-Path $corpusRoot "Provided\SMSGateway-om"
$cli = Join-Path $seqdocRoot "src\SeqDoc.Cli\bin\Release\net10.0\SeqDoc.Cli.dll"
$sourceStatusBefore = (& git -C $sourceRepository status --porcelain=v1 --untracked-files=all) -join "`n"
if ($LASTEXITCODE -ne 0) { throw "Failed to capture source repository status." }
$worktreeListBefore = (& git -C $sourceRepository worktree list --porcelain) -join "`n"
if ($LASTEXITCODE -ne 0) { throw "Failed to capture source worktree registrations." }
$localConfigBefore = (& git -C $sourceRepository config --local --list --show-origin) -join "`n"
if ($LASTEXITCODE -ne 0) { throw "Failed to capture source local configuration." }
$bodyError = $null
$cleanupError = $null
$results = @()
try {
    dotnet build (Join-Path $seqdocRoot "src\SeqDoc.Cli\SeqDoc.Cli.csproj") -c Release
    if ($LASTEXITCODE -ne 0) { throw "SeqDoc CLI build failed." }

    foreach ($run in 1..2) {
        $runRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("seqdoc-gh87-run$run-" + [guid]::NewGuid().ToString("N"))
        $worktree = Join-Path $runRoot "source"
        $cache = Join-Path $runRoot "cache.db"
        $output = Join-Path $runRoot "output"
        $project = Join-Path $worktree "Source\LP.SMSGateway.Manager\LP.SMSGateway.Manager.csproj"
        if (Test-Path -LiteralPath $worktree) { throw "The disposable worktree path already exists." }
        New-Item -ItemType Directory -Path $runRoot | Out-Null
        $runError = $null
        try {
            git -C $sourceRepository worktree add --detach $worktree 7ca797356b1856eb815922ca977e9d85a569cb84
            if ($LASTEXITCODE -ne 0) { throw "Failed to create the pinned SMSGateway worktree." }
            if ((git -C $worktree rev-parse HEAD).Trim() -ne "7ca797356b1856eb815922ca977e9d85a569cb84") {
                throw "The SMSGateway revision is not pinned."
            }

            dotnet restore $project
            if ($LASTEXITCODE -ne 0) { throw "SMSGateway restore failed." }
            dotnet build $project -c Release -f net9.0 --no-restore
            if ($LASTEXITCODE -ne 0) { throw "SMSGateway build failed." }

            $json = (& dotnet $cli analyze $project --repository-root $worktree --configuration Release --framework net9.0 --cache $cache --output $output --json) -join "`n"
            $result = $json | ConvertFrom-Json
            if (@($result.data.runs).Count -ne 1) { throw "Expected one active profile." }
            if ([string]::IsNullOrWhiteSpace($result.data.runs[0].profileId)) { throw "Missing active profile ID." }
            if ($result.data.runs[0].indexFingerprint.Length -ne 64) { throw "Missing Program Index fingerprint." }
            if ($result.outcome -eq "AnalysisFailure") { throw "BD2020 still caused an analysis failure." }
            $bd2020 = @($result.diagnostics | Where-Object { $_.code -eq "BD2020" })
            if ($bd2020.Count -ne 1) { throw "Expected exactly one retained BD2020 warning." }
            if ($bd2020[0].severity -ne "Warning" -or $bd2020[0].stage -ne "BaselineIndex") {
                throw "BD2020 severity or stage changed."
            }
            $blocking = @($result.diagnostics | Where-Object { $_.severity -eq "Error" })
            if ($blocking.Count -ne 1 -or $blocking[0].code -ne "SD4008") {
                throw "A blocking diagnostic other than the accepted root-less SD4008 remains."
            }
            $results += [pscustomobject]@{
                ProfileId = $result.data.runs[0].profileId
                IndexFingerprint = $result.data.runs[0].indexFingerprint
                Bd2020Record = ($bd2020[0] | ConvertTo-Json -Compress -Depth 20)
            }
        } catch {
            $runError = $_
        } finally {
            if (Test-Path -LiteralPath $worktree) {
                git -C $sourceRepository worktree remove --force $worktree
                if ($LASTEXITCODE -ne 0) { $cleanupError = "Failed to remove the disposable worktree registration." }
            }
            if (Test-Path -LiteralPath $worktree) {
                $cleanupError = "The disposable worktree directory remains after cleanup."
            }
            if (-not (Test-Path -LiteralPath $worktree) -and (Test-Path -LiteralPath $runRoot)) {
                try { Remove-Item -LiteralPath $runRoot -Recurse -Force -ErrorAction Stop }
                catch { $cleanupError = "Failed to remove the disposable run directory." }
            }
        }
        if ($cleanupError) { throw $cleanupError }
        if ($runError) { throw $runError }
    }

    if ($results.Count -ne 2) { throw "Expected two completed clean runs." }
    if ($results[0].ProfileId -cne $results[1].ProfileId) { throw "Profile ID changed between runs." }
    if ($results[0].IndexFingerprint -cne $results[1].IndexFingerprint) { throw "Program Index fingerprint changed between runs." }
    if ($results[0].Bd2020Record -cne $results[1].Bd2020Record) { throw "BD2020 record changed between runs." }
    "SMSGateway verification passed: profile=$($results[0].ProfileId); fingerprint=$($results[0].IndexFingerprint); BD2020=$($results[0].Bd2020Record)"
} catch {
    $bodyError = $_
} finally {
    $sourceStatusAfter = (& git -C $sourceRepository status --porcelain=v1 --untracked-files=all) -join "`n"
    if ($LASTEXITCODE -ne 0) { $cleanupError = "Failed to verify source repository status after cleanup." }
    $worktreeListAfter = (& git -C $sourceRepository worktree list --porcelain) -join "`n"
    if ($LASTEXITCODE -ne 0) { $cleanupError = "Failed to verify source worktree registrations after cleanup." }
    $localConfigAfter = (& git -C $sourceRepository config --local --list --show-origin) -join "`n"
    if ($LASTEXITCODE -ne 0) { $cleanupError = "Failed to verify source local configuration after cleanup." }
    if ($sourceStatusAfter -cne $sourceStatusBefore) { $cleanupError = "Source repository status changed." }
    if ($worktreeListAfter -cne $worktreeListBefore) { $cleanupError = "Source worktree registrations changed." }
    if ($localConfigAfter -cne $localConfigBefore) { $cleanupError = "Source local configuration changed." }
}
if ($cleanupError) { throw $cleanupError }
if ($bodyError) { throw $bodyError }
```

Each unique run root provides a clean checkout, cache, and output directory. The producer-shaped focused test proves
retained evidence, least-confident certainty, and the absence of an invented catch continuation because CLI JSON does
not expose the complete internal evidence collection. The external command proves that behavior activation progresses
past `BD2020`, retains the warning on the exact supplied target, reproduces the profile, fingerprint, and complete
`BD2020` record, and leaves only root-less `SD4008` as a blocking diagnostic. Overall `Succeeded` and
`DocumentationGenerationFailure` are not pass/fail criteria for this lane. It is external evidence, not permission to
edit or commit supplied source.

Amended external verification evidence, 2026-09-10:

- the authorized two-run command completed once with two clean disposable worktrees;
- both runs produced profile
  `profile:v1:b61dd23590917d06569d12aa87f5bdea045499225c2cf2022c206b115b119dcc` and Program Index fingerprint
  `2358f330cc245f6ca5217dee9aef036baf0bde6e57f09a7db6b4bf53f9a61563`;
- both runs retained the same `BD2020` record,
  `diagnostic:v1:4e6f1cc40c7e167dfb59e1b5d50b81b56a8287b3e3a3bb30d81a75485f70a7b4`, as an `Exact` `Warning` at
  `BaselineIndex` with summary `A catch continuation mapping is ambiguous and was withheld.`;
- neither run produced `AnalysisFailure`; the only blocking diagnostic was the accepted root-less `SD4008`;
- shared source status, worktree registrations, and local Git configuration were byte-equal before and after, and both
  disposable worktree and run roots were removed.

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

Stop if the repair requires a path outside the allowlist, if `BD2020` does not come from the local `continue` withholding
path, if SMSGateway still fails because of `BD2020`, if any blocking diagnostic other than root-less `SD4008` remains,
if either clean run changes the profile, Program Index fingerprint, or `BD2020` record, if cleanup changes shared source
status, worktree registrations, or local Git configuration, or if any structural/unknown diagnostic becomes non-blocking.
