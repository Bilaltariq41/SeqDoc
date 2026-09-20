# Delegated Contribution Workflow

Delegated changes remain candidates until worker self-review and one complete latest-head, non-author human peer review.
The canonical record under `docs/project/work-items/` is the current-state authority; GitHub lifecycle labels and
checkpoint prose are projections. Preserve the submitted branch, record its base revision, inspect the actual diff, and
classify each area as accepted, repairable, or rejected. Follow [collaboration-model.md](collaboration-model.md) for
review, receipts, continuation, and T4 decisions. Record findings with file/line evidence, risk, expected behavior, and
one focused verification command. Findings continue on the same PR; the worker repairs, replans, pairs, and absorbs
necessary acceptance work until the latest head is ready. Block only as permitted by
[collaboration-model.md](collaboration-model.md), including `reviewer unavailable`; the owner is not an automatic
fallback.

For each finding, record a repair trace:

| Finding | Production repair | Producer/boundary test | Observable assertion | Residual boundary |
|---|---|---|---|---|

A repair is complete only when the trace reaches its required observable or persisted consumer and the contributor's
full-candidate self-review finds no regression outside the repair delta.

Use the same continuation loop for an available implementation agent: resume the same session with exact findings,
require it to fix its own delta, and verify changed risks. Do not silently rewrite repairable delegated work. A new
implementation issue is for a genuinely independent deliverable, not a discovery from the current outcome.

Automated or unavailable-author candidates may be hardened on a local branch based on the submission. Retain correct
code and repair only demonstrated defects. Before publication, compare the full candidate against canonical `main`,
run risk-focused tests and one realistic acceptance scenario, then squash the verified tree onto a clean branch from
`main`. Scratch branches, worktrees, copied fixtures, misleading execution records, and intermediate commits never
enter public history.

Canonical documentation is rewritten from verified evidence. For each integrated candidate record what was reused,
what was repaired or rejected, why, the verification performed, and whether delegation reduced total work. Optimize
the workflow from recurring defect categories rather than weakening review standards.

## Recurring semantic review failures

Recurring semantic failures are broken evidence chains, stronger downstream claims, fail-open identity, lost control
placement, and silent unsupported forms. Apply the five proof gates in `AGENTS.md`. Reviewers should search for fixtures
with no production-path test and hand-built facts with no producer or observable proof.

Scope drift is a separate recurring category. The worker may add paths and adjust contracts when reasonably necessary
for the existing outcome, recording the change and preserving quality invariants. A genuinely separate capability is
the boundary requiring human preapproval and a new issue.

## Semantic delivery sequence

Use one primary contributor or coding agent for a semantic package from issue brief through production implementation,
focused tests, and complete-candidate self-review. The issue brief must name the exact compiler identity and operation
shape, registration or admission requirement, evidence chain to the first observable consumer, unsupported forms,
negative lookalikes, target paths, and one focused command. Do not split one semantic chain among layer-specific agents
unless the contract has already been reviewed and the paths are independent.

Follow [collaboration-model.md](collaboration-model.md) for grandfathered rules, one-peer review, continuation, and T4
decisions. This workflow preserves the complete vertical candidate, focused verification, evidence, attribution, and
final-gate requirements.

## Delivery package sizing

Use the parent workstream to group implementation issues and dependencies. An implementation issue owns one outcome and
may use multiple ordered commits inside one PR; it cannot create child issues. Absorb discoveries serving that outcome or
return them to the parent backlog. Create a new implementation issue only for a genuinely independent deliverable.

Keep a contract and its first consumer together when that makes the abstraction demonstrably useful. Separate a shared
foundation when its review could materially redirect dependent work, and keep supplied-project acceptance separate
when it would obscure the semantic implementation diff. Never enlarge a package with unrelated cleanup merely to
reduce review count.

## Review latency and dependent work

Contributors may claim eligible Ready items independently when their dependencies and path leases permit it. Do not use
child issues or a two-level stack to route discoveries; absorb them or return them to the parent backlog.

## Repair trace: PR #33 (`feature/issue-5-exact-contract-operation`, closes #5 and #7)

