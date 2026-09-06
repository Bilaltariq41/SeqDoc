# Issue 13 supplied-project persistence acceptance checkpoint

## State

`Blocked`

Owner activation was granted on 2026-09-02 at
https://github.com/Bilaltariq41/SeqDoc/issues/13#issuecomment-5507318372. Start from `origin/main` at
`d09ed1c17631722d4b3e431cef8e8ea1430cf80b`; the accepted Issue #12 product baseline is
`e145f3c3a5a872ea4369142d2d652ff018a1c0d0`. GitHub Issue #13 and its readiness PASS comment are the frozen
acceptance contract.

The first required-lane run proved that the six-root config does not belong to the directly targeted Engine profile.
The superseded owner amendment https://github.com/Bilaltariq41/SeqDoc/issues/13#issuecomment-5509513784 corrected the
configured CLI target to `CreditTransferWeb/CreditTransfer.csproj`; `CreditTransferEngine/CreditTransferEngine.csproj`
and its EDMX remain the referenced EF6/EDMX producer identity. At that point, source revision, config hash, roots,
profile TFM, and semantics were unchanged. The repeated `SD4011` result is accepted red evidence; do not rerun the
known-wrong target/config pair.

This amendment is prepared against remote `main` at `25fe076ea5725ac87c19edfdac800cf281f0ec29`. The accepted clean
pinned-revision Web catalog drift evidence is recorded at owner disposition
https://github.com/Bilaltariq41/SeqDoc/issues/13#issuecomment-5511399631. The six configured roots are re-frozen in
historical root order for the Web profile, with config SHA-256
`e74e73432c03469585f50709b2b92a889225312f434d0288209a1ca49e192395`, current profile
`profile:v1:7b541674d40a3d4f5bfa8d548ae9b20bc13cb440eacb11f872cf942138fe2700`, and current Program Index fingerprint
`a9e7acae089cebae8a6dae9e0e30f818bc599b751e8122d6d37cb9e7f6bd204d`. Historical root/profile identities are comparison
evidence only, not current identity. Ahmad resumes I13 only after this amendment merges; do not rerun before that
merge and reactivation.

### Independent review disposition

- `I13A-F1` — **Fixed**: canonical GH-13 `contractRevision` was updated through `tools/governance/work_state.py` from
  the superseded comment `5509513784` to owner re-freeze disposition `5511399631`.

## Objective

Prove the accepted persistence semantics against the four pinned supplied applications through the production CLI and
first observable Markdown/Mermaid output. Classify query calls, mutation requests, `SaveChanges` calls, assignments,
persistence-request transitions, caller-visible structural results, EDMX declarations, diagnostics, and withheld
claims without changing product semantics.

Completion requires exact external revision/profile/project/config identity, evidence and certainty checks, complete
artifact manifests, budget and link validation, Mermaid rendering, credential scanning, and byte equality across two
clean runs. Compiler calls must never be strengthened into database execution, successful persistence, transaction or
commit, rows affected, stored-procedure execution, durable state, or resulting database contents.

## Target paths

Implementation and acceptance assertions:

- `tests/SeqDoc.AcceptanceTests/PersistenceExternalCorpusTests.cs` (new)

Checkpoint evidence:

- `docs/work/persistence/I13/**`

Canonical activation records:

- `docs/project/work-items/GH-13.json`
- `docs/project/execution.json`

Read-only inputs:

- `tests/SeqDoc.AcceptanceTests/BehaviorDocumentationFourFlowTests.cs`
- `tests/SeqDoc.AcceptanceTests/EntityFramework6EdmxProductionTests.cs`
- `tests/SeqDoc.AcceptanceTests/PersistenceAcceptanceTests.cs`
- `tests/SeqDoc.AcceptanceTests/ServiceClientExternalCorpusTests.cs`
- `docs/examples/credit-transfer.yaml`
- `docs/examples/fraud-management.yaml`
- `docs/examples/sms-gateway.yaml`
- the four pinned repositories under the resolved `SEQDOC_TEST_PROJECTS_ROOT`

An additional test or evidence path requires owner approval before edit. External source, checked-in example configs,
caches, build output, and generated documentation remain uncommitted.

## Non-goals

- Any edit under `src/**` or any compiler, framework, IR, Scenario Graph, planner, wording, renderer, CLI, persistence,
  configuration, or public-contract change.
