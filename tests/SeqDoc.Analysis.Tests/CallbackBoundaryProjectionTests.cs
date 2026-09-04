using System.Collections.Immutable;
using System.Diagnostics;
using SeqDoc.Analysis.Behavior;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Application.Analysis;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.FrameworkModels;
using SeqDoc.FrameworkModels.Workers;
using Xunit;

namespace SeqDoc.Analysis.Tests;

/// <summary>
/// accepted contract risk-based projection tests for the new memory-only <see cref="CallbackBoundaryFactSet"/>.
/// The fixture mirrors the accepted callback-boundary vocabulary: exact anonymous-function,
/// local-function, and method-group callback arguments to a source delegate parameter, a conditional
/// single invoke, a repeated invoke, unresolvable delegate-variable/metadata-only/unsupported shapes,
/// Behavior delegate/event call sites that must stay Unknown, and callback-local return/throw
/// completion. Claim 1 proves the three exact target kinds with SourceBody provenance, ExactlyOnce
/// unconditional cardinality, RejoinsCaller completion, exact evidence, and canonical non-empty member
/// operations (the method-group anchor must resolve through the Program Index); claim 2 proves a single
/// conditional invoke is ZeroOrOne/Conditional with a non-null trigger while a repeated invoke is
/// RepeatedOrUnknown and never ExactlyOnce; claim 3 proves unresolvable shapes never select an exact or
/// first target and never upgrade Behavior delegate/event call sites; claim 4 proves callback-local
/// return rejoins the caller while a throw stays Unknown; claim 5 proves deterministic identity, debug
/// projection, Program Index fingerprint, and canonical members across repeated construction and two
/// relocated git-free roots with no checkout path or raw captured sentinel leak; claim 6 proves a
/// pre-cancelled extraction reports Cancelled; claim 7 runs the accepted BehaviorAnalyzer over the
/// same extraction and proves every delegate/event call site, including the fixture's direct delegate
/// invoke and private event dispatch, stays CallKind.DelegateOrEvent with an Unknown, incomplete,
/// empty-candidate resolution, and that a repeated extraction reproduces an identical Behavior
/// fingerprint (the companion facts never upgrade accepted Behavior delegate/event dispatch).
/// The accepted contract claims extend the metadata-unknown partition: a compiler-bound static metadata callee
/// (for example the FusionCache 2.6.0 GetOrSetAsync extension) may project a target/member boundary
/// from an exact source callback argument even though its source contract body is unavailable, but
/// that boundary stays Unknown in provenance/cardinality/trigger with null contract anchors and
/// never claims a definite source-body contract; a delegate-variable factory never projects a
/// boundary, and repeated/relocated extraction keeps the metadata-boundary ids and debug projection
/// deterministic.
/// </summary>
[Collection(MsBuildIntegrationGroup.Name)]
public sealed class CallbackBoundaryProjectionTests
{
    private const string FixtureRelativePath = "tests/fixtures/AdvancedAnalysis/CallbackBoundaries/CallbackBoundaries.csproj";

    /// <summary>
    /// FusionCache 2.6.0 fixture whose supported GetByIdAsync call binds an exact anonymous factory
    /// to the metadata GetOrSetAsync extension method; the accepted contract metadata-contract claims use it to
    /// prove that a metadata callee projects an exact target with an Unknown contract only.
    /// </summary>
    private const string FusionCacheFixtureRelativePath = "tests/fixtures/AdvancedAnalysis/FusionCacheCallbacks/FusionCacheCallbacks.csproj";

    /// <summary>
    /// regression cross-project solution: the caller and the exact source contract/target live in
    /// separate projects, so resolution must not depend on per-project processing order.
    /// </summary>
    private const string CrossProjectRelativePath = "tests/fixtures/AdvancedAnalysis/CallbackBoundaries/CrossProject/CallbackBoundaries.CrossProject.slnx";

    /// <summary>
    /// Raw captured value the fixture plants inside a callback body; the debug projection must never
    /// serialize it (deterministic, path-free, and without raw captured values).
    /// </summary>
    private const string NeverSerializedSentinel = "987654321";

    private static readonly ImmutableArray<string> FixtureOwnedFiles = [];
    private static readonly ImmutableArray<string> RelocatedOwnedFiles = [];