Base revision after sequencing: rebased onto `upstream/main` at `4c935c5` (PR #34 merged), new branch head `abb4b0f`
before this repair's commits. `.github/*` review-policy files confirmed untouched by the candidate diff.

**Scope authority.** The original issue-#5/#7 comments (2026-08-20) requested separate PRs, and an automated
pre-repair review flagged this PR as conflicting with that instruction. Owner authority (`@Bilaltariq41`, in PR
review) subsequently and explicitly grandfathered the combined package for this PR specifically: do not split it
merely for packaging, and both issues are correctly closed together here. This is a PR-review-level exception to
the general separate-PR instruction on the issues, not a change to the issues themselves or to the default
packaging policy in [Delivery package sizing](#delivery-package-sizing) above.

| Finding | Production repair | Producer/boundary test | Observable assertion | Residual boundary |
|---|---|---|---|---|
| Capability treated as execution: a `[ServiceContract]`/`[OperationContract]` match alone admitted a Scenario Graph root. | Split `CoreWcfServiceModel` into `ServiceOperationCapabilityFact` (attribute+body proof only) and `ServiceEndpointRegistrationFact` (exact `IServiceBuilder.AddServiceEndpoint<TService,TContract>(Binding,string)` proof); `ScenarioGraphBuilder.BuildServiceOperationEntries` joins the two by (implementation, contract) before admitting a root. | `CoreWcfServiceOperationScenarioTests.CapabilityWithoutMatchingRegistrationAdmitsNoRootAndProducesAConservativeDiagnostic`; `CoreWcfServiceModelProjectionTests.RegisteredCapabilityAdmitsARootAndUnregisteredCapabilityProducesAConservativeDiagramDiagnostic` | Unregistered `ExplicitCalculatorService` capability admits no graph and emits `SC-SERVICE-UNSUPPORTED-DISPATCH`; registered `CalculatorService` operations admit roots with `ServiceOperation` wording. | None open. |
| Attribute identity matched on `ProgramAttributeApplication.AttributeType` display strings, accepting same-named foreign-assembly or mixed CoreWCF/`System.ServiceModel` pairs. | Added `FrameworkAttributeApplicationIdentity`/`FrameworkInterfaceMemberIdentity.InterfaceTypeAttributes`/`InterfaceMethodAttributes`, resolved via `ProjectTypeIdentity(attribute.AttributeClass)` (assembly + version + metadata name); `TryGetAdmittedFamily` requires the ServiceContract/OperationContract pair to resolve to the same family. | `CoreWcfServiceModelTests.ForeignAssemblySameQualifiedNameAttributeNeverAdmitsCapability`, `MixedFamilyAttributesNeverAdmitCapability`; fixture `Lookalikes.cs` (`FakeService`, `MixedFamilyService`) through `CoreWcfServiceModelProjectionTests`. | Foreign-assembly and mixed-family lookalikes never produce a capability fact through the real producer. | None open. |
| Service facts bypassed `FrameworkFactsBound`, so a foreign profile or stale Program Index fingerprint could still contribute a root. | `ScenarioGraphBuilder` gates `BuildServiceOperationEntries` behind the same `FrameworkFactsBound(request)` guard `HostedWorker` roots already use. | `CoreWcfServiceOperationScenarioTests.ForeignProfileServiceFactsCannotAdmitARoot`, `MissingProgramIndexFingerprintCannotAdmitARoot` | Mismatched profile/fingerprint facts admit zero graphs and zero diagnostics. | None open. |
| Effective certainty was taken from the input symbol only, never lowered by the contributing evidence's own certainty. | `CoreWcfServiceModel.WeakestCertainty` takes the highest `CertaintyLevel` ordinal (weakest) across the input and every contributing evidence item; the Scenario join takes the weaker of capability and registration certainty. | `CoreWcfServiceModelTests.ClassicSystemServiceModelIdentityIsAlsoAdmitted` (certainty propagation); `CoreWcfServiceOperationScenarioTests.AdmittedRootCombinesCapabilityAndRegistrationEvidence` (evidence union). | Certainty never exceeds the weakest contributor; combined evidence contains both `service-operation-capability` and `service-endpoint-registration` artifacts. | Node-presentation certainty in `ScenarioGraphBuilder.CreateNodeWithPresentation` is a separate, pre-existing evidence-array minimum unrelated to this fact-level rule; left unchanged as out of scope. |
| Issue #5's declared fact boundary (source/generated clients, endpoint metadata, faults) was incomplete. | Added `ServiceClientBoundaryFact` (`ClientBase<T>` derivation, classified by the exact `GeneratedCodeAttribute` marker), `ServiceEndpointRegistrationFact` (endpoint metadata), `ServiceFaultContractFact` (`[FaultContract(typeof(X))]` per admitted operation). | `CoreWcfServiceModelProjectionTests.PassCFixtureCompilationAdmitsExactCapabilitiesFaultsAndClientBoundariesAndWithholdsNegativeBoundaries`, `PassCFixtureCompilationProducesTheExactAddServiceEndpointRegistrationFromStartup` | Real fixture compilation produces both client kinds, the endpoint registration, and the `SquareRoot` fault fact, all with `CertaintyLevel.Exact`. | None open. |
| Explicit-interface-implemented operations (`double ICalculatorService.Add(...)`) silently never admitted capability. | `IsEligibleServiceOperation` required `shape.IsOrdinary`, but Roslyn reports `MethodKind.ExplicitInterfaceImplementation` (never `Ordinary`) for an explicit implementation; relaxed to `shape.IsOrdinary \|\| member.IsExplicitImplementation`. Found only once the vertical producer test replaced the hand-built unit fixture, which had incorrectly hardcoded `IsOrdinary: true` for its explicit-implementation case. | `CoreWcfServiceModelTests.ExplicitInterfaceImplementationAdmitsCapabilityDespiteNonPublicMethod` (now asserts `isOrdinary: false`, matching real Roslyn), `NonOrdinaryImplicitMethodKindNeverAdmitsCapability`; fixture `ExplicitCalculatorService` through the real pipeline. | `ExplicitCalculatorService`'s five explicit-implementation operations all admit capability through the real Roslyn producer. | None open. |
| Every admitting method on a `ClientBase<T>` client type independently emitted `ServiceClientBoundaryFact` under the same `BehaviorFactId` (type-level anchor); `FrameworkModelHost` never deduplicates custom `BehaviorFact` subtypes by payload equality, so the repeated identity reported as a genuine conflict and both facts were discarded. | `BuildClientBoundaryFact`'s anchor now includes the triggering method's own `SymbolId`, so each admitting method emits an independently identified fact instead of colliding. | `CoreWcfServiceModelProjectionTests.PassCFixtureCompilationAdmitsExactCapabilitiesFaultsAndClientBoundariesAndWithholdsNegativeBoundaries` | `CalculatorSourceClient`/`CalculatorGeneratedClient` client-boundary facts survive `FrameworkModelHost` aggregation instead of being removed as conflicting. | None open. |
| `GeneratedCodeAttribute`'s admitted identity used its runtime assembly (`System.Private.CoreLib`), but Roslyn's compile-time `ContainingAssembly` resolves to the `System.Runtime` reference-assembly facade it is type-forwarded through, so real generated-client detection always failed closed to `SourceClient`. | Corrected `Identity.CoreLibAssembly` to `"System.Runtime"`, confirmed by reflecting `typeof(GeneratedCodeAttribute).Assembly.GetName()` from a real compilation under the pinned SDK and comparing against the Roslyn-projected identity; the test fixture's matching constant was equally wrong and is corrected together. | `CoreWcfServiceModelProjectionTests.PassCFixtureCompilationAdmitsExactCapabilitiesFaultsAndClientBoundariesAndWithholdsNegativeBoundaries` | `CalculatorGeneratedClient` (marked with the real `[GeneratedCodeAttribute]`) now produces `ServiceClientKind.GeneratedClient`. | None open. |
| Clean-checkout reproducibility for the switched `Microsoft.NET.Sdk.Web`/`CoreWCF.Http` fixture. | Regenerated `packages.lock.json`; verified `dotnet restore --locked-mode` succeeds standalone before any build step. | Declared verification command (fixture restore, then build/test each affected project with `--no-build --no-restore`). | `dotnet restore tests/fixtures/PassC/CoreWcfServices/CoreWcfServices.csproj --locked-mode` succeeds from a clean `obj`/`bin`. | None open. |

### Second Copilot pass (post-repair correctness gaps)

| Finding | Production repair | Producer/boundary test | Observable assertion | Residual boundary |
|---|---|---|---|---|
| `ScenarioTestFactory`'s service-operation tests anchored `RootMethod` to `ActionMethod` (`GadgetsController.GetById`, a controller action), so the tests never exercised a real service-implementation method ID or its own Method Flow — masking bugs in root identity, evidence propagation, and topology that depend on the actual admitted service method. | Added a dedicated `ServiceOperationMethod` (`CoreWcfServices.CalculatorService.Add`) with its own minimal Program Index entry and Method Flow (`CreateServiceBaseRequest`), independent of the GetMeaning controller/action fixture; `CreateServiceOperationRequest`/`CreateUnregisteredServiceCapabilityRequest` now build from it instead of `CreateGetRequest()`. | `CoreWcfServiceOperationScenarioTests` (all 5, rerun against the new anchor) | `ServiceOperationEntryPoint`/`RootMethod` resolve to the real service method identity; all 5 scenario tests and the full 170-test Scenarios.Tests suite still pass. | None open. |
| `BuildClientBoundaryFact` hardcoded the input certainty as `CertaintyLevel.Exact`, ignoring the triggering `SymbolDescriptor.Certainty`, so a Conservative/Heuristic symbol input could still emit an `Exact` `ServiceClientBoundaryFact`. | `BuildClientBoundaryFact` now takes the triggering method's `SymbolDescriptor.Certainty` as its weakest-certainty input, matching the rule used everywhere else in this model. | `CoreWcfServiceModelTests.ClientBoundaryCertaintyNeverExceedsTheTriggeringSymbolsCertainty` (new) | A `Conservative`-certainty triggering symbol produces a `Conservative`, never `Exact`, client-boundary fact. | None open. |
| `IsApplicable`'s doc comment claimed it detects an "exact admitted attribute identity," but the implementation only matches `ProgramAttributeApplication.AttributeType` metadata-name strings, which cannot distinguish a real CoreWCF/`System.ServiceModel` attribute from a same-qualified-name foreign-assembly lookalike. | Rewrote the doc comment to accurately describe `IsApplicable` as a coarse string-based pre-filter that only decides whether the model runs, with the real exact-identity decision made later per member in `AnalyzeMethod`. No behavior change — `IsApplicable` was never the admission decision, only the doc overstated it. | N/A (documentation-only; existing `ForeignAssemblySameQualifiedNameAttributeNeverAdmitsCapability`/`MixedFamilyAttributesNeverAdmitCapability` already prove the real, later admission decision fails closed on lookalikes). | Doc comment on `CoreWcfServiceModel.IsApplicable` no longer overstates its guarantee. | None open. |

Full declared verification command run after all repairs: `CoreWcfServiceModelProjection\|InterfaceMemberEligibility` (Analysis.Tests) 9/9, `CoreWcf` (FrameworkModels.Tests) 21/21, `CoreWcfServiceOperation` (Scenarios.Tests) 5/5, `CoreWcfServiceOperation` (Wording.Tests) 1/1. Full-suite regression check: FrameworkModels.Tests 252/252, Scenarios.Tests 170/170, Analysis.Tests 197/207 (the 10 failures are the pre-existing `SD1102` multi-SDK MSBuildLocator registration conflict on unrelated fixtures — confirmed present in isolation, independent of this candidate, and unrelated to Issues #5/#7).

### Third repair round: active host-chain proof (supersedes the earlier registration-shape-only technical instructions)

The owner's third review (2026-08-24T20:28:20Z) accepted the two rounds above but identified the real remaining gap:
`CoreWcfServiceModel` admitted an executable root from **any** exact `AddServiceEndpoint<TService,TContract>(Binding,string)`
invocation found anywhere in source, never proving it was reachable from an actively-running host. This round
supersedes the earlier, less specific registration-detection instructions with an exact 7-link host-chain
requirement, typed (symbol-level) identity through every fact and join, a protocol-neutral `ScenarioRootKind`,
deterministic multi-registration handling, and a deeper evidence chain.

Every required API's exact compiler identity was independently confirmed via reflection against the actually-restored
packages (not guessed) before implementation — see the PR description's admission table for the full identity list.

| Finding | Production repair | Producer/boundary test | Observable assertion | Residual boundary |
|---|---|---|---|---|
| Any exact `AddServiceEndpoint` invocation anywhere in source (dead helper, disconnected callback, unchained `AddService`/`AddServiceEndpoint` pair) was treated as dispatch evidence, never proving reachability from an active host. | New `CoreWcfHostChainScanner`: a single, non-heuristic structural scan (never matches `Main`/`Startup`/`Configure` by name) that proves the complete chain — `Host.CreateDefaultBuilder(args).ConfigureWebHostDefaults(w => w.UseStartup<TStartup>())`, `TStartup`'s own exact `Configure(IApplicationBuilder)`, its `UseServiceModel(Action<IServiceBuilder>)` call, and an `AddServiceEndpoint<TService,TContract>` call whose receiver is the exact matching `AddService<TService>()` call in the same lambda. `FrameworkServiceEndpointShapeDescriptor` gained `HostChainProven`/`HostChainEvidence`; `CoreWcfServiceModel.AnalyzeOperation` only emits `ServiceEndpointRegistrationFact` when `HostChainProven` is true. | `CoreWcfServiceModelTests.UnprovenHostChainNeverProducesARegistrationFactDespiteTheExactEndpointShape`; fixture `UnusedRegistrationHelper.NeverCalled` (dead helper) and `UnusedStartup` (disconnected callback) through `CoreWcfServiceModelProjectionTests.PassCFixtureCompilationProducesTheExactAddServiceEndpointRegistrationFromStartup` | The real fixture contains three source-identical `AddServiceEndpoint<CalculatorService,ICalculatorService>` shapes; exactly one (the real, admitted `Startup.Configure` chain) produces a registration fact. | Classic `System.ServiceModel` self-host registration (an `AddServiceEndpoint`-equivalent for that family) is not modeled; a classic-family capability without a CoreWCF registration correctly reports the conservative unsupported-dispatch diagnostic. |
| Facts and joins reduced service/contract/fault/client types to metadata-name strings, losing exact symbol/assembly/project identity and the constructed-generic-argument proof a `ClientBase<TContract>` derivation requires. | `FrameworkServiceEndpointShapeDescriptor`, `ServiceOperationCapabilityFact`, `ServiceEndpointRegistrationFact`, `ServiceFaultContractFact` (+ `FaultTypeIdentity`), and `ServiceClientBoundaryFact` all gained typed `SymbolId`/`FrameworkTypeIdentity` fields alongside their existing presentation strings. New additive `FrameworkTypeShape.BaseTypeChainWithArguments`/`FrameworkBaseTypeIdentity` carries each base type's constructed generic arguments (mirrors the existing `FrameworkAttributeApplicationIdentity` shape); `IsClientBaseDerivedForContract` now requires the *constructed* argument to equal the exact admitted contract, not just `ClientBase\`1`'s presence in the chain. `ScenarioGraphBuilder.BuildServiceOperationEntries` joins on `(ImplementationTypeSymbol, ServiceContractTypeSymbol)`, never metadata-name strings. | `CoreWcfServiceModelTests.ClientBaseConstructedForOneContractNeverEmitsABoundaryForAnUnrelatedAdmittedContract` (unit); fixture `MismatchedContractClient` (derives `ClientBase<ICalculatorService>`, separately implements admitted `IClassicEchoService`) through `CoreWcfServiceModelProjectionTests.ClientBaseConstructedForOneContractNeverEmitsABoundaryForASeparatelyImplementedContract` | `MismatchedContractClient` gets a client boundary only for `ICalculatorService` (the constructed argument); `IClassicEchoService`'s own operation admits ordinary capability instead. | Cross-project same-metadata-name collision and same-name-different-assembly fault types are covered at the producer-adjacent unit level (hand-built `ProjectId`s/`FrameworkTypeIdentity`s), not a second real fixture project, since adding one would be an unapproved solution change. |
| `SC-SERVICE-UNSUPPORTED-DISPATCH` hard-coded `CertaintyLevel.Exact`; `BuildClientBoundaryFact`'s evidence only covered `type.Evidence`, never the triggering symbol or the `GeneratedCodeAttribute` marker's own evidence; `ServiceFaultContractFact`'s evidence only wrapped operation-attribute evidence, never the `[FaultContract]` application's own evidence. | Diagnostic certainty is now the weakest of the capability's own certainty and evidence. `FrameworkAttributeApplicationIdentity` gained an additive `Evidence` field (populated from the same per-attribute source evidence Program Index already builds) so a model never re-derives attribute evidence through a separate target-symbol-plus-metadata-name lookup a foreign lookalike could contaminate; client-boundary and fault-contract evidence now union every real contributor. | `CoreWcfServiceOperationScenarioTests.ConservativeCapabilityWithoutRegistrationDegradesTheUnsupportedDispatchDiagnosticsCertainty` (Conservative capability → Conservative diagnostic certainty, exact evidence ID present), `ConservativeRegistrationRetainsBothContributorsExactEvidenceIdsAndOwnCertainty` (exact evidence IDs from both contributors, each item's own certainty) | Diagnostic/evidence assertions check exact certainty values and exact evidence IDs, not just artifact-name presence. | `ScenarioNode`'s own presentation `Certainty` is computed by the pre-existing `CreateNodeWithPresentation` as `evidence.Min(item => item.Certainty)` over the raw evidence array (so one Exact item makes the whole node read Exact even when a sibling fact is Conservative) — a separate, out-of-scope mechanism from the fact-level weakest-certainty rule this round proves; the new tests assert on evidence items and the diagnostic's own certainty instead of the node's presentation certainty for that reason. |
| `NormalizedEntry.RootKind` silently fell through to `ScenarioRootKind.HttpEntryPoint` for `ScenarioActionKind.ServiceOperation`. | Added `ScenarioRootKind.ServiceOperation` as the last member (preserves every prior numeric value: `HttpEntryPoint=0, ConfiguredMethod=1, HostedWorker=2, ServiceOperation=3`); mapped explicitly. | `ScenarioRootKindTests` (new, `SeqDoc.Core.Tests`); `CoreWcfServiceModelProjectionTests`/`CoreWcfServiceOperationScenarioTests` root-kind assertions updated | No persistence layer currently serializes `ScenarioGraphSet`/`ScenarioRootKind` (confirmed by repository-wide search), so the cheapest correct compatibility check is the int-stability test, not a round-trip fixture. | None open. |
| `BuildServiceOperationEntries` used `FirstOrDefault` for the matching registration, silently discarding additional matches and their evidence. | Collects every registration matching the exact `(ImplementationTypeSymbol, ServiceContractTypeSymbol)` pair, unions their evidence in stable ID order, and takes the weakest certainty across all of them — one root per exact pair, never one per endpoint. | `CoreWcfServiceOperationScenarioTests.TwoValidRegistrationsForTheSamePairAdmitExactlyOneRootWithUnionedEvidenceDeterministically` (forward and reversed input order) | Exactly one root; both registrations' evidence present; reversing the registration array's order produces byte-identical node/evidence/certainty identities. | No concrete, producible trigger was found for "candidates disagree on required identity" beyond what exact symbol matching already prevents by construction (a mismatched identity never matches, it does not produce a conflicting match), so no ambiguity diagnostic was added for that case. |
| Discovery note (not a review finding, found while wiring the scanner): an `AddServiceEndpoint` call nested inside a lambda argument (the `UseServiceModel`/`AddService` chain) is projected through a *second*, previously-unaudited `ProjectOperationDescriptor` call site in `RoslynCallbackBoundaryFactCollector` (the companion-operation path for anonymous/local callback bodies, which have no accepted extracted Method Flow), not only the main per-method operation walk. The host-chain proof was initially wired only to the main walk, so every registration silently reported unproven. | Threaded the scanner's proof (and the project id needed to resolve typed symbols) into `RoslynCallbackBoundaryFactCollector` too, via an additive `AddHostChainProof`/`CompanionTarget.Project`, merged per project the same way `AddProjectContext` already accumulates other per-project state. | `CoreWcfServiceModelProjectionTests.PassCFixtureCompilationProducesTheExactAddServiceEndpointRegistrationFromStartup` (real fixture, real lambda-nested call) | The real `Startup.Configure` → `UseServiceModel` → `AddService().AddServiceEndpoint()` chain (a lambda-nested invocation) now correctly reports `HostChainProven = true` end to end. | None open. |

Verification after this round: `CoreWcfServiceModelProjection\|InterfaceMemberEligibility` (Analysis.Tests) 10/10, `CoreWcf` (FrameworkModels.Tests) 23/23, `CoreWcfServiceOperation` (Scenarios.Tests) 6/6, `CoreWcfServiceOperation` (Wording.Tests) 1/1, `ScenarioRootKind` (Core.Tests) 2/2. Full-suite regression: FrameworkModels.Tests 254/254, Scenarios.Tests 171/171, Wording.Tests 113/113, Core.Tests 82/82, Analysis.Tests 198/208 (the same 10 pre-existing `SD1102` failures as the prior round, on the same unrelated fixtures, confirmed by identical error signatures).

### Fourth pass: evidence-ID-level Conservative assertions

A follow-up request re-issued the full review checklist verbatim; cross-checked against the third round above,
every item was already satisfied except one: the repair trace's claim of "evidence-ID-level assertions... not
certainty-only" for the diagnostic/evidence-union rule referenced tests that did not yet exist. Two were added:

| Finding | Production repair | Producer/boundary test | Observable assertion | Residual boundary |
|---|---|---|---|---|
| The Conservative-certainty test fixture helpers (`ScenarioTestFactory.CreateServiceCapabilityFact`/`CreateServiceRegistrationFact`) set the fact's own `Certainty` field from the caller's parameter but always attached a hard-coded `Exact`-certainty `SourceEvidence` item regardless, so a "Conservative capability" test fixture's own evidence never actually proved Conservative provenance — masking exactly the kind of certainty-vs-evidence mismatch this round's tests were meant to catch. | `CreateServiceCapabilityFact`/`CreateServiceRegistrationFact` now attach the existing `ConservativeEvidence` helper (already present, previously unused by these two factories) whenever the caller requests non-`Exact` certainty, so the fixture's evidence item and its fact-level certainty always agree. | `CoreWcfServiceOperationScenarioTests.ConservativeCapabilityWithoutRegistrationDegradesTheUnsupportedDispatchDiagnosticsCertainty`, `ConservativeRegistrationRetainsBothContributorsExactEvidenceIdsAndOwnCertainty` (new) | A Conservative capability with no registration produces a Conservative (not `Exact`) `SC-SERVICE-UNSUPPORTED-DISPATCH` diagnostic with its exact contributing evidence ID present; a Conservative registration's own evidence item keeps its Conservative certainty when unioned into an admitted root's evidence, and the capability's own evidence ID is present by exact ID. | `ScenarioNode`'s presentation `Certainty` (`CreateNodeWithPresentation`'s `evidence.Min(item => item.Certainty)` over the raw evidence array) is a separate, pre-existing, out-of-scope mechanism from the fact-level weakest-certainty rule — one Exact evidence item in the union still makes the node's own presentation certainty read Exact even when a sibling fact is Conservative. The new tests deliberately assert on the diagnostic's own certainty and individual evidence items' certainty instead of the node's presentation certainty, matching the same boundary already recorded for `AdmittedRootCombinesCapabilityAndRegistrationEvidence` in the first repair round. |

Every other item in the re-issued checklist (7-link host chain and its production-path negatives, typed identity
across every fact/join, `ScenarioRootKind.ServiceOperation`, deterministic multiple-registration with reversed-order
proof, the full vertical fixture, scope discipline, and the PR admission table/changed-paths/requirement-to-test map)
was already complete from the third round; re-verified rather than re-implemented.

Verification after this pass: `CoreWcfServiceOperation` (Scenarios.Tests) 8/8 (was 6/6). Full-suite regression:
Scenarios.Tests 173/173 (was 171/171); FrameworkModels.Tests, Wording.Tests, Core.Tests, and Analysis.Tests
unaffected by this pass's changes (only `ScenarioTestFactory.cs`/`CoreWcfServiceOperationScenarioTests.cs`
touched) and were not re-run in full given no production or shared-fixture code changed.