- Any fixture, solution, SDK, package, workflow, or build-configuration change.
- New EF/EDMX versions, overloads, operation shapes, assignment kinds, root IDs, or application-specific matching.
- Repairing a semantic gap discovered by acceptance; record it as a separate blocked prerequisite.
- Treating historical artifact counts as required post-#11/#12 counts without producer evidence.
- Committing external source or generated acceptance artifacts.

## Frozen lanes

| Lane | Revision and target | Profile/config identity | Required boundary |
|---|---|---|---|
| CreditTransfer | `02b82a5115ef6e2d138c70670f28b959fb646f6e`; direct configured target `Provided/CreditTransfer-om/CreditTransferWeb/CreditTransfer.csproj`; referenced producer `CreditTransferEngine/CreditTransferEngine.csproj` and `DataAccess/CreditTransfer.edmx` | `Release/net9.0`; EF 6.4.4 / assembly 6.0.0.0; EDMX 2.0; `docs/examples/credit-transfer.yaml`, 6 exact roots, SHA-256 `e74e73432c03469585f50709b2b92a889225312f434d0288209a1ca49e192395`; current profile `profile:v1:7b541674d40a3d4f5bfa8d548ae9b20bc13cb440eacb11f872cf942138fe2700`; current fingerprint `a9e7acae089cebae8a6dae9e0e30f818bc599b751e8122d6d37cb9e7f6bd204d` | Exact EF6 query/mutation/save and conservative declaration-only EDMX claims; no FunctionImport execution claim. |
| FraudManagement | `7aabfef98fa4d47781bd8a98b9061ddcafb88836`; `Provided/FraudManagement/FraudManagement.sln` with `DAL/DAL.csproj` identity | `Release/net9.0`; EF Core SQL Server 9.0.11; `docs/examples/fraud-management.yaml`, 21 exact roots, SHA-256 `19c7ab43be2cfcdd80d57c3af91d907c24f4e6c221a5833aa0a2eb9144ce53c8` | Unsupported family/version behavior remains diagnostic or withheld unless already admitted exactly. |
| SMSGateway | `7ca797356b1856eb815922ca977e9d85a569cb84`; `Provided/SMSGateway-om/Source/LP.SMSGateway.WindowsHost/LP.SMSGateway.WindowsHost.csproj` with Manager project identity | `Release/net9.0`; EF Core 9.0.0; `docs/examples/sms-gateway.yaml`, 14 exact roots, SHA-256 `47443392ea3e0d5df1855f47f88c547c62ca5bbf29f94c56156ce15a7f4e0951` | Exact profile/version isolation and deterministic conservative boundaries. Use an isolated worktree; do not consume tracked build-output dirt. |
| TicketReservation | `1e25b6943a7dcfc443b8dca2ea946ee28afe811f`; `Provided/TicketReservation-Solution/TicketReservation.Api/TicketReservation.Api.csproj` | `Release/net10.0`; EF Core SQL Server 10.0.10; existing resolver, no configurable-root catalog | Preserve accepted PR #27 and #12 behavior with no runtime persistence claim. |

Historical profiles/fingerprints and artifact baselines from Issue #13 are comparison evidence, not permission to accept
drift: CreditTransfer `3f0c16b6`/`dd9b9153`, FraudManagement `f874be7e`/`f9a36fd5`, SMSGateway
`a82776da`/`f1bfaa65`, and TicketReservation `85f1a38b`/`a265118a`.

## Risk inventory

1. A lane silently analyzes the wrong checkout, project, framework, config, roots, profile, or Program Index snapshot.
2. Existing external checkout dirt or stale `.seqdoc` state contaminates results or the second-run comparison.
3. EF Core and EF6 identities cross families or unsupported versions become exact positives.
4. An EDMX declaration or FunctionImport is presented as execution.
5. Assignment, mutation, save, or result facts from incompatible methods, entities, guards, chronology, profiles, or
   snapshots are joined into a transition.
