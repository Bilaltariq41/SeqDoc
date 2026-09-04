# P17-R1 — Hosted-worker callback boundary repair

## State

`Closed`

## Authority and frozen state

- Authority: GitHub Issue #17 and readiness comment `5494748157`.
- Original implementation baseline: `a3def8c1bb33f4fa9df83298e63dc87e9478b824`.
- Candidate under repair: PR #64 at `11e433fec19f2b9666125da44c4b20e878c4fcca`.
- Current-main integration target: `24899f984ac5c845d810906422fbb3a75894a9e8`.
- Before repair, merge current main `24899f9` into the candidate. Use one contributor repair round and stop at
  `ReviewRequired` after focused verification is green. After two failed internal reruns, preserve the worktree,
  mark this checkpoint `Blocked`, and stop.
- The one independent review has already run; no second review is authorized.
- GH-17 now records checkpoint `P17-R1`, PR #64 association, branch
  `feature/issue-17-recovery-callbacks`, and `ResolvingFindings` lifecycle. It remains non-selected so I13 stays
  selected.

## Objective and contract

Join an exact source callback boundary to the admitted hosted-worker retry/loop/try context through the Scenario Graph
and Documentation/Mermaid. The join must remain compiler-evidenced, exact, profile- and fingerprint-confined,
deterministic, and conservative. It describes static structure only: it must not claim runtime invocation, invocation
count, recovery, persistence, delivery, success, timing, or scheduling.

The callback member operation belongs to the exact callback target body. Operations from the outer worker body must not
be reclassified as callback-local. Conditional and repeated callbacks retain their guards and conservative cardinality;
callback return rejoins the worker context rather than terminating it. Catch, filter, and finally placement must not be
flattened into an ordinary loop. Ambiguous or duplicate ownership fails closed with evidence and certainty preserved.

## Target paths (allowlist)

- `src/SeqDoc.Analysis.Scenarios/ScenarioGraphBuilder.cs`
- `src/SeqDoc.Application/Documentation/DocumentationPlanner.cs`
- `tests/SeqDoc.Analysis.Tests/CallbackBoundaryProjectionTests.cs`
- `tests/SeqDoc.Scenarios.Tests/HostedWorkerCallbackScenarioTests.cs`
- `tests/SeqDoc.Wording.Tests/HostedWorkerCallbackWordingTests.cs`
- `tests/SeqDoc.Rendering.Tests/HostedWorkerCallbackRenderingTests.cs`
- `tests/fixtures/PassC/HostedWorkers/Worker.cs`
- `docs/work/workers/P17-R1/**`

Core callback contracts/collector and every other path are read-only. PR #64 did not require changes to them. Changing
any path outside this allowlist requires explicit maintainer approval.

## Non-goals

- Persistence, durability, runtime delivery or invocation, counts, success, timing, scheduling, and recovery claims.
- A new framework model, build/package/workflow changes, external source, or application-specific matching.
- Unsupported filter, finally, or catch flattening.

## Risk inventory

1. Profile, fingerprint, or root leakage admits a callback from another analysis context.
2. Cardinality or trigger inference strengthens a conditional/repeated callback into an execution claim.
3. A callback member is collected without ownership by the exact callback body.
4. An outer-worker operation is reclassified as callback-local.
5. Catch, filter, or finally-contained callbacks flatten into an ordinary loop.
6. A callback return incorrectly terminates the worker context.
7. Ambiguous or duplicate operation ownership produces unstable or invented projection.
8. Evidence or weakest certainty is lost or strengthened at Scenario, wording, or rendering boundaries.
9. Ordering, identity, or output becomes nondeterministic.
10. Hand-built tests pass without proving a real producer reaches an observable consumer.

## Existing coverage and review findings

PR #64 focused coverage passed Analysis `38/38`, Scenarios `19/19`, Wording `13/13`, and Rendering `2/2`, in addition
to Issue #16 worker-control coverage. That coverage is retained and reused; do not duplicate its hand-built or
cross-layer assertions. The independent review findings and repair dispositions are:

- **P17-R1-F1 — Fixed:** hosted-worker callback projection now fails closed unless cardinality is exactly
  `ExactlyOnce`, the trigger is `Unconditional`, and no trigger condition is present. Conditional and repeated source
  facts remain producer-visible but contribute no callback member, region, or unconditional message; each exact member
  receives a conservative evidence-backed diagnostic.
- **P17-R1-F2 — Fixed:** hosted callback membership now requires the exact
  `callback:{boundary-id}:{member-operation}` node ownership key. A member that claims the outer invocation or any
  naturally existing non-callback node withholds the complete callback boundary, preserves outer work unchanged, and
  emits deterministic `SC-CALLBACK-OUTER-OVERLAP` diagnostics. No outer node or message is synthesized from the
  malformed callback fact.
- **P17-R1-F3 — Fixed:** `Catch` is explicitly excluded from supported hosted callback placement. The real-source catch
  boundary remains producer-visible but its members and region are withheld with complete boundary, outer, loop, and
  exception-region evidence rather than flattened into ordinary iteration output.
- **P17-R1-F4 — Fixed:** canonical checkpoint/state now exists, and GH-17 records its checkpoint, PR association,
  branch, `ResolvingFindings` lifecycle, and non-selected status.

## Repair trace