### P33M maintainer takeover repair trace

Preserved the contributor implementation and merged `origin/main` at `520f6f1` without rewriting history. Repaired
only the declared scanner/model paths: active host admission now requires the exact `Build` followed by supported
`Run`/`RunAsync` on the same operation chain, with Build and terminal evidence; framework links compare the restored
assembly/version/signature identities; and service/fault evidence is selected from the exact typed attribute records
for the admitted family rather than a metadata-name Program Index re-query. The preserved red claims cover the dead
full-host negative and exact typed evidence coexistence. Focused verification passed: Analysis 11/11, FrameworkModels
24/24, Scenarios 8/8, Wording 1/1, Core 2/2. The repair additionally fails closed for empty exact contract or
operation evidence (and suppresses empty-evidence faults), gives hand-built exact attributes deterministic source
evidence unless emptiness is explicitly supplied, requires an ordinary non-abstract Configure candidate, and binds
the terminal's IHost argument to the exact Build receiver of the selected ConfigureWebHostDefaults operation rather
than any nested Build syntax. The restore-only `tests/SeqDoc.Rendering.Tests/packages.lock.json` change was removed.
No budget exception; final gate remains intentionally unrun.

The final P33M self-review repair added two exactness corrections:

| Finding | Production repair | Producer/boundary test | Observable assertion | Residual boundary |
|---|---|---|---|---|
| Fault certainty ignored the exact `[FaultContract]` application's own evidence. | `CoreWcfServiceModel` now computes each fault's certainty as the weakest capability certainty plus that exact typed fault application's evidence, and uses it for both model evidence and `ServiceFaultContractFact.Certainty`. | `CapabilityAndFaultEvidenceUseOnlyExactTypedAttributesOnTheTarget` (extended) | Exact contract/operation contributors produce an Exact capability, while Conservative fault evidence independently lowers the fault fact and its model evidence; foreign evidence remains excluded and empty evidence remains withheld. | None open. |
| `Action<T>` callback identity was admitted by namespace/name/arity alone. | `IsExactActionOfServiceBuilder` and `IsExactActionOfWebHostBuilder` now require `IsExactCoreType` for the finite, versioned `System.Action` facade before checking the exact constructed argument. | Existing host-chain producer coverage and focused Analysis tests | The active host-chain proof continues to admit the exact restored callback identities while lookalike callback identities fail closed. | None open. |

