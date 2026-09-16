# I18 acceptance evidence ledger

Test Writer candidate for `tests/SeqDoc.AcceptanceTests/WorkerExternalCorpusTests.cs`. All commands below were
run from the worktree root `C:\Users\qhata\OneDrive\Desktop\training\seqdoc-i18-worktree`, against the sibling
corpus at `C:\Users\qhata\OneDrive\Desktop\training\SeqDoc-TestProjects`.

## Lane derivation commands

### FraudManagement (revision `7aabfef98fa4d47781bd8a98b9061ddcafb88836`)

Confirmed the corpus checkout is already at the frozen revision (no worktree isolation needed):

```
git -C Provided/FraudManagement rev-parse HEAD
# 7aabfef98fa4d47781bd8a98b9061ddcafb88836
```

Manual pre-authoring probe run (the acceptance test itself performs the equivalent run twice, in-process, via
`CliHost.RunAsync`):

```
dotnet run --project src/SeqDoc.Cli/SeqDoc.Cli.csproj -c Release --no-build -- analyze \
  "<corpus>/Provided/FraudManagement/FraudManagement.sln" \
  --repository-root "<corpus>/Provided/FraudManagement" \
  --config "docs/examples/fraud-management.yaml" \
  --configuration Release --framework net9.0 \
  --cache <temp>/fm-cache.db --output <temp>/fm-out --json
```

Result: `outcome: Succeeded`, 36 flow documents + `index.md` (37 `.md` files), diagnostics
`[BE1001, BE2010, BE2010, PRED001]`. Exactly one `# Hosted worker` document
(`hosted-worker-fraudmanagementwindowsservice-worker-89186e40.md`), naming only
`FraudManagementWindowsService.Worker.ExecuteAsync` with cancellation-parameter evidence `stoppingToken`. No
`Quartz`/`IJob`/`JobBuilder`/`CronTrigger`/`TimerSetup`/"registers a timer callback" token appears anywhere in
the generated output - the real Quartz `BaseCronJob`/`IJob` dispatch (a runtime-string-switched
`JobBuilder.Create<T>()`) is not mis-admitted as a registered scheduler callback.

### SMSGateway (revision `7ca797356b1856eb815922ca977e9d85a569cb84`)

Confirmed the corpus checkout is already at the frozen revision (no worktree isolation needed):

```
git -C Provided/SMSGateway-om rev-parse HEAD
# 7ca797356b1856eb815922ca977e9d85a569cb84
```

```
dotnet run --project src/SeqDoc.Cli/SeqDoc.Cli.csproj -c Release --no-build -- analyze \
  "<corpus>/Provided/SMSGateway-om/Source/LP.SMSGateway.WindowsHost/LP.SMSGateway.WindowsHost.csproj" \
  --repository-root "<corpus>/Provided/SMSGateway-om" \
  --config "docs/examples/sms-gateway.yaml" \
  --configuration Release --framework net9.0 \
  --cache <temp>/sms-cache.db --output <temp>/sms-out --json
```

Result: `outcome: Succeeded`, 15 flow documents + `index.md` (16 `.md` files). Exactly one `# Hosted worker`
document (`hosted-worker-lp-smsgateway-windowshost-smsgatewayworker-59728dbc.md`), naming only
`LP.SMSGateway.WindowsHost.SMSGatewayWorker.ExecuteAsync` with cancellation-parameter evidence
`stoppingToken`. Confirmed real (not fixture-invented) legacy negative candidates present in this lane's
source: `Source/LP.Messaging.SMS/SmscCluster.cs` (`new Thread(() => NodeDispatchLoop(...))`) and
`Source/LP.SMSGateway.Common/JobScheduler.cs` (`new Thread(SchedulingThreadMethod)`) - neither is admitted as
a `# Hosted worker` document title anywhere in the lane. No ACK/NACK/acknowledg/delivered/durable/exactly-once
overclaim token appears anywhere in the lane's Markdown.

### CreditTransfer (revision `e65a94b873e712a17bc337a2b687ab4bd3dacece`, isolated worktree)