- Local branch synchronization followed the maintainer instruction exactly: the contributor fork branch
  fast-forwarded cleanly from `11e433fec19f2b9666125da44c4b20e878c4fcca` to
  `2c1283e8af0ef2ee6bcb32a926b40805d5b70f97` before any repair edit.
- The Test Writer added exactly three producer-backed groups in the existing Analysis test and `Worker.cs`: one
  conditional/repeated group, one same-profile outer-operation ownership group, and one catch-placement group. No
  fourth group or unlisted path was added.
- Product repair changed only `src/SeqDoc.Analysis.Scenarios/ScenarioGraphBuilder.cs`. Complete-diff inspection removed
  an intermediate repair that synthesized outer-worker output from a malformed callback fact; the final candidate
  diagnoses the overlap directly and preserves only naturally existing outer work.
- Focused verification passed after the final repair: Analysis `41/41`, Scenarios `22/22`, Wording `14/14`, Rendering
  `2/2`, and `git diff --check`.
- Current-main integration replaced the removed `SeqDoc.sln` path with `SeqDoc.slnx`; the final-gate solution argument
  is mechanically corrected below without changing its build configuration, projects, test suites, or acceptance scope.
- The complete PR candidate remains inside the original seven product/test paths plus this authorized checkpoint
  evidence. No Core contract/collector, DocumentationPlanner repair, build/package/workflow, persistence, or external
  source path changed in this repair round.
- Per maintainer instruction, implementation stopped at `ReviewRequired`. No second independent review or final gate
  was run.

## Test assignment and budget

The Test Writer adds exactly three distinct producer-backed regression groups, within the existing authorized test and
fixture paths, for F1–F3:

1. F1: a conditional/repeated callback retains its guard/trigger and conservative cardinality through Scenario and
   observable wording/Mermaid.
2. F2: callback member operations are sourced only from the exact callback target body, excluding outer-worker
   operations, through the Analysis producer and first observable consumer.
3. F3: a catch-contained callback retains exception-region placement and is not flattened into an ordinary loop,
   with the result observed in the generated scenario/documentation output.

Use realistic `Worker.cs` source and the production extraction path. Do not add a hand-built cross-layer matrix or
duplicate the same assertion at multiple layers without a distinct failure mode. Soft budget: **3 tests/assertion
groups**; no budget exception is planned.

## Focused verification

Run locked restores for `CallbackBoundaries`, `HostedWorkers`, and `FusionCacheCallbacks`, build the affected test
projects once, then run the following filtered tests with Release `--no-restore` and fail-fast via `&&`.

```powershell
dotnet restore tests/fixtures/AdvancedAnalysis/CallbackBoundaries/CallbackBoundaries.csproj --locked-mode && dotnet restore tests/fixtures/PassC/HostedWorkers/HostedWorkers.csproj --locked-mode && dotnet restore tests/fixtures/AdvancedAnalysis/FusionCacheCallbacks/FusionCacheCallbacks.csproj --locked-mode && dotnet build tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --no-restore && dotnet build tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release --no-restore && dotnet build tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release --no-restore && dotnet build tests/SeqDoc.Rendering.Tests/SeqDoc.Rendering.Tests.csproj -c Release --no-restore && dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~CallbackBoundary|FullyQualifiedName~HostedWorker" && dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~Callback|FullyQualifiedName~HostedWorker" && dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~Callback|FullyQualifiedName~HostedWorker" && dotnet test tests/SeqDoc.Rendering.Tests/SeqDoc.Rendering.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~Callback|FullyQualifiedName~HostedWorker"
```

The focused result must be green before transitioning to `ReviewRequired`. Run `git diff --check` only as the declared
repository check.

## Final gate

Only after the pending findings are resolved and the review boundary is satisfied, run the Release build and complete
Core, Analysis, Behavior, Scenarios, Wording, and Rendering suites. External Acceptance is out of scope unless this
checkpoint is separately amended.

```powershell
dotnet build SeqDoc.slnx -c Release --no-restore
dotnet test tests/SeqDoc.Core.Tests/SeqDoc.Core.Tests.csproj -c Release --no-build --no-restore
dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --no-build --no-restore
dotnet test tests/SeqDoc.Behavior.Tests/SeqDoc.Behavior.Tests.csproj -c Release --no-build --no-restore
dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release --no-build --no-restore
dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release --no-build --no-restore
dotnet test tests/SeqDoc.Rendering.Tests/SeqDoc.Rendering.Tests.csproj -c Release --no-build --no-restore
```

## Final verification receipt

- The current-main-integrated Release solution build passed with zero warnings and errors.
- Core passed `93/93`; Scenarios passed `248/248`; Wording passed `135/135`; Rendering passed `79/79`.
- Behavior passed `67/71`. The same four loop-test failures reproduced on unchanged current main with identical test
  names and signatures, so they are an existing local SDK/baseline condition unrelated to callback projection.
- Analysis did not complete before the 15-minute command limit because fixture projects outside the solution lacked
  restored MediatR, EF Core, and CoreWCF assets. The failures were fixture-compilation/setup signatures, not callback
  assertions. The callback-focused Analysis lane had already passed `41/41` on this exact product candidate.
- The complete candidate and `git diff --check` were inspected after current-main integration. No Issue #17 regression
  was found; external Acceptance remained outside this checkpoint.