Verification after this final self-review repair: the declared focused command passed — Analysis 11/11,
FrameworkModels 24/24, Scenarios 8/8, Wording 1/1, and Core 2/2; Release solution build succeeded with 0 warnings
and 0 errors; `git diff --check` passed. No budget exception; final gate remains intentionally unrun. Candidate is
ready for independent review.

### Repair rerun 1: P33M-F1–F3

| Finding | Disposition | Production repair | Producer/boundary test | Observable assertion | Residual boundary |
|---|---|---|---|---|---|
| P33M-F1 — nested/uninvoked entry-point callback host chains are admitted | Fixed | `CoreWcfHostChainScanner` now starts only at `Compilation.GetEntryPoint(CancellationToken.None)`, requires source ownership, excludes nested anonymous/local-function bodies during entry-point, startup-callback, and service-model scans, and binds Build/Run discovery to that same operation tree. | `CoreWcfServiceModelProjectionTests.ConfiguredHostChainWithoutBuildOrRunProducesNoRegistrationOrRoot`; updated `Program.cs` uninvoked local-function chain | Only the executed Startup chain produces registration/root evidence; `UnbuiltStartup` and its nested complete chain remain absent. | Unsupported entry-point forms remain fail-closed when Roslyn exposes no source-owned syntax/body. |
| P33M-F2 — client boundaries omit or fail to require exact contract, operation, and generated-marker evidence | Fixed | `CoreWcfServiceModel` now requires nonempty exact typed ServiceContract/OperationContract evidence for every ClientBase member, includes both evidence sets in boundary evidence and weakest certainty, rejects empty generated-marker evidence, and prevents client-shaped types falling through to service capability. | `CoreWcfServiceModelTests.ClientBoundaryRequiresExactContractAndOperationEvidence`; `GeneratedClientWithEmptyMarkerEvidenceFailsClosed`; existing source/generated producer assertions | Empty required evidence produces no client or capability facts; realistic source/generated clients remain admitted with correct classification and evidence. | Other unsupported client metadata remains outside this model scope. |
| P33M-F3 — real Roslyn producer coexistence proof absent and trace overstated it | Fixed | Added a runtime-only temporary foreign assembly, restored/built before extraction, and referenced its DLL under the `foreign` metadata alias; copied repository package/build/SDK identity files needed for the relocated fixture and excluded foreign build sources from the main project. | `CoreWcfServiceModelProjectionTests.RealRoslynCoexistenceKeepsForeignSameQualifiedAttributesOutOfTheDiagram` | Real Roslyn extraction through FrameworkModelHost, ScenarioGraphBuilder, and DocumentationPlanner emits the genuine capability/root/diagram while foreign same-qualified evidence is absent. | Temporary project artifacts are deleted after assertion; no checked-in project or package was added. |