The current `Provided/CreditTransfer-om` checkout HEAD (`02b82a5115ef6e2d138c70670f28b959fb646f6e`) postdates
the frozen revision, so the fixture materialises the exact frozen commit in an isolated detached worktree
(mirroring `ServiceClientExternalCorpusFixture`'s SMS isolation approach), restores it, analyzes it with no
`--config` (automatic hosted-worker admission), and removes the worktree afterward.

```
git -C Provided/CreditTransfer-om cat-file -e e65a94b873e712a17bc337a2b687ab4bd3dacece   # present, non-shallow
git -C Provided/CreditTransfer-om worktree add --detach <temp>/ct-worktree e65a94b873e712a17bc337a2b687ab4bd3dacece
dotnet restore <temp>/ct-worktree/CreditTransferWorker/CreditTransferWorker.csproj
dotnet run --project src/SeqDoc.Cli/SeqDoc.Cli.csproj -c Release --no-build -- analyze \
  "<temp>/ct-worktree/CreditTransferWorker/CreditTransferWorker.csproj" \
  --repository-root "<temp>/ct-worktree" \
  --configuration Release --framework net9.0 \
  --cache <temp>/ct-cache.db --output <temp>/ct-out --json
git -C Provided/CreditTransfer-om worktree remove --force <temp>/ct-worktree
git -C Provided/CreditTransfer-om worktree prune --expire now
```

Result: `outcome: Succeeded`, exactly one flow document
(`hosted-worker-credittransferworker-worker-9f903af4.md`) + `index.md`. The admitted root hash
`method:v1:8a78a24d943ce76ce80d2b6108cadb8cadce7382c67de290a62baf3433a09970` appears in the CLI `--json`
output, matching the checkpoint's frozen value exactly - no acceptance-only root configuration was supplied.
Behavior text is bounded to `Hosted worker lifecycle entry point.` and `The registered hosted-worker lifecycle
includes ExecuteAsync with cancellation parameter evidence: stoppingToken.` - no polling/retry/catch/delay/
terminal/durable-recovery wording, even though the real `Worker.ExecuteAsync` body contains a `while` loop,
`try`/`catch`, and `Task.Delay` retry-like shape.

After the acceptance test run, `git worktree list` in `Provided/CreditTransfer-om` shows no residual
`seqdoc-i18-ct-worktree-*` entry, and `git status --short` in the shared repository is unchanged by the run
(the pre-existing untracked/deleted entries observed both before and after are unrelated local state, not
caused by this lane).

## Artifact hashes (SHA-256)

Representative hosted-worker documents from the pre-authoring probe runs (the acceptance test itself performs
independent equivalent runs and asserts byte-for-byte equality between its own two runs, not against these
constants):

| Lane | File | SHA-256 |
|---|---|---|
| FraudManagement | `hosted-worker-fraudmanagementwindowsservice-worker-89186e40.md` | `62fcd6dd17cd9057d6b93b43107f47a0c5cacc329b6c1b2a9475c48a03b3f6bd` |
| FraudManagement | `hosted-worker-fraudmanagementwindowsservice-worker-89186e40.mmd` | `1357271040f6650cbae7bc6d7df3e7518cac076e26cd08c2fc8b4183153b407d` |
| SMSGateway | `hosted-worker-lp-smsgateway-windowshost-smsgatewayworker-59728dbc.md` | `cc0dd4dff964edb0cba3b98ddf08c23bbf61064dda225414abd985d4ca72d79b` |
| SMSGateway | `hosted-worker-lp-smsgateway-windowshost-smsgatewayworker-59728dbc.mmd` | `609d52f6b637fbf8ff1a3e8595bf146cb76f9b0b9b5fb7bf185ce3b6959aa25a` |
| CreditTransfer | `hosted-worker-credittransferworker-worker-9f903af4.md` | `e759921baedbfc10119ec07bdf399614cf22cacf4bbff33db05aa43a02ef34af` |
| CreditTransfer | `hosted-worker-credittransferworker-worker-9f903af4.mmd` | `a471afb2b80359b0f70dbd4799f8da9cf48efcc591f58841bc9369a63be2b738` |

Complete-output-set digest (SHA-256 over `sha256sum` of every generated `.md`/`.mmd`/`seqdoc.manifest.json`,
piped through `sha256sum` again), per lane, from the probe runs:

| Lane | File count | Complete-output digest |
|---|---|---|
| FraudManagement | 74 (36 flow `.md` + 36 `.mmd` + `index.md` + `seqdoc.manifest.json`) | `2c5b690c8321db5003140f2e8fc259e3ca5447d236b75f6eaa10caaba9574fb2` |
| SMSGateway | 32 (15 flow `.md` + 15 `.mmd` + `index.md` + `seqdoc.manifest.json`) | `b6e9a39741fdce2822ab3648ef381cdb6910861a3ebe2da37299ec442ac9a7d3` |
| CreditTransfer | 4 (1 flow `.md` + 1 `.mmd` + `index.md` + `seqdoc.manifest.json`) | `fde4e9e6b4b37a6b76f8af435363c467ac079c3ebfdddd35f97f6458b3af0768` |

## Mermaid CLI rendering

Mermaid CLI is available in this environment via `npx`/`node`:

```
node --version   # v24.20.0
npx --yes @mermaid-js/mermaid-cli@11.16.0 --version   # 11.16.0
```

Rendered the hosted-worker `.mmd` diagram from each of the three lanes with mermaid-cli 11.16.0
(`npx --yes @mermaid-js/mermaid-cli@11.16.0 -i <file>.mmd -o <file>.svg`); all three produced a non-empty SVG:

| Lane | SVG SHA-256 | Bytes |
|---|---|---|
| FraudManagement | `bcb8265094b828f99d18aa10549f5fc0862db7b480968a9622f7cc526894d6b9` | 23170 |
| SMSGateway | `8b0a523ef0fe18a9628d297b3907e73c76c67a1bf6302f3508e6202051dd1dae` | 23234 |
| CreditTransfer | `8d5f94e1ddfc4f8a2580253e55d181b63e90f1f4bd5a6c2d867c383fb2f53669` | 23107 |

The acceptance test itself (`EveryLaneStaysWithinBudgetHasNoDanglingLinksAndIsCredentialSafe`) validates every
generated `.mmd` structurally via `SeqDoc.Rendering.Markdown.MermaidValidator`, rather than shelling out to
mermaid-cli per test run (matching the lighter `ServiceClientExternalCorpusTests` pattern this checkpoint
follows, not the heavier per-diagram subprocess render `OutboundHttpExternalCorpusTests` performs for its
single frozen lane). The real mermaid-cli 11.16.0 render above is the ledger's independent confirmation that
the structurally-validated diagrams also render.

## Two-clean-run byte equality (claim 4)

`WorkerExternalCorpusTests.EveryLaneProducesByteIdenticalOutputAcrossTwoIndependentRuns` runs all three lanes
twice each, with independent temp output directories and independent SQLite caches per run, and asserts:

- identical relative file-path sets between run 1 and run 2;
- identical CLI `--json` diagnostic records (raw, in emitted order) between run 1 and run 2;
- byte-for-byte identical content for every generated file between run 1 and run 2.

Result: **PASS** for all three lanes (FraudManagement, SMSGateway, CreditTransfer) in the focused test run
recorded below.

## Focused command result

```
$env:SEQDOC_TEST_PROJECTS_ROOT = "C:\Users\qhata\OneDrive\Desktop\training\SeqDoc-TestProjects"
dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter FullyQualifiedName~WorkerExternalCorpus
```

Result: **6 discovered, 6 passed, 0 failed, 0 skipped** (Release, net10.0 test host), ~1.5 minutes.

```
Passed SeqDoc.AcceptanceTests.WorkerExternalCorpusTests.SmsGatewayWorkerLifecycleAndCallbackRecoveryBoundaryHolds
Passed SeqDoc.AcceptanceTests.WorkerExternalCorpusTests.CreditTransferNoConfigAutomaticHostedWorkerAdmissionIsBounded
Passed SeqDoc.AcceptanceTests.WorkerExternalCorpusTests.EveryLaneStaysWithinBudgetHasNoDanglingLinksAndIsCredentialSafe
Passed SeqDoc.AcceptanceTests.WorkerExternalCorpusTests.LegacyAndForeignWorkerConstructsAreNotAdmittedAsHostedWorkerRoots
Passed SeqDoc.AcceptanceTests.WorkerExternalCorpusTests.FraudManagementWorkerRootIsAdmittedWithBoundedSchedulerRegistrationBoundary
Passed SeqDoc.AcceptanceTests.WorkerExternalCorpusTests.EveryLaneProducesByteIdenticalOutputAcrossTwoIndependentRuns
```

Verified both forms resolve correctly from this worktree location (`seqdoc-i18-worktree` and
`SeqDoc-TestProjects` are siblings under the same parent directory, same as the main `SeqDoc` checkout): the
default fallback (`..\SeqDoc-TestProjects` relative to the worktree, used when `SEQDOC_TEST_PROJECTS_ROOT` is
unset) and an explicit `$env:SEQDOC_TEST_PROJECTS_ROOT = "C:\Users\qhata\OneDrive\Desktop\training\SeqDoc-TestProjects"`
both produced 6/6 passed. The recorded run above used the explicit form; a second confirmation run with the
variable unset also passed 6/6 (0 failed, 0 skipped).