    /// <summary>
    /// Producer-to-first-observable regression: the real RetryWorker source places anonymous,
    /// local-function, and method-group callbacks inside its admitted retry loop/try context.
    /// Facts alone are insufficient; all three exact boundaries must reach the admitted worker
    /// graph and documentation without flattening callback work into the outer worker.
    /// </summary>
    [Fact]
    public async Task HostedWorkerCallbacksReachScenarioAndDocumentationWithRecoveryPlacement()
    {
        const string relativeProject = "tests/fixtures/PassC/HostedWorkers/HostedWorkers.csproj";
        var root = FindRepositoryRoot();
        var profile = CompilationProfile.Create(relativeProject, "Release", "net10.0");
        var result = await new RoslynProfileAnalysisExtractor().ExtractAsync(
            new CompilationAnalysisRequest(root, Path.Combine(root, relativeProject.Replace('/', Path.DirectorySeparatorChar)), profile),
            CancellationToken.None);
        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.TechnicalCause)));
        var extraction = Assert.IsType<ProfileAnalysisExtraction>(result.Value);
        var retryType = extraction.ProgramIndex.Types.Single(type => type.MetadataName == "HostedWorkers.RetryWorker").Id;
        var execute = extraction.ProgramIndex.Methods.Single(method => method.ContainingType == retryType && method.Name == "ExecuteAsync").Id;
        var boundaries = extraction.CallbackBoundaryFacts.Boundaries.Where(boundary => boundary.CallerMethod == execute).ToArray();

        var behavior = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(extraction.ProgramIndex, extraction.BehaviorInput), CancellationToken.None);
        Assert.True(behavior.IsSuccess, string.Join(Environment.NewLine, behavior.Diagnostics.Select(item => item.TechnicalCause)));
        var frameworks = await new FrameworkModelHost([new HostedWorkerModel(), new SchedulerModel()]).AnalyzeAsync(
            new FrameworkAnalysisRequest(
                new FrameworkDetectionContext(profile, extraction.ProgramIndex),
                new FrameworkAnalysisContext(profile, extraction.ProgramIndex, extraction.CallbackBoundaryFacts),
                extraction.Operations,
                extraction.Symbols),
            CancellationToken.None);
        var behaviorSnapshot = Assert.IsType<BehaviorSnapshot>(behavior.Value);
        var retryFlow = behaviorSnapshot.MethodFlows.Single(flow => flow.Method == execute);
        var catchRegion = Assert.Single(retryFlow.Regions, region => region.Kind == FlowRegionKind.Catch);
        var catchBoundaries = boundaries.Where(boundary => retryFlow.Nodes.OfType<InvocationFlowNode>().Any(node =>
            node.Operation == boundary.OuterInvocationOperation
            && node.BlockOrdinal >= catchRegion.StartBlockOrdinal
            && node.BlockOrdinal <= catchRegion.EndBlockOrdinal)).ToArray();
        Assert.Single(catchBoundaries);
        var originalTryBoundaries = boundaries.Where(boundary => boundary.Cardinality == CallbackCardinality.ExactlyOnce
                && boundary.Trigger == CallbackTriggerKind.Unconditional
                && !catchBoundaries.Contains(boundary)).ToArray();
        Assert.Equal(3, originalTryBoundaries.Length);
        Assert.Equal(
            [CallbackTargetKind.AnonymousFunction, CallbackTargetKind.LocalFunction, CallbackTargetKind.MethodGroup],
            originalTryBoundaries.Select(boundary => boundary.TargetKind).OrderBy(kind => kind));
        Assert.All(originalTryBoundaries, boundary =>
        {
            Assert.Equal(CallbackContractProvenance.SourceBody, boundary.ContractProvenance);
            Assert.NotEmpty(boundary.MemberOperations);
            Assert.NotEmpty(boundary.Evidence);
        });
        var graphs = ScenarioGraphBuilder.Build(new ScenarioAnalysisRequest(
            profile,
            extraction.ProgramIndex,
            behaviorSnapshot,
            frameworks,
            extraction.SemanticFacts,
            extraction.DependencyInjectionFacts,
            extraction.StructuralResultFacts,
            extraction.NonGetSemanticFacts,
            extraction.ConditionalDependencyInjectionFacts,
            extraction.ConfigurationSemanticFacts,
            extraction.CallbackBoundaryFacts,
            extraction.PredicateSemanticFacts,
            extraction.MinimalApiHandlerFacts));
        var graph = Assert.Single(graphs.Graphs, item => item.RootKind == ScenarioRootKind.HostedWorker && item.OperationKey.Contains("RetryWorker", StringComparison.Ordinal));
        Assert.Equal(3, graph.CallbackRegions.Length);
        var unregisteredExecute = extraction.ProgramIndex.Methods.Single(method =>
            method.ContainingType == extraction.ProgramIndex.Types.Single(type => type.MetadataName == "HostedWorkers.UnregisteredWorker").Id
            && method.Name == "ExecuteCallbackAsync").Id;
        Assert.Single(extraction.CallbackBoundaryFacts.Boundaries, boundary => boundary.CallerMethod == unregisteredExecute);
        Assert.DoesNotContain(graphs.Graphs, candidate => candidate.RootKind == ScenarioRootKind.HostedWorker
            && candidate.OperationKey.Contains("UnregisteredWorker", StringComparison.Ordinal));
        Assert.All(graph.CallbackRegions, region =>
        {
            Assert.NotEmpty(region.MemberNodes);
            foreach (var member in region.MemberNodes)
            {
                var placements = graph.Topology.FlowPlacements.Where(placement => placement.ScenarioNode == member).ToArray();
                Assert.Single(placements);
                var placement = placements[0];
                Assert.Equal(execute, placement.Method);
                Assert.NotEmpty(placement.Containers);
                Assert.All(placement.Containers, container =>
                    Assert.Contains(graph.Topology.FlowContainers, candidate => candidate.Region == container && candidate.Method == execute));
                Assert.Contains(placement.Containers, container => graph.Topology.FlowContainers.Any(candidate =>
                    candidate.Region == container && candidate.Method == execute
                    && candidate.Kind is ScenarioFlowContainerKind.TryRegion or ScenarioFlowContainerKind.TryAndCatchRegion));
            }
            Assert.All(region.MemberNodes, member => Assert.Contains(graph.Nodes, node => node.Id == member));
            Assert.All(region.Evidence, evidence => Assert.NotEqual(CertaintyLevel.Unknown, evidence.Certainty));
        });
        Assert.Contains(graph.Topology.FlowContainers, container =>
            container.Method == execute && container.Kind == ScenarioFlowContainerKind.NaturalLoop);
        Assert.DoesNotContain(graph.Nodes, node => graph.CallbackRegions.SelectMany(region => region.MemberNodes).Contains(node.Id)
            && node.Presentation?.HostedWorkerControlKind is HostedWorkerControlKind.TerminalOutcome
                or HostedWorkerControlKind.ReturnBoundary
                or HostedWorkerControlKind.ThrowBoundary);
        var documentation = DocumentationPlanner.Plan(graph);
        Assert.NotEmpty(documentation.Wording.Phrases);
        Assert.NotEmpty(documentation.Diagram.Messages);
        var callbackMemberIds = graph.CallbackRegions.SelectMany(region => region.MemberNodes).Distinct().ToArray();
        var callbackMessages = documentation.Diagram.Messages
            .Where(message => message.Label == "source callback operation")
            .ToArray();
        Assert.Equal(callbackMemberIds.Length, callbackMessages.Length);
        var loopReferences = documentation.Diagram.Sequence.Fragments.SelectMany(LoopReferences).ToHashSet();
        Assert.All(callbackMessages, message => Assert.Contains(message.Id, loopReferences));
        Assert.DoesNotContain(documentation.Diagram.Sequence.MessageRefs, message => callbackMessages.Any(callback => callback.Id == message));
        var allReferences = documentation.Diagram.Sequence.MessageRefs.Concat(loopReferences).ToArray();
        Assert.Equal(allReferences.Length, allReferences.Distinct().Count());
        var retryLoop = documentation.Diagram.Sequence.Fragments
            .SelectMany(FlattenFragments)
            .Single(fragment => fragment.Kind == DiagramFragmentKind.Loop);
        Assert.Equal("Retry", retryLoop.Label);
        Assert.All(callbackMessages, message => Assert.Equal(1, loopReferences.Count(reference => reference == message.Id)));
        var executeFlow = behaviorSnapshot.MethodFlows.Single(method => method.Method == execute);
        Assert.NotEmpty(executeFlow.CatchContinuations);
        Assert.Contains(graph.Nodes, node => node.Presentation?.HostedWorkerControlKind == HostedWorkerControlKind.CatchLoopContinuation
            && node.Presentation.HostedWorkerFlowRegion is not null);
        var repeatedDocumentation = DocumentationPlanner.Plan(graph);
        Assert.Equal(documentation.Diagram.DebugProjection, repeatedDocumentation.Diagram.DebugProjection);
        Assert.Equal(documentation.Wording.DebugProjection, repeatedDocumentation.Wording.DebugProjection);
        Assert.DoesNotContain(documentation.Wording.Phrases, phrase => phrase.Text.Contains("runtime", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(documentation.Wording.Phrases, phrase => phrase.Text.Contains("persist", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HostedWorkerConditionalAndRepeatedCallbacksRemainVisibleAtProducerButAreWithheldFromOutput()
    {
        var request = await CreateHostedWorkerRequestAsync();
        var retryType = request.ProgramIndex.Types.Single(type => type.MetadataName == "HostedWorkers.RetryWorker").Id;
        var execute = request.ProgramIndex.Methods.Single(method => method.ContainingType == retryType && method.Name == "ExecuteAsync").Id;
        var boundaries = request.CallbackBoundaryFacts!.Boundaries.Where(boundary => boundary.CallerMethod == execute).ToArray();
        var conditional = Assert.Single(boundaries, boundary => boundary.Cardinality == CallbackCardinality.ZeroOrOne);
        var repeated = Assert.Single(boundaries, boundary => boundary.Cardinality == CallbackCardinality.RepeatedOrUnknown);
        Assert.Equal(CallbackTriggerKind.Conditional, conditional.Trigger);
        Assert.NotNull(conditional.TriggerCondition);
        Assert.NotEqual(CallbackCardinality.ExactlyOnce, repeated.Cardinality);
        Assert.NotEmpty(conditional.MemberOperations);
        Assert.NotEmpty(repeated.MemberOperations);
        var affectedMemberOperations = conditional.MemberOperations
            .Concat(repeated.MemberOperations)
            .ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(affectedMemberOperations);

        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs,
            candidate => candidate.OperationKey.Contains("RetryWorker", StringComparison.Ordinal));
        Assert.DoesNotContain(graph.CallbackRegions, region => region.BoundaryId == conditional.Id || region.BoundaryId == repeated.Id);
        var affectedNodeIds = graph.Nodes
            .Where(node => node.Operation is { } operation && affectedMemberOperations.Contains(operation.Value))
            .Select(node => node.Id)
            .ToHashSet();
        Assert.Empty(affectedNodeIds);
        var plan = DocumentationPlanner.Plan(graph);
        var affectedMessageIds = graph.Edges
            .Where(edge => affectedNodeIds.Contains(edge.Source) || affectedNodeIds.Contains(edge.Target))
            .Select(edge => new DiagramPlanElementId("diagram-element:v1:message:" + edge.Id.Value))
            .ToHashSet();
        Assert.DoesNotContain(plan.Diagram.Messages, message => affectedMessageIds.Contains(message.Id));
        Assert.Contains(graph.Diagnostics, diagnostic => diagnostic.Code == "SC-WORKER-UNSUPPORTED-PLACEMENT"
            && diagnostic.Detail.Contains(conditional.Id.Value, StringComparison.Ordinal));
        Assert.Contains(graph.Diagnostics, diagnostic => diagnostic.Code == "SC-WORKER-UNSUPPORTED-PLACEMENT"
            && diagnostic.Detail.Contains(repeated.Id.Value, StringComparison.Ordinal));
    }

    [Fact]
    public async Task HostedWorkerCatchCallbackIsProducerVisibleButWithheldFromScenarioDocumentation()
    {
        var request = await CreateHostedWorkerRequestAsync();
        var retryType = request.ProgramIndex.Types.Single(type => type.MetadataName == "HostedWorkers.RetryWorker").Id;
        var execute = request.ProgramIndex.Methods.Single(method => method.ContainingType == retryType && method.Name == "ExecuteAsync").Id;
        var flow = request.Behavior.MethodFlows.Single(item => item.Method == execute);
        var catchRegion = Assert.Single(flow.Regions, region => region.Kind == FlowRegionKind.Catch);
        var catchBoundary = Assert.Single(request.CallbackBoundaryFacts!.Boundaries.Where(boundary =>
            boundary.CallerMethod == flow.Method && flow.Nodes.OfType<InvocationFlowNode>().Any(node =>
                node.Operation == boundary.OuterInvocationOperation
                && node.BlockOrdinal >= catchRegion.StartBlockOrdinal
                && node.BlockOrdinal <= catchRegion.EndBlockOrdinal)));
        Assert.Equal(CallbackCardinality.ExactlyOnce, catchBoundary.Cardinality);
        Assert.NotEmpty(catchBoundary.MemberOperations);
        Assert.NotEmpty(catchBoundary.Evidence);

        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs,
            candidate => candidate.OperationKey.Contains("RetryWorker", StringComparison.Ordinal));
        Assert.DoesNotContain(graph.CallbackRegions, region => region.BoundaryId == catchBoundary.Id);
        Assert.DoesNotContain(graph.Nodes, node => node.Operation is { } operation
            && catchBoundary.MemberOperations.Contains(operation.Value));
        var plan = DocumentationPlanner.Plan(graph);
        Assert.Equal(graph.CallbackRegions.SelectMany(region => region.MemberNodes).Distinct().Count(),
            plan.Diagram.Messages.Count(message => message.Label == "source callback operation"));
        Assert.Contains(graph.Diagnostics, diagnostic => diagnostic.Code == "SC-WORKER-UNSUPPORTED-PLACEMENT"
            && diagnostic.Detail.Contains(catchBoundary.Id.Value, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SameProfileOuterOperationOverlapWithholdsCallbackOwnershipDeterministically()
    {
        var request = await CreateHostedWorkerRequestAsync();
        var retryType = request.ProgramIndex.Types.Single(type => type.MetadataName == "HostedWorkers.RetryWorker").Id;
        var execute = request.ProgramIndex.Methods.Single(method => method.ContainingType == retryType && method.Name == "ExecuteAsync").Id;
        var flow = request.Behavior.MethodFlows.Single(item => item.Method == execute);
        var catchRegion = Assert.Single(flow.Regions, region => region.Kind == FlowRegionKind.Catch);
        var catchOperations = flow.Nodes.OfType<InvocationFlowNode>()
            .Where(node => node.BlockOrdinal >= catchRegion.StartBlockOrdinal && node.BlockOrdinal <= catchRegion.EndBlockOrdinal)
            .Select(node => node.Operation)
            .ToHashSet();
        var boundary = request.CallbackBoundaryFacts!.Boundaries
            .Where(candidate => candidate.Cardinality == CallbackCardinality.ExactlyOnce
                && candidate.Trigger == CallbackTriggerKind.Unconditional
                && candidate.CallerMethod == execute
                && candidate.TargetKind == CallbackTargetKind.LocalFunction
                && !catchOperations.Contains(candidate.OuterInvocationOperation))
            .Single();
        var originalOuterFlowNode = Assert.Single(flow.Nodes.OfType<InvocationFlowNode>(),
            node => node.Operation == boundary.OuterInvocationOperation);
        var originalGraph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs,
            candidate => candidate.OperationKey.Contains("RetryWorker", StringComparison.Ordinal));
        var naturallyProducedOuterNodes = originalGraph.Nodes
            .Where(node => node.Operation == boundary.OuterInvocationOperation)
            .ToArray();
        var overlapped = new CallbackBoundaryFact(boundary.Id, boundary.CallerMethod, boundary.OuterInvocationOperation,
            boundary.ParameterOrdinal, boundary.TargetKind, boundary.TargetMethod, boundary.TargetBodyOperation,
            boundary.ContractMethod, boundary.ContractInvokeOperation, boundary.Cardinality, boundary.Trigger,
            boundary.TriggerCondition, boundary.Completion, boundary.ContractProvenance,
            boundary.MemberOperations.Add(boundary.OuterInvocationOperation.Value), boundary.Evidence, boundary.Certainty);
        var facts = new CallbackBoundaryFactSet(1, "producer-regression", request.Profile, request.ProgramIndex.IndexFingerprint,
            request.CallbackBoundaryFacts.Boundaries.Select(item => item.Id == boundary.Id ? overlapped : item).ToImmutableArray(),
            request.CallbackBoundaryFacts.Diagnostics, "same-profile-outer-overlap");
        var mutatedRequest = request with { CallbackBoundaryFacts = facts };
        var mutatedFlow = mutatedRequest.Behavior.MethodFlows.Single(item => item.Method == execute);
        var mutatedOuterFlowNode = Assert.Single(mutatedFlow.Nodes.OfType<InvocationFlowNode>(),
            node => node.Operation == boundary.OuterInvocationOperation);
        Assert.Equal(originalOuterFlowNode.Id, mutatedOuterFlowNode.Id);
        Assert.Equal(originalOuterFlowNode.Operation, mutatedOuterFlowNode.Operation);
        Assert.Equal(originalOuterFlowNode.BlockOrdinal, mutatedOuterFlowNode.BlockOrdinal);

        var graph = Assert.Single(ScenarioGraphBuilder.Build(mutatedRequest).Graphs,
            candidate => candidate.OperationKey.Contains("RetryWorker", StringComparison.Ordinal));
        var outerNodes = graph.Nodes.Where(node => node.Operation == boundary.OuterInvocationOperation).ToArray();
        Assert.All(outerNodes, node => Assert.DoesNotContain($"callback:{boundary.Id.Value}:", node.Key, StringComparison.Ordinal));
        if (naturallyProducedOuterNodes.Length > 0)
        {
            Assert.Equal(
                naturallyProducedOuterNodes.Select(node => node.Id),
                outerNodes.Where(node => !node.Key.StartsWith($"callback:{boundary.Id.Value}:", StringComparison.Ordinal)).Select(node => node.Id));
        }
        var outerNodeIds = outerNodes.Select(node => node.Id).ToHashSet();
        Assert.DoesNotContain(graph.CallbackRegions, region => region.MemberNodes.Any(outerNodeIds.Contains));
        var callbackLocalOperations = boundary.MemberOperations
            .Where(operation => !string.Equals(operation, boundary.OuterInvocationOperation.Value, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(callbackLocalOperations);
        Assert.DoesNotContain(graph.Nodes, node => node.Operation is { } operation
            && callbackLocalOperations.Contains(operation.Value));
        Assert.DoesNotContain(graph.Nodes, node => callbackLocalOperations.Any(operation =>
            node.Key == $"callback:{boundary.Id.Value}:{operation}"));
        Assert.Contains(graph.Diagnostics, diagnostic => diagnostic.Code == "SC-CALLBACK-OUTER-OVERLAP"
            && diagnostic.Detail.Contains(boundary.Id.Value, StringComparison.Ordinal));

        var reversedFacts = new CallbackBoundaryFactSet(
            facts.SchemaVersion,
            facts.ProducerVersion,
            facts.Profile,
            facts.ProgramIndexFingerprint,
            facts.Boundaries.Reverse().ToImmutableArray(),
            facts.Diagnostics,
            "same-profile-outer-overlap-reversed");
        var reversed = Assert.Single(ScenarioGraphBuilder.Build(request with { CallbackBoundaryFacts = reversedFacts }).Graphs,
            candidate => candidate.OperationKey.Contains("RetryWorker", StringComparison.Ordinal));
        Assert.Equal(graph.DebugProjection, reversed.DebugProjection);
        Assert.Equal(
            graph.Diagnostics.Select(diagnostic => $"{diagnostic.Code}|{diagnostic.Detail}|{diagnostic.Certainty}").Order(StringComparer.Ordinal),
            reversed.Diagnostics.Select(diagnostic => $"{diagnostic.Code}|{diagnostic.Detail}|{diagnostic.Certainty}").Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("fingerprint")]
    public async Task HostedWorkerCallbacksAreWithheldWhenBehaviorSnapshotIsStale(string stalePart)
    {
        var request = await CreateHostedWorkerRequestAsync();
        var behavior = stalePart == "profile"
            ? request.Behavior with
            {
                Profile = CompilationProfile.Create("tests/fixtures/foreign/Foreign.csproj", "Release", "net10.0"),
            }
            : request.Behavior with { ProgramIndexFingerprint = "foreign-behavior-fingerprint" };

        var graph = Assert.Single(ScenarioGraphBuilder.Build(request with { Behavior = behavior }).Graphs,
            candidate => candidate.RootKind == ScenarioRootKind.HostedWorker
                && candidate.OperationKey.Contains("RetryWorker", StringComparison.Ordinal));
        Assert.Empty(graph.CallbackRegions);
        Assert.DoesNotContain(graph.Nodes, node => node.Detail == "source callback operation");
        Assert.DoesNotContain(graph.Topology.FlowPlacements, placement => placement.ScenarioNode.Value.Contains("callback", StringComparison.Ordinal));
        Assert.DoesNotContain(DocumentationPlanner.Plan(graph).Diagram.Messages, message => message.Label == "source callback operation");
    }

    [Theory]
    [InlineData("missing-recovery")]
    [InlineData("filter")]
    [InlineData("finally")]
    [InlineData("nested-outer")]
    [InlineData("missing-outer")]
    [InlineData("duplicate-outer")]
    public async Task HostedWorkerCallbacksFailClosedForUnrepresentableRecoveryPlacement(string placementKind)
    {
        var request = await CreateHostedWorkerRequestAsync();
        var retryType = request.ProgramIndex.Types.Single(type => type.MetadataName == "HostedWorkers.RetryWorker").Id;
        var callbackCaller = request.ProgramIndex.Methods
            .Single(method => method.ContainingType == retryType && method.Name == "ExecuteAsync")
            .Id;
        var flow = request.Behavior.MethodFlows.Single(method => method.Method == callbackCaller);
        var exactOuterOperations = request.CallbackBoundaryFacts!.Boundaries
            .Where(boundary => boundary.CallerMethod == callbackCaller)
            .Select(boundary => boundary.OuterInvocationOperation)
            .ToHashSet();
        var flowNodes = flow.Nodes;
        if (placementKind == "missing-outer")
        {
            flowNodes = flowNodes
                .Where(node => node is not InvocationFlowNode invocation || !exactOuterOperations.Contains(invocation.Operation))
                .ToImmutableArray();
        }
        else if (placementKind == "duplicate-outer")
        {
            flowNodes = flowNodes
                .SelectMany(node =>
                {
                    if (node is InvocationFlowNode invocation && exactOuterOperations.Contains(invocation.Operation))
                    {
                        return new FlowNode[] { node, node with { Id = new FlowNodeId($"{node.Id.Value}:duplicate") } };
                    }

                    return new FlowNode[] { node };
                })
                .ToImmutableArray();
        }
        var regions = placementKind == "missing-recovery"
            ? flow.Regions.Where(region => region.Kind == FlowRegionKind.NaturalLoop).ToImmutableArray()
            : placementKind is "missing-outer" or "duplicate-outer"
                ? flow.Regions
            : flow.Regions.Select(region => region.Kind == FlowRegionKind.NaturalLoop
                ? region
                : region with
                {
                    Kind = placementKind switch
                    {
                        "filter" => FlowRegionKind.Filter,
                        "finally" => FlowRegionKind.Finally,
                        _ => FlowRegionKind.TryAndFinally,
                    },
                    Parent = placementKind == "nested-outer" ? region.Parent : null,
                }).ToImmutableArray();
        var behavior = request.Behavior with
        {
            MethodFlows = request.Behavior.MethodFlows
                .Select(candidate => candidate.Method == flow.Method ? flow with { Nodes = flowNodes, Regions = regions } : candidate)
                .ToImmutableArray(),
        };

        var graph = Assert.Single(ScenarioGraphBuilder.Build(request with { Behavior = behavior }).Graphs,
            candidate => candidate.RootKind == ScenarioRootKind.HostedWorker
                && candidate.OperationKey.Contains("RetryWorker", StringComparison.Ordinal));
        Assert.Empty(graph.CallbackRegions);
        Assert.DoesNotContain(graph.Nodes, node => node.Detail == "source callback operation");
        var placementDiagnostics = graph.Diagnostics
            .Where(diagnostic => diagnostic.Code == "SC-WORKER-UNSUPPORTED-PLACEMENT"
                && diagnostic.Detail.Contains("callback-boundary=", StringComparison.Ordinal))
            .OrderBy(diagnostic => diagnostic.Id.Value, StringComparer.Ordinal)
            .ToArray();
        var exactCallbackMemberOperations = request.CallbackBoundaryFacts!.Boundaries
            .Where(boundary => boundary.CallerMethod == callbackCaller)
            .SelectMany(boundary => boundary.MemberOperations)
            .ToArray();
        Assert.Equal(exactCallbackMemberOperations.Length, placementDiagnostics.Length);
        Assert.Equal(exactCallbackMemberOperations.Length, exactCallbackMemberOperations.Distinct().Count());
        Assert.Equal(exactCallbackMemberOperations.Length, placementDiagnostics.Select(diagnostic => diagnostic.Id).Distinct().Count());
        Assert.Equal(exactCallbackMemberOperations.Length, placementDiagnostics.Select(diagnostic => diagnostic.Detail).Distinct().Count());

        if (placementKind == "missing-recovery")
        {
            var sourceFacts = request.CallbackBoundaryFacts!;
            var reversedFacts = new CallbackBoundaryFactSet(
                sourceFacts.SchemaVersion,
                sourceFacts.ProducerVersion,
                sourceFacts.Profile,
                sourceFacts.ProgramIndexFingerprint,
                sourceFacts.Boundaries.Reverse().ToImmutableArray(),
                sourceFacts.Diagnostics,
                "hosted-worker-callbacks-reversed");
            var reversed = Assert.Single(ScenarioGraphBuilder.Build(request with
            {
                Behavior = behavior,
                CallbackBoundaryFacts = reversedFacts,
            }).Graphs, candidate => candidate.RootKind == ScenarioRootKind.HostedWorker
                && candidate.OperationKey.Contains("RetryWorker", StringComparison.Ordinal));
            var reversedDiagnostics = reversed.Diagnostics
                .Where(diagnostic => diagnostic.Code == "SC-WORKER-UNSUPPORTED-PLACEMENT"
                    && diagnostic.Detail.Contains("callback-boundary=", StringComparison.Ordinal))
                .OrderBy(diagnostic => diagnostic.Id.Value, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(placementDiagnostics.Select(diagnostic => diagnostic.Id), reversedDiagnostics.Select(diagnostic => diagnostic.Id));
            Assert.Equal(placementDiagnostics.Select(diagnostic => diagnostic.Detail), reversedDiagnostics.Select(diagnostic => diagnostic.Detail));
        }
    }

    private static IEnumerable<DiagramPlanElementId> LoopReferences(DiagramFragment fragment)
        => fragment.MessageRefs.Concat(fragment.Fragments.SelectMany(LoopReferences));

    private static IEnumerable<DiagramFragment> FlattenFragments(DiagramFragment fragment)
    {
        yield return fragment;
        foreach (var child in fragment.Fragments.SelectMany(FlattenFragments))
        {
            yield return child;
        }
    }

    private static async Task<ScenarioAnalysisRequest> CreateHostedWorkerRequestAsync()
    {
        const string relativeProject = "tests/fixtures/PassC/HostedWorkers/HostedWorkers.csproj";
        var root = FindRepositoryRoot();
        var profile = CompilationProfile.Create(relativeProject, "Release", "net10.0");
        var result = await new RoslynProfileAnalysisExtractor().ExtractAsync(
            new CompilationAnalysisRequest(root, Path.Combine(root, relativeProject.Replace('/', Path.DirectorySeparatorChar)), profile),
            CancellationToken.None);
        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.TechnicalCause)));
        var extraction = Assert.IsType<ProfileAnalysisExtraction>(result.Value);
        var behavior = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(extraction.ProgramIndex, extraction.BehaviorInput), CancellationToken.None);
        Assert.True(behavior.IsSuccess, string.Join(Environment.NewLine, behavior.Diagnostics.Select(item => item.TechnicalCause)));
        var frameworks = await new FrameworkModelHost([new HostedWorkerModel(), new SchedulerModel()]).AnalyzeAsync(
            new FrameworkAnalysisRequest(
                new FrameworkDetectionContext(profile, extraction.ProgramIndex),
                new FrameworkAnalysisContext(profile, extraction.ProgramIndex, extraction.CallbackBoundaryFacts),
                extraction.Operations, extraction.Symbols), CancellationToken.None);
        return new ScenarioAnalysisRequest(profile, extraction.ProgramIndex, Assert.IsType<BehaviorSnapshot>(behavior.Value),
            frameworks, extraction.SemanticFacts, extraction.DependencyInjectionFacts, extraction.StructuralResultFacts,
            extraction.NonGetSemanticFacts, extraction.ConditionalDependencyInjectionFacts, extraction.ConfigurationSemanticFacts,
            extraction.CallbackBoundaryFacts, extraction.PredicateSemanticFacts, extraction.MinimalApiHandlerFacts);
    }

    /// <summary>
    /// Claim 1: the three exact source callback targets project with their exact target kind, SourceBody
    /// contract provenance, ExactlyOnce unconditional cardinality, RejoinsCaller completion, Exact
    /// certainty over non-empty evidence, and canonical non-empty member operations. The method-group
    /// boundary must resolve its exact parameterless overload through the Program Index rather than
    /// guessing or picking a candidate by position.
    /// </summary>
    [Theory]
    [InlineData("InvokeCapturingLambda", CallbackTargetKind.AnonymousFunction)]
    [InlineData("InvokeLocalFunction", CallbackTargetKind.LocalFunction)]
    [InlineData("InvokeOverloadedMethodGroup", CallbackTargetKind.MethodGroup)]
    public async Task ExactCallbackArgumentsProjectExactTargetKindAndSourceBoundaryFacts(
        string callerName,
        CallbackTargetKind expectedTargetKind)
    {
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.CallbackBoundaryFacts;

        var caller = FindCaller(extraction, callerName);
        var boundary = Assert.Single(facts.Boundaries, candidate => candidate.CallerMethod == caller);

        Assert.Equal(expectedTargetKind, boundary.TargetKind);
        Assert.Equal(CallbackContractProvenance.SourceBody, boundary.ContractProvenance);
        Assert.Equal(CallbackCardinality.ExactlyOnce, boundary.Cardinality);
        Assert.Equal(CallbackTriggerKind.Unconditional, boundary.Trigger);
        Assert.Null(boundary.TriggerCondition);
        Assert.Equal(CallbackCompletionKind.RejoinsCaller, boundary.Completion);
        Assert.Equal(CertaintyLevel.Exact, boundary.Certainty);
        AssertEvidence(boundary.Evidence, boundary.Certainty);
        Assert.NotEmpty(boundary.MemberOperations);
        Assert.All(boundary.MemberOperations, member => Assert.False(string.IsNullOrWhiteSpace(member)));

        // The method-group anchor must resolve to one exact Program Index method (the parameterless
        // overload); the other two kinds anchor their exact source body instead.
        if (expectedTargetKind == CallbackTargetKind.MethodGroup)
        {
            Assert.NotNull(boundary.TargetMethod);
            Assert.Contains(extraction.ProgramIndex.Methods, method => method.Id == boundary.TargetMethod);
        }
    }

    /// <summary>
    /// Claim 2b: a method-group callback whose accepted body exposes no flattenable member operations
    /// carries no authoritative member set, so no boundary is projected and extraction still succeeds.
    /// The boundary fails closed exactly like a target whose body was never extracted; it must never
    /// crash behavior extraction or project an identity over an empty member set. Accepted exact
    /// boundaries elsewhere in the fixture remain untouched (no over-broad skipping).
    /// </summary>
    [Fact]
    public async Task EmptyMethodGroupCallbackTargetFailsClosedWithoutCrashingExtraction()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.CallbackBoundaryFacts;

        var caller = FindCaller(extraction, "InvokeEmptyMethodGroup");
        Assert.DoesNotContain(facts.Boundaries, candidate => candidate.CallerMethod == caller);

        // The accepted method-group boundary in the same fixture still projects with its
        // non-empty canonical member set; skipping is scoped to member-less targets only.
        var overloadCaller = FindCaller(extraction, "InvokeOverloadedMethodGroup");
        var overloadBoundary = Assert.Single(facts.Boundaries, candidate => candidate.CallerMethod == overloadCaller);
        Assert.Equal(CallbackTargetKind.MethodGroup, overloadBoundary.TargetKind);
        Assert.NotEmpty(overloadBoundary.MemberOperations);
    }

    /// <summary>
    /// Claim 2: a single direct conditional invoke projects ZeroOrOne with a Conditional trigger and a
    /// non-null trigger condition anchor, while a repeated/twice invoke projects RepeatedOrUnknown and
    /// never ExactlyOnce. Cardinality is never inferred from the callback argument alone.
    /// </summary>
    [Theory]
    [InlineData("InvokeRunWhen", CallbackCardinality.ZeroOrOne)]
    [InlineData("InvokeTwice", CallbackCardinality.RepeatedOrUnknown)]
    public async Task ConditionalAndRepeatedCallbackInvokesProjectConservativeCardinality(
        string callerName,
        CallbackCardinality expectedCardinality)
    {
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.CallbackBoundaryFacts;

        var caller = FindCaller(extraction, callerName);
        var boundary = Assert.Single(facts.Boundaries, candidate => candidate.CallerMethod == caller);

        Assert.Equal(expectedCardinality, boundary.Cardinality);
        Assert.NotEqual(CallbackCardinality.ExactlyOnce, boundary.Cardinality);
        if (expectedCardinality == CallbackCardinality.ZeroOrOne)
        {
            Assert.Equal(CallbackTriggerKind.Conditional, boundary.Trigger);
            Assert.NotNull(boundary.TriggerCondition);
        }
    }

    /// <summary>
    /// Claim 3: delegate-variable, unsupported-shape, and Behavior delegate/event call sites never
    /// project an exact target and never select the first candidate. The collector fails closed (no
    /// boundary) or emits an explicit Unknown boundary with no target anchor; Behavior's own
    /// delegate/event resolution stays Unknown with incomplete candidates as pinned by
    /// CallResolverTests. The metadata-only Task.Run caller is covered by the accepted contract metadata-contract
    /// claim instead: it now projects an exact anonymous target with an Unknown contract rather than
    /// failing closed.
    /// </summary>
    [Theory]
    [InlineData("InvokeDelegateVariable")]
    [InlineData("InvokeUnsupported")]
    [InlineData("InvokeBehaviorDelegate")]
    [InlineData("InvokeBehaviorEvent")]
    public async Task UnresolvableCallbackArgumentsNeverSelectExactOrFirstTarget(string callerName)
    {
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.CallbackBoundaryFacts;

        var caller = FindCaller(extraction, callerName);
        Assert.DoesNotContain(
            facts.Boundaries,
            boundary => boundary.CallerMethod == caller
                && (boundary.TargetKind != CallbackTargetKind.Unknown || boundary.TargetMethod is not null));
    }

    /// <summary>
    /// Claim 7: the accepted <see cref="BehaviorAnalyzer"/> over the same CallbackBoundaries fixture
    /// keeps every delegate/event call site (including the direct delegate invoke and the private
    /// event dispatch) as <see cref="CallKind.DelegateOrEvent"/> with an Unknown, incomplete,
    /// empty-candidate resolution, and a repeated extraction reproduces an identical Behavior
    /// fingerprint. This pins the delegate-unknown and fingerprint-preservation evidence required by
    /// accepted contract without touching the excluded golden files.
    /// </summary>
    [Fact]
    public async Task BehaviorDelegateAndEventSitesStayUnknownWithStableFingerprint()
    {
        var first = await ExtractSuccessfullyAsync();
        var firstSnapshot = await AnalyzeBehaviorSuccessfullyAsync(first);
        var delegateOrEventSites = firstSnapshot.CallGraph.CallSites
            .Where(site => site.Kind == CallKind.DelegateOrEvent);

        var behaviorDelegateCaller = FindCaller(first, "InvokeBehaviorDelegate");
        var behaviorEventCaller = FindCaller(first, "InvokeBehaviorEvent");
        Assert.Contains(delegateOrEventSites, site => site.ContainingMethod == behaviorDelegateCaller);
        Assert.Contains(delegateOrEventSites, site => site.ContainingMethod == behaviorEventCaller);
        Assert.All(
            delegateOrEventSites,
            site =>
            {
                Assert.Equal(CallResolutionKind.Unknown, site.Resolution.Kind);
                Assert.False(site.Resolution.IsComplete);
                Assert.Empty(site.Resolution.Candidates);
                Assert.Equal(CertaintyLevel.Unknown, site.Resolution.Certainty);
            });

        var repeated = await ExtractSuccessfullyAsync();
        var repeatedSnapshot = await AnalyzeBehaviorSuccessfullyAsync(repeated);
        Assert.Equal(firstSnapshot.BehaviorFingerprint, repeatedSnapshot.BehaviorFingerprint);
    }

    /// <summary>
    /// Claim 4: callback-local return rejoins the outer caller while a throw inside the callback stays
    /// Unknown and must never terminate the outer scenario by inference.
    /// </summary>
    [Theory]
    [InlineData("InvokeReturningCallback", CallbackCompletionKind.RejoinsCaller)]
    [InlineData("InvokeThrowingCallback", CallbackCompletionKind.Unknown)]
    public async Task CallbackLocalCompletionIsRejoinForReturnAndUnknownForThrow(
        string callerName,
        CallbackCompletionKind expectedCompletion)
    {
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.CallbackBoundaryFacts;

        var caller = FindCaller(extraction, callerName);
        var boundary = Assert.Single(facts.Boundaries, candidate => candidate.CallerMethod == caller);

        Assert.Equal(expectedCompletion, boundary.Completion);
    }

    /// <summary>
    /// Claim 5 (projection side): boundary identities, the debug projection, the Program Index
    /// fingerprint, and canonical member operations are deterministic across repeated construction and
    /// two physically relocated checkout roots, and no absolute checkout path or raw captured sentinel
    /// leaks into the projection.
    /// </summary>
    [Fact]
    public async Task CallbackBoundaryFactsAreDeterministicAcrossRepeatedConstructionAndRelocatedRoots()
    {
        var first = await ExtractSuccessfullyAsync();
        var second = await ExtractSuccessfullyAsync();
        Assert.Equal(
            CollectCallbackBoundaryIds(first.CallbackBoundaryFacts),
            CollectCallbackBoundaryIds(second.CallbackBoundaryFacts));
        Assert.Equal(
            first.CallbackBoundaryFacts.DebugProjection,
            second.CallbackBoundaryFacts.DebugProjection);
        Assert.Equal(
            first.CallbackBoundaryFacts.ProgramIndexFingerprint,
            second.CallbackBoundaryFacts.ProgramIndexFingerprint);
        Assert.DoesNotContain(
            FindRepositoryRoot(),
            first.CallbackBoundaryFacts.DebugProjection,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            NeverSerializedSentinel,
            first.CallbackBoundaryFacts.DebugProjection,
            StringComparison.Ordinal);

        var source = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "fixtures",
            "AdvancedAnalysis",
            "CallbackBoundaries");
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"seqdoc-callback-boundaries-relocation-{Guid.NewGuid():N}");
        var firstRoot = Path.Combine(temporaryDirectory, "first");
        var secondRoot = Path.Combine(temporaryDirectory, "second");
        try
        {
            CopyFixture(source, firstRoot);
            CopyFixture(source, secondRoot);
            await RestoreAsync(firstRoot);
            await RestoreAsync(secondRoot);

            var relocatedFirst = await ExtractRelocatedAsync(firstRoot);
            var relocatedSecond = await ExtractRelocatedAsync(secondRoot);

            Assert.Equal(
                CollectCallbackBoundaryIds(relocatedFirst.CallbackBoundaryFacts),
                CollectCallbackBoundaryIds(relocatedSecond.CallbackBoundaryFacts));
            Assert.Equal(
                CollectMemberOperations(relocatedFirst.CallbackBoundaryFacts),
                CollectMemberOperations(relocatedSecond.CallbackBoundaryFacts));
            Assert.Equal(
                relocatedFirst.CallbackBoundaryFacts.DebugProjection,
                relocatedSecond.CallbackBoundaryFacts.DebugProjection);
            Assert.Equal(
                relocatedFirst.CallbackBoundaryFacts.ProgramIndexFingerprint,
                relocatedSecond.CallbackBoundaryFacts.ProgramIndexFingerprint);
            Assert.DoesNotContain(
                firstRoot,
                relocatedFirst.CallbackBoundaryFacts.DebugProjection,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                secondRoot,
                relocatedSecond.CallbackBoundaryFacts.DebugProjection,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                NeverSerializedSentinel,
                relocatedFirst.CallbackBoundaryFacts.DebugProjection,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                NeverSerializedSentinel,
                relocatedSecond.CallbackBoundaryFacts.DebugProjection,
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Claim 6: a pre-cancelled extraction reports <see cref="ApplicationOutcome.Cancelled"/> and never
    /// runs the compilation/analysis pipeline.
    /// </summary>
    [Fact]
    public async Task PreCancelledExtractionReturnsCancelledOutcome()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var request = new CompilationAnalysisRequest(
            FindRepositoryRoot(),
            Path.Combine(FindRepositoryRoot(), FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            CompilationProfile.Create(FixtureRelativePath, "Release", "net10.0"),
            RepositoryOwnedConfigurationFiles: FixtureOwnedFiles);
        var result = await new RoslynProfileAnalysisExtractor().ExtractAsync(request, cancellation.Token);

        Assert.Equal(ApplicationOutcome.Cancelled, result.Outcome);
    }

    /// <summary>
    /// regression regression coverage: boundary operation anchors must be authoritative extracted
    /// operation identities, never recreated IDs. For each exact lambda, local-function, and
    /// method-group caller, the outer invocation operation resolves into the caller's extracted
    /// body and the contract invoke operation resolves into the contract method's extracted body.
    /// A method-group target must also carry canonical member operations that each occur among the
    /// exact target body's operation identities.
    /// </summary>
    [Theory]
    [InlineData("InvokeCapturingLambda")]
    [InlineData("InvokeLocalFunction")]
    [InlineData("InvokeOverloadedMethodGroup")]
    public async Task BoundaryOperationAnchorsResolveToAuthoritativeExtractedBodies(string callerName)
    {
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.CallbackBoundaryFacts;

        var caller = FindCaller(extraction, callerName);
        var boundary = Assert.Single(facts.Boundaries, candidate => candidate.CallerMethod == caller);

        var callerBody = Assert.Single(extraction.BehaviorInput.Methods, method => method.Method == caller);
        Assert.Contains(callerBody.Operations, operation => operation.Id == boundary.OuterInvocationOperation);

        Assert.NotNull(boundary.ContractMethod);
        Assert.NotNull(boundary.ContractInvokeOperation);
        var contractBody = Assert.Single(
            extraction.BehaviorInput.Methods,
            method => method.Method == boundary.ContractMethod);
        Assert.Contains(contractBody.Operations, operation => operation.Id == boundary.ContractInvokeOperation);

        if (boundary.TargetKind == CallbackTargetKind.MethodGroup)
        {
            Assert.NotNull(boundary.TargetMethod);
            var targetBody = Assert.Single(
                extraction.BehaviorInput.Methods,
                method => method.Method == boundary.TargetMethod);
            Assert.NotEmpty(boundary.MemberOperations);
            Assert.All(
                boundary.MemberOperations,
                member => Assert.Contains(targetBody.Operations, operation => operation.Id.Value == member));
        }
    }

    /// <summary>
    /// regression regression coverage: a dispatchable (virtual/interface) source contract must never be
    /// presented as an exact SourceBody contract that executes exactly once, because runtime dispatch
    /// may execute another override. The virtual-contract caller either fails closed or projects only
    /// a degraded boundary; it never claims ExactlyOnce cardinality or SourceBody provenance.
    /// </summary>
    [Fact]
    public async Task VirtualDispatchableContractIsNeverExactlyOnceOrSourceBody()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.CallbackBoundaryFacts;

        var caller = FindCaller(extraction, "InvokeVirtualContract");
        var boundary = facts.Boundaries.SingleOrDefault(candidate => candidate.CallerMethod == caller);
        if (boundary is not null)
        {
            Assert.NotEqual(CallbackCardinality.ExactlyOnce, boundary.Cardinality);
            Assert.NotEqual(CallbackContractProvenance.SourceBody, boundary.ContractProvenance);
        }
    }

    /// <summary>
    /// regression regression coverage: an earlier terminating path before a syntactically direct
    /// callback invoke allows zero executions, so the boundary must never claim ExactlyOnce. The
    /// early-return caller projects only Unknown cardinality and Unknown trigger, if it projects a
    /// boundary at all.
    /// </summary>
    [Fact]
    public async Task EarlyReturnBeforeCallbackInvokeIsNeverExactlyOnce()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.CallbackBoundaryFacts;

        var caller = FindCaller(extraction, "InvokeAfterEarlyReturn");
        var boundary = facts.Boundaries.SingleOrDefault(candidate => candidate.CallerMethod == caller);
        if (boundary is not null)
        {
            Assert.Equal(CallbackCardinality.Unknown, boundary.Cardinality);
            Assert.Equal(CallbackTriggerKind.Unknown, boundary.Trigger);
            Assert.NotEqual(CallbackCardinality.ExactlyOnce, boundary.Cardinality);
        }
    }

    /// <summary>
    /// regression regression coverage: cross-project resolution must not depend on processing order.
    /// The solution-level extraction resolves a caller in one project and the exact source contract
    /// and method-group target in another project; the projected boundary is MethodGroup with
    /// SourceBody provenance and ExactlyOnce cardinality, and both anchors exist in the Program
    /// Index.
    /// </summary>
    [Fact]
    public async Task CrossProjectCallbackResolvesExactMethodGroupBoundaryThroughProgramIndex()
    {
        var extraction = await ExtractCrossProjectSuccessfullyAsync();
        var facts = extraction.CallbackBoundaryFacts;

        var caller = FindCaller(extraction, "InvokeCrossProject");
        var boundary = Assert.Single(facts.Boundaries, candidate => candidate.CallerMethod == caller);

        Assert.Equal(CallbackTargetKind.MethodGroup, boundary.TargetKind);
        Assert.Equal(CallbackContractProvenance.SourceBody, boundary.ContractProvenance);
        Assert.Equal(CallbackCardinality.ExactlyOnce, boundary.Cardinality);

        Assert.NotNull(boundary.TargetMethod);
        Assert.NotNull(boundary.ContractMethod);
        Assert.Contains(extraction.ProgramIndex.Methods, method => method.Id == boundary.TargetMethod);
        Assert.Contains(extraction.ProgramIndex.Methods, method => method.Id == boundary.ContractMethod);
    }

    /// <summary>
    /// regression regression coverage: asynchronous and exception-region (try/finally) callback
    /// completion semantics are unsupported, so each exact boundary degrades its callback-local
    /// completion to Unknown rather than claiming the callback rejoins the caller.
    /// </summary>
    [Theory]
    [InlineData("InvokeAsyncCallback")]
    [InlineData("InvokeTryFinallyCallback")]
    public async Task AsyncAndTryFinallyCallbackCompletionDegradesToUnknown(string callerName)
    {
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.CallbackBoundaryFacts;

        var caller = FindCaller(extraction, callerName);
        var boundary = Assert.Single(facts.Boundaries, candidate => candidate.CallerMethod == caller);

        Assert.Equal(CallbackCompletionKind.Unknown, boundary.Completion);
    }

    /// <summary>
    /// accepted contract metadata-contract claim, folding the accepted contract metadata-only partition: a compiler-bound
    /// static metadata callee with an exact source callback argument may project a target/member
    /// boundary even though its source contract body is unavailable, but the boundary stays Unknown
    /// in provenance/cardinality/trigger with null contract anchors and never claims a definite
    /// source-body contract. The CallbackBoundaries Task.Run caller now projects exactly one
    /// AnonymousFunction boundary with an Unknown contract. The FusionCache 2.6.0 GetByIdAsync
    /// caller projects exactly one factory boundary at declaration ordinal 2 with an
    /// AnonymousFunction target, a non-null body anchor, null contract anchors, Unknown
    /// provenance/cardinality/trigger, conservative certainty, non-empty canonical members and
    /// evidence, and an outer operation that resolves into the caller's accepted extracted body. A
    /// delegate-variable factory (GetFromDelegateVariableAsync) never projects a boundary at the
    /// factory ordinal, and the unsupported tagged/duration/fallback/factory-context overloads may
    /// retain Unknown-contract targets but never SourceBody provenance or definite cardinality.
    /// </summary>
    [Fact]
    public async Task MetadataContractCallbacksProjectExactTargetsWithUnknownContract()
    {
        var boundaryFixture = await ExtractSuccessfullyAsync();
        var metadataOnlyCaller = FindCaller(boundaryFixture, "InvokeMetadataOnly");
        var metadataOnlyBoundary = Assert.Single(
            boundaryFixture.CallbackBoundaryFacts.Boundaries,
            candidate => candidate.CallerMethod == metadataOnlyCaller);
        Assert.Equal(CallbackTargetKind.AnonymousFunction, metadataOnlyBoundary.TargetKind);
        Assert.Null(metadataOnlyBoundary.ContractMethod);
        Assert.Null(metadataOnlyBoundary.ContractInvokeOperation);
        Assert.Equal(CallbackContractProvenance.Unknown, metadataOnlyBoundary.ContractProvenance);
        Assert.Equal(CallbackCardinality.Unknown, metadataOnlyBoundary.Cardinality);
        Assert.Equal(CallbackTriggerKind.Unknown, metadataOnlyBoundary.Trigger);

        var extraction = await ExtractFusionCacheSuccessfullyAsync();
        var facts = extraction.CallbackBoundaryFacts;

        var getById = FindCaller(extraction, "GetByIdAsync");
        var factoryBoundary = Assert.Single(
            facts.Boundaries,
            candidate => candidate.CallerMethod == getById && candidate.ParameterOrdinal == 2);

        Assert.Equal(2, factoryBoundary.ParameterOrdinal);
        Assert.Equal(CallbackTargetKind.AnonymousFunction, factoryBoundary.TargetKind);
        Assert.NotNull(factoryBoundary.TargetBodyOperation);
        Assert.Null(factoryBoundary.TargetMethod);
        Assert.Null(factoryBoundary.ContractMethod);
        Assert.Null(factoryBoundary.ContractInvokeOperation);
        Assert.Null(factoryBoundary.TriggerCondition);
        Assert.Equal(CallbackContractProvenance.Unknown, factoryBoundary.ContractProvenance);
        Assert.Equal(CallbackCardinality.Unknown, factoryBoundary.Cardinality);
        Assert.Equal(CallbackTriggerKind.Unknown, factoryBoundary.Trigger);
        Assert.Equal(CallbackCompletionKind.Unknown, factoryBoundary.Completion);
        Assert.Equal(CertaintyLevel.Conservative, factoryBoundary.Certainty);
        AssertEvidence(factoryBoundary.Evidence, factoryBoundary.Certainty);
        Assert.NotEmpty(factoryBoundary.MemberOperations);
        Assert.All(
            factoryBoundary.MemberOperations,
            member => Assert.False(string.IsNullOrWhiteSpace(member)));

        // The outer invocation must be the caller's accepted extracted operation identity, never a
        // recreated id, so the framework model can join the exact same operation.
        var callerBody = Assert.Single(extraction.BehaviorInput.Methods, method => method.Method == getById);
        Assert.Contains(callerBody.Operations, operation => operation.Id == factoryBoundary.OuterInvocationOperation);

        // accepted contract real-flow companion projection: the source-backed invocation nested inside the
        // anonymous factory body is projected into the framework-model request with the exact
        // companion operation id, so the scenario graph can join callback-contained work (the real
        // CustomerManagement flow projects the EF FirstOrDefaultAsync query this way; the generic
        // fixture mirrors the shape with RecordStore.FindAsync). The projected terminal invocation
        // shares the boundary's canonical member OperationId and carries the exact target identity,
        // while the outer metadata GetOrSetAsync invocation itself is never projected from the
        // callback body.
        var nestedInvocation = Assert.Single(
            extraction.Operations,
            operation => operation.Method == getById
                && operation.TargetIdentity is { MethodMetadataName: "FindAsync" });
        Assert.Contains(nestedInvocation.Id.Value, factoryBoundary.MemberOperations);
        Assert.Equal("Invocation", nestedInvocation.Kind);
        Assert.NotEmpty(nestedInvocation.Evidence);
        var nestedIdentity = nestedInvocation.TargetIdentity!;
        Assert.Equal("FusionCacheCallbacks", nestedIdentity.AssemblyIdentity);
        Assert.Equal("AdvancedAnalysis.FusionCacheCallbacks.RecordStore", nestedIdentity.ContainingMetadataType);
        Assert.Equal("FindAsync", nestedIdentity.MethodMetadataName);
        Assert.Equal(0, nestedIdentity.GenericArity);
        Assert.Collection(
            nestedIdentity.Parameters,
            parameter =>
            {
                Assert.Equal(ParameterRefKind.None, parameter.RefKind);
                Assert.Equal("System.Int32", parameter.FullyQualifiedType);
            },
            parameter =>
            {
                Assert.Equal(ParameterRefKind.None, parameter.RefKind);
                Assert.Equal("System.Threading.CancellationToken", parameter.FullyQualifiedType);
            });
        Assert.StartsWith("System.Threading.Tasks.Task<", nestedIdentity.ReturnType, StringComparison.Ordinal);

        // A delegate-variable factory never becomes an exact target and never projects a boundary at
        // the factory ordinal.
        var fromDelegateVariable = FindCaller(extraction, "GetFromDelegateVariableAsync");
        Assert.DoesNotContain(
            facts.Boundaries,
            candidate => candidate.CallerMethod == fromDelegateVariable && candidate.ParameterOrdinal == 2);

        // Unsupported FusionCache overloads (tagged, duration, fallback, factory-context) may retain
        // Unknown-contract targets but never claim a definite source-body contract.
        foreach (var unsupportedName in new[]
                 {
                     "GetWithTagsAsync",
                     "GetWithDurationAsync",
                     "GetWithFallbackAsync",
                     "GetWithFactoryContextAsync",
                 })
        {
            var unsupportedCaller = FindCaller(extraction, unsupportedName);
            foreach (var boundary in facts.Boundaries.Where(candidate => candidate.CallerMethod == unsupportedCaller))
            {
                Assert.Equal(CallbackContractProvenance.Unknown, boundary.ContractProvenance);
                Assert.Equal(CallbackCardinality.Unknown, boundary.Cardinality);
                Assert.Equal(CallbackTriggerKind.Unknown, boundary.Trigger);
                Assert.Null(boundary.ContractMethod);
                Assert.Null(boundary.ContractInvokeOperation);
            }
        }
    }

    /// <summary>
    /// accepted contract determinism claim: the FusionCache metadata-contract boundary ids, member operations,
    /// debug projection, and Program Index fingerprint are stable across repeated construction and
    /// two physically relocated checkout roots, and no absolute checkout path leaks into the debug
    /// projection. The relocated copies keep the central-package-management version file so the
    /// exact 2.6.0 package reference restores, while the repository-root import and checked-in lock
    /// file stay behind.
    /// </summary>
    [Fact]
    public async Task FusionCacheMetadataBoundariesAreDeterministicAcrossRepeatedConstructionAndRelocatedRoots()
    {
        var first = await ExtractFusionCacheSuccessfullyAsync();
        var second = await ExtractFusionCacheSuccessfullyAsync();
        Assert.Equal(
            CollectCallbackBoundaryIds(first.CallbackBoundaryFacts),
            CollectCallbackBoundaryIds(second.CallbackBoundaryFacts));
        Assert.Equal(
            first.CallbackBoundaryFacts.DebugProjection,
            second.CallbackBoundaryFacts.DebugProjection);
        Assert.Equal(
            first.CallbackBoundaryFacts.ProgramIndexFingerprint,
            second.CallbackBoundaryFacts.ProgramIndexFingerprint);
        Assert.Equal(
            CollectNestedInvocationDescriptorIds(first, "GetByIdAsync"),
            CollectNestedInvocationDescriptorIds(second, "GetByIdAsync"));
        Assert.DoesNotContain(
            FindRepositoryRoot(),
            first.CallbackBoundaryFacts.DebugProjection,
            StringComparison.OrdinalIgnoreCase);

        var source = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "fixtures",
            "AdvancedAnalysis",
            "FusionCacheCallbacks");
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"seqdoc-fusioncache-callbacks-relocation-{Guid.NewGuid():N}");
        var firstRoot = Path.Combine(temporaryDirectory, "first");
        var secondRoot = Path.Combine(temporaryDirectory, "second");
        try
        {
            CopyFusionCacheFixture(source, firstRoot);
            CopyFusionCacheFixture(source, secondRoot);
            await RestoreProjectAsync(firstRoot, "FusionCacheCallbacks.csproj");
            await RestoreProjectAsync(secondRoot, "FusionCacheCallbacks.csproj");

            var relocatedFirst = await ExtractRelocatedFusionCacheAsync(firstRoot);
            var relocatedSecond = await ExtractRelocatedFusionCacheAsync(secondRoot);

            Assert.Equal(
                CollectCallbackBoundaryIds(relocatedFirst.CallbackBoundaryFacts),
                CollectCallbackBoundaryIds(relocatedSecond.CallbackBoundaryFacts));
            Assert.Equal(
                CollectMemberOperations(relocatedFirst.CallbackBoundaryFacts),
                CollectMemberOperations(relocatedSecond.CallbackBoundaryFacts));
            Assert.Equal(
                relocatedFirst.CallbackBoundaryFacts.DebugProjection,
                relocatedSecond.CallbackBoundaryFacts.DebugProjection);
            Assert.Equal(
                relocatedFirst.CallbackBoundaryFacts.ProgramIndexFingerprint,
                relocatedSecond.CallbackBoundaryFacts.ProgramIndexFingerprint);
            Assert.Equal(
                CollectNestedInvocationDescriptorIds(relocatedFirst, "GetByIdAsync"),
                CollectNestedInvocationDescriptorIds(relocatedSecond, "GetByIdAsync"));
            Assert.DoesNotContain(
                firstRoot,
                relocatedFirst.CallbackBoundaryFacts.DebugProjection,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                secondRoot,
                relocatedSecond.CallbackBoundaryFacts.DebugProjection,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    private static async Task<ProfileAnalysisExtraction> ExtractSuccessfullyAsync()
    {
        var result = await ExtractFixtureAsync();
        Assert.True(
            result.Outcome == ApplicationOutcome.Succeeded,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));
        return Assert.IsType<ProfileAnalysisExtraction>(result.Value);
    }

    private static async Task<BehaviorSnapshot> AnalyzeBehaviorSuccessfullyAsync(ProfileAnalysisExtraction extraction)
    {
        var result = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(extraction.ProgramIndex, extraction.BehaviorInput),
            CancellationToken.None);
        Assert.True(
            result.Outcome == ApplicationOutcome.Succeeded,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));
        return Assert.IsType<BehaviorSnapshot>(result.Value);
    }

    private static async Task<ApplicationResult<ProfileAnalysisExtraction>> ExtractFixtureAsync()
    {
        var root = FindRepositoryRoot();
        var request = new CompilationAnalysisRequest(
            root,
            Path.Combine(root, FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            CompilationProfile.Create(FixtureRelativePath, "Release", "net10.0"),
            RepositoryOwnedConfigurationFiles: FixtureOwnedFiles);
        return await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
    }

    private static async Task<ProfileAnalysisExtraction> ExtractCrossProjectSuccessfullyAsync()
    {
        var root = FindRepositoryRoot();
        var request = new CompilationAnalysisRequest(
            root,
            Path.Combine(root, CrossProjectRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            CompilationProfile.Create(CrossProjectRelativePath, "Release", "net10.0"),
            RepositoryOwnedConfigurationFiles: FixtureOwnedFiles);
        var result = await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
        Assert.True(
            result.Outcome == ApplicationOutcome.Succeeded,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));
        return Assert.IsType<ProfileAnalysisExtraction>(result.Value);
    }

    private static async Task<ProfileAnalysisExtraction> ExtractRelocatedAsync(string root)
    {
        var request = new CompilationAnalysisRequest(
            root,
            Path.Combine(root, "CallbackBoundaries.csproj"),
            CompilationProfile.Create("CallbackBoundaries.csproj", "Release", "net10.0"),
            RepositoryOwnedConfigurationFiles: RelocatedOwnedFiles);
        var result = await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
        Assert.True(
            result.Outcome == ApplicationOutcome.Succeeded,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));
        return Assert.IsType<ProfileAnalysisExtraction>(result.Value);
    }

    private static async Task<ProfileAnalysisExtraction> ExtractFusionCacheSuccessfullyAsync()
    {
        var root = FindRepositoryRoot();
        var request = new CompilationAnalysisRequest(
            root,
            Path.Combine(root, FusionCacheFixtureRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            CompilationProfile.Create(FusionCacheFixtureRelativePath, "Release", "net10.0"),
            RepositoryOwnedConfigurationFiles: FixtureOwnedFiles);
        var result = await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
        Assert.True(
            result.Outcome == ApplicationOutcome.Succeeded,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));
        return Assert.IsType<ProfileAnalysisExtraction>(result.Value);
    }

    private static async Task<ProfileAnalysisExtraction> ExtractRelocatedFusionCacheAsync(string root)
    {
        var request = new CompilationAnalysisRequest(
            root,
            Path.Combine(root, "FusionCacheCallbacks.csproj"),
            CompilationProfile.Create("FusionCacheCallbacks.csproj", "Release", "net10.0"),
            RepositoryOwnedConfigurationFiles: RelocatedOwnedFiles);
        var result = await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
        Assert.True(
            result.Outcome == ApplicationOutcome.Succeeded,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));
        return Assert.IsType<ProfileAnalysisExtraction>(result.Value);
    }

    /// <summary>
    /// Copies the FusionCache fixture without build output or intermediate artifacts. Unlike the
    /// CallbackBoundaries copy, the central-package-management file must travel so the exact 2.6.0
    /// package version resolves; the repository-root props import and the checked-in lock file must
    /// stay behind because neither resolves under a relocated root.
    /// </summary>
    private static void CopyFusionCacheFixture(string sourceDirectory, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDirectory, file);
            string[] segments = relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(segment => segment is "bin" or "obj-custom"))
            {
                continue;
            }

            if (relative is "Directory.Build.props" or "packages.lock.json")
            {
                continue;
            }

            string destination = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

    private static async Task RestoreProjectAsync(string root, string projectFileName)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"restore {projectFileName} --nologo",
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        Assert.True(process.Start());
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"{await output}\n{await error}");
    }

    /// <summary>
    /// Resolves a fixture caller method name to its exact <see cref="MethodId"/> through the Program
    /// Index so boundary facts can be filtered by caller without depending on checkout paths.
    /// </summary>
    private static MethodId FindCaller(ProfileAnalysisExtraction extraction, string callerName)
    {
        MethodId? callerId = null;
        foreach (var method in extraction.ProgramIndex.Methods)
        {
            if (method.Name == callerName)
            {
                Assert.Null(callerId);
                callerId = method.Id;
            }
        }

        Assert.True(callerId.HasValue, $"Fixture caller '{callerName}' was not found in the Program Index.");
        return callerId.Value;
    }

    private static string CollectCallbackBoundaryIds(CallbackBoundaryFactSet facts)
        => string.Join(
            "\n",
            facts.Boundaries
                .Select(fact => fact.Id.Value)
                .Order(StringComparer.Ordinal));

    private static string CollectMemberOperations(CallbackBoundaryFactSet facts)
        => string.Join(
            "\n",
            facts.Boundaries
                .SelectMany(fact => fact.MemberOperations)
                .Order(StringComparer.Ordinal));

    /// <summary>
    /// Collects the canonical extracted operation identities of every Invocation operation owned by
    /// the exact caller, ordered ordinally and joined newline-separated. The framework-model request
    /// operations are the authoritative join surface for callback-contained work (for example the
    /// nested EF query inside the FusionCache anonymous factory), so a deterministic projection of
    /// these ids pins the exact join surface across repeated construction and relocated roots.
    /// </summary>
    private static string CollectNestedInvocationDescriptorIds(ProfileAnalysisExtraction extraction, string callerName)
    {
        var caller = FindCaller(extraction, callerName);
        return string.Join(
            "\n",
            extraction.Operations
                .Where(operation => operation.Method == caller && operation.Kind == "Invocation")
                .Select(operation => operation.Id.Value)
                .Order(StringComparer.Ordinal));
    }

    private static void AssertEvidence(ImmutableArray<EvidenceRef> evidence, CertaintyLevel certainty)
    {
        Assert.NotEmpty(evidence);
        Assert.All(evidence, item => Assert.False(string.IsNullOrWhiteSpace(item.Artifact)));
        Assert.True(certainty != CertaintyLevel.Unknown, "A projected fact must carry explicit certainty.");
        Assert.True(certainty >= evidence.Max(item => item.Certainty), "Fact certainty must never exceed its strongest evidence.");
    }

    private static void CopyFixture(string sourceDirectory, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDirectory, file);
            string[] segments = relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(segment => segment is "bin" or "obj-custom"))
            {
                continue;
            }

            if (relative is "packages.lock.json" or "Directory.Build.props" or "Directory.Packages.props")
            {
                continue;
            }

            string destination = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

    private static async Task RestoreAsync(string root)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "restore CallbackBoundaries.csproj --nologo",
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        Assert.True(process.Start());
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"{await output}\n{await error}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SeqDoc.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