Repair rerun 1 focused verification passed: Release solution build 0 warnings/0 errors; Analysis 12/12, FrameworkModels
26/26, Scenarios 8/8, Wording 1/1, and Core 2/2. The earlier F3 temporary-project failure was repaired by excluding
the foreign source tree from the main SDK project and using a built foreign DLL with an explicit metadata alias; the
final producer test now passes. No budget exception; final full gate remains intentionally unrun. Findings F1–F3 are
fixed and the candidate is returned to `ReviewRequired`.

### Repair rerun 1 self-review correction

The self-review found two evidence-proof gaps before disposition. Client boundary construction now carries each
admitted member's exact contract family through deduplication and filters ServiceContract/OperationContract evidence
by that family before canonical deduplication, ordering, and weakest-certainty calculation. The existing client
evidence test now supplies foreign same-qualified application evidence and proves it is absent while exact IDs and
the conservative exact contributor remain. The real Roslyn coexistence test now locates the exact implemented
interface member, partitions its applications by `CoreWCF.Primitives` and `ForeignAttributes`, records actual producer
evidence IDs, and proves genuine IDs are retained, foreign IDs are absent, certainty remains the weakest genuine
contributor, and the service-operation root/diagram remains admitted. The initial focused rerun exposed the test's
contaminant mistakenly attached to an exact application; that test was corrected to use distinct foreign applications,
then rerun successfully.