6. A compiler-evidenced request is strengthened into runtime success, commit, rows affected, or durable database state.
7. Evidence is empty, certainty is strengthened, or a diagnostic/withheld boundary disappears.
8. One runtime loop is mistaken for multiple compiler call sites.
9. A required lane is skipped, an infrastructure failure is treated as a product pass, or partial output is accepted.
10. Artifact names, links, Mermaid, character budgets, hashes, or ordering are invalid or nondeterministic.
11. Output or diagnostics expose credentials, checkout paths, timestamps, or machine-local state.
12. Acceptance pressure causes an out-of-scope production, config, fixture, external-source, or package change.

## Existing relevant coverage

- `EntityFramework6EdmxProductionTests` proves the pinned CreditTransfer producer and conservative EDMX wording through
  generated documentation.
- `BehaviorDocumentationFourFlowTests` proves the internal FourFlows producer and pinned TicketReservation flows,
  evidence, chronology, guard placement, neutral wording, and application-name isolation.
- `PersistenceAcceptanceTests` proves deterministic internal persistence wording and Mermaid, exact evidence/certainty,
  supported mutation order, guarded placement, and forbidden runtime claims.
- `ServiceClientExternalCorpusTests` provides established isolated-worktree, config-driven CLI, artifact, link, budget,
  credential, and repeated-run patterns for CreditTransfer, FraudManagement, and SMSGateway. It is read-only here; Issue
  #13 must not weaken or repurpose Issue #8 assertions.
- I21 supplied-project measurements record the accepted profile/fingerprint, artifact, message, and character baselines.

## Test Writer assignment and soft budget

A Test Writer is required because this checkpoint adds acceptance-critical external-corpus assertions, exact identity
and false-positive boundaries, evidence/certainty checks, deterministic output, and a concrete four-application
regression signature.

Uncovered risks are the twelve items above. Add approximately 4 to 8 distinct tests in
`tests/SeqDoc.AcceptanceTests/PersistenceExternalCorpusTests.cs`, grouped by shared expensive lane fixture where useful.
Each required lane must have a completion assertion at generated Markdown/Mermaid or diagnostic/withheld output, not
only intermediate facts. Reuse patterns from the read-only coverage; do not duplicate generic CLI parsing, service
client claims, or internal semantic unit tests. Do not change production, existing tests, configs, or external source.

Focused Test Writer command:

```powershell
$env:SEQDOC_TEST_PROJECTS_ROOT = (Resolve-Path "../SeqDoc-TestProjects").Path; dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter "FullyQualifiedName~PersistenceExternalCorpus"
```

## Focused implementation command

```powershell
$env:SEQDOC_TEST_PROJECTS_ROOT = (Resolve-Path "../SeqDoc-TestProjects").Path; dotnet build "$env:SEQDOC_TEST_PROJECTS_ROOT/Provided/CreditTransfer-om/CreditTransferWeb/CreditTransfer.csproj" -c Release --no-restore; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter "FullyQualifiedName~PersistenceExternalCorpus|FullyQualifiedName~EntityFramework6EdmxProductionTests|FullyQualifiedName~BehaviorDocumentationFourFlow|FullyQualifiedName~PersistenceAcceptanceTests"
```

Record nonzero discovery and exact counts. A missing corpus, compile/restore failure, wrong revision, stale identity,
required-lane skip, or zero discovery is not an accepted pass.

## Review boundary

Implementation stops at `ReviewRequired`. The Orchestrator inspects the complete diff and generated artifacts, then
invokes one independent Reviewer. Record every finding as Fixed, Rejected with evidence, or Deferred with explicit owner
approval. Run the final gate only after findings are resolved. After two failed repair reruns, preserve the worktree,
mark the checkpoint `Blocked`, and stop.

## Final gate

Run once after review resolution:

```powershell
$env:SEQDOC_TEST_PROJECTS_ROOT = (Resolve-Path "../SeqDoc-TestProjects").Path; dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release
```

The `PersistenceExternalCorpusTests` lane must analyze all four exact revisions using fresh cache/output locations,
inspect every generated Markdown/Mermaid/diagnostic artifact, render every `.mmd` with the recorded Mermaid CLI version,
resolve every Markdown link, enforce configured character budgets, scan for credentials and forbidden runtime claims,
and prove byte equality across two clean runs. CreditTransfer and TicketReservation are required. FraudManagement and
SMSGateway must run or fail closed deterministically as specified by Issue #13; an unavailable required lane, changed
identity, stale output, or unexplained artifact change blocks closure.
