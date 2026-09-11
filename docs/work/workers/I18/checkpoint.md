# I18 worker and recovery acceptance checkpoint

## State

`Building`

## Authority and frozen state

Issue [#18](https://github.com/Bilaltariq41/SeqDoc/issues/18) is the specification authority. This checkpoint freezes
its current reconciled body (post v2 CreditTransfer amendment) at baseline `ac1a41f25357b32fbf2e0b2bff82ffc6cb1fe586`.

- Planned branch: `acceptance/issue-18-worker-recovery`.
- Prerequisites GH-16, GH-17, and GH-87 are closed.
- Frozen v2 CreditTransfer amendment: https://github.com/Bilaltariq41/SeqDoc/issues/18#issuecomment-5633321734
- Independent non-author T2 approval and reproduction: https://github.com/Bilaltariq41/SeqDoc/issues/18#issuecomment-5633920464
- Supplemental T2 receipt (behavior fingerprint, run ID, two-run byte equality, config hash): https://github.com/Bilaltariq41/SeqDoc/issues/18#issuecomment-5634313569
- Readiness review repair (I18-R1/R2/R3/R4): https://github.com/Bilaltariq41/SeqDoc/pull/96

This readiness transaction authorizes publication only. It does not authorize implementation and does not activate
GH-18. Implementation begins only after this readiness PR merges, GH-18 is selected and activated through
`tools/governance/work_state.py`, and the required Test Writer is dispatched before any acceptance-test edit.

## Objective

Run the exact accepted #16/#17 worker contracts through the production compiler/model/scenario/planner/rendering
pipeline against the frozen external FraudManagement, SMSGateway, and CreditTransfer lanes, and inspect the generated
Markdown/Mermaid for exact worker lifecycle/root and scheduler/timer registration boundaries, compiler-evidenced
polling/batch/retry/cancellation/terminal placement, exact callback target/cardinality/non-durable recovery boundaries,
and exact guard/catch/loop/finally/terminal chronology, all evidence-backed, profile/fingerprint-confined,
deterministic, and conservative.

## Frozen external lanes

| Lane | Exact revision | Project/profile | Existing config/baseline | Required post-prerequisite coverage |
|---|---|---|---|---|
| FraudManagement worker/scheduler | `7aabfef98fa4d47781bd8a98b9061ddcafb88836` | `Provided/FraudManagement/FraudManagementWindowsService/FraudManagementWindowsService.csproj`, `Release/net9.0`; `BackgroundService`, Quartz 3.15.1 | `docs/examples/fraud-management.yaml` (21 roots); 36 baseline diagrams, profile `f874be7e`, fingerprint `f9a36fd5` | worker root, timer/scheduler registration boundary, #16 polling/retry/cancellation/terminal facts, #17 callback/non-durable recovery boundaries |
| SMSGateway worker/events | `7ca797356b1856eb815922ca977e9d85a569cb84` | `Provided/SMSGateway-om/Source/LP.SMSGateway.WindowsHost/LP.SMSGateway.WindowsHost.csproj`, `Release/net9.0`; `SMSGatewayWorker : BackgroundService` | `docs/examples/sms-gateway.yaml` (14 roots); 15 baseline diagrams, profile `a82776da`, fingerprint `f1bfaa65` | worker lifecycle and exact event/callback boundaries; subscription is not execution and ACK/NACK is not delivery proof |
| CreditTransfer hosted worker | `e65a94b873e712a17bc337a2b687ab4bd3dacece` | `CreditTransferWorker/CreditTransferWorker.csproj`, `Release/net9.0`; references `CreditTransferEngine/CreditTransferEngine.csproj`; root `CreditTransferWorker.Worker.ExecuteAsync(System.Threading.CancellationToken)` registered by `AddHostedService<Worker>()` | no `--config`; automatic exact hosted-worker admission; `docs/examples/credit-transfer.yaml` remains read-only and is not used for worker admission | reproduce the approved hosted-worker registration, lifecycle-entry, and cancellation-parameter observable without strengthening it into polling, retry, timing, delivery, or durable-recovery proof |
| Notification callback secondary (conditional) | exact project/revision resolved by `ExternalCorpusResolver` if available | exact profile/project recorded at readiness transition | no substitution if unavailable | unrelated callback/event negative/secondary evidence only; may be recorded unavailable without substitution |

CreditTransfer is frozen to revision `e65a94b873e712a17bc337a2b687ab4bd3dacece` in the supplied full, non-shallow
sibling checkout, where the exact commit is present in local history. The recorded upstream
`https://github.com/BeyondOneGroup/CreditTransfer-om.git` was unreachable during T2 review; availability from a fresh
upstream clone is therefore not an acceptance prerequisite and no substitute revision or project is permitted. Stop if
the exact commit is absent from the supplied sibling checkout. The admitted root is
`method:v1:8a78a24d943ce76ce80d2b6108cadb8cadce7382c67de290a62baf3433a09970`; no acceptance-only root configuration is
required for this lane.

## Target paths (allowlist)

Readiness publication (this transaction):

- `docs/work/workers/I18/checkpoint.md`
- generated `docs/project/work-items/GH-18.json` and `docs/project/execution.json`, only through
  `tools/governance/work_state.py`

Acceptance implementation and evidence, after activation:

- `tests/SeqDoc.AcceptanceTests/WorkerExternalCorpusTests.cs` (new; soft budget 4-8 distinct claims)
- `docs/work/workers/I18/checkpoint.md`
- `docs/work/workers/I18/ledger.md`
- generated canonical lifecycle files only through `tools/governance/work_state.py`

Read-only inputs: existing worker acceptance/scenario/framework-model tests, `docs/examples/fraud-management.yaml`,
`docs/examples/sms-gateway.yaml`, `docs/examples/credit-transfer.yaml`, and the supplied external projects. No other
acceptance test, YAML/configuration, `src/**`, fixture, package, solution, SDK, workflow, build configuration,
generated artifact, cache, or external-source change is allowed. Treat this list as an allowlist; stop before editing
any other path.

## Non-goals

- No new compiler/framework semantics, production repair, or `src/**` change of any kind.
- No runtime invocation, exactly-once, delivery-success, durable-recovery, or timing claim.
- No application, repository, package, route, type, method, or business-name matching rule.
- No strengthening of the CreditTransfer observable beyond registration, lifecycle entry, and cancellation-parameter
  evidence (no polling/batch/retry/catch/delay/terminal/durable-recovery claim for that lane without separate
  artifact evidence).
- No configuration entry treated as runtime schedule/execution proof; no event subscription treated as callback
  invocation proof; no ACK/NACK treated as delivery proof; no delay token treated as shutdown proof; no
  `Thread.Abort` treated as cancellation proof; no `SaveChanges` call treated as durability proof; no retry/recovery
  branch treated as eventual-success proof.

## Risk inventory

1. A legacy service (for example `ServiceBase` or manual `Thread` use) is mistaken for a `BackgroundService`/hosted-
   worker root.
2. An unregistered or foreign worker is admitted as a root.
3. An unsupported timer/scheduler overload or a configuration-only job is treated as a registered schedule.
4. A mutually exclusive retry path, or an unsupported loop/catch/finally topology, is flattened or misrepresented.
5. A cancellation-like symbol with the wrong token identity is treated as the cancellation parameter of the worker.
6. An event/delegate subscription without invocation, or a dynamic/metadata-only callback, is treated as executed.
7. A repeated or ambiguous callback target collapses into one claim, or a callback-local return incorrectly
   terminates the outer worker flow.
8. Runtime multiplicity or exactly-once wording is invented where only static structure is proven.
9. A stale cache, profile ID, or Program Index fingerprint leaks a claim across lanes or across unrelated runs.
10. A missing or ambiguous root, or credential-bearing output, reaches a generated artifact.
11. The frozen no-config automatic-admission boundary or the bounded observable claim of the CreditTransfer lane is
    silently expanded.
12. Two clean runs of the same lane produce different artifact bytes, diagnostic digests, or IDs/order.

## Existing relevant coverage

- `tests/SeqDoc.AcceptanceTests/HostedWorkerDocumentationTests.cs`
- `tests/SeqDoc.FrameworkModels.Tests/Workers/HostedWorkerAndSchedulerModelTests.cs`
- `tests/SeqDoc.Scenarios.Tests/HostedWorkerCallbackScenarioTests.cs` and `HostedWorkerScenarioTests.cs`
- `tests/SeqDoc.Analysis.Tests/HostedWorkerProductionProjectionTests.cs`
- `tests/SeqDoc.Wording.Tests/HostedWorkerCallbackWordingTests.cs`
- `tests/SeqDoc.Rendering.Tests/HostedWorkerCallbackRenderingTests.cs`
- `tests/SeqDoc.AcceptanceTests/ServiceClientExternalCorpusTests.cs` and `OutboundHttpExternalCorpusTests.cs` as the
  established external-corpus acceptance pattern: in-process production CLI harness via `CliHost.RunAsync`, whole-suite
  skip when the supplied corpus is absent, and structural/wording/determinism assertions rather than frozen
  profile/fingerprint equality for floating external packages beyond the point-in-time ledger anchor.

None of this existing coverage proves the full accepted #16/#17 worker-control and callback/recovery claim end-to-end
through the external FraudManagement, SMSGateway, and CreditTransfer corpus via the production pipeline.

## Test Writer assignment and soft budget

A Test Writer is required: this checkpoint has exact worker-root/registration-boundary risk, evidence/certainty
degradation risk, deterministic artifact/byte-equality risk across external corpus runs, and false-positive risk
against legacy/unregistered/foreign workers.

Add approximately 4 to 8 distinct claims in `tests/SeqDoc.AcceptanceTests/WorkerExternalCorpusTests.cs` covering: one
FraudManagement worker/scheduler-registration plus #16 polling/retry/cancellation claim; one SMSGateway worker
lifecycle plus #17 callback/non-durable-recovery boundary claim; one CreditTransfer no-config automatic hosted-worker
admission claim bounded to registration/lifecycle/cancellation-parameter evidence only; one determinism/byte-equality
claim across two clean runs; and at least one required negative (for example a legacy/unregistered/foreign worker or
an unsupported scheduler overload). Avoid duplicate assertions already covered by the existing suites above.

The Test Writer must not edit production code, existing tests, configuration, external source, build files, or any
path outside the allowlist above.

## Focused command

```powershell
if (-not $env:SEQDOC_TEST_PROJECTS_ROOT) { $env:SEQDOC_TEST_PROJECTS_ROOT = (Resolve-Path ../SeqDoc-TestProjects).Path }; dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter FullyQualifiedName~WorkerExternalCorpus
```

This preserves an already-configured `SEQDOC_TEST_PROJECTS_ROOT` and falls back to the default sibling path only
when the variable is absent, per `docs/usage.md`. This is an owner-authorized bounded correction to the exact command
text originally in the Issue #18 body (readiness review finding I18-R2); the issue body should be updated to match
in a follow-up so the specification and the checkpoint stay in sync.

## Implementation boundary

Start from a failing or absent acceptance claim. Reuse the existing typed facts and the
`ServiceClientExternalCorpusTests` harness pattern; do not rescan source or duplicate per-layer assertions already
proven by the existing coverage above. The implementation ledger (`docs/work/workers/I18/ledger.md`) must record exact
lane derivation commands, hash every artifact, render every `.mmd` with the recorded Mermaid CLI version, repeat clean
runs, and compare bytes. Notification callback evidence is secondary and may be recorded unavailable without
substitution.

Inspect the complete diff after focused verification. Stop at `ReviewRequired`.

## Independent review

Run one independent complete-candidate review after focused verification. Review the full diff against this
checkpoint allowlist, non-goals, risk inventory, and the required-negatives list in Issue #18. Record each finding as
`Fixed`, `Rejected` with evidence, or `Deferred` with explicit owner approval.

Exactly one batched repair round is authorized: fix every open finding together, rerun the focused command once, and
re-review the complete candidate once. A second material repair need blocks I18; preserve the worktree, transition
GH-18 to `Blocked`, and obtain a separate authorized decision.

## Final gate

Run once only, after every review finding is resolved:

```powershell
if (-not $env:SEQDOC_TEST_PROJECTS_ROOT) { $env:SEQDOC_TEST_PROJECTS_ROOT = (Resolve-Path ../SeqDoc-TestProjects).Path }; dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release
```

## Stop conditions

Stop and report the exact command, error, evidence, and smallest decision needed if:

- any required change falls outside the allowlist;
- the exact supplied CreditTransfer revision is absent from the sibling checkout;
- a lane revision, profile, or configuration drifts from the frozen table above;
- a required lane or root is missing or ambiguous;
- output is stale, non-deterministic, oversized, or credential-bearing;
- a link is broken or Mermaid CLI rendering fails;
- an unexplained diagnostic appears;
- any production, configuration, external-source, or build change would be required;
- the one authorized batched repair round does not resolve every finding.