Verification after correction: `dotnet test SeqDoc.slnx --no-restore --nologo --filter "FullyQualifiedName~CoreWcf"`
passed Analysis 12/12, FrameworkModels 26/26, Scenarios 8/8, Wording 1/1, and Core 2/2; `git diff --check` passed.
No budget exception; final gate remains intentionally unrun. Candidate is returned to `ReviewRequired`.

## Repair trace: PR #36 (issue-6-wcf-client-outbound-boundaries, closes #6) — maintainer review round

Maintainer Bilaltariq41 reviewed PR #36 (diff a38bc09..8a620d1) and requested changes with 5 Major and 1 Minor finding. All six were repaired in one coherent producer-to-observable round, then independently re-reviewed (reviewer-medium), which found two additional non-blocking items (I6-F-review-2, Minor; I6-F-review-3, Observation) that were also repaired before returning to the maintainer.

| Finding | Production repair | Producer/boundary test | Observable assertion | Residual boundary |
|---|---|---|---|---|
| B1 — no producer-to-observable proof for the full vertical | No production change; existing pipeline was already correct, coverage was the gap | `CoreWcfClientInvocationProjectionTests.RealFixtureCallSiteProducesExactlyOneVisibleClientInvocationMessageThroughScenarioAndPlanner` | Real Roslyn extraction through `CoreWcfServiceModel`, `ScenarioGraphBuilder`, and `DocumentationPlanner` produces exactly one client-invocation message with correct wording and diagram participant/message | None open |
| B2 — conflicting client-boundary anchors silently admitted via `boundaries[0].ClientKind` | `ScenarioGraphBuilder.cs`: added `SC-CLIENT-CONFLICTING-BOUNDARY` diagnostic; withhold the node when `distinctClientKinds.Length > 1` instead of picking `boundaries[0]` | `MultipleAgreeingClientBoundariesStillAdmitOneCoherentNode`, `ConflictingClientKindBoundariesForTheSameClientContractPairWithholdTheNodeAndDiagnoseInstead` | Agreeing boundaries admit one node with the shared `ClientKind`; disagreeing boundaries withhold the node and emit the diagnostic naming both kinds | None open |
| B3 — duplicate/conflicting invocation facts could create multiple nodes for one call site | `ScenarioGraphBuilder.cs`: grouped `admitted` by `InvocationOperation`; added `ClientInvocationFactsAgree` and `SC-CLIENT-CONFLICTING-INVOCATION` diagnostic | `DuplicateAgreeingInvocationFactsForTheSameCallSiteStillAdmitExactlyOneNode`, `ConflictingInvocationFactsForTheSameCallSiteWithholdTheNodeAndDiagnoseInstead` | Duplicate agreeing facts admit exactly one node; disagreeing facts withhold the node and emit the diagnostic naming the call site | None open |
| B4 — unrealistic hand-built negative for the exact-signature/ref-kind boundary | No production change (correct behavior already fails closed); added a real compilable negative fixture | New `CalculatorRefOverloadClient.Add(double, ref double)` in `ClientCallers.cs`; `RefParameterOverloadLookalikeNeverAdmitsAnInvocationThroughTheRealProducer` | The real Roslyn producer admits no `ServiceClientInvocationFact` for the ref-parameter overload call site | None open |
| B5 — repeated-call chronology/determinism unproven at the observable layer | No production change; existing ordering (`BlockOrdinal`/`EvaluationOrdinal` in `ScenarioGraphBuilder`, `SequenceOrdinal` in `DocumentationPlanner`) was already correct | `TwoDistinctClientInvocationCallSitesAdmitDeterministicallyRegardlessOfFrameworkFactInputOrder` (cross-operation, Scenarios.Tests); `TwoSequentialCallsToTheSameOperationOnAStraightLinePathAdmitTwoOrderedNodesRegardlessOfFrameworkFactOrder` (same-operation, Analysis.Tests) | Two real call sites through `ScenarioGraphBuilder`/`DocumentationPlanner` with reversed framework-fact input produce order-independent node identity/evidence/certainty and identical, source-ordered message labels | Closed. `CoreWcfClientInvocationProjectionTests.TwoSequentialCallsToTheSameOperationOnAStraightLinePathAdmitTwoOrderedNodesRegardlessOfFrameworkFactOrder` (new) carries the existing `CallTwice` fixture (`client.Add(a,b)` then `client.Add(c,d)`, same operation, sequential, no branching) through the real Roslyn extraction, `ScenarioGraphBuilder`, and `DocumentationPlanner` pipeline, once forward and once with the framework-fact array fully reversed. Confirms exactly two distinct, correctly-ordered `ClientOperationInvocation` nodes in both directions (the B3 `InvocationOperation`-grouping fix does not conflate genuinely distinct same-operation call sites, since `InvocationOperation` is keyed per call-site syntax node, not per operation name) and that rendered message order stays source-ordered regardless of input order. No production change was needed — the existing `BlockOrdinal`/`EvaluationOrdinal`-based ordering was already correct for this shape; this was a real verification, not an assumed fix. Test passed on first run (9/9 in the containing file, no regression). |
| B6 — declared focused command not reproducible in a clean worktree (NETSDK1004) | None (documentation only) | `docs/work/services/I6/checkpoint.md` Focused Command section | Restoring the four test projects before the first `dotnet test` run succeeds from a clean worktree; re-verified counts recorded (Analysis 8/8, Scenarios 18/18) | None open |
| I6-F-review-2 — `SC-CLIENT-CONFLICTING-BOUNDARY` diagnostic used only `facts[0]`'s evidence/certainty instead of the combined group | `ScenarioGraphBuilder.cs`: evidence now `Combine(Combine(facts.Select(f => f.Evidence)...), Combine(boundaries...))`; certainty now `boundaries...Append(facts.Select(f => f.Certainty).Max()).Max()` | Existing `ConflictingClientKindBoundariesForTheSameClientContractPairWithholdTheNodeAndDiagnoseInstead` (rerun, unchanged pass) | Diagnostic evidence/certainty now reflects the full duplicate-fact group, not just the first fact, in the rare double-conflict overlap case | None open |
| I6-F-review-3 — `ClientInvocationFactsAgree` does not check `Certainty` equality unlike sibling `InvocationFactsAgree` | `ScenarioGraphBuilder.cs`: added an XML doc comment on `ClientInvocationFactsAgree` explaining the deliberate omission (certainty is folded via the weakest-contributor `Max()` rule regardless of agreement, so it cannot strengthen a claim) | N/A — documentation only, no behavior change | Doc comment no longer reads as an unexplained inconsistency with the sibling method | None open |
