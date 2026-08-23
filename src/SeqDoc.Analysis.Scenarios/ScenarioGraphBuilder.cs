using System.Collections.Immutable;
using System.Text;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Configuration;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Core.Semantics;

namespace SeqDoc.Analysis.Scenarios;

/// <summary>
/// Carries every Roslyn-neutral input the scenario builder joins for one compilation profile. The
/// request contains only Core-owned contracts; no Roslyn, MSBuild, SQLite, CLI, or renderer type
/// crosses this boundary. The conditional dependency-injection fact set and the configuration
/// semantic fact set are optional accepted contract companions: a null conditional set keeps every SC001
/// partition unchanged. The callback boundary fact set is an optional accepted contract companion: a null or
/// foreign set contributes no callback region and no membership.
/// </summary>
public sealed record ScenarioAnalysisRequest(
    CompilationProfile Profile,
    ProgramIndexSnapshot ProgramIndex,
    BehaviorSnapshot Behavior,
    FrameworkAnalysisResult FrameworkFacts,
    SemanticFactSet SemanticFacts,
    DependencyInjectionFactSet DependencyInjectionFacts,
    StructuralResultFactSet StructuralResultFacts,
    NonGetSemanticFactSet NonGetSemanticFacts,
    ConditionalDependencyInjectionFactSet? ConditionalDependencyInjectionFacts = null,
    ConfigurationSemanticFactSet? ConfigurationSemanticFacts = null,
    CallbackBoundaryFactSet? CallbackBoundaryFacts = null,
    PredicateSemanticFactSet? PredicateSemanticFacts = null,
    MinimalApiHandlerFactSet? HandlerFacts = null,
    ImmutableArray<MethodId> ConfiguredRoots = default,
    DiagramBudget? DiagramBudget = null)
{
    public DiagramBudget EffectiveDiagramBudget => DiagramBudget ?? SeqDoc.Core.Configuration.DiagramBudget.Default;
}

/// <summary>
/// Builds deterministic, evidence-backed v0 scenario graphs by joining the accepted entry-point,
/// call, DI-target, EF-query, structural-result, and HTTP-outcome facts. Every node and edge carries
/// evidence and explicit certainty; ambiguous or incomplete joins become explicit diagnostics and
/// never select one candidate. The builder is pure and Roslyn-neutral; the graph set is memory-only.
/// </summary>
public static class ScenarioGraphBuilder
{
    private const string ProducerVersion = "0.1.0-alpha";

    private static ScenarioPredicateWording? PredicateWording(ScenarioAnalysisRequest request, MethodId method, OperationId operation)
    {
        var set = request.PredicateSemanticFacts;
        if (set is null || set.Profile.Id != request.Profile.Id || set.ProgramIndexFingerprint != request.ProgramIndex.IndexFingerprint)
        {
            return null;
        }
        var mapping = set.Mappings.FirstOrDefault(item => item.Method == method && item.LoweredConditionOperations.Contains(operation));
        var predicate = mapping is null ? null : set.Predicates.FirstOrDefault(item => item.Id == mapping.PredicateId && item.Method == method);
        if (mapping is null || predicate is null) { return null; }
        bool owner = mapping.LoweredConditionOperations[0] == operation;
        var evidence = predicate.Evidence.AddRange(mapping.Evidence);
        var certainty = evidence.Max(item => item.Certainty);
        return new ScenarioPredicateWording(predicate.Id, predicate.Root,
            owner ? ScenarioPredicateWordingRole.Owner : ScenarioPredicateWordingRole.Subordinate,
            evidence, certainty);
    }

    public static ScenarioGraphSet Build(ScenarioAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var frameworkEntries = request.FrameworkFacts.Facts
            .Where(fact => fact is HttpEntryPointFact or MinimalApiRouteFact)
            .Select(fact => fact is MinimalApiRouteFact minimal
                ? new NormalizedEntry(minimal.EntryPointId, minimal.HandlerRoot, minimal.HttpMethod, minimal.CanonicalRoute, minimal.OperationKey, ScenarioActionKind.MinimalApiHandler, minimal.Evidence)
                : new NormalizedEntry(((HttpEntryPointFact)fact).EntryPointId, ((HttpEntryPointFact)fact).RootMethod, ((HttpEntryPointFact)fact).HttpMethod, ((HttpEntryPointFact)fact).CanonicalRoute, ((HttpEntryPointFact)fact).OperationKey, ScenarioActionKind.ControllerAction, fact.Evidence))
            .ToArray();
        var admittedMethods = frameworkEntries.Select(entry => entry.RootMethod).ToHashSet();
        var configuredEntries = (request.ConfiguredRoots.IsDefault ? [] : request.ConfiguredRoots)
            .Where(method => !admittedMethods.Contains(method))
            .OrderBy(method => method.Value, StringComparer.Ordinal)
            .Select(method => new NormalizedEntry(
                StableIdentity.CreateConfiguredMethodEntryPointId(new ConfiguredMethodEntryPointIdentityDescriptor(request.Profile.Id, method)),
                 method, HttpMethodKind.Unknown, string.Empty,
                 ConfiguredDisplaySignature(request.ProgramIndex, method),
                 ScenarioActionKind.ConfiguredMethod,
                 request.ProgramIndex.Methods.First(item => item.Id == method).Evidence))
            .ToArray();
        var graphs = frameworkEntries
            .OrderBy(fact => fact.EntryPointId.Value, StringComparer.Ordinal)
            .Select(fact => BuildGraph(request, fact))
            .Concat(configuredEntries.Select(entry => BuildGraph(request, entry)))
            .OrderBy(graph => graph.EntryPoint.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        var debugProjection = BuildSetDebugProjection(request, graphs);
        return new ScenarioGraphSet(
            1,
            ProducerVersion,
            request.Profile,
            request.ProgramIndex.IndexFingerprint,
            graphs,
            [],
            debugProjection);
    }

    private sealed record NormalizedEntry(EntryPointId EntryPointId, MethodId RootMethod, HttpMethodKind HttpMethod, string CanonicalRoute, string OperationKey, ScenarioActionKind ActionKind, ImmutableArray<EvidenceRef> Evidence)
    {
        public ScenarioRootKind RootKind => ActionKind == ScenarioActionKind.ConfiguredMethod ? ScenarioRootKind.ConfiguredMethod : ScenarioRootKind.HttpEntryPoint;
    }

    private static ScenarioGraph BuildGraph(ScenarioAnalysisRequest request, NormalizedEntry entryPoint)
    {
        var profileId = request.Profile.Id;
        var entryPointId = entryPoint.EntryPointId;
        var nodes = new List<ScenarioNode>();
        var edges = new List<ScenarioEdge>();
        var diagnostics = new List<ScenarioGraphDiagnostic>();

        var entryNode = CreateNode(
            profileId,
            entryPointId,
            ScenarioNodeKind.EntryPoint,
            entryPointId.Value,
            entryPoint.RootMethod,
            null,
            entryPoint.OperationKey,
            entryPoint.Evidence);
        nodes.Add(entryNode);

        var actionPresentation = entryPoint.ActionKind == ScenarioActionKind.ConfiguredMethod
            ? ConfiguredMethodPresentation(request.ProgramIndex, entryPoint.RootMethod)
            : entryPoint.ActionKind == ScenarioActionKind.MinimalApiHandler
            ? MinimalApiActionPresentation(request.ProgramIndex, entryPoint.RootMethod)
            : ControllerActionPresentation(request.ProgramIndex, entryPoint.RootMethod);
        var actionNode = CreateNodeWithPresentation(
            profileId,
            entryPointId,
            ScenarioNodeKind.Action,
            $"action:{entryPoint.RootMethod.Value}",
            entryPoint.RootMethod,
            null,
                 entryPoint.ActionKind == ScenarioActionKind.ConfiguredMethod ? "configured method" : entryPoint.ActionKind == ScenarioActionKind.MinimalApiHandler ? "minimal API handler" : "controller action",
             actionPresentation with { ActionKind = entryPoint.ActionKind },
            entryPoint.Evidence);
        nodes.Add(actionNode);
        edges.Add(CreateEdge(
            profileId,
            entryPointId,
            entryNode,
            actionNode,
            ScenarioEdgeKind.Entry,
            string.Empty,
            entryPoint.Evidence));

        if (entryPoint.ActionKind == ScenarioActionKind.ConfiguredMethod)
        {
            var method = request.ProgramIndex.Methods.Single(item => item.Id == entryPoint.RootMethod);
            if (request.Behavior.Profile.Id != request.Profile.Id
                || !string.Equals(request.Behavior.ProgramIndexFingerprint, request.ProgramIndex.IndexFingerprint, StringComparison.Ordinal))
            {
                var mismatch = CreateDiagnostic(profileId, entryPointId, "SC-DIRECT-MISMATCH",
                    "The configured method behavior snapshot does not match the active analysis.",
                    $"behaviorProfile={request.Behavior.Profile.Id.Value}; requestProfile={request.Profile.Id.Value}; "
                    + $"behaviorFingerprint={request.Behavior.ProgramIndexFingerprint}; requestFingerprint={request.ProgramIndex.IndexFingerprint}",
                    entryPoint.Evidence);
                diagnostics.Add(mismatch);
                return FinalizeGraph(request, entryPoint, profileId, nodes, edges, diagnostics, ScenarioTopology.Empty,
                    directCallExpansion: new ScenarioDirectCallExpansion([], false, [mismatch]));
            }
            var flows = request.Behavior.MethodFlows.Where(item => item.Method == method.Id).ToArray();
            if (method.BodyFingerprint is null || flows.Length == 0)
            {
                diagnostics.Add(CreateDiagnostic(profileId, entryPointId,
                    method.BodyFingerprint is null ? "SC002" : "SC-DIRECT-NO-FLOW",
                    method.BodyFingerprint is null ? "The configured method body is unavailable." : "No unique Method Flow was produced for the configured method.",
                    method.BodyFingerprint is null ? $"No source body is available for {method.Id.Value}; behavior is withheld." : $"No Method Flow was produced for {method.Id.Value}; behavior is withheld."));
                return FinalizeGraph(request, entryPoint, profileId, nodes, edges, diagnostics, ScenarioTopology.Empty);
            }
            if (flows.Length > 1)
            {
                var diagnostic = CreateDiagnostic(profileId, entryPointId, "SC-DIRECT-AMBIGUOUS-FLOW",
                    "More than one Method Flow matches the configured method; behavior is withheld.",
                    $"Ambiguous Method Flow count={flows.Length} for {method.Id.Value}; no flow was selected.");
                diagnostics.Add(diagnostic);
                return FinalizeGraph(request, entryPoint, profileId, nodes, edges, diagnostics, ScenarioTopology.Empty,
                    directCallExpansion: new ScenarioDirectCallExpansion([], false, [diagnostic]));
            }

            var directExpansion = AddConfiguredDirectCalls(request, entryPoint, profileId, actionNode, nodes, edges, diagnostics);
            var topologyNodes = nodes.Where(node => node.Kind != ScenarioNodeKind.MethodCall
                || directExpansion.Steps.Any(step => step.ScenarioNodeId == node.Id && step.Depth == 1)).ToImmutableArray();
            var configuredTopology = BuildTopology(request, profileId, entryPointId, entryPoint, entryPoint.RootMethod,
                topologyNodes, diagnostics);
            directExpansion = InheritDirectCallMembership(configuredTopology, directExpansion);
            configuredTopology = AddDirectCallMemberships(configuredTopology, directExpansion, entryPoint.RootMethod, profileId);
            return FinalizeGraph(request, entryPoint, profileId, nodes, edges, diagnostics, configuredTopology,
                directCallExpansion: directExpansion);
        }

        if (entryPoint.ActionKind == ScenarioActionKind.MinimalApiHandler
            && TryGetHandlerFacts(request, entryPoint, out var handlerFacts))
        {
            var handlerTopology = AddHandlerFacts(request, entryPoint, handlerFacts, nodes, edges);
            return FinalizeGraph(request, entryPoint, profileId, nodes, edges, diagnostics, ScenarioTopology.Empty, null, handlerTopology);
        }

        var dispatches = request.FrameworkFacts.Facts.OfType<DispatchFact>()
            .Where(fact => fact.Profile == request.Profile.Id
                && fact.ProgramIndexFingerprint == request.ProgramIndex.IndexFingerprint
                && fact.CallerMethod == entryPoint.RootMethod)
            .OrderBy(fact => fact.OperationId.Value, StringComparer.Ordinal)
            .ToArray();
        if (dispatches.Length > 0)
        {
            ScenarioDispatchHandlerExpansion? dispatchExpansion = null;
            for (var dispatchOrdinal = 0; dispatchOrdinal < dispatches.Length; dispatchOrdinal++)
            {
                var dispatch = dispatches[dispatchOrdinal];
                var pipelineEvidence = dispatch.Pipeline.Stages.SelectMany(stage => stage.Evidence).ToImmutableArray();
                var dispatchEvidence = Combine(dispatch.Evidence, pipelineEvidence);
                var dispatchCertainty = LeastConfident(dispatch.Certainty, dispatchEvidence);
                var dispatchNode = CreateNodeWithPresentation(profileId, entryPointId, ScenarioNodeKind.Dispatch,
                    $"dispatch:{dispatch.OperationId.Value}", dispatch.CallerMethod, dispatch.OperationId,
                    dispatch.RequestType, new ScenarioNodePresentation(
                        RequestTypeName: dispatch.RequestType, ResponseTypeName: dispatch.ResponseType), dispatchEvidence,
                    dispatchCertainty);
                nodes.Add(dispatchNode);
                edges.Add(CreateEdge(profileId, entryPointId, actionNode, dispatchNode, ScenarioEdgeKind.Dispatch,
                    "dispatch", dispatchEvidence, dispatchCertainty, dispatchOrdinal));

                if (dispatch.SelectedHandler is { } selected)
                {
                    dispatchExpansion = ScenarioDispatchHandlerExpansionBuilder.Build(request, dispatch);
                    diagnostics.AddRange(dispatchExpansion.Diagnostics);
                    var handlerEvidence = Combine(dispatchEvidence, selected.Evidence);
                    var handlerCertainty = LeastConfident(dispatch.Certainty, handlerEvidence, selected.Certainty);
                    var handlerNode = CreateNodeWithPresentation(profileId, entryPointId, ScenarioNodeKind.Handler,
                        $"handler:{dispatch.OperationId.Value}:{selected.Method.Value}", selected.Method, dispatch.OperationId,
                        selected.BodyAvailable ? selected.DisplayName : "handler body unavailable",
                        new ScenarioNodePresentation(HandlerTypeName: selected.DisplayName, HandlerBodyAvailable: selected.BodyAvailable),
                        handlerEvidence, handlerCertainty);
                    nodes.Add(handlerNode);
                    edges.Add(CreateEdge(profileId, entryPointId, dispatchNode, handlerNode, ScenarioEdgeKind.Dispatch,
                        "dispatch", handlerEvidence, handlerCertainty, dispatchOrdinal));
                }
                else
                {
                    var code = dispatch.Resolution switch
                    {
                        DispatchResolution.Ambiguous => "SC-DISPATCH-AMBIGUOUS",
                        DispatchResolution.OpenGeneric => "SC-DISPATCH-OPEN-GENERIC",
                        _ => "SC-DISPATCH-UNRESOLVED",
                    };
                    diagnostics.Add(CreateDiagnostic(profileId, entryPointId, code,
                        "The dispatch boundary could not be joined to exactly one handler.",
                        $"{dispatch.OperationId.Value}\u001f{string.Join("\u001f", dispatch.Candidates.Select(candidate => candidate.Method.Value).OrderBy(name => name, StringComparer.Ordinal))}"));
                }
            }

            return FinalizeGraph(request, entryPoint, profileId, nodes, edges, diagnostics, ScenarioTopology.Empty, null, null, dispatchExpansion);
        }

        var resolution = TryResolveService(request, entryPoint.RootMethod, out var ambiguityReason);
        if (resolution is null)
        {
            // accepted contract: when the current multiple DI bindings are completely accounted for by one exact
            // alternative group and call resolution is complete, resolve each arm independently to
            // exactly one implementation method and suppress SC001 for that proven pair. The typed
            // composition lives in ScenarioGraph.Composition; the flat visible graph stays sparse with
            // no service/data/outcome node and no cross-arm leakage until accepted contract renders alternatives.
            // Every missing group, extra unguarded binding/registration, missing/incomplete/ambiguous
            // candidate keeps the exact existing SC001 reason.
            var composition = TryResolveComposition(request, entryPoint.RootMethod, profileId);
            if (composition is not null)
            {
                // accepted contract: the complete conditional composition materializes its arms in the flat graph
                // (one service node and its method-specific facts per arm) before finalization. The
                // arm member identities become the canonical membership authority for rendering; the
                // arms stay disjoint because each arm joins only its own resolved method facts.
                composition = JoinCompositionArms(request, entryPoint, profileId, composition, actionNode, nodes, edges, diagnostics);
                return FinalizeGraph(request, entryPoint, profileId, nodes, edges, diagnostics, ScenarioTopology.Empty, composition);
            }

            diagnostics.Add(CreateDiagnostic(
                profileId,
                entryPointId,
                "SC001",
                "The action call could not be joined to exactly one DI-resolved service implementation.",
                $"{entryPoint.RootMethod.Value}\u001f{ambiguityReason ?? "unknown"}"));
            AddRootDirectCalls(request, entryPoint, profileId, actionNode, nodes, edges);
            var rootTopology = BuildTopology(
                request,
                profileId,
                entryPointId,
                entryPoint,
                entryPoint.RootMethod,
                nodes.ToImmutableArray(),
                diagnostics);
            return FinalizeGraph(request, entryPoint, profileId, nodes, edges, diagnostics, rootTopology, composition: null);
        }

        var serviceNode = CreateNodeWithPresentation(
            profileId,
            entryPointId,
            ScenarioNodeKind.ServiceCall,
            $"service:{resolution.ServiceMethod.Value}",
            resolution.ServiceMethod,
            resolution.CallSite.InvocationOperation,
            $"resolved service implementation {resolution.Registration.ImplementationType}",
            new ScenarioNodePresentation(
                ContractTypeName: resolution.Binding.ServiceType,
                ImplementationTypeName: resolution.Registration.ImplementationType,
                CalledMemberName: MethodConciseName(request.ProgramIndex, resolution.ServiceMethod),
                ArgumentLabel: FormatArgumentLabel(InvocationConstantArguments(request, resolution.ServiceMethod, resolution.CallSite.InvocationOperation))),
            Combine(resolution.CallSite.Evidence, resolution.CallSite.Resolution.Evidence, resolution.Binding.Evidence, resolution.Registration.Evidence));
        nodes.Add(serviceNode);
        edges.Add(CreateEdge(
            profileId,
            entryPointId,
            actionNode,
            serviceNode,
            ScenarioEdgeKind.Call,
            $"call through {resolution.Binding.ServiceType}",
            Combine(resolution.CallSite.Evidence, resolution.CallSite.Resolution.Evidence, resolution.Binding.Evidence, resolution.Registration.Evidence)));

        JoinEntityQueries(
            request,
            profileId,
            entryPointId,
            resolution.ServiceMethod,
            serviceNode,
            nodes,
            edges,
            diagnostics);
        JoinStateAssignments(
            request,
            profileId,
            entryPointId,
            resolution.ServiceMethod,
            serviceNode,
            nodes,
            edges);
        JoinEntityMutations(
            request,
            profileId,
            entryPointId,
            resolution.ServiceMethod,
            serviceNode,
            nodes,
            edges);
        JoinSourceObservations(
            request,
            profileId,
            entryPointId,
            entryPoint.RootMethod,
            resolution.ServiceMethod,
            actionNode,
            serviceNode,
            nodes,
            edges);

        var switchArms = request.NonGetSemanticFacts.StatusSwitchArms
            .Where(arm => arm.Method == entryPoint.RootMethod)
            .ToImmutableArray();
        if (switchArms.Length == 0)
        {
            JoinStructuralResultOutcomes(
                request,
                profileId,
                entryPointId,
                entryPoint.RootMethod,
                resolution,
                serviceNode,
                nodes,
                edges,
                diagnostics);
        }
        else
        {
            JoinStatusSwitchOutcomes(
                request,
                profileId,
                entryPointId,
                serviceNode,
                nodes,
                edges,
                diagnostics,
                switchArms);
        }

        var topology = BuildTopology(
            request,
            profileId,
            entryPointId,
            entryPoint,
            resolution.ServiceMethod,
            nodes.ToImmutableArray(),
            diagnostics);
        return FinalizeGraph(request, entryPoint, profileId, nodes, edges, diagnostics, topology);
    }

    private static void AddRootDirectCalls(
        ScenarioAnalysisRequest request,
        NormalizedEntry entryPoint,
        CompilationProfileId profileId,
        ScenarioNode actionNode,
        List<ScenarioNode> nodes,
        List<ScenarioEdge> edges)
    {
        var flow = request.Behavior.MethodFlows.SingleOrDefault(candidate => candidate.Method == entryPoint.RootMethod);
        if (flow is null)
        {
            return;
        }

        var admitted = flow.Nodes.OfType<InvocationFlowNode>()
            .GroupBy(invocation => invocation.Operation)
            .Select(group => group.OrderBy(invocation => invocation.Id.Value, StringComparer.Ordinal).First())
            .Where(invocation => invocation.Certainty == CertaintyLevel.Exact
                && !invocation.Evidence.IsDefaultOrEmpty
                && invocation.Target is not null
                && invocation.IsSourceBacked
                && !invocation.IsPlatformTarget
                && !invocation.IsInsideNestedFunction
                && !invocation.IsDynamic
                && !invocation.IsDelegateOrEventInvoke
                && !invocation.IsConstructor
                && !string.IsNullOrWhiteSpace(invocation.TargetContainingTypeName)
                && !string.IsNullOrWhiteSpace(invocation.TargetMethodName))
            .Select(invocation => (Invocation: invocation, Sites: request.Behavior.CallGraph.CallSites
                .Where(site => site.ContainingMethod == entryPoint.RootMethod
                    && site.InvocationOperation == invocation.Operation)
                .OrderBy(site => site.Id.Value, StringComparer.Ordinal)
                .ToArray()))
            .Where(item => item.Sites.Length == 1
                && item.Sites.Select(site => site.DeclaredTarget).Distinct().Count() == 1)
            .Where(item =>
            {
                var site = item.Sites[0];
                var target = item.Invocation.Target!.Value;
                return site.DeclaredTarget == target
                    && site.Certainty == CertaintyLevel.Exact
                    && !site.Evidence.IsDefaultOrEmpty
                    && site.Resolution.Kind == CallResolutionKind.DirectExact
                    && site.Resolution.IsComplete
                    && site.Resolution.Certainty == CertaintyLevel.Exact
                    && site.Resolution.Candidates.Length == 1
                    && site.Resolution.Candidates[0] == target
                    && !site.Resolution.Evidence.IsDefaultOrEmpty
                    && site.Resolution.Evidence.All(evidence => evidence.Certainty == CertaintyLevel.Exact)
                    && site.Evidence.All(evidence => evidence.Certainty == CertaintyLevel.Exact)
                    && item.Invocation.Evidence.All(evidence => evidence.Certainty == CertaintyLevel.Exact);
            })
            .OrderBy(item => item.Invocation.BlockOrdinal)
            .ThenBy(item => item.Invocation.EvaluationOrdinal)
            .ThenBy(item => item.Invocation.Id.Value, StringComparer.Ordinal)
            .ToArray();

        for (var ordinal = 0; ordinal < admitted.Length; ordinal++)
        {
            var invocation = admitted[ordinal].Invocation;
            var site = admitted[ordinal].Sites[0];
            var evidence = Combine(invocation.Evidence, site.Evidence, site.Resolution.Evidence);
            var node = CreateNodeWithPresentation(
                profileId,
                entryPoint.EntryPointId,
                ScenarioNodeKind.MethodCall,
                $"method-call:{invocation.Operation.Value}",
                invocation.Target,
                invocation.Operation,
                $"calls {invocation.TargetContainingTypeName}.{invocation.TargetMethodName}",
                new ScenarioNodePresentation(
                    TargetContainingTypeName: invocation.TargetContainingTypeName,
                    TargetMemberName: invocation.TargetMethodName),
                evidence,
                CertaintyLevel.Exact,
                ordinal);
            nodes.Add(node);
            edges.Add(CreateEdge(profileId, entryPoint.EntryPointId, actionNode, node, ScenarioEdgeKind.Call,
                "direct method call", evidence, CertaintyLevel.Exact, ordinal));
        }

    }

    private static ScenarioDirectCallExpansion AddConfiguredDirectCalls(
        ScenarioAnalysisRequest request,
        NormalizedEntry entryPoint,
        CompilationProfileId profileId,
        ScenarioNode actionNode,
        List<ScenarioNode> nodes,
        List<ScenarioEdge> edges,
        List<ScenarioGraphDiagnostic> diagnostics)
    {
        var steps = new List<ScenarioDirectCallExpansionStep>();
        var path = new HashSet<MethodId>();
        var expandedMethods = new HashSet<MethodId> { entryPoint.RootMethod };
        var budget = request.EffectiveDiagramBudget;
        var complete = true;
        var duplicateOperations = DuplicateInvocationOperations(request, entryPoint.RootMethod);
        foreach (var operation in duplicateOperations)
        {
            var invocation = request.Behavior.MethodFlows.Single(flow => flow.Method == entryPoint.RootMethod)
                .Nodes.OfType<InvocationFlowNode>().First(node => node.Operation == operation);
            Boundary("SC-DIRECT-DUPLICATE", operation.Value, invocation.Evidence,
                "duplicate invocation anchors disagree on material facts");
        }
        var rootCalls = DirectCalls(request, entryPoint.RootMethod);

        void Boundary(string code, string operation, ImmutableArray<EvidenceRef> evidence, string detail)
        {
            complete = false;
            var label = code switch
            {
                "SC-DIRECT-METHOD-BUDGET" => "expanded-method budget",
                "SC-DIRECT-CALL-BUDGET" => "call budget",
                "SC-DIRECT-CYCLE" => "cycle",
                "SC-DIRECT-BODY-UNAVAILABLE" => "body-unavailable",
                "SC-DIRECT-SOURCE-UNAVAILABLE" => "source-unavailable",
                "SC-DIRECT-CROSS-PROJECT" => "cross-project",
                "SC-DIRECT-MISMATCH" => "method-flow mismatch",
                "SC-DIRECT-DUPLICATE" => "duplicate anchor",
                "SC-DIRECT-GUARDED" => "guarded nested call",
                _ => "incomplete",
            };
            var diagnostic = CreateDiagnostic(profileId, entryPoint.EntryPointId, code,
                $"The direct call expansion stopped at a {label} boundary.",
                $"operation={operation}; detail={detail}", evidence);
            diagnostics.Add(diagnostic);
        }

        var work = new Stack<(MethodId Caller, (InvocationFlowNode Invocation, CallSite Site) Candidate,
            int Depth, string? ParentStepId, bool Exit, MethodId Target)>();
        path.Add(entryPoint.RootMethod);
        foreach (var root in rootCalls.Reverse())
        {
            work.Push((entryPoint.RootMethod, root, 1, null, false, default));
        }

        while (work.Count > 0)
        {
            var frame = work.Pop();
            if (frame.Exit)
            {
                path.Remove(frame.Target);
                continue;
            }

            var caller = frame.Caller;
            var candidate = frame.Candidate;
            var depth = frame.Depth;
            var parentStepId = frame.ParentStepId;
            var invocation = candidate.Invocation;
            var site = candidate.Site;
            var evidence = Combine(invocation.Evidence, site.Evidence, site.Resolution.Evidence);
            if (steps.Count >= budget.MaxExpandedCalls)
            {
                Boundary("SC-DIRECT-CALL-BUDGET", invocation.Operation.Value, evidence,
                    $"maximum projected call occurrences reached ({budget.MaxExpandedCalls})");
                continue;
            }

            var target = site.Resolution.Candidates[0];
            var stepId = StableIdentity.CreateScenarioDirectCallExpansionId(
                new ScenarioDirectCallExpansionIdentityDescriptor(profileId, entryPoint.EntryPointId, site.Id.Value,
                    parentStepId, caller, target, invocation.Operation, depth));
            var node = CreateNodeWithPresentation(profileId, entryPoint.EntryPointId, ScenarioNodeKind.MethodCall,
                $"method-call:{stepId}", target, invocation.Operation,
                $"calls {invocation.TargetContainingTypeName}.{invocation.TargetMethodName}",
                new ScenarioNodePresentation(TargetContainingTypeName: invocation.TargetContainingTypeName,
                 TargetMemberName: invocation.TargetMethodName,
                 ArgumentLabel: FormatArgumentLabel(invocation.ConstantArguments)), evidence, CertaintyLevel.Exact, SourceOrdinal(invocation));
            nodes.Add(node);
            edges.Add(CreateEdge(profileId, entryPoint.EntryPointId,
                parentStepId is null ? actionNode : nodes.Single(item => item.Id == steps.Single(step => step.Id == parentStepId).ScenarioNodeId),
                 node, ScenarioEdgeKind.Call, "direct method call", evidence, CertaintyLevel.Exact, SourceOrdinal(invocation)));

            var method = request.ProgramIndex.Methods.SingleOrDefault(item => item.Id == target);
            var targetFlows = request.Behavior.MethodFlows.Where(flow => flow.Method == target).Take(2).ToArray();
            var stepComplete = true;
            var cycle = path.Contains(target);
            if (method is null || method.BodyFingerprint is null)
            {
                Boundary("SC-DIRECT-BODY-UNAVAILABLE", invocation.Operation.Value, evidence, target.Value);
                stepComplete = false;
            }
            else if (!invocation.IsLoadedProjectTarget
                || method.Evidence.Any(item => item.Kind == EvidenceKind.GeneratedSource))
            {
                Boundary("SC-DIRECT-SOURCE-UNAVAILABLE", invocation.Operation.Value, evidence,
                    method.Evidence.Any(item => item.Kind == EvidenceKind.GeneratedSource)
                        ? $"generated-source:{target.Value}"
                        : $"unloaded-project:{target.Value}");
                stepComplete = false;
            }
            else if (targetFlows.Length == 0)
            {
                Boundary("SC-DIRECT-NO-FLOW", invocation.Operation.Value, evidence, target.Value);
                stepComplete = false;
            }
            else if (targetFlows.Length > 1)
            {
                Boundary("SC-DIRECT-AMBIGUOUS-FLOW", invocation.Operation.Value, evidence, target.Value);
                stepComplete = false;
            }
            else if (cycle)
            {
                Boundary("SC-DIRECT-CYCLE", invocation.Operation.Value, evidence, target.Value);
                stepComplete = false;
            }
            else if (!expandedMethods.Contains(target) && expandedMethods.Count >= budget.MaxExpandedMethods)
            {
                Boundary("SC-DIRECT-METHOD-BUDGET", invocation.Operation.Value, evidence,
                    $"maximum expanded methods reached ({budget.MaxExpandedMethods})");
                stepComplete = false;
            }
            var step = new ScenarioDirectCallExpansionStep(stepId, parentStepId, depth, caller, target,
                invocation.Operation, node.Id, SourceOrdinal(invocation), evidence, CertaintyLevel.Exact, stepComplete, cycle);
            steps.Add(step);
            if (!stepComplete) { complete = false; continue; }

            expandedMethods.Add(target);
            path.Add(target);
            foreach (var operation in DuplicateInvocationOperations(request, target))
            {
                var duplicate = request.Behavior.MethodFlows.Single(flow => flow.Method == target)
                    .Nodes.OfType<InvocationFlowNode>().First(node => node.Operation == operation);
                Boundary("SC-DIRECT-DUPLICATE", operation.Value, duplicate.Evidence,
                    "duplicate invocation anchors disagree on material facts");
            }
            var children = new List<(InvocationFlowNode Invocation, CallSite Site)>();
            foreach (var child in DirectCalls(request, target))
            {
                if (HasLocalGuard(request, target, child.Invocation))
                {
                    Boundary("SC-DIRECT-GUARDED", child.Invocation.Operation.Value,
                        Combine(child.Invocation.Evidence, child.Site.Evidence), target.Value);
                    continue;
                }
                children.Add(child);
            }
            work.Push((target, default, 0, null, true, target));
            foreach (var child in children.AsEnumerable().Reverse())
            {
                work.Push((target, child, depth + 1, stepId, false, default));
            }
        }

        return new ScenarioDirectCallExpansion(steps.ToImmutableArray(), complete,
            diagnostics.Where(item => item.Code.StartsWith("SC-DIRECT-", StringComparison.Ordinal)).ToImmutableArray());
    }

    private static ScenarioDirectCallExpansion InheritDirectCallMembership(
        ScenarioTopology topology, ScenarioDirectCallExpansion expansion)
    {
        var memberships = topology.Memberships.GroupBy(item => item.ScenarioNode)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Arm).Distinct().OrderBy(item => item.Value, StringComparer.Ordinal).ToImmutableArray());
        var byId = expansion.Steps.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var steps = expansion.Steps.Select(step =>
        {
            var inherited = step.ParentStepId is { } parent && byId.TryGetValue(parent, out var parentStep)
                ? parentStep.RootArmIds
                : memberships.GetValueOrDefault(step.ScenarioNodeId, []);
            return step with { RootArmIds = inherited };
        }).ToImmutableArray();
        return expansion with { Steps = steps };
    }

    private static ScenarioTopology AddDirectCallMemberships(
        ScenarioTopology topology, ScenarioDirectCallExpansion expansion, MethodId rootMethod, CompilationProfileId profileId)
    {
        var decisionById = topology.Decisions.ToDictionary(decision => decision.Id);
        var armById = topology.Arms.ToDictionary(arm => arm.Id);
        var additions = new List<ScenarioMembership>();
        foreach (var step in expansion.Steps.Where(item => item.Depth > 1))
        {
            var parent = expansion.Steps.FirstOrDefault(item => item.Id == step.ParentStepId);
            foreach (var armId in step.RootArmIds)
            {
                var parentMembership = parent is null
                    ? null
                    : topology.Memberships.FirstOrDefault(item => item.ScenarioNode == parent.ScenarioNodeId && item.Arm == armId);
                parentMembership ??= topology.Memberships.FirstOrDefault(item => item.Arm == armId);
                if (parentMembership is null) { continue; }
                var evidence = Combine(parentMembership.Evidence, step.Evidence);
                additions.Add(new ScenarioMembership(
                    StableIdentity.CreateScenarioMembershipId(new ScenarioMembershipIdentityDescriptor(
                        profileId, rootMethod, armId, step.ScenarioNodeId)), armId, step.ScenarioNodeId,
                    evidence, LeastConfident(parentMembership.Certainty, evidence, step.Certainty)));
            }
        }
        return topology with
        {
            Memberships = topology.Memberships.Concat(additions)
                .GroupBy(item => (item.Arm.Value, item.ScenarioNode.Value)).Select(group => group.First())
                .OrderBy(item => decisionById[armById[item.Arm].Decision].ControllingFlowNode.Value, StringComparer.Ordinal)
                .ThenBy(item => armById[item.Arm].IsTrue)
                .ThenBy(item => item.ScenarioNode.Value, StringComparer.Ordinal)
                .ToImmutableArray()
        };
    }

    private static (InvocationFlowNode Invocation, CallSite Site)[] DirectCalls(ScenarioAnalysisRequest request, MethodId method)
    {
        var flow = request.Behavior.MethodFlows.SingleOrDefault(item => item.Method == method);
        if (flow is null)
        {
            return [];
        }
        return flow.Nodes.OfType<InvocationFlowNode>()
            .GroupBy(item => item.Operation)
            .Where(group => InvocationFactsAgree(group))
            .Select(group => group.OrderBy(item => item.Id.Value, StringComparer.Ordinal).First())
            .OrderBy(item => item.BlockOrdinal).ThenBy(item => item.EvaluationOrdinal)
            .ThenBy(item => item.Id.Value, StringComparer.Ordinal)
            .Select(invocation => (Invocation: invocation, Site: CanonicalSite(request, flow, invocation)))
            .Where(item => item.Site is not null && IsDirectExact(item.Invocation, item.Site!))
            .Select(item => (item.Invocation, item.Site!)).ToArray();
    }

    private static ImmutableArray<OperationId> DuplicateInvocationOperations(
        ScenarioAnalysisRequest request, MethodId method)
    {
        var flow = request.Behavior.MethodFlows.SingleOrDefault(item => item.Method == method);
        if (flow is null) { return []; }
        return flow.Nodes.OfType<InvocationFlowNode>()
            .GroupBy(item => item.Operation)
            .Where(group => group.Count() > 1 && !InvocationFactsAgree(group))
            .Select(group => group.Key)
            .OrderBy(operation => operation.Value, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static bool InvocationFactsAgree(IEnumerable<InvocationFlowNode> candidates)
    {
        var first = candidates.First();
        return candidates.All(candidate => candidate.Target == first.Target
            && candidate.IsDispatchable == first.IsDispatchable
            && candidate.IsDelegateOrEventInvoke == first.IsDelegateOrEventInvoke
            && candidate.IsStatic == first.IsStatic
            && candidate.IsConstructor == first.IsConstructor
            && candidate.IsDynamic == first.IsDynamic
            && candidate.Certainty == first.Certainty
            && candidate.TargetContainingTypeName == first.TargetContainingTypeName
            && candidate.TargetMethodName == first.TargetMethodName
            && candidate.IsInsideNestedFunction == first.IsInsideNestedFunction
            && candidate.IsSourceBacked == first.IsSourceBacked
            && candidate.IsLoadedProjectTarget == first.IsLoadedProjectTarget
            && candidate.BlockOrdinal == first.BlockOrdinal
            && candidate.EvaluationOrdinal == first.EvaluationOrdinal
            && candidate.TargetAssemblyName == first.TargetAssemblyName
            && candidate.IsPlatformTarget == first.IsPlatformTarget);
    }

    private static bool IsDirectExact(InvocationFlowNode invocation, CallSite site)
        => invocation.Certainty == CertaintyLevel.Exact && !invocation.Evidence.IsDefaultOrEmpty && invocation.IsSourceBacked
            && invocation.Target is not null && invocation.TargetContainingTypeName is not null
            && invocation.TargetMethodName is not null && !invocation.IsPlatformTarget
            && !invocation.IsInsideNestedFunction && !invocation.IsDynamic
            && !invocation.IsDelegateOrEventInvoke && !invocation.IsConstructor
             && site.DeclaredTarget == invocation.Target && site.Certainty == CertaintyLevel.Exact && !site.Evidence.IsDefaultOrEmpty
             && site.Resolution.Kind == CallResolutionKind.DirectExact && site.Resolution.IsComplete
             && site.Resolution.Candidates.Length == 1 && site.Resolution.Candidates[0] == invocation.Target
             && !site.Resolution.Evidence.IsDefaultOrEmpty
             && invocation.Evidence.All(item => item.Certainty == CertaintyLevel.Exact)
             && site.Evidence.All(item => item.Certainty == CertaintyLevel.Exact)
            && site.Resolution.Evidence.All(item => item.Certainty == CertaintyLevel.Exact);

    private static CallSite? CanonicalSite(ScenarioAnalysisRequest request, MethodFlowSnapshot flow, InvocationFlowNode invocation)
    {
        var sites = request.Behavior.CallGraph.CallSites.Where(site => site.ContainingMethod == flow.Method
                && site.InvocationOperation == invocation.Operation).ToArray();
        return sites.Length == 1 ? sites[0] : null;
    }

    private static bool HasLocalGuard(ScenarioAnalysisRequest request, MethodId method, InvocationFlowNode invocation)
    {
        var flow = request.Behavior.MethodFlows.Single(item => item.Method == method);
        var anchors = BuildOperationAnchors(flow);
        return anchors.TryGetValue(invocation.Operation.Value, out var ids)
            && ids.Any(id => flow.ControlDependences.Any(dependence => dependence.ControlledNode == id));
    }

    private static int SourceOrdinal(InvocationFlowNode invocation)
        => checked(invocation.BlockOrdinal * 1_000_000 + invocation.EvaluationOrdinal);

    private static ProjectId? ProjectOf(ScenarioAnalysisRequest request, MethodId method)
    {
        var programMethod = request.ProgramIndex.Methods.SingleOrDefault(item => item.Id == method);
        var type = programMethod is null ? null : request.ProgramIndex.Types.SingleOrDefault(item => item.Id == programMethod.ContainingType);
        return type?.Project;
    }

    private static bool TryGetHandlerFacts(ScenarioAnalysisRequest request, NormalizedEntry entry, out MinimalApiHandlerFact fact)
    {
        fact = null!;
        var set = request.HandlerFacts;
        if (set is null || set.Profile.Id != request.Profile.Id || set.ProgramIndexFingerprint != request.ProgramIndex.IndexFingerprint)
        {
            return false;
        }
        fact = set.Facts.FirstOrDefault(candidate => entry is not null
            && (candidate.HandlerRoot == entry.RootMethod
                || request.CallbackBoundaryFacts?.Boundaries.Any(boundary => boundary.Id == candidate.BoundaryId && boundary.TargetBodyOperation == candidate.BodyAnchor) == true))!;
        return fact is not null;
    }

    private static ScenarioHandlerTopology AddHandlerFacts(
        ScenarioAnalysisRequest request, NormalizedEntry entry, MinimalApiHandlerFact fact,
        List<ScenarioNode> nodes, List<ScenarioEdge> edges)
    {
        var parameters = fact.Parameters.Select(parameter => new ScenarioHandlerParameter(parameter.Name, parameter.TypeName, parameter.BindingKind, parameter.Evidence.IsDefaultOrEmpty ? fact.Evidence : parameter.Evidence, parameter.Certainty)).ToImmutableArray();
        foreach (var (parameter, sourceOrdinal) in parameters.Select((parameter, ordinal) => (parameter, ordinal)))
        {
            var node = CreateNodeWithPresentation(request.Profile.Id, entry.EntryPointId, ScenarioNodeKind.SourceObservation,
                $"handler-parameter:{parameter.Name}", entry.RootMethod, fact.BodyAnchor,
                $"receives {parameter.TypeName} {parameter.Name}",
                new ScenarioNodePresentation(
                    ActionKind: ScenarioActionKind.MinimalApiHandler,
                    HandlerBindingKind: parameter.BindingKind,
                    HandlerParameterName: parameter.Name,
                    HandlerParameterTypeName: parameter.TypeName,
                    SourceOrdinal: sourceOrdinal),
                parameter.Evidence);
            nodes.Add(node);
            edges.Add(CreateEdge(request.Profile.Id, entry.EntryPointId, nodes[1], node, ScenarioEdgeKind.Observation, string.Empty, parameter.Evidence));
        }
        var predicates = fact.Predicates.OrderBy(predicate => predicate.TrueArm.DecisionOrdinal).ToArray();
        var decisions = predicates.Select(predicate =>
        {
            var ordinal = predicate.TrueArm.DecisionOrdinal!.Value;
            var preceding = predicates.FirstOrDefault(candidate => candidate.TrueArm.DecisionOrdinal == ordinal - 1);
            return new ScenarioHandlerDecision(
            ordinal, preceding?.TrueArmTerminates == true ? ordinal - 1 : null,
            preceding?.TrueArmTerminates == true ? false : null,
            predicate.PredicateText, predicate.Evidence, predicate.Certainty);
        }).ToImmutableArray();
        var outcomes = fact.Outcomes.OrderBy(outcome => outcome.Arm.SourceOrdinal)
            .Select(outcome =>
            {
                var arm = ResolveHandlerArm(outcome.Arm, predicates);
                return new ScenarioHandlerOutcome(outcome.Arm.SourceOrdinal, arm.DecisionOrdinal, arm.IsTrue,
                    outcome.StatusCode!.Value, outcome.FactoryIdentity, outcome.Evidence, outcome.Certainty);
            }).ToImmutableArray();
        var delays = fact.Operations.Where(operation => operation.Kind == MinimalApiHandlerOperationKind.Delay)
            .OrderBy(operation => operation.Arm.SourceOrdinal)
            .Select(operation =>
            {
                var arm = ResolveHandlerArm(operation.Arm, predicates);
                return new ScenarioHandlerDelay(operation.Arm.SourceOrdinal, arm.DecisionOrdinal, arm.IsTrue,
                    operation.DelayMilliseconds!.Value, operation.Evidence, operation.Certainty);
            }).ToImmutableArray();
        foreach (var operation in fact.Operations.Where(operation => operation.Kind is MinimalApiHandlerOperationKind.Delay or MinimalApiHandlerOperationKind.Outcome)
                     .OrderBy(operation => operation.Arm.SourceOrdinal))
        {
            var kind = operation.Kind == MinimalApiHandlerOperationKind.Delay ? ScenarioNodeKind.Delay : ScenarioNodeKind.Outcome;
            var detail = operation.Kind == MinimalApiHandlerOperationKind.Delay
                ? $"requested delay {operation.DelayMilliseconds} milliseconds"
                : $"HTTP {operation.StatusCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"} ({operation.FactoryIdentity})";
            nodes.Add(CreateNodeWithPresentation(
                request.Profile.Id,
                entry.EntryPointId,
                kind,
                $"handler-operation:{operation.Id.Value}",
                entry.RootMethod,
                operation.Id,
                detail,
                new ScenarioNodePresentation(
                    ActionKind: ScenarioActionKind.MinimalApiHandler,
                    OutcomeStatusCode: kind == ScenarioNodeKind.Outcome ? operation.StatusCode : null,
                    SourceOrdinal: operation.Arm.SourceOrdinal),
                operation.Evidence));
        }
        return new ScenarioHandlerTopology(parameters, decisions, outcomes, delays);
    }

    private static (int DecisionOrdinal, bool IsTrue) ResolveHandlerArm(
        MinimalApiHandlerArm arm, MinimalApiHandlerPredicate[] predicates)
    {
        if (arm.DecisionOrdinal is int explicitOrdinal)
        {
            return (explicitOrdinal, arm.IsTrue);
        }
        var matches = new List<(int DecisionOrdinal, bool IsTrue)>();
        for (int ordinal = 0; ordinal < predicates.Length; ordinal++)
        {
            var predicate = predicates[ordinal];
            if (predicate.TrueArm.SourceOrdinal == arm.SourceOrdinal)
            {
                matches.Add((ordinal, true));
            }
            if (predicate.FalseArm.SourceOrdinal == arm.SourceOrdinal)
            {
                matches.Add((ordinal, false));
            }
        }
        if (matches.Count > 0)
        {
            return matches[^1];
        }
        return (0, arm.IsTrue);
    }

    /// <summary>
    /// Materializes the complete conditional composition into the flat graph (accepted contract requirement 7).
    /// Each arm gains one exact <see cref="ScenarioNodeKind.ServiceCall"/> node anchored at the
    /// shared action call-site operation and evidence plus an action-&gt;service Call edge, and then
    /// joins that arm's method-specific EF query/state/mutation facts to its exact service node. The
    /// nodes added for each arm (service node plus its own facts) become the arm's canonical
    /// member-node identities, so the arms are disjoint by construction: the SQL arm never carries
    /// the JSON arm's nodes or facts and no cross-parent/cross-arm evidence leaks. A missing call
    /// site (defensively impossible after <see cref="TryResolveComposition"/>) retains the sparse
    /// composition unchanged.
    /// </summary>
    private static ScenarioServiceComposition JoinCompositionArms(
        ScenarioAnalysisRequest request,
        NormalizedEntry entryPoint,
        CompilationProfileId profileId,
        ScenarioServiceComposition composition,
        ScenarioNode actionNode,
        List<ScenarioNode> nodes,
        List<ScenarioEdge> edges,
        List<ScenarioGraphDiagnostic> diagnostics)
    {
        var entryPointId = entryPoint.EntryPointId;
        var callSite = FindCompositionCallSite(request, entryPoint.RootMethod, composition.ServiceType);
        if (callSite is null)
        {
            return composition;
        }

        var trueStart = nodes.Count;
        var trueServiceNode = CreateCompositionArmServiceNode(
            request,
            profileId,
            entryPointId,
            composition.ServiceType,
            composition.TrueArm,
            callSite);
        nodes.Add(trueServiceNode);
        edges.Add(CreateEdge(
            profileId,
            entryPointId,
            actionNode,
            trueServiceNode,
            ScenarioEdgeKind.Call,
            $"call through {composition.ServiceType}",
            Combine(callSite.Evidence, callSite.Resolution.Evidence, composition.TrueArm.Evidence)));
        JoinEntityQueries(
            request,
            profileId,
            entryPointId,
            composition.TrueArm.ResolvedMethod,
            trueServiceNode,
            nodes,
            edges,
            diagnostics);
        JoinStateAssignments(
            request,
            profileId,
            entryPointId,
            composition.TrueArm.ResolvedMethod,
            trueServiceNode,
            nodes,
            edges);
        JoinEntityMutations(
            request,
            profileId,
            entryPointId,
            composition.TrueArm.ResolvedMethod,
            trueServiceNode,
            nodes,
            edges);
        var trueMemberNodes = nodes.Skip(trueStart).Select(node => node.Id).ToImmutableArray();

        var falseStart = nodes.Count;
        var falseServiceNode = CreateCompositionArmServiceNode(
            request,
            profileId,
            entryPointId,
            composition.ServiceType,
            composition.FalseArm,
            callSite);
        nodes.Add(falseServiceNode);
        edges.Add(CreateEdge(
            profileId,
            entryPointId,
            actionNode,
            falseServiceNode,
            ScenarioEdgeKind.Call,
            $"call through {composition.ServiceType}",
            Combine(callSite.Evidence, callSite.Resolution.Evidence, composition.FalseArm.Evidence)));
        JoinEntityQueries(
            request,
            profileId,
            entryPointId,
            composition.FalseArm.ResolvedMethod,
            falseServiceNode,
            nodes,
            edges,
            diagnostics);
        JoinStateAssignments(
            request,
            profileId,
            entryPointId,
            composition.FalseArm.ResolvedMethod,
            falseServiceNode,
            nodes,
            edges);
        JoinEntityMutations(
            request,
            profileId,
            entryPointId,
            composition.FalseArm.ResolvedMethod,
            falseServiceNode,
            nodes,
            edges);
        var falseMemberNodes = nodes.Skip(falseStart).Select(node => node.Id).ToImmutableArray();

        var trueArm = new ScenarioServiceAlternativeArm(
            composition.TrueArm.IsTrue,
            composition.TrueArm.RegistrationId,
            composition.TrueArm.ImplementationType,
            composition.TrueArm.ResolvedMethod,
            composition.TrueArm.Evidence,
            composition.TrueArm.Certainty,
            trueMemberNodes);
        var falseArm = new ScenarioServiceAlternativeArm(
            composition.FalseArm.IsTrue,
            composition.FalseArm.RegistrationId,
            composition.FalseArm.ImplementationType,
            composition.FalseArm.ResolvedMethod,
            composition.FalseArm.Evidence,
            composition.FalseArm.Certainty,
            falseMemberNodes);
        return new ScenarioServiceComposition(
            composition.Id,
            composition.ServiceType,
            composition.Decision,
            trueArm,
            falseArm,
            composition.ProfileSelection);
    }

    /// <summary>
    /// The exact single action call site whose declared target implements the composition service
    /// type. <see cref="TryResolveComposition"/> already proved exactly one matching site with
    /// complete resolution, so the lookup is defensive and never first-selects.
    /// </summary>
    private static CallSite? FindCompositionCallSite(
        ScenarioAnalysisRequest request,
        MethodId action,
        string serviceType)
    {
        var programIndex = request.ProgramIndex;
        return request.Behavior.CallGraph.CallSites
            .FirstOrDefault(callSite => callSite.ContainingMethod == action
                && callSite.DeclaredTarget is { } target
                && DeclaringTypeName(programIndex, target) == serviceType);
    }

    /// <summary>
    /// One exact service-call node for one composition arm: the arm's resolved implementation
    /// method anchored at the shared action call-site operation, with the composition service type,
    /// the arm implementation type, and the canonical called-member name as typed presentation. The
    /// evidence is the shared action call-site evidence plus the arm's own resolution/registration
    /// evidence.
    /// </summary>
    private static ScenarioNode CreateCompositionArmServiceNode(
        ScenarioAnalysisRequest request,
        CompilationProfileId profileId,
        EntryPointId entryPointId,
        string serviceType,
        ScenarioServiceAlternativeArm arm,
        CallSite callSite)
        => CreateNodeWithPresentation(
            profileId,
            entryPointId,
            ScenarioNodeKind.ServiceCall,
            $"service:{arm.ResolvedMethod.Value}",
            arm.ResolvedMethod,
            callSite.InvocationOperation,
            $"resolved service implementation {arm.ImplementationType}",
            new ScenarioNodePresentation(
                ContractTypeName: serviceType,
                ImplementationTypeName: arm.ImplementationType,
                CalledMemberName: MethodConciseName(request.ProgramIndex, arm.ResolvedMethod),
                ArgumentLabel: FormatArgumentLabel(InvocationConstantArguments(request, arm.ResolvedMethod, callSite.InvocationOperation))),
            Combine(callSite.Evidence, callSite.Resolution.Evidence, arm.Evidence));

    /// <summary>
    /// Joins every EF query fact of the service method as its own evidence-backed query node, ordered
    /// by the source-order sequence the Roslyn traversal recorded. A query without a sequence entry
    /// falls back to stable operation identity so ordering never depends on encounter order. The
    /// exact service node parameter keeps every query edge scoped to the method's own service node,
    /// so composition arms never share query membership.
    /// </summary>
    private static void JoinEntityQueries(
        ScenarioAnalysisRequest request,
        CompilationProfileId profileId,
        EntryPointId entryPointId,
        MethodId serviceMethod,
        ScenarioNode serviceNode,
        List<ScenarioNode> nodes,
        List<ScenarioEdge> edges,
        List<ScenarioGraphDiagnostic> diagnostics)
    {
        var efFacts = request.FrameworkFacts.Facts
            .OfType<EntityFrameworkQueryFact>()
            .Where(fact => fact.Method == serviceMethod)
            .ToArray();
        var queryOrder = request.NonGetSemanticFacts.EfOperationSequence
            .Where(item => item.Method == serviceMethod && item.Kind == EfOperationSequenceKind.QueryTerminal)
            .GroupBy(item => item.Operation.Value)
            .ToDictionary(group => group.Key, group => group.Min(item => item.Ordinal), StringComparer.Ordinal);
        var ordered = efFacts
            .OrderBy(fact => queryOrder.GetValueOrDefault(fact.Operation.Value, int.MaxValue))
            .ThenBy(fact => fact.Operation.Value, StringComparer.Ordinal)
            .ToArray();
        foreach (var fact in ordered)
        {
            int ordinal = queryOrder.GetValueOrDefault(fact.Operation.Value, 0);
            var queryNode = BuildEntityQueryNode(
                request,
                profileId,
                entryPointId,
                serviceMethod,
                fact,
                diagnostics,
                ordinal);
            nodes.Add(queryNode);
            edges.Add(CreateEdge(
                profileId,
                entryPointId,
                serviceNode,
                queryNode,
                ScenarioEdgeKind.Query,
                BuildQueryEdgeDetail(fact),
                queryNode.Evidence,
                queryNode.Certainty,
                ordinal));
        }
    }

    /// <summary>Joins every exact state assignment of the service method as an ordered state node.</summary>
    private static void JoinStateAssignments(
        ScenarioAnalysisRequest request,
        CompilationProfileId profileId,
        EntryPointId entryPointId,
        MethodId serviceMethod,
        ScenarioNode serviceNode,
        List<ScenarioNode> nodes,
        List<ScenarioEdge> edges)
    {
        foreach (var assignment in request.NonGetSemanticFacts.StateAssignments
                     .Where(fact => fact.Method == serviceMethod)
                     .OrderBy(fact => fact.SequenceOrdinal)
                     .ThenBy(fact => fact.Operation.Value, StringComparer.Ordinal))
        {
            var stateNode = CreateNode(
                profileId,
                entryPointId,
                ScenarioNodeKind.StateAssignment,
                $"state:{assignment.Operation.Value}",
                serviceMethod,
                assignment.Operation,
                $"{ShortTypeName(assignment.TargetMember)} = {assignment.Value}",
                assignment.Evidence,
                assignment.Certainty,
                assignment.SequenceOrdinal);
            nodes.Add(stateNode);
            edges.Add(CreateEdge(
                profileId,
                entryPointId,
                serviceNode,
                stateNode,
                ScenarioEdgeKind.StateAssignment,
                "exact state assignment",
                assignment.Evidence,
                assignment.Certainty,
                assignment.SequenceOrdinal));
        }
    }

    /// <summary>Joins every exact EF mutation and save of the service method in source order.</summary>
    private static void JoinEntityMutations(
        ScenarioAnalysisRequest request,
        CompilationProfileId profileId,
        EntryPointId entryPointId,
        MethodId serviceMethod,
        ScenarioNode serviceNode,
        List<ScenarioNode> nodes,
        List<ScenarioEdge> edges)
    {
        foreach (var mutation in request.NonGetSemanticFacts.EntityFrameworkMutations
                     .Where(fact => fact.Method == serviceMethod)
                     .OrderBy(fact => fact.SequenceOrdinal))
        {
            var mutationNode = CreateNode(
                profileId,
                entryPointId,
                ScenarioNodeKind.EntityMutation,
                $"mutation:{mutation.Operation.Value}",
                serviceMethod,
                mutation.Operation,
                BuildMutationDetail(mutation),
                mutation.Evidence,
                mutation.Certainty,
                mutation.SequenceOrdinal,
                new ScenarioNodePresentation(
                    DbContextTypeName: mutation.DbContextType,
                    EntityTypeName: mutation.EntityType,
                    MutationKind: mutation.MutationKind));
            nodes.Add(mutationNode);
            var edgeKind = mutation.MutationKind == EntityFrameworkMutationKind.SaveChangesAsync
                ? ScenarioEdgeKind.Save
                : ScenarioEdgeKind.Mutation;
            edges.Add(CreateEdge(
                profileId,
                entryPointId,
                serviceNode,
                mutationNode,
                edgeKind,
                edgeKind == ScenarioEdgeKind.Save ? "persists changes" : "mutates tracked entities",
                mutation.Evidence,
                mutation.Certainty,
                mutation.SequenceOrdinal));
        }
    }

    /// <summary>
    /// Joins every evidenced source observation of the action and service methods. Observations are
    /// non-interaction facts: they carry evidence and conservative certainty but never become diagram
    /// interactions or behavioral edges.
    /// </summary>
    private static void JoinSourceObservations(
        ScenarioAnalysisRequest request,
        CompilationProfileId profileId,
        EntryPointId entryPointId,
        MethodId actionMethod,
        MethodId serviceMethod,
        ScenarioNode actionNode,
        ScenarioNode serviceNode,
        List<ScenarioNode> nodes,
        List<ScenarioEdge> edges)
    {
        foreach (var observation in request.NonGetSemanticFacts.SourceObservations
                     .Where(fact => fact.Method == actionMethod || fact.Method == serviceMethod)
                     .OrderBy(fact => fact.Method.Value, StringComparer.Ordinal)
                     .ThenBy(fact => fact.AnchorOperation.Value, StringComparer.Ordinal))
        {
            var sourceNode = observation.Method == actionMethod ? actionNode : serviceNode;
            var observationNode = CreateNode(
                profileId,
                entryPointId,
                ScenarioNodeKind.SourceObservation,
                $"observation:{observation.Id.Value}",
                observation.Method,
                observation.AnchorOperation,
                BuildObservationDetail(observation),
                observation.Evidence,
                observation.Certainty);
            nodes.Add(observationNode);
            edges.Add(CreateEdge(
                profileId,
                entryPointId,
                sourceNode,
                observationNode,
                ScenarioEdgeKind.Observation,
                "source observation (non-interaction)",
                observation.Evidence));
        }
    }

    /// <summary>
    /// Joins compiler-proven status-switch arms to exact HTTP outcomes by method and exact operation
    /// identity. The helper kind is only a consistency check, never the join key; an arm whose exact
    /// outcome operation is missing, non-unique, or helper-kind-mismatched leaves that arm's outcome
    /// claim withheld. A CreatedAtAction arm additionally requires exactly one Get entry point for its
    /// compiler-bound target controller method identity.
    /// </summary>
    private static void JoinStatusSwitchOutcomes(
        ScenarioAnalysisRequest request,
        CompilationProfileId profileId,
        EntryPointId entryPointId,
        ScenarioNode serviceNode,
        List<ScenarioNode> nodes,
        List<ScenarioEdge> edges,
        List<ScenarioGraphDiagnostic> diagnostics,
        ImmutableArray<StatusSwitchArmFact> arms)
    {
        var outcomes = request.FrameworkFacts.Facts
            .OfType<HttpDirectOutcomeFact>()
            .Where(fact => fact.RootMethod == arms[0].Method)
            .ToImmutableArray();
        var statusResultNode = CreateNode(
            profileId,
            entryPointId,
            ScenarioNodeKind.Result,
            "result-status",
            arms[0].Method,
            arms[0].SwitchOperation,
            $"status result of {arms[0].StatusEnumType}",
            Combine(arms.Select(arm => arm.Evidence).ToArray()));
        nodes.Add(statusResultNode);
        edges.Add(CreateEdge(
            profileId,
            entryPointId,
            serviceNode,
            statusResultNode,
            ScenarioEdgeKind.ResultStatus,
            "status result",
            statusResultNode.Evidence,
            statusResultNode.Certainty));

        foreach (var arm in arms.OrderBy(arm => arm.StatusMemberName, StringComparer.Ordinal))
        {
            var outcomeFacts = outcomes
                .Where(outcome => outcome.Operation == arm.OutcomeOperation)
                .ToArray();
            if (outcomeFacts.Length != 1
                || outcomeFacts[0].HelperKind != arm.HelperKind)
            {
                diagnostics.Add(CreateDiagnostic(
                    profileId,
                    entryPointId,
                    "SC004",
                    "The status-switch arm has no unique HTTP outcome fact for its exact operation.",
                    $"{entryPointId.Value}\u001f{arm.HelperKind.ToString()}\u001f{arm.StatusMemberName}\u001f{arm.OutcomeOperation.Value}"));
                continue;
            }

            var outcome = outcomeFacts[0];
            var polarity = OutcomePolarityEdgeKind(outcome.StatusCode);
            if (polarity is null)
            {
                // Unsupported HTTP status polarity (1xx/3xx) fails closed: no outcome node and no
                // edge, with an explicit deterministic SC004 diagnostic.
                diagnostics.Add(CreateDiagnostic(
                    profileId,
                    entryPointId,
                    "SC004",
                    "The status-switch arm outcome has an unsupported HTTP status polarity.",
                    $"{entryPointId.Value}\u001f{arm.HelperKind.ToString()}\u001f{arm.StatusMemberName}\u001f{outcome.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
                continue;
            }

            var detail = $"{arm.HelperKind} -> HTTP {outcome.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            string? createdRoute = null;
            if (arm.HelperKind == HttpOutcomeHelperKind.CreatedAtAction
                && arm.CreatedTargetMethod is { } targetMethod)
            {
                createdRoute = ResolveCreatedGetRoute(request, targetMethod);
                if (createdRoute is null)
                {
                    diagnostics.Add(CreateDiagnostic(
                        profileId,
                        entryPointId,
                        "SC010",
                        "The created outcome could not be joined to exactly one Get entry point.",
                        $"{arm.Method.Value}\u001f{targetMethod.Value}"));
                }
                else
                {
                    detail += $" links to GET {createdRoute}";
                }
            }

            var key = $"outcome:{outcome.StatusCode}:{outcome.HelperKind}";
            var outcomeNode = nodes.FirstOrDefault(node => node.Key == key);
            if (outcomeNode is null)
            {
                outcomeNode = CreateNodeWithPresentation(
                    profileId,
                    entryPointId,
                    ScenarioNodeKind.Outcome,
                    key,
                    outcome.RootMethod,
                    outcome.Operation,
                    detail,
                    new ScenarioNodePresentation(
                        OutcomeHelperKind: outcome.HelperKind,
                        OutcomeStatusCode: outcome.StatusCode,
                        OutcomeCreatedRoute: createdRoute),
                    outcome.Evidence);
                nodes.Add(outcomeNode);
            }

            var edgeKind = polarity.Value;
            edges.Add(CreateEdge(
                profileId,
                entryPointId,
                statusResultNode,
                outcomeNode,
                edgeKind,
                $"{arm.HelperKind} outcome",
                Combine(statusResultNode.Evidence, outcome.Evidence)));
        }

        JoinDirectTerminalOutcomes(
            request,
            profileId,
            entryPointId,
            statusResultNode,
            nodes,
            edges,
            diagnostics,
            arms);
    }

    /// <summary>
    /// Joins direct terminal outcomes of the same action that are NOT represented by any status-arm
    /// operation. Each companion fact is keyed by its exact invocation operation; an operation already
    /// carried by an admitted arm is skipped (dedupe by exact operation identity), so a CreatedAtAction
    /// inside a switch stays single. The join reuses the authoritative <see cref="HttpDirectOutcomeFact"/>
    /// for the exact status and helper kind; a missing, non-unique, or helper-kind-mismatched fact
    /// fails closed with SC004 and never invents an outcome. A CreatedAtAction terminal additionally
    /// requires exactly one Get entry point for its compiler-bound target controller method identity;
    /// unrelated or ambiguous targets fail closed with SC010.
    /// </summary>
    private static void JoinDirectTerminalOutcomes(
        ScenarioAnalysisRequest request,
        CompilationProfileId profileId,
        EntryPointId entryPointId,
        ScenarioNode statusResultNode,
        List<ScenarioNode> nodes,
        List<ScenarioEdge> edges,
        List<ScenarioGraphDiagnostic> diagnostics,
        ImmutableArray<StatusSwitchArmFact> arms)
    {
        var method = arms[0].Method;
        var outcomes = request.FrameworkFacts.Facts
            .OfType<HttpDirectOutcomeFact>()
            .Where(fact => fact.RootMethod == method)
            .ToArray();
        var representedOperations = arms
            .Select(arm => arm.OutcomeOperation.Value)
            .ToHashSet(StringComparer.Ordinal);
        var terminals = request.NonGetSemanticFacts.DirectTerminalOutcomes
            .Where(fact => fact.Method == method)
            .OrderBy(fact => fact.SequenceOrdinal)
            .ThenBy(fact => fact.Operation.Value, StringComparer.Ordinal)
            .ToArray();
        foreach (var terminal in terminals)
        {
            if (representedOperations.Contains(terminal.Operation.Value))
            {
                // The status arm already carries this exact invocation; the direct-terminal companion
                // never duplicates an arm outcome (dedupe by exact operation identity).
                continue;
            }

            var outcomeFacts = outcomes
                .Where(outcome => outcome.Operation == terminal.Operation)
                .ToArray();
            if (outcomeFacts.Length != 1
                || outcomeFacts[0].HelperKind != terminal.HelperKind)
            {
                diagnostics.Add(CreateDiagnostic(
                    profileId,
                    entryPointId,
                    "SC004",
                    "The direct terminal outcome has no unique HTTP outcome fact for its exact operation.",
                    $"{entryPointId.Value}\u001f{terminal.HelperKind.ToString()}\u001f{terminal.Operation.Value}"));
                continue;
            }

            var outcome = outcomeFacts[0];
            var polarity = OutcomePolarityEdgeKind(outcome.StatusCode);
            if (polarity is null)
            {
                // Unsupported HTTP status polarity (1xx/3xx) fails closed: no outcome node and no
                // edge, with an explicit deterministic SC004 diagnostic.
                diagnostics.Add(CreateDiagnostic(
                    profileId,
                    entryPointId,
                    "SC004",
                    "The direct terminal outcome has an unsupported HTTP status polarity.",
                    $"{entryPointId.Value}\u001f{terminal.HelperKind.ToString()}\u001f{terminal.Operation.Value}\u001f{outcome.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
                continue;
            }

            var detail = $"{terminal.HelperKind} -> HTTP {outcome.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            string? createdRoute = null;
            if (terminal.HelperKind == HttpOutcomeHelperKind.CreatedAtAction
                && terminal.CreatedTargetMethod is { } targetMethod)
            {
                createdRoute = ResolveCreatedGetRoute(request, targetMethod);
                if (createdRoute is null)
                {
                    diagnostics.Add(CreateDiagnostic(
                        profileId,
                        entryPointId,
                        "SC010",
                        "The created outcome could not be joined to exactly one Get entry point.",
                        $"{terminal.Method.Value}\u001f{targetMethod.Value}"));
                }
                else
                {
                    detail += $" links to GET {createdRoute}";
                }
            }

            var key = $"outcome:{outcome.StatusCode}:{outcome.HelperKind}";
            var outcomeNode = nodes.FirstOrDefault(node => node.Key == key);
            if (outcomeNode is null)
            {
                outcomeNode = CreateNodeWithPresentation(
                    profileId,
                    entryPointId,
                    ScenarioNodeKind.Outcome,
                    key,
                    outcome.RootMethod,
                    outcome.Operation,
                    detail,
                    new ScenarioNodePresentation(
                        OutcomeHelperKind: outcome.HelperKind,
                        OutcomeStatusCode: outcome.StatusCode,
                        OutcomeCreatedRoute: createdRoute),
                    outcome.Evidence);
                nodes.Add(outcomeNode);
            }

            var edgeKind = polarity.Value;
            edges.Add(CreateEdge(
                profileId,
                entryPointId,
                statusResultNode,
                outcomeNode,
                edgeKind,
                $"{terminal.HelperKind} outcome",
                Combine(statusResultNode.Evidence, outcome.Evidence)));
        }
    }

    /// <summary>
    /// Derives the exact outcome polarity from the compiler-proven HTTP status code, never from the
    /// helper kind: 200-299 is success, 400-599 is failure, and any other status (1xx/3xx) is
    /// unsupported so the arm/terminal fails closed with SC004 and no outcome claim.
    /// </summary>
    private static ScenarioEdgeKind? OutcomePolarityEdgeKind(int statusCode)
        => statusCode is >= 200 and <= 299 ? ScenarioEdgeKind.OutcomeSuccess
            : statusCode is >= 400 and <= 599 ? ScenarioEdgeKind.OutcomeFailure
            : null;

    /// <summary>
    /// Resolves the Get route a CreatedAtAction arm links to. The join is by the compiler-bound target
    /// controller method identity only; a global action-name text match can never select an unrelated
    /// or overloaded controller. Exactly one Get entry point for that root method is required.
    /// </summary>
    private static string? ResolveCreatedGetRoute(ScenarioAnalysisRequest request, MethodId targetMethod)
    {
        var matches = request.FrameworkFacts.Facts
            .OfType<HttpEntryPointFact>()
            .Where(entryPoint => entryPoint.HttpMethod == HttpMethodKind.Get
                && entryPoint.RootMethod == targetMethod)
            .ToArray();
        return matches.Length == 1 ? matches[0].CanonicalRoute : null;
    }

    private static string BuildQueryEdgeDetail(EntityFrameworkQueryFact fact)
    {
        var terminal = fact.Chain.LastOrDefault()?.OperatorKind;
        return terminal == EntityFrameworkQueryOperatorKind.CountAsync
            ? $"count on {fact.EntityType}"
            : $"single-or-default on {fact.EntityType}";
    }

    private static string BuildMutationDetail(EntityFrameworkMutationFact mutation)
    {
        var entityName = ShortTypeName(mutation.EntityType);
        return mutation.MutationKind switch
        {
            EntityFrameworkMutationKind.Add => $"adds {entityName}",
            EntityFrameworkMutationKind.RemoveRange => $"removes {entityName} records",
            EntityFrameworkMutationKind.Clear => $"clears the tracked {entityName} set",
            EntityFrameworkMutationKind.SaveChangesAsync => $"saves changes to {ShortTypeName(mutation.DbContextType)}",
            _ => "mutates the data store",
        };
    }

    private static string BuildObservationDetail(SourceObservationSemanticFact observation)
    {
        // The wording phrase already prefixes "Source observation:"; stripping the marker avoids the
        // duplicated "todo — TODO:" form while keeping the evidenced comment text.
        foreach (var marker in new[] { "TODO:", "FIXME:", "HACK:", "NOTE:" })
        {
            if (observation.Text.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            {
                return observation.Text[marker.Length..].TrimStart();
            }
        }

        return observation.Text;
    }

    private static string ShortTypeName(string fullyQualifiedName)
    {
        var last = fullyQualifiedName.Split(['.', '+'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return string.IsNullOrWhiteSpace(last) ? fullyQualifiedName : last;
    }

    /// <summary>
    /// Joins the accepted structural-result decision (an exact IsSuccess branch) to its HTTP outcomes.
    /// This is the Get-flow path; status-switch flows use <see cref="JoinStatusSwitchOutcomes"/>.
    /// </summary>
    private static void JoinStructuralResultOutcomes(
        ScenarioAnalysisRequest request,
        CompilationProfileId profileId,
        EntryPointId entryPointId,
        MethodId rootMethod,
        ServiceResolution resolution,
        ScenarioNode serviceNode,
        List<ScenarioNode> nodes,
        List<ScenarioEdge> edges,
        List<ScenarioGraphDiagnostic> diagnostics)
    {
        var decision = request.StructuralResultFacts.Decisions
            .FirstOrDefault(fact => fact.Method == rootMethod);
        if (decision is null)
        {
            diagnostics.Add(CreateDiagnostic(
                profileId,
                entryPointId,
                "SC006",
                "The action has no compiler-proven IsSuccess decision to join to HTTP outcomes.",
                rootMethod.Value));
            return;
        }

        var association = TryResolveResultAssociation(
            request,
            resolution,
            decision,
            profileId,
            entryPointId,
            diagnostics);
        if (association is null)
        {
            // SC007 was recorded; result and outcome claims are withheld entirely.
            return;
        }

        var (successFactory, failureFactory) = association.Value;
        var resultSuccessNode = CreateNodeWithPresentation(
            profileId,
            entryPointId,
            ScenarioNodeKind.Result,
            "result-success",
            resolution.ServiceMethod,
            successFactory.Operation,
            $"success result with data of {successFactory.ResultType}",
            new ScenarioNodePresentation(ResultFactoryKind: successFactory.FactoryKind),
            Combine(successFactory.Evidence, decision.Evidence));
        nodes.Add(resultSuccessNode);
        edges.Add(CreateEdge(
            profileId,
            entryPointId,
            serviceNode,
            resultSuccessNode,
            ScenarioEdgeKind.ResultSuccess,
            "success factory carries data",
            successFactory.Evidence));

        var resultFailureNode = CreateNodeWithPresentation(
            profileId,
            entryPointId,
            ScenarioNodeKind.Result,
            "result-failure",
            resolution.ServiceMethod,
            failureFactory.Operation,
            $"failure result with status {failureFactory.FactoryKind.ToString()} of {failureFactory.ResultType}",
            new ScenarioNodePresentation(ResultFactoryKind: failureFactory.FactoryKind),
            Combine(failureFactory.Evidence, decision.Evidence));
        nodes.Add(resultFailureNode);
        edges.Add(CreateEdge(
            profileId,
            entryPointId,
            serviceNode,
            resultFailureNode,
            ScenarioEdgeKind.ResultFailure,
            "failure factory carries status",
            failureFactory.Evidence));

        var outcomes = request.FrameworkFacts.Facts
            .OfType<HttpDirectOutcomeFact>()
            .Where(fact => fact.RootMethod == rootMethod)
            .ToImmutableArray();
        JoinOutcomePaths(
            request,
            profileId,
            entryPointId,
            nodes,
            edges,
            diagnostics,
            decision.SuccessPath,
            ScenarioEdgeKind.OutcomeSuccess,
            "result-success",
            outcomes);
        JoinOutcomePaths(
            request,
            profileId,
            entryPointId,
            nodes,
            edges,
            diagnostics,
            decision.FailurePath,
            ScenarioEdgeKind.OutcomeFailure,
            "result-failure",
            outcomes);
    }

    /// <summary>
    /// Associates the service result with exactly one success and one failure factory whose results
    /// are proven to flow to the service return (return provenance), and joins the decision's result
    /// local through the accepted local value graph to the service result type. Any missing,
    /// duplicate, or unrelated factory, or an unjoinable decision local, records SC007 and withholds
    /// every result/outcome claim.
    /// </summary>
    private static (StructuralResultFactoryFact Success, StructuralResultFactoryFact Failure)? TryResolveResultAssociation(
        ScenarioAnalysisRequest request,
        ServiceResolution resolution,
        StructuralResultDecisionFact decision,
        CompilationProfileId profileId,
        EntryPointId entryPointId,
        List<ScenarioGraphDiagnostic> diagnostics)
    {
        var factories = request.StructuralResultFacts.Factories
            .Where(fact => fact.Method == resolution.ServiceMethod)
            .ToArray();
        var returnedOperations = request.SemanticFacts.ReturnProvenances
            .Where(provenance => provenance.Method == resolution.ServiceMethod)
            .Select(provenance => provenance.ValueOperation.Value)
            .ToHashSet(StringComparer.Ordinal);
        var associated = factories
            .Where(fact => returnedOperations.Contains(fact.Operation.Value))
            .ToArray();
        if (associated.Length == 0)
        {
            diagnostics.Add(CreateDiagnostic(
                profileId,
                entryPointId,
                "SC007",
                "No factory result is proven to flow to the service return; result and outcome claims are withheld.",
                $"{resolution.ServiceMethod.Value}\u001fno-return-provenance"));
            return null;
        }

        var successFactories = associated
            .Where(fact => fact.FactoryKind == StructuralResultFactoryKind.Success)
            .ToArray();
        var failureFactories = associated
            .Where(fact => fact.FactoryKind == StructuralResultFactoryKind.NotFound)
            .ToArray();
        if (successFactories.Length != 1 || failureFactories.Length != 1)
        {
            diagnostics.Add(CreateDiagnostic(
                profileId,
                entryPointId,
                "SC007",
                "The service result requires exactly one success and one failure factory; result and outcome claims are withheld.",
                $"{resolution.ServiceMethod.Value}\u001fsuccess={successFactories.Length}\u001ffailure={failureFactories.Length}"));
            return null;
        }

        var actionFlow = request.Behavior.MethodFlows
            .FirstOrDefault(flow => flow.Method == decision.Method);
        var resultLocal = actionFlow?.ValueGraph.Nodes.FirstOrDefault(node =>
            string.Equals(node.Name, decision.ResultLocalName, StringComparison.Ordinal));
        if (resultLocal is null
            || !string.Equals(resultLocal.TypeDescriptor, successFactories[0].ResultType, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                profileId,
                entryPointId,
                "SC007",
                "The decision result could not be joined to the service result through the local value graph; result and outcome claims are withheld.",
                $"{decision.Method.Value}\u001flocal={decision.ResultLocalName ?? "?"}"));
            return null;
        }

        return (successFactories[0], failureFactories[0]);
    }

    /// <summary>
    /// True for the exact single-value EF query terminals that carry a fact-level predicate anchor:
    /// <see cref="EntityFrameworkQueryOperatorKind.SingleOrDefaultAsync"/> and
    /// <see cref="EntityFrameworkQueryOperatorKind.FirstOrDefaultAsync"/>. The CountAsync aggregation
    /// has no terminal predicate, so it is deliberately excluded from the single-value set and keeps
    /// its count-only handling everywhere in this builder.
    /// </summary>
    private static bool IsSingleValueQueryTerminal(EntityFrameworkQueryOperatorKind? terminal)
        => terminal is EntityFrameworkQueryOperatorKind.SingleOrDefaultAsync
            or EntityFrameworkQueryOperatorKind.FirstOrDefaultAsync;

    private static ScenarioNode BuildEntityQueryNode(
        ScenarioAnalysisRequest request,
        CompilationProfileId profileId,
        EntryPointId entryPointId,
        MethodId serviceMethod,
        EntityFrameworkQueryFact fact,
        List<ScenarioGraphDiagnostic> diagnostics,
        int sequenceOrdinal)
    {
        var terminal = fact.Chain.LastOrDefault()?.OperatorKind;
        var comparisonEvidence = ImmutableArray<EvidenceRef>.Empty;
        if (IsSingleValueQueryTerminal(terminal) && fact.PredicateOperation is { } predicateOperation)
        {
            // A single-value terminal predicate (SingleOrDefaultAsync or FirstOrDefaultAsync) is
            // linked to a comparison semantic fact. A CountAsync aggregation has no terminal
            // predicate and never degrades here.
            var comparison = request.SemanticFacts.Comparisons
                .FirstOrDefault(candidate => candidate.Method == serviceMethod && candidate.Operation == predicateOperation);
            comparisonEvidence = comparison?.Evidence ?? ImmutableArray<EvidenceRef>.Empty;
            if (comparison is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    profileId,
                    entryPointId,
                    "SC005",
                    "The EF query predicate comparison has no linked comparison semantic fact.",
                    $"{fact.Method.Value}\u001f{fact.Operation.Value}"));
            }
        }

        var chain = string.Join(",", fact.Chain.Select(item => item.OperatorKind.ToString()));
        var evidence = Combine(fact.Evidence, comparisonEvidence);
        // Query existence is retained from the exact EF fact, but a single-value terminal without a
        // linked terminal comparison degrades the node and its query edge to Conservative certainty.
        var certainty = IsSingleValueQueryTerminal(terminal)
            && fact.PredicateOperation is { } degradedPredicate
            && request.SemanticFacts.Comparisons.All(candidate => candidate.Method != serviceMethod || candidate.Operation != degradedPredicate)
            ? CertaintyLevel.Conservative
            : evidence.Min(item => item.Certainty);
        var presentationOperator = IsSingleValueQueryTerminal(terminal) || terminal == EntityFrameworkQueryOperatorKind.CountAsync
            ? terminal
            : null;
        return CreateNode(
            profileId,
            entryPointId,
            ScenarioNodeKind.EntityQuery,
            $"query:{fact.Operation.Value}",
            serviceMethod,
            fact.Operation,
            $"{fact.DbContextType}.{fact.DbSetMemberType} {chain} on {fact.EntityType}",
            evidence,
            certainty,
            sequenceOrdinal,
            new ScenarioNodePresentation(
                DbContextTypeName: fact.DbContextType,
                EntityTypeName: fact.EntityType,
                QueryOperatorKind: presentationOperator));
    }

    private static void JoinOutcomePaths(
        ScenarioAnalysisRequest request,
        CompilationProfileId profileId,
        EntryPointId entryPointId,
        List<ScenarioNode> nodes,
        List<ScenarioEdge> edges,
        List<ScenarioGraphDiagnostic> diagnostics,
        ImmutableArray<StructuralOutcomePath> paths,
        ScenarioEdgeKind edgeKind,
        string resultNodeKey,
        ImmutableArray<HttpDirectOutcomeFact> outcomes)
    {
        var resultNode = nodes.FirstOrDefault(node => node.Key == resultNodeKey);
        if (resultNode is null)
        {
            return;
        }

        foreach (var path in paths
                     .GroupBy(path => path.HelperKind)
                     .OrderBy(group => group.Key)
                     .Select(group => group.First()))
        {
            var outcomeFacts = outcomes
                .Where(outcome => outcome.HelperKind == path.HelperKind)
                .ToArray();
            if (outcomeFacts.Length != 1)
            {
                diagnostics.Add(CreateDiagnostic(
                    profileId,
                    entryPointId,
                    "SC004",
                    "The decision path helper has no unique HTTP outcome fact.",
                    $"{entryPointId.Value}\u001f{path.HelperKind.ToString()}"));
                continue;
            }

            var outcome = outcomeFacts[0];
            var key = $"outcome:{outcome.StatusCode}:{outcome.HelperKind.ToString()}";
            var outcomeNode = nodes.FirstOrDefault(node => node.Key == key);
            if (outcomeNode is null)
            {
                outcomeNode = CreateNodeWithPresentation(
                    profileId,
                    entryPointId,
                    ScenarioNodeKind.Outcome,
                    key,
                    outcome.RootMethod,
                    outcome.Operation,
                    $"{outcome.HelperKind.ToString()} -> HTTP {outcome.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    new ScenarioNodePresentation(
                        OutcomeHelperKind: outcome.HelperKind,
                        OutcomeStatusCode: outcome.StatusCode),
                    outcome.Evidence);
                nodes.Add(outcomeNode);
            }

            edges.Add(CreateEdge(
                profileId,
                entryPointId,
                resultNode,
                outcomeNode,
                edgeKind,
                $"{path.HelperKind.ToString()} outcome",
                Combine(resultNode.Evidence, outcome.Evidence)));
        }
    }

    private static ServiceResolution? TryResolveService(
        ScenarioAnalysisRequest request,
        MethodId action,
        out string? ambiguityReason)
    {
        ambiguityReason = null;
        var programIndex = request.ProgramIndex;
        var actionMethod = programIndex.Methods.FirstOrDefault(method => method.Id == action);
        if (actionMethod is null)
        {
            ambiguityReason = "action-method-missing";
            return null;
        }

        var constructor = programIndex.Methods
            .FirstOrDefault(method => method.Name == ".ctor" && method.ContainingType == actionMethod.ContainingType);
        if (constructor is null)
        {
            ambiguityReason = "controller-constructor-missing";
            return null;
        }

        var bindings = request.DependencyInjectionFacts.Bindings
            .Where(binding => binding.ConstructorMethod == constructor.Id)
            .ToArray();
        if (bindings.Length == 0)
        {
            ambiguityReason = "no-di-bindings";
            return null;
        }

        var callSites = request.Behavior.CallGraph.CallSites
            .Where(callSite => callSite.ContainingMethod == action)
            .ToArray();
        foreach (var serviceType in bindings
                     .Select(binding => binding.ServiceType)
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            var matchingSites = callSites
                .Where(callSite => callSite.DeclaredTarget is { } target
                    && DeclaringTypeName(programIndex, target) == serviceType)
                .ToArray();
            if (matchingSites.Length == 0)
            {
                continue;
            }

            if (matchingSites.Length > 1)
            {
                // A scenario joins exactly one call site; several matching call sites are ambiguous
                // and never first-selected.
                ambiguityReason = $"multiple-matching-call-sites:{serviceType}";
                return null;
            }

            if (!matchingSites[0].Resolution.IsComplete)
            {
                // An incomplete target resolution cannot prove the candidate set is exhaustive, so
                // the join fails closed.
                ambiguityReason = $"incomplete-resolution:{serviceType}";
                return null;
            }

            var serviceBindings = bindings
                .Where(binding => binding.ServiceType == serviceType)
                .ToArray();
            var implementations = serviceBindings
                .Select(binding => binding.ImplementationType)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (implementations.Length > 1)
            {
                ambiguityReason = $"multiple-di-targets:{serviceType}";
                return null;
            }

            var candidates = matchingSites
                .SelectMany(callSite => callSite.Resolution.Candidates)
                .Where(candidate => DeclaringTypeName(programIndex, candidate) == implementations[0])
                .Distinct()
                .ToArray();
            if (candidates.Length == 1)
            {
                var binding = serviceBindings[0];
                var registration = request.DependencyInjectionFacts.Registrations
                    .FirstOrDefault(candidate => candidate.Id == binding.RegistrationId);
                if (registration is null)
                {
                    ambiguityReason = "registration-missing";
                    return null;
                }

                return new ServiceResolution(candidates[0], matchingSites[0], binding, registration);
            }

            if (candidates.Length > 1)
            {
                ambiguityReason = $"multiple-implementation-candidates:{serviceType}";
                return null;
            }

            ambiguityReason = $"no-implementation-candidate:{serviceType}";
            return null;
        }

        ambiguityReason = "no-service-call-matched";
        return null;
    }

    /// <summary>
    /// Resolves the accepted contract typed service composition for a controller action whose multiple DI bindings
    /// cannot join to one implementation through the ordinary path. A composition is returned only
    /// when the controller binds exactly one distinct service type, one complete same-condition
    /// alternative group accounts for that complete binding/registration set, the single matching call
    /// site has complete resolution, and each arm's implementation type resolves to exactly one
    /// candidate method. Any missing group, extra unguarded binding/registration, missing/incomplete/
    /// ambiguous candidate, or cross-service overlap returns null so the existing SC001 reason is
    /// retained unchanged. The composition identity derives only from the profile, conditional
    /// top-level method, condition/read operations, key, service type, and true/false registration
    /// identities; the entry point and route are excluded.
    /// </summary>
    private static ScenarioServiceComposition? TryResolveComposition(
        ScenarioAnalysisRequest request,
        MethodId action,
        CompilationProfileId profileId)
    {
        var conditionalFacts = request.ConditionalDependencyInjectionFacts;
        if (conditionalFacts is null
            || !IsConditionalFactsBound(request, conditionalFacts)
            || conditionalFacts.Groups.IsEmpty)
        {
            // A null or foreign conditional fact set never suppresses SC001 (regression).
            return null;
        }

        var programIndex = request.ProgramIndex;
        var actionMethod = programIndex.Methods.FirstOrDefault(method => method.Id == action);
        if (actionMethod is null)
        {
            return null;
        }

        var constructor = programIndex.Methods
            .FirstOrDefault(method => method.Name == ".ctor" && method.ContainingType == actionMethod.ContainingType);
        if (constructor is null)
        {
            return null;
        }

        var bindings = request.DependencyInjectionFacts.Bindings
            .Where(binding => binding.ConstructorMethod == constructor.Id)
            .ToArray();
        if (bindings.Length == 0)
        {
            return null;
        }

        // One exact alternative group can account for the complete binding set only when the
        // controller binds exactly one distinct service type.
        var distinctServiceTypes = bindings
            .Select(binding => binding.ServiceType)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (distinctServiceTypes.Length != 1)
        {
            return null;
        }

        var serviceType = distinctServiceTypes[0];
        var groupCandidates = conditionalFacts.Groups
            .Where(group => group.ServiceType == serviceType)
            .ToArray();
        if (groupCandidates.Length != 1)
        {
            return null;
        }

        var group = groupCandidates[0];
        var serviceBindings = bindings
            .Where(binding => binding.ServiceType == serviceType)
            .ToArray();
        var bindingRegistrationIds = serviceBindings
            .Select(binding => binding.RegistrationId.Value)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();
        var groupRegistrationIds = new[] { group.TrueRegistrationId.Value, group.FalseRegistrationId.Value }
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!bindingRegistrationIds.SequenceEqual(groupRegistrationIds))
        {
            // An extra unguarded binding/registration or a missing group registration means the exact
            // group cannot account for the complete binding set.
            return null;
        }

        // The admitted DI registration set must also equal the group pair; the collector guarantees
        // this for a formed group, so this is a defensive fail-closed check.
        var registrationIds = request.DependencyInjectionFacts.Registrations
            .Where(registration => registration.ServiceType == serviceType)
            .Select(registration => registration.Id.Value)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!registrationIds.SequenceEqual(groupRegistrationIds))
        {
            return null;
        }

        var callSites = request.Behavior.CallGraph.CallSites
            .Where(callSite => callSite.ContainingMethod == action)
            .ToArray();
        var matchingSites = callSites
            .Where(callSite => callSite.DeclaredTarget is { } target
                && DeclaringTypeName(programIndex, target) == serviceType)
            .ToArray();
        if (matchingSites.Length != 1 || !matchingSites[0].Resolution.IsComplete)
        {
            // Missing, ambiguous, or incomplete call resolution keeps the existing SC001 reason.
            return null;
        }

        var armsByRegistrationId = conditionalFacts.RegistrationArms
            .GroupBy(arm => arm.RegistrationId.Value, StringComparer.Ordinal)
            .ToDictionary(groupByArm => groupByArm.Key, groupByArm => groupByArm.First(), StringComparer.Ordinal);
        if (!armsByRegistrationId.TryGetValue(group.TrueRegistrationId.Value, out var trueArmFact)
            || !armsByRegistrationId.TryGetValue(group.FalseRegistrationId.Value, out var falseArmFact))
        {
            return null;
        }

        var resolution = matchingSites[0].Resolution;
        var trueArm = TryResolveArm(programIndex, group.TrueImplementationType, trueArmFact, resolution, isTrue: true);
        var falseArm = TryResolveArm(programIndex, group.FalseImplementationType, falseArmFact, resolution, isTrue: false);
        if (trueArm is null || falseArm is null)
        {
            // A missing or ambiguous candidate for either arm never suppresses SC001.
            return null;
        }

        return new ScenarioServiceComposition(
            CreateCompositionId(profileId, group),
            serviceType,
            BuildConfigurationDecision(request, group),
            trueArm,
            falseArm,
            BuildProfileSelection(request, group.Key));
    }

    /// <summary>
    /// Resolves one composition arm to exactly one implementation method: the single compiler call
    /// candidate whose declaring type equals the arm's implementation type. Zero or several distinct
    /// candidates fail closed with null so SC001 is never suppressed for an incomplete candidate set.
    /// The resolved method identity is the exact candidate <see cref="MethodId"/> from the compiler
    /// call resolution and Program Index — never a reconstructed display string. The arm evidence is
    /// the canonical union of the arm fact and call-resolution evidence, with certainty degraded to
    /// the weakest contributor.
    /// </summary>
    private static ScenarioServiceAlternativeArm? TryResolveArm(
        ProgramIndexSnapshot programIndex,
        string implementationType,
        ConditionalDependencyInjectionRegistrationArmFact armFact,
        CallTargetResolution resolution,
        bool isTrue)
    {
        var candidates = resolution.Candidates
            .Where(candidate => DeclaringTypeName(programIndex, candidate) == implementationType)
            .Distinct()
            .ToArray();
        if (candidates.Length != 1)
        {
            return null;
        }

        var evidence = Combine(armFact.Evidence, resolution.Evidence);
        return new ScenarioServiceAlternativeArm(
            isTrue,
            armFact.RegistrationId,
            implementationType,
            candidates[0],
            evidence,
            evidence.Max(item => item.Certainty));
    }

    /// <summary>
    /// True when the conditional dependency-injection fact set belongs to the current compilation
    /// profile and Program Index. A foreign set never contributes a composition (regression).
    /// </summary>
    private static bool IsConditionalFactsBound(
        ScenarioAnalysisRequest request,
        ConditionalDependencyInjectionFactSet facts)
        => facts.Profile.Id == request.Profile.Id
            && string.Equals(facts.ProgramIndexFingerprint, request.ProgramIndex.IndexFingerprint, StringComparison.Ordinal);

    /// <summary>
    /// True when the configuration semantic fact set belongs to the current compilation profile and
    /// Program Index. A foreign set contributes no decision evidence and no profile selection
    /// (regression), while a valid conditional composition may remain.
    /// </summary>
    private static bool IsConfigurationFactsBound(
        ScenarioAnalysisRequest request,
        ConfigurationSemanticFactSet facts)
        => facts.Profile.Id == request.Profile.Id
            && string.Equals(facts.ProgramIndexFingerprint, request.ProgramIndex.IndexFingerprint, StringComparison.Ordinal);

    /// <summary>
    /// Builds the configuration decision evidence as the canonical union of the conditional group
    /// evidence plus the matching accepted contract read and condition evidence when the configuration set is
    /// bound to the current profile/Program Index. The Conservative checked-in observation
    /// participates only in weakest-certainty degradation: its value never selects an arm and never
    /// claims a runtime choice, so the decision certainty is never promoted past the observation's
    /// certainty. A checked-in value alone never supports a decision, and a foreign configuration set
    /// never leaks evidence into the decision.
    /// </summary>
    private static ScenarioConfigurationDecision BuildConfigurationDecision(
        ScenarioAnalysisRequest request,
        ConditionalDependencyInjectionGroupFact group)
    {
        var evidence = group.Evidence.ToList();
        var configurationFacts = request.ConfigurationSemanticFacts;
        if (configurationFacts is not null && IsConfigurationFactsBound(request, configurationFacts))
        {
            var read = configurationFacts.Reads
                .FirstOrDefault(fact => fact.Operation == group.ReadOperation);
            if (read is not null)
            {
                evidence.AddRange(read.Evidence);
            }

            var condition = configurationFacts.Conditions
                .FirstOrDefault(fact => fact.ConditionOperation == group.ConditionOperation);
            if (condition is not null)
            {
                evidence.AddRange(condition.Evidence);
            }

            var checkedIn = configurationFacts.CheckedInValues
                .FirstOrDefault(fact => fact.Key == group.Key);
            if (checkedIn is not null)
            {
                evidence.AddRange(checkedIn.Evidence);
            }
        }

        var combined = evidence
            .DistinctBy(item => item.Id.Value, StringComparer.Ordinal)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        return new ScenarioConfigurationDecision(
            group.ConditionOperation,
            group.ReadOperation,
            group.Key,
            combined,
            combined.Max(item => item.Certainty));
    }

    /// <summary>
    /// Builds the optional analysis-profile selection metadata from a matching accepted contract profile-known
    /// value for the composition key, only when the configuration set is bound to the current
    /// profile/Program Index. Only profile-known values select an arm; checked-in JSON values never
    /// select (accepted contract requirement 9), and a foreign configuration set never selects an arm (regression).
    /// The selection evidence is exactly the matching profile-known fact evidence and certainty
    /// degrades to its weakest contributor while the analysis-profile provenance is retained. A
    /// missing, foreign, or non-matching profile-known fact yields null so both arms remain possible.
    /// </summary>
    private static ScenarioCompositionProfileSelection? BuildProfileSelection(
        ScenarioAnalysisRequest request,
        string key)
    {
        var configurationFacts = request.ConfigurationSemanticFacts;
        if (configurationFacts is null || !IsConfigurationFactsBound(request, configurationFacts))
        {
            return null;
        }

        var profileKnown = configurationFacts.ProfileKnownValues
            .FirstOrDefault(fact => fact.Key == key);
        if (profileKnown is null)
        {
            return null;
        }

        return new ScenarioCompositionProfileSelection(
            profileKnown.Value,
            profileKnown.AnalysisProfileSource,
            profileKnown.Evidence,
            profileKnown.Evidence.Max(item => item.Certainty));
    }

    /// <summary>
    /// Creates the deterministic composition identity from the profile, the conditional top-level
    /// method, the condition/read operations, the key, the service type, and the registration
    /// identities only (accepted contract requirement 5/12). The entry point and route never contribute, so
    /// entry-point/route-only changes keep the identity while condition/method changes churn it.
    /// </summary>
    private static ScenarioCompositionId CreateCompositionId(
        CompilationProfileId profileId,
        ConditionalDependencyInjectionGroupFact group)
        => StableIdentity.CreateScenarioCompositionId(new ScenarioCompositionIdentityDescriptor(
            profileId,
            group.ProgramMethod,
            group.ServiceType,
            group.ConditionOperation,
            group.ReadOperation,
            group.Key,
            group.TrueRegistrationId,
            group.FalseRegistrationId));

    private static string? DeclaringTypeName(ProgramIndexSnapshot index, MethodId methodId)
    {
        var method = index.Methods.FirstOrDefault(candidate => candidate.Id == methodId);
        if (method is null)
        {
            return null;
        }

        var type = index.Types.FirstOrDefault(candidate => candidate.Id == method.ContainingType);
        return type?.MetadataName;
    }

    /// <summary>Canonical controller type metadata name for a controller action method; null when the Program Index cannot prove it.</summary>
    private static string? ControllerTypeName(ProgramIndexSnapshot index, MethodId actionMethod)
    {
        var method = index.Methods.FirstOrDefault(candidate => candidate.Id == actionMethod);
        return method is null ? null : DeclaringTypeName(index, actionMethod);
    }

    private static ScenarioNodePresentation ControllerActionPresentation(ProgramIndexSnapshot index, MethodId actionMethod)
    {
        var methods = index.Methods.Where(candidate => candidate.Id == actionMethod).ToArray();
        if (methods.Length != 1 || string.IsNullOrEmpty(methods[0].Name))
        {
            return new ScenarioNodePresentation();
        }

        var types = index.Types.Where(candidate => candidate.Id == methods[0].ContainingType).ToArray();
        return types.Length == 1 && !string.IsNullOrEmpty(types[0].MetadataName)
            ? new ScenarioNodePresentation(
                ControllerTypeName: types[0].MetadataName,
                ActionMethodName: methods[0].Name)
            : new ScenarioNodePresentation();
    }

    private static ScenarioNodePresentation MinimalApiActionPresentation(ProgramIndexSnapshot index, MethodId actionMethod)
    {
        var methods = index.Methods.Where(candidate => candidate.Id == actionMethod).ToArray();
        if (methods.Length != 1 || string.IsNullOrEmpty(methods[0].Name)
            || (methods[0].Name.StartsWith('<') && methods[0].Name.Contains('>')))
        {
            return new ScenarioNodePresentation();
        }

        var types = index.Types.Where(candidate => candidate.Id == methods[0].ContainingType).ToArray();
        return types.Length == 1 && !string.IsNullOrEmpty(types[0].MetadataName)
            ? new ScenarioNodePresentation(
                ControllerTypeName: types[0].MetadataName,
                ActionMethodName: methods[0].Name)
            : new ScenarioNodePresentation();
    }

    private static ScenarioNodePresentation ConfiguredMethodPresentation(ProgramIndexSnapshot index, MethodId methodId)
    {
        var method = index.Methods.Single(item => item.Id == methodId);
        var type = index.Types.Single(item => item.Id == method.ContainingType);
        return new ScenarioNodePresentation(
            ConfiguredContainingTypeName: type.MetadataName,
            ConfiguredMethodName: method.Name,
            ConfiguredDisplaySignature: ConfiguredDisplaySignature(index, methodId),
            ActionKind: ScenarioActionKind.ConfiguredMethod);
    }

    private static string ConfiguredDisplaySignature(ProgramIndexSnapshot index, MethodId methodId)
    {
        var method = index.Methods.Single(item => item.Id == methodId);
        if (!string.IsNullOrWhiteSpace(method.DisplaySignature))
        {
            return method.DisplaySignature;
        }

        var type = index.Types.Single(item => item.Id == method.ContainingType);
        return $"{type.MetadataName}.{method.Name}";
    }

    /// <summary>Concise (short) member name of a method from the Program Index, used for the exact called-member display.</summary>
    private static string? MethodConciseName(ProgramIndexSnapshot index, MethodId methodId)
        => index.Methods.FirstOrDefault(candidate => candidate.Id == methodId)?.Name;

    /// <summary>
    /// Formats compiler-proven constant arguments into a concise parenthesized display label.
    /// String arguments are quoted; numeric and boolean arguments are bare; enum and other
    /// arguments are rendered as their constant value. Returns null when no constant arguments
    /// are available so the caller falls back to the bare member name.
    /// </summary>
    private static string? FormatArgumentLabel(ImmutableArray<CompilerProvenArgument> arguments)
    {
        if (arguments.IsDefaultOrEmpty
            || arguments.Select(argument => argument.Ordinal).Distinct().Count() != arguments.Length
            || !arguments.Select(argument => argument.Ordinal).Order().SequenceEqual(Enumerable.Range(0, arguments.Length)))
        {
            return null;
        }

        if (arguments.Any(argument => LooksSensitive(argument.Value)))
        {
            return null;
        }

        var parts = new List<string>(arguments.Length);
        foreach (var arg in arguments)
        {
            parts.Add(FormatArgumentValue(arg));
        }

        return string.Join(", ", parts);

        static string FormatArgumentValue(CompilerProvenArgument arg)
        {
            if (arg.IsNull)
            {
                return "null";
            }

            if (arg.FullyQualifiedType == "System.String")
            {
                var escaped = arg.Value!
                    .Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("\"", "\\\"", StringComparison.Ordinal)
                    .Replace("\r", "\\r", StringComparison.Ordinal)
                    .Replace("\n", "\\n", StringComparison.Ordinal)
                    .Replace("\t", "\\t", StringComparison.Ordinal)
                    .Replace("`", "\\`", StringComparison.Ordinal);
                return $"\"{escaped}\"";
            }

            return arg.Value!;
        }

        static bool LooksSensitive(string? value)
        {
            if (value is null)
            {
                return true;
            }
            var lower = value.ToLowerInvariant();
            if (lower.Contains("password", StringComparison.Ordinal)
                || lower.Contains("secret", StringComparison.Ordinal)
                || lower.Contains("token", StringComparison.Ordinal)
                || lower.Contains("connection-string", StringComparison.Ordinal)
                || lower.Contains("connectionstring", StringComparison.Ordinal)
                || lower.Contains("api_key", StringComparison.Ordinal)
                || lower.Contains("apikey", StringComparison.Ordinal))
            {
                return true;
            }

            return ((value.StartsWith("AKIA", StringComparison.Ordinal) || value.StartsWith("ASIA", StringComparison.Ordinal))
                    && value.Length == 20)
                || value.StartsWith("ghp_", StringComparison.Ordinal)
                || value.StartsWith("gho_", StringComparison.Ordinal)
                || value.StartsWith("ghu_", StringComparison.Ordinal)
                || value.StartsWith("ghs_", StringComparison.Ordinal)
                || value.StartsWith("ghr_", StringComparison.Ordinal)
                || value.StartsWith("github_pat_", StringComparison.Ordinal)
                || (value.StartsWith("sk-", StringComparison.Ordinal) && value.Length >= 20)
                || (value.Count(character => character == '.') == 2
                    && value.Split('.').All(part => part.Length >= 8
                        && part.All(character => char.IsLetterOrDigit(character) || character is '-' or '_')))
                || (value.Length >= 16 && value.All(char.IsLetterOrDigit)
                    && value.Distinct().Count() >= 10 && value.Any(char.IsDigit));
        }
    }

    /// <summary>
    /// Looks up the InvocationFlowNode for the given operation in the method flow, returning
    /// its ConstantArguments if available. Returns default when the flow or node is absent.
    /// </summary>
    private static ImmutableArray<CompilerProvenArgument> InvocationConstantArguments(ScenarioAnalysisRequest request, MethodId method, OperationId operation)
    {
        var flow = request.Behavior.MethodFlows.SingleOrDefault(item => item.Method == method);
        if (flow is null)
        {
            return [];
        }

        var invocation = flow.Nodes.OfType<InvocationFlowNode>().SingleOrDefault(node => node.Operation == operation);
        return invocation?.ConstantArguments ?? [];
    }

    /// <summary>
    /// Derives the architecture decision decision topology from Method Flow alone: decisions and arms from
    /// <see cref="DecisionFlowNode"/>s, memberships from direct control dependences joined to exact
    /// operation anchors, and terminal/rejoin classifications from flow edges, represented terminals,
    /// and regions. A material scenario node without an exact eligible operation anchor stays visible
    /// and unscoped with SC011; disagreeing invocation/await/duplicate anchors never silently select
    /// one anchor (SC011); same-decision dual-polarity conflicts fail closed with SC012 while valid
    /// membership under unrelated decisions is retained; and unsupported or incomplete terminal/rejoin
    /// topology (loop-back, switch shape, exception region, mixed or operation-derived boundary) fails
    /// closed with SC013. The builder never inspects Roslyn or source strings and never selects a
    /// diagram fragment.
    /// </summary>
    private static ScenarioTopology BuildTopology(
        ScenarioAnalysisRequest request,
        CompilationProfileId profileId,
        EntryPointId entryPointId,
        NormalizedEntry entryPoint,
        MethodId serviceMethod,
        ImmutableArray<ScenarioNode> nodes,
        List<ScenarioGraphDiagnostic> diagnostics)
    {
        var decisions = new List<ScenarioDecision>();
        var arms = new List<ScenarioArm>();
        var memberships = new List<ScenarioMembership>();
        var terminals = new List<ScenarioArmTerminal>();
        var rootMethod = entryPoint.RootMethod;

        var flows = request.Behavior.MethodFlows
            .Where(flow => flow.Method == rootMethod || flow.Method == serviceMethod)
            .GroupBy(flow => flow.Method)
            .Select(group => group.OrderBy(flow => flow.FlowFingerprint, StringComparer.Ordinal).First())
            .OrderBy(flow => flow.Method.Value, StringComparer.Ordinal)
            .ToArray();

        // One combined anchor/dependence index across every admitted flow so a material node whose
        // exact operation lives in another flow (for example the action-flow service call) still
        // joins. The hand-authored and compiler flows can contain several nodes sharing one identity
        // (for example an awaited invocation's operation node and its duplicate); the id map keeps the
        // first canonical node so topology joins never throw on duplicate keys.
        var anchorsByOperation = new Dictionary<string, ImmutableArray<FlowNodeId>>(StringComparer.Ordinal);
        var dependencesByControlled = new Dictionary<FlowNodeId, ImmutableArray<ControlDependence>>();

        foreach (var flow in flows)
        {
            var flowNodesById = flow.Nodes
                .GroupBy(node => node.Id)
                .ToDictionary(group => group.Key, group => group.First());
            var flowDecisions = flow.Nodes
                .OfType<DecisionFlowNode>()
                .OrderBy(node => node.Id.Value, StringComparer.Ordinal)
                .ToArray();

            foreach (var anchor in BuildOperationAnchors(flow))
            {
                anchorsByOperation[anchor.Key] = anchorsByOperation.TryGetValue(anchor.Key, out var existing)
                    ? existing.AddRange(anchor.Value)
                    : anchor.Value;
            }

            foreach (var group in flow.ControlDependences.GroupBy(dependence => dependence.ControlledNode))
            {
                dependencesByControlled[group.Key] = group.ToImmutableArray();
            }

            foreach (var decision in flowDecisions)
            {
                var decisionId = StableIdentity.CreateScenarioDecisionId(new ScenarioDecisionIdentityDescriptor(
                    profileId, rootMethod, flow.Method, decision.Id));
                decisions.Add(new ScenarioDecision(
                    decisionId,
                    flow.Method,
                    decision.Id,
                    decision.Condition,
                    decision.Evidence,
                    decision.Certainty,
                    PredicateWording(request, flow.Method, decision.Condition)));
                arms.Add(new ScenarioArm(
                    StableIdentity.CreateScenarioArmId(new ScenarioArmIdentityDescriptor(
                        profileId, rootMethod, decisionId, IsTrue: true)),
                    decisionId,
                    IsTrue: true,
                    decision.Evidence,
                    decision.Certainty));
                arms.Add(new ScenarioArm(
                    StableIdentity.CreateScenarioArmId(new ScenarioArmIdentityDescriptor(
                        profileId, rootMethod, decisionId, IsTrue: false)),
                    decisionId,
                    IsTrue: false,
                    decision.Evidence,
                    decision.Certainty));
            }

            foreach (var decision in flowDecisions)
            {
                var decisionId = decisions.First(candidate => candidate.ControllingFlowNode == decision.Id).Id;
                var trueClassification = ClassifyArmTerminal(flow, flowNodesById, decision, isTrue: true);
                var falseClassification = ClassifyArmTerminal(flow, flowNodesById, decision, isTrue: false);
                if (trueClassification.UnsupportedReason is not null || falseClassification.UnsupportedReason is not null)
                {
                    diagnostics.Add(CreateDiagnostic(
                        profileId,
                        entryPointId,
                        "SC013",
                        "The decision has unsupported or incomplete terminal/rejoin topology; exact arm classification is withheld.",
                        $"{flow.Method.Value}\u001f{decision.Id.Value}\u001f{trueClassification.UnsupportedReason ?? falseClassification.UnsupportedReason}"));
                }

                terminals.Add(BuildArmTerminal(
                    arms.First(arm => arm.Decision == decisionId && arm.IsTrue).Id,
                    trueClassification,
                    decision));
                terminals.Add(BuildArmTerminal(
                    arms.First(arm => arm.Decision == decisionId && !arm.IsTrue).Id,
                    falseClassification,
                    decision));
            }
        }

        // Arm placement exists only when the graph has at least one Method Flow decision to place
        // material nodes under. A decision-free flat graph has no arm-membership question, so it keeps
        // its existing flat behavior without SC011 noise; SC011/SC012 and memberships run only when
        // there is topology to join (review F3 regression guard).
        if (decisions.Count > 0)
        {
            foreach (var node in nodes
                         .Where(IsMaterialTopologyNode)
                         .OrderBy(node => node.Id.Value, StringComparer.Ordinal))
            {
                if (node.Operation is not { } operation)
                {
                    diagnostics.Add(CreateDiagnostic(
                        profileId,
                        entryPointId,
                        "SC011",
                        "The material scenario node has no exact operation identity; arm membership is withheld.",
                        $"{node.Method?.Value ?? string.Empty}\u001f{node.Key}"));
                    continue;
                }

                if (!anchorsByOperation.TryGetValue(operation.Value, out var anchorIds))
                {
                    diagnostics.Add(CreateDiagnostic(
                        profileId,
                        entryPointId,
                        "SC011",
                        "The scenario node has no exact eligible Method Flow operation anchor; arm membership is withheld.",
                        $"{node.Method!.Value}\u001f{operation.Value}\u001f{node.Key}"));
                    continue;
                }

                // Every eligible anchor must agree on the node's control memberships; a disagreeing
                // invocation/await/duplicate anchor never silently wins (review F2).
                var membershipSets = anchorIds
                    .Select(anchorId => AnchorMembershipSet(anchorId, dependencesByControlled))
                    .ToArray();
                var firstSet = membershipSets[0];
                if (membershipSets.Skip(1).Any(candidate => !MembershipSetsEqual(firstSet, candidate)))
                {
                    diagnostics.Add(CreateDiagnostic(
                        profileId,
                        entryPointId,
                        "SC011",
                        "The scenario node's operation anchors disagree on control membership; arm membership is withheld.",
                        $"{node.Method!.Value}\u001f{operation.Value}\u001f{node.Key}"));
                    continue;
                }

                // All same-decision dual-polarity conflicts are reported deterministically; only each
                // conflicting decision's memberships are withheld and valid membership under unrelated
                // decisions is retained (review F5).
                var conflictDecisions = firstSet
                    .GroupBy(membership => membership.ControllingDecision.Value, StringComparer.Ordinal)
                    .Where(group => group.Any(membership => membership.ControlledOnTrue)
                        && group.Any(membership => !membership.ControlledOnTrue))
                    .Select(group => group.Key)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                foreach (var conflict in conflictDecisions)
                {
                    diagnostics.Add(CreateDiagnostic(
                        profileId,
                        entryPointId,
                        "SC012",
                        "The scenario node is directly controlled by both semantic arms of the same decision; arm membership is withheld.",
                        $"{node.Method!.Value}\u001f{operation.Value}\u001f{conflict}"));
                }

                var withheld = conflictDecisions.ToHashSet(StringComparer.Ordinal);
                foreach (var dependence in firstSet
                             .OrderBy(membership => membership.ControllingDecision.Value, StringComparer.Ordinal)
                             .ThenBy(membership => membership.ControlledOnTrue))
                {
                    if (withheld.Contains(dependence.ControllingDecision.Value))
                    {
                        continue;
                    }

                    var decision = decisions.FirstOrDefault(candidate => candidate.ControllingFlowNode == dependence.ControllingDecision);
                    if (decision is null)
                    {
                        continue;
                    }

                    var arm = arms.First(candidate => candidate.Decision == decision.Id && candidate.IsTrue == dependence.ControlledOnTrue);
                    memberships.Add(new ScenarioMembership(
                        StableIdentity.CreateScenarioMembershipId(new ScenarioMembershipIdentityDescriptor(
                            profileId, rootMethod, arm.Id, node.Id)),
                        arm.Id,
                        node.Id,
                        dependence.Evidence,
                        dependence.Certainty));
                }
            }
        }

        // Canonical semantic order (architecture decision and review F7): controlling flow-node identity, then
        // explicit polarity, then controlled scenario-node identity — never hashed identity order.
        var decisionById = decisions.ToDictionary(decision => decision.Id);
        var armById = arms.ToDictionary(arm => arm.Id);
        return new ScenarioTopology(
            decisions
                .OrderBy(decision => decision.ControllingFlowNode.Value, StringComparer.Ordinal)
                .ToImmutableArray(),
            arms
                .OrderBy(arm => decisionById[arm.Decision].ControllingFlowNode.Value, StringComparer.Ordinal)
                .ThenBy(arm => arm.IsTrue)
                .ToImmutableArray(),
            memberships
                .GroupBy(membership => (membership.Arm.Value, membership.ScenarioNode.Value))
                .Select(group => group.First())
                .OrderBy(membership => decisionById[armById[membership.Arm].Decision].ControllingFlowNode.Value, StringComparer.Ordinal)
                .ThenBy(membership => armById[membership.Arm].IsTrue)
                .ThenBy(membership => membership.ScenarioNode.Value, StringComparer.Ordinal)
                .ToImmutableArray(),
            terminals
                .OrderBy(terminal => decisionById[armById[terminal.Arm].Decision].ControllingFlowNode.Value, StringComparer.Ordinal)
                .ThenBy(terminal => armById[terminal.Arm].IsTrue)
                .ToImmutableArray());
    }

    /// <summary>
    /// The scenario kinds that require exact operation topology membership. Entry/action nodes and
    /// non-interaction source-observation/container nodes are explicitly excluded and never receive
    /// SC011 or membership; this is a declared kind policy, never an accidental null-operation filter.
    /// </summary>
    private static bool IsMaterialTopologyNode(ScenarioNode node)
        => node.Kind is ScenarioNodeKind.ServiceCall
            or ScenarioNodeKind.MethodCall
            or ScenarioNodeKind.EntityQuery
            or ScenarioNodeKind.StateAssignment
            or ScenarioNodeKind.EntityMutation
            or ScenarioNodeKind.Result
            or ScenarioNodeKind.Outcome;

    /// <summary>
    /// Maps each exact operation identity in one method flow to EVERY eligible anchor node
    /// (<see cref="OperationFlowNode"/>, <see cref="InvocationFlowNode"/>, and
    /// <see cref="AwaitFlowNode"/>). All eligible anchors are retained so the topology join can prove
    /// they agree instead of silently preferring an invocation over an await. Terminal, structural,
    /// entry/exit, loop, and unknown nodes are never anchors.
    /// </summary>
    private static Dictionary<string, ImmutableArray<FlowNodeId>> BuildOperationAnchors(
        MethodFlowSnapshot flow)
    {
        var anchors = new Dictionary<string, List<FlowNodeId>>(StringComparer.Ordinal);
        foreach (var node in flow.Nodes.OrderBy(node => node.Id.Value, StringComparer.Ordinal))
        {
            var key = node switch
            {
                OperationFlowNode operationNode => operationNode.Operation.Value,
                InvocationFlowNode invocationNode => invocationNode.Operation.Value,
                AwaitFlowNode awaitNode => awaitNode.Operand.Value,
                _ => null,
            };
            if (key is null)
            {
                continue;
            }

            if (!anchors.TryGetValue(key, out var list))
            {
                list = new List<FlowNodeId>();
                anchors[key] = list;
            }

            list.Add(node.Id);
        }

        return anchors.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.OrderBy(id => id.Value, StringComparer.Ordinal).ToImmutableArray(),
            StringComparer.Ordinal);
    }

    /// <summary>Returns the direct control dependences that target one anchor node.</summary>
    private static ImmutableArray<ControlDependence> AnchorMembershipSet(
        FlowNodeId anchorId,
        Dictionary<FlowNodeId, ImmutableArray<ControlDependence>> dependencesByControlled)
        => dependencesByControlled.TryGetValue(anchorId, out var dependences) ? dependences : [];

    /// <summary>
    /// Compares two anchors' control memberships by the (controlling decision, semantic polarity)
    /// keys only. Evidence and certainty differences never make otherwise identical memberships
    /// disagree.
    /// </summary>
    private static bool MembershipSetsEqual(
        ImmutableArray<ControlDependence> left,
        ImmutableArray<ControlDependence> right)
    {
        var leftKeys = left
            .Select(dependence => (dependence.ControllingDecision.Value, dependence.ControlledOnTrue))
            .OrderBy(key => key.Item1, StringComparer.Ordinal)
            .ThenBy(key => key.Item2)
            .ToArray();
        var rightKeys = right
            .Select(dependence => (dependence.ControllingDecision.Value, dependence.ControlledOnTrue))
            .OrderBy(key => key.Item1, StringComparer.Ordinal)
            .ThenBy(key => key.Item2)
            .ToArray();
        return leftKeys.SequenceEqual(rightKeys);
    }

    /// <summary>
    /// Builds one terminal/rejoin fact from the decision evidence, every traversed supported edge
    /// evidence, and the exact boundary evidence, with certainty degraded to the weakest/most
    /// conservative support (review F4).
    /// </summary>
    private static ScenarioArmTerminal BuildArmTerminal(
        ScenarioArmId arm,
        ArmTerminalClassification classification,
        DecisionFlowNode decision)
    {
        var evidence = Combine(decision.Evidence, classification.TraversedEdgeEvidence, classification.BoundaryEvidence);
        return new ScenarioArmTerminal(
            arm,
            classification.Kind,
            evidence,
            evidence.Max(item => item.Certainty));
    }

    /// <summary>
    /// Classifies one decision arm as terminating or rejoining from the complete reachable arm
    /// subgraph and Method Flow edges alone. The walk accepts only represented block terminal
    /// return/throw nodes whose outgoing edge is Return/Throw/Rethrow to the successor/exit (or a
    /// sink); an operation-derived duplicate return/throw node with a continuation is never accepted.
    /// Accepted CT-4 design item 1 admits the exact own-header loop shape: a decision that IS the
    /// <see cref="LoopNode.Header"/> of an existing loop classifies its normal arms even when
    /// compiler lowering places that header in a Try region, and a LoopBack edge to that same header
    /// is a represented iteration boundary classified as rejoining. Everything else — loop-back to a
    /// foreign header, a decision that is not an exact loop header, catch/filter/finally placement,
    /// an unrelated LoopNode boundary, switches, mixed or incomplete boundaries — fails closed as
    /// <see cref="ScenarioTerminalKind.Unknown"/> with an unsupported reason. The classification is
    /// computed from the complete boundary set so it never depends on edge input order.
    /// </summary>
    private static ArmTerminalClassification ClassifyArmTerminal(
        MethodFlowSnapshot flow,
        Dictionary<FlowNodeId, FlowNode> nodesById,
        DecisionFlowNode decision,
        bool isTrue)
    {
        // The exact owning loop, when the decision is the header of an existing LoopNode. Only this
        // decision may reuse its loop's body/LoopBack facts for arm classification; the loop facts
        // never admit a different decision (foreign/mixed loop rejection).
        var owningLoop = flow.Nodes.OfType<LoopNode>().FirstOrDefault(loop => loop.Header == decision.Id);
        var outgoing = flow.Edges.Where(edge => edge.Source == decision.Id).ToArray();
        var loopBackEdges = outgoing.Where(edge => edge.Kind == FlowEdgeKind.LoopBack).ToArray();
        if (loopBackEdges.Length > 0 && owningLoop is null)
        {
            return Unsupported("loop-back");
        }

        if (loopBackEdges.Any(edge => edge.Target != decision.Id))
        {
            return Unsupported("foreign-loop-back");
        }

        var polarityEdgeCount = outgoing.Count(edge => edge.Kind == (isTrue ? FlowEdgeKind.True : FlowEdgeKind.False));
        if (outgoing.Any(edge => edge.Kind is FlowEdgeKind.SwitchCase or FlowEdgeKind.SwitchDefault)
            || polarityEdgeCount > 1)
        {
            return Unsupported("switch");
        }

        // The compiler lowers a loop header into a Try region (enumerator-disposal shape), so an
        // exact own-header decision may sit in a non-Root region without becoming an exception
        // decision. Genuine catch/filter/finally placement stays unsupported even for an exact
        // header, and a decision that is not an exact loop header gains the Try carve-out only when
        // every containing region is Root or Try (accepted CT-4 design item 1).
        if (flow.Regions.Any(region => region.Kind is FlowRegionKind.Catch or FlowRegionKind.Filter or FlowRegionKind.Finally
                && region.Nodes.Contains(decision.Id))
            || (owningLoop is null
                && flow.Regions.Any(region => region.Nodes.Contains(decision.Id)
                    && region.Kind is not FlowRegionKind.Root and not FlowRegionKind.Try)))
        {
            return Unsupported("exception-region");
        }

        var successorEdge = outgoing.FirstOrDefault(edge => edge.Kind == (isTrue ? FlowEdgeKind.True : FlowEdgeKind.False));
        if (successorEdge is null)
        {
            return Unsupported("missing-arm-edge");
        }

        // A plain Try arm cannot have an exception transition, even when its normal edge is otherwise
        // classifiable. Exception semantics are deliberately not reconstructed in this checkpoint.
        if (outgoing.Any(edge => edge.Kind is FlowEdgeKind.ExceptionHandler or FlowEdgeKind.Filter or FlowEdgeKind.Finally))
        {
            return Unsupported("exception-edge");
        }

        var explored = new HashSet<FlowNodeId>();
        var traversedEdgeEvidence = new List<ImmutableArray<EvidenceRef>>();
        var boundaryKinds = new List<ScenarioTerminalKind>();
        var boundaryEvidence = new List<ImmutableArray<EvidenceRef>>();
        var pending = new Stack<(FlowNodeId Node, ImmutableArray<EvidenceRef> EdgeEvidence)>();
        pending.Push((successorEdge.Target, successorEdge.Evidence));
        while (pending.TryPop(out var item))
        {
            // A node reached by more than one path (a diamond merge) is explored once; only the
            // decision-level LoopBack check and LoopNode/unsupported-edge handling express cycles,
            // because the method-flow builder always materializes back edges as LoopBack edges and
            // natural loops as LoopNode/region facts.
            if (!explored.Add(item.Node))
            {
                continue;
            }

            if (!nodesById.TryGetValue(item.Node, out var node))
            {
                return Unsupported("missing-node");
            }

            // A normal transition into a handler/filter/finally region is still an exception boundary;
            // do not let the plain-Try carve-out classify it as a normal rejoin.
            if (IsExceptionRegionNode(flow, item.Node))
            {
                return Unsupported("exception-region-target");
            }

            traversedEdgeEvidence.Add(item.EdgeEvidence);
            switch (node)
            {
                case ExitFlowNode:
                    if (!IsAgreedOwnHeaderExitBoundary(owningLoop, node.Id))
                    {
                        return Unsupported("mismatched-exit");
                    }

                    boundaryKinds.Add(ScenarioTerminalKind.Rejoins);
                    boundaryEvidence.Add(node.Evidence);
                    continue;
                case DecisionFlowNode:
                    if (!IsAgreedOwnHeaderExitBoundary(owningLoop, node.Id))
                    {
                        return Unsupported("mismatched-exit");
                    }

                    boundaryKinds.Add(ScenarioTerminalKind.Rejoins);
                    boundaryEvidence.Add(node.Evidence);
                    continue;
                case LoopNode:
                    return Unsupported("loop");
                case ReturnFlowNode or ThrowFlowNode:
                    if (flow.Edges.Where(edge => edge.Source == node.Id).Any(edge => IsExceptionRegionNode(flow, edge.Target)))
                    {
                        return Unsupported("exception-boundary-target");
                    }

                    if (!IsRepresentedTerminalBoundary(node, flow))
                    {
                        return Unsupported("operation-derived-duplicate-terminal");
                    }

                    boundaryKinds.Add(ScenarioTerminalKind.Terminates);
                    boundaryEvidence.Add(node.Evidence);
                    continue;
            }

            var unsupportedEdge = flow.Edges
                .Where(edge => edge.Source == node.Id)
                .FirstOrDefault(edge => !IsSupportedTraversalEdge(edge.Kind)
                    && !IsOwnHeaderIterationBoundary(edge, owningLoop, decision));
            if (unsupportedEdge is not null)
            {
                return Unsupported($"unsupported-edge:{unsupportedEdge.Kind.ToString()}");
            }

            foreach (var edge in flow.Edges
                         .Where(edge => edge.Source == node.Id)
                         .OrderBy(edge => edge.Id.Value, StringComparer.Ordinal))
            {
                if (IsOwnHeaderIterationBoundary(edge, owningLoop, decision))
                {
                    // The accepted back edge must originate in the owning loop's recorded body; a
                    // source outside <see cref="LoopNode.Body"/> proves a foreign or incomplete loop
                    // snapshot and fails closed instead of classifying represented iteration.
                    if (!owningLoop!.Body.Contains(edge.Source))
                    {
                        return Unsupported("foreign-body-source");
                    }

                    // A LoopBack to this decision's own header is the represented iteration
                    // boundary: the arm re-enters the loop header rather than terminating or
                    // rejoining the enclosing flow. The boundary evidence includes the LoopBack edge
                    // and the loop fact's own evidence; certainty degrades to the weakest
                    // contributor in BuildArmTerminal. The target is never re-traversed, so the walk
                    // stays finite.
                    boundaryKinds.Add(ScenarioTerminalKind.Rejoins);
                    boundaryEvidence.Add(Combine(edge.Evidence, owningLoop!.Evidence));
                    continue;
                }

                pending.Push((edge.Target, edge.Evidence));
            }
        }

        if (boundaryKinds.Count == 0)
        {
            return Unsupported("no-reachable-boundary");
        }

        if (boundaryKinds.All(kind => kind == ScenarioTerminalKind.Terminates))
        {
            return new ArmTerminalClassification(
                ScenarioTerminalKind.Terminates,
                null,
                Combine(traversedEdgeEvidence.ToArray()),
                Combine(boundaryEvidence.ToArray()));
        }

        if (boundaryKinds.All(kind => kind == ScenarioTerminalKind.Rejoins))
        {
            return new ArmTerminalClassification(
                ScenarioTerminalKind.Rejoins,
                null,
                Combine(traversedEdgeEvidence.ToArray()),
                Combine(boundaryEvidence.ToArray()));
        }

        return Unsupported("mixed-boundary");
    }

    /// <summary>
    /// Accepts a Return/Throw node as a represented block terminal only when it is the block tail:
    /// every outgoing edge is Return/Throw/Rethrow to the successor/exit, or the node is a sink. An
    /// operation-derived duplicate return/throw node with a Normal continuation is never a boundary.
    /// </summary>
    private static bool IsRepresentedTerminalBoundary(FlowNode node, MethodFlowSnapshot flow)
    {
        var outgoing = flow.Edges.Where(edge => edge.Source == node.Id).ToArray();
        return outgoing.Length == 0
            || outgoing.All(edge => edge.Kind is FlowEdgeKind.Return or FlowEdgeKind.Throw or FlowEdgeKind.Rethrow);
    }

    private static bool IsSupportedTraversalEdge(FlowEdgeKind kind)
        => kind is FlowEdgeKind.Normal
            or FlowEdgeKind.True
            or FlowEdgeKind.False
            or FlowEdgeKind.Return
            or FlowEdgeKind.Throw
            or FlowEdgeKind.Rethrow;

    /// <summary>
    /// True when a LoopBack edge re-enters the exact header decision that owns this classification.
    /// Accepted CT-4 design item 1 admits only this same-header iteration boundary; a LoopBack to a foreign
    /// header, or to a decision that is not the exact <see cref="LoopNode.Header"/> of an existing
    /// loop, remains unsupported and fails closed with SC013.
    /// </summary>
    private static bool IsOwnHeaderIterationBoundary(
        FlowEdge edge,
        LoopNode? owningLoop,
        DecisionFlowNode decision)
        => edge.Kind == FlowEdgeKind.LoopBack
            && owningLoop is not null
            && edge.Target == decision.Id;

    /// <summary>
    /// True when an exact own-header loop arm's normal exit boundary agrees with the owning
    /// <see cref="LoopNode.Exits"/> facts. A decision without an owning loop has no loop-exit
    /// contract, so any rejoin boundary is accepted. For an exact own-header decision, a Rejoins
    /// boundary that is not recorded in the loop's exits proves a mismatched or incomplete loop
    /// snapshot and fails closed with SC013 rather than classifying represented iteration.
    /// </summary>
    private static bool IsAgreedOwnHeaderExitBoundary(LoopNode? owningLoop, FlowNodeId boundaryNode)
        => owningLoop is null || owningLoop.Exits.Contains(boundaryNode);

    private static bool IsExceptionRegionNode(MethodFlowSnapshot flow, FlowNodeId node)
        => flow.Regions.Any(region => region.Nodes.Contains(node)
            && region.Kind is FlowRegionKind.Catch or FlowRegionKind.Filter or FlowRegionKind.Finally);

    private static ArmTerminalClassification Unsupported(string reason)
        => new(ScenarioTerminalKind.Unknown, reason, [], []);

    /// <summary>Carries one arm classification plus the evidence needed to build the terminal fact.</summary>
    private sealed record ArmTerminalClassification(
        ScenarioTerminalKind Kind,
        string? UnsupportedReason,
        ImmutableArray<EvidenceRef> TraversedEdgeEvidence,
        ImmutableArray<EvidenceRef> BoundaryEvidence);

    private static ScenarioGraph FinalizeGraph(
        ScenarioAnalysisRequest request,
        NormalizedEntry entryPoint,
        CompilationProfileId profileId,
        List<ScenarioNode> nodes,
        List<ScenarioEdge> edges,
        List<ScenarioGraphDiagnostic> diagnostics,
        ScenarioTopology topology,
        ScenarioServiceComposition? composition = null,
        ScenarioHandlerTopology? handlerTopology = null,
        ScenarioDispatchHandlerExpansion? dispatchHandlerExpansion = null,
        ScenarioDirectCallExpansion? directCallExpansion = null)
    {
        var orderedNodes = nodes.OrderBy(node => node.Id.Value, StringComparer.Ordinal).ToImmutableArray();
        var orderedEdges = edges.OrderBy(edge => edge.Id.Value, StringComparer.Ordinal).ToImmutableArray();

        // Callback processing runs before diagnostics are finalized so the SC014 unsupported-cache
        // diagnostic (and any other callback-time diagnostic) is retained in the canonical set. The
        // framework candidate that cannot join an exact fact withholds its boundary member nodes:
        // those nodes and their connected edges are pruned here and their identities are removed
        // from the composition arm membership, while both service-call arm nodes/edges and the
        // composition itself are retained (regression).
        var (callbackRegions, withheldNodeIds) = BuildCallbackRegions(
            request,
            profileId,
            entryPoint.EntryPointId,
            orderedNodes,
            diagnostics,
            composition);
        var withheld = withheldNodeIds.ToHashSet();
        var prunedNodes = withheld.Count == 0
            ? orderedNodes
            : orderedNodes.Where(node => !withheld.Contains(node.Id)).ToImmutableArray();
        var prunedEdges = withheld.Count == 0
            ? orderedEdges
            : orderedEdges
                .Where(edge => !withheld.Contains(edge.Source) && !withheld.Contains(edge.Target))
                .ToImmutableArray();
        var prunedComposition = withheld.Count == 0
            ? composition
            : PruneWithheldCompositionMembers(composition, withheld);
        var orderedDiagnostics = diagnostics
            .OrderBy(diagnostic => diagnostic.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        return new ScenarioGraph(
            entryPoint.EntryPointId,
            profileId,
            entryPoint.RootMethod,
            entryPoint.HttpMethod,
            entryPoint.CanonicalRoute,
            entryPoint.OperationKey,
            prunedNodes,
            prunedEdges,
            orderedDiagnostics,
            BuildGraphDebugProjection(prunedNodes, prunedEdges, orderedDiagnostics, topology, prunedComposition, callbackRegions, entryPoint.RootKind),
            topology,
            prunedComposition,
            callbackRegions,
            handlerTopology,
            dispatchHandlerExpansion,
            entryPoint.RootKind,
            directCallExpansion);
    }

    /// <summary>
    /// Removes withheld callback-member node identities from the composition arm member sets while
    /// retaining both arms, the service-call arm nodes, and the composition itself. A null
    /// composition or a composition whose arms never referenced a withheld node is returned
    /// unchanged.
    /// </summary>
    private static ScenarioServiceComposition? PruneWithheldCompositionMembers(
        ScenarioServiceComposition? composition,
        HashSet<ScenarioNodeId> withheld)
    {
        if (composition is null)
        {
            return null;
        }

        var trueMembers = composition.TrueArm.MemberNodes
            .Where(node => !withheld.Contains(node))
            .ToImmutableArray();
        var falseMembers = composition.FalseArm.MemberNodes
            .Where(node => !withheld.Contains(node))
            .ToImmutableArray();
        if (trueMembers.Length == composition.TrueArm.MemberNodes.Length
            && falseMembers.Length == composition.FalseArm.MemberNodes.Length)
        {
            return composition;
        }

        var trueArm = new ScenarioServiceAlternativeArm(
            composition.TrueArm.IsTrue,
            composition.TrueArm.RegistrationId,
            composition.TrueArm.ImplementationType,
            composition.TrueArm.ResolvedMethod,
            composition.TrueArm.Evidence,
            composition.TrueArm.Certainty,
            trueMembers);
        var falseArm = new ScenarioServiceAlternativeArm(
            composition.FalseArm.IsTrue,
            composition.FalseArm.RegistrationId,
            composition.FalseArm.ImplementationType,
            composition.FalseArm.ResolvedMethod,
            composition.FalseArm.Evidence,
            composition.FalseArm.Certainty,
            falseMembers);
        return new ScenarioServiceComposition(
            composition.Id,
            composition.ServiceType,
            composition.Decision,
            trueArm,
            falseArm,
            composition.ProfileSelection);
    }

    /// <summary>
    /// Builds typed callback regions from the bound callback boundary fact set. A null or foreign
    /// fact set (different profile ID or Program Index fingerprint) contributes no region or
    /// membership. accepted contract adds a framework path before the generic source-boundary path: an exact-one
    /// <see cref="FusionCacheGetOrSetFact"/> matching the boundary's caller/outer operation/factory
    /// ordinal joins the boundary's exact member nodes into one typed cache-miss region whose
    /// cardinality/trigger/framework condition come from the fact. Only the exact one matching fact
    /// and the exact one Unknown-provenance Anonymous boundary may produce a region; zero, multiple,
    /// or foreign candidates never first-select and never fall through to invent callback semantics
    /// from a metadata boundary. An unmatched framework candidate that belongs to a composition arm
    /// and is diagnosed with the exact SEQFC001 unsupported-shape code for this exact boundary outer
    /// operation yields the deterministic SC014
    /// diagnostic and reports the boundary's exact member node identities as withheld; the caller
    /// prunes those nodes/edges from the flat graph and their identities from the arm membership so
    /// unsupported cache work is never presented as unconditional SQL work. The generic
    /// source-boundary path is unchanged for every non-candidate boundary: the caller method must be
    /// represented by a generated graph node and every member operation must map to generated nodes
    /// by exact <see cref="ScenarioNode.Operation"/> identity; a boundary with no exact member nodes
    /// produces no region, and member operations never match by display text or node kind. Region
    /// identity derives only from profile, entry point, and boundary identity; fields, evidence, and
    /// certainty come from the facts unchanged (no promotion). Regions are canonically ordered by
    /// region identity and members by node identity, and withheld identities are canonical and
    /// distinct.
    /// </summary>
    private static (ImmutableArray<ScenarioCallbackRegion> Regions, ImmutableArray<ScenarioNodeId> WithheldNodeIds) BuildCallbackRegions(
        ScenarioAnalysisRequest request,
        CompilationProfileId profileId,
        EntryPointId entryPointId,
        ImmutableArray<ScenarioNode> nodes,
        List<ScenarioGraphDiagnostic> diagnostics,
        ScenarioServiceComposition? composition)
    {
        var facts = request.CallbackBoundaryFacts;
        if (facts is null
            || facts.Profile.Id != request.Profile.Id
            || !string.Equals(facts.ProgramIndexFingerprint, request.ProgramIndex.IndexFingerprint, StringComparison.Ordinal))
        {
            // A null or foreign callback fact set never contributes a region or membership
            // (accepted contract requirement 7).
            return ([], []);
        }

        var regions = new List<ScenarioCallbackRegion>();
        var withheldNodeIds = new List<ScenarioNodeId>();
        foreach (var boundary in facts.Boundaries)
        {
            // accepted contract framework path: a metadata-anonymous boundary (Unknown contract provenance and an
            // anonymous-function target) is a FusionCache candidate. When the exact-one
            // GetOrSetAsync fact joins it, the framework region carries the cache-miss semantics;
            // otherwise the boundary is fully handled below (no invented region or diagnostic) and
            // never falls through to the generic source-boundary path.
            if (boundary.TargetKind == CallbackTargetKind.AnonymousFunction
                && boundary.ContractProvenance == CallbackContractProvenance.Unknown)
            {
                var join = TryBuildFrameworkCallbackRegion(
                    request,
                    profileId,
                    entryPointId,
                    facts,
                    boundary,
                    nodes,
                    diagnostics,
                    composition);
                if (join.Region is not null)
                {
                    regions.Add(join.Region);
                }

                if (!join.WithheldMemberNodes.IsEmpty)
                {
                    withheldNodeIds.AddRange(join.WithheldMemberNodes);
                }

                continue;
            }

            if (!nodes.Any(node => node.Method == boundary.CallerMethod))
            {
                // The boundary's caller method is not represented by a generated graph node, so the
                // boundary cannot join any member node.
                continue;
            }

            var memberOperations = boundary.MemberOperations.ToHashSet(StringComparer.Ordinal);
            var memberNodes = nodes
                .Where(node => node.Operation is { } operation && memberOperations.Contains(operation.Value))
                .Select(node => node.Id)
                .OrderBy(id => id.Value, StringComparer.Ordinal)
                .ToImmutableArray();
            if (memberNodes.IsEmpty)
            {
                // No exact member nodes means no region; the boundary never invents membership or
                // presents callback work as unconditional top-level behavior.
                continue;
            }

            regions.Add(new ScenarioCallbackRegion(
                StableIdentity.CreateScenarioCallbackRegionId(new ScenarioCallbackRegionIdentityDescriptor(
                    profileId, entryPointId, boundary.Id)),
                boundary.Id,
                boundary.Cardinality,
                boundary.Trigger,
                boundary.TriggerCondition,
                boundary.Completion,
                memberNodes,
                boundary.Evidence,
                boundary.Certainty));
        }

        return (
            regions
                .OrderBy(region => region.Id.Value, StringComparer.Ordinal)
                .ToImmutableArray(),
            withheldNodeIds
                .Distinct()
                .OrderBy(id => id.Value, StringComparer.Ordinal)
                .ToImmutableArray());
    }

    /// <summary>
    /// accepted contract framework callback join. The boundary must be the exact one Unknown-provenance
    /// AnonymousFunction boundary for its caller/outer operation/ordinal, and the framework facts
    /// must carry exactly one <see cref="FusionCacheGetOrSetFact"/> for that same profile, Program
    /// Index fingerprint, boundary identity, caller/outer operation, and ordinal. A fact anchored
    /// to a foreign profile, foreign fingerprint, or a different boundary never matches and never
    /// selects or forms a region. The resulting region uses the fact's bounded
    /// ZeroOrOne/Conditional/CacheMiss semantics with the boundary's callback-local completion, a
    /// null operation trigger condition, the union of fact and boundary evidence, and the weakest
    /// contributor certainty. A zero or multiple candidate set never first-selects; an unmatched
    /// framework candidate keeps the surrounding service branch and adds the deterministic SC014
    /// diagnostic only when the framework facts already report the exact SEQFC001 unsupported
    /// FusionCache code for this exact boundary outer operation, so the builder never invents a
    /// cache diagnostic on its own. In that
    /// unsupported case every generated node whose exact <see cref="ScenarioNode.Operation"/> is one
    /// of the boundary's member operations is reported as withheld so the caller can remove those
    /// nodes, their connected edges, and their arm-membership identities instead of presenting
    /// unsupported cache work as unconditional SQL work.
    /// </summary>
    private static FrameworkCallbackJoinResult TryBuildFrameworkCallbackRegion(
        ScenarioAnalysisRequest request,
        CompilationProfileId profileId,
        EntryPointId entryPointId,
        CallbackBoundaryFactSet facts,
        CallbackBoundaryFact boundary,
        ImmutableArray<ScenarioNode> nodes,
        List<ScenarioGraphDiagnostic> diagnostics,
        ScenarioServiceComposition? composition)
    {
        var matchingFacts = request.FrameworkFacts.Facts
            .OfType<FusionCacheGetOrSetFact>()
            .Where(fact => fact.ProfileId == request.Profile.Id
                && string.Equals(fact.ProgramIndexFingerprint, request.ProgramIndex.IndexFingerprint, StringComparison.Ordinal)
                && fact.CallbackBoundaryId == boundary.Id
                && fact.Method == boundary.CallerMethod
                && fact.Operation == boundary.OuterInvocationOperation
                && fact.FactoryParameterOrdinal == boundary.ParameterOrdinal)
            .ToArray();
        var matchingBoundaries = facts.Boundaries
            .Where(candidate => candidate.CallerMethod == boundary.CallerMethod
                && candidate.OuterInvocationOperation == boundary.OuterInvocationOperation
                && candidate.ParameterOrdinal == boundary.ParameterOrdinal
                && candidate.TargetKind == CallbackTargetKind.AnonymousFunction
                && candidate.ContractProvenance == CallbackContractProvenance.Unknown)
            .ToArray();
        if (matchingFacts.Length != 1
            || matchingBoundaries.Length != 1
            || matchingBoundaries[0].Id != boundary.Id)
        {
            // No first candidate/multiple: an unsupported or unmatched FusionCache shape withholds
            // cache-miss membership. When the boundary belongs to a composition arm method and the
            // framework facts already diagnosed the exact SEQFC001 unsupported-shape code for this
            // exact boundary outer operation, the degradation is explicit SC014 and every generated
            // node whose exact operation is a boundary member is withheld; otherwise no cache
            // diagnostic is invented and nothing is withheld.
            if (composition is not null
                && IsCompositionArmMethod(composition, boundary.CallerMethod)
                && HasFusionCacheUnsupportedDiagnostic(request, boundary))
            {
                diagnostics.Add(CreateDiagnostic(
                    profileId,
                    entryPointId,
                    "SC014",
                    "The FusionCache callback boundary has no exact supported GetOrSetAsync contract; cache-miss membership is withheld.",
                    $"{boundary.CallerMethod.Value}\u001f{boundary.OuterInvocationOperation.Value}\u001f{boundary.ParameterOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
                var unsupportedMemberOperations = boundary.MemberOperations.ToHashSet(StringComparer.Ordinal);
                var withheld = nodes
                    .Where(node => node.Operation is { } operation && unsupportedMemberOperations.Contains(operation.Value))
                    .Select(node => node.Id)
                    .ToImmutableArray();
                return new FrameworkCallbackJoinResult(null, withheld);
            }

            return new FrameworkCallbackJoinResult(null, []);
        }

        var fact = matchingFacts[0];
        var memberOperations = boundary.MemberOperations.ToHashSet(StringComparer.Ordinal);
        var memberNodes = nodes
            .Where(node => node.Operation is { } operation && memberOperations.Contains(operation.Value))
            .Select(node => node.Id)
            .OrderBy(id => id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        if (memberNodes.IsEmpty)
        {
            // No exact member nodes means no region; the framework contract never invents
            // membership or presents cache-miss work as unconditional behavior.
            return new FrameworkCallbackJoinResult(null, []);
        }

        var evidence = Combine(fact.Evidence, boundary.Evidence);
        return new FrameworkCallbackJoinResult(
            new ScenarioCallbackRegion(
                StableIdentity.CreateScenarioCallbackRegionId(new ScenarioCallbackRegionIdentityDescriptor(
                    profileId, entryPointId, boundary.Id)),
                boundary.Id,
                fact.Cardinality,
                fact.Trigger,
                null,
                boundary.Completion,
                memberNodes,
                evidence,
                (CertaintyLevel)Math.Max((int)fact.Certainty, (int)boundary.Certainty),
                fact.Condition),
            []);
    }

    /// <summary>True when the boundary caller is one of the composition arm's resolved implementation methods.</summary>
    private static bool IsCompositionArmMethod(ScenarioServiceComposition composition, MethodId method)
        => method == composition.TrueArm.ResolvedMethod || method == composition.FalseArm.ResolvedMethod;

    /// <summary>
    /// True when the framework fact set already diagnosed the exact SEQFC001 unsupported FusionCache
    /// shape for this exact boundary's outer operation. The builder only surfaces SC014 when the
    /// framework model reported the exact stable code AND the diagnostic's canonical
    /// <see cref="AnalysisDiagnostic.InternalDetail"/> matches the boundary's exact outer invocation
    /// operation through <see cref="FusionCacheDiagnosticCodes.MatchesUnsupportedShapeOperation"/>;
    /// a diagnostic for a foreign operation never degrades this boundary. The builder never invents
    /// a cache diagnostic from the graph layer alone and never matches a substring.
    /// </summary>
    private static bool HasFusionCacheUnsupportedDiagnostic(
        ScenarioAnalysisRequest request,
        CallbackBoundaryFact boundary)
        => request.FrameworkFacts.Diagnostics.Any(diagnostic =>
            string.Equals(diagnostic.Code, FusionCacheDiagnosticCodes.UnsupportedShape, StringComparison.Ordinal)
            && FusionCacheDiagnosticCodes.MatchesUnsupportedShapeOperation(
                diagnostic.InternalDetail,
                boundary.OuterInvocationOperation));

    private static ScenarioNode CreateNode(
        CompilationProfileId profileId,
        EntryPointId entryPointId,
        ScenarioNodeKind kind,
        string key,
        MethodId? method,
        OperationId? operation,
        string detail,
        params ImmutableArray<EvidenceRef>[] evidenceSources)
    {
        var evidence = Combine(evidenceSources);
        return CreateNode(
            profileId,
            entryPointId,
            kind,
            key,
            method,
            operation,
            detail,
            evidence,
            evidence.Min(item => item.Certainty));
    }

    /// <summary>
    /// Creates a node with typed presentation inputs plus evidence-source variadics. The
    /// presentation carries only the authoritative typed facts the node's kind proves; display
    /// wording never parses the detail string.
    /// </summary>
    private static ScenarioNode CreateNodeWithPresentation(
        CompilationProfileId profileId,
        EntryPointId entryPointId,
        ScenarioNodeKind kind,
        string key,
        MethodId? method,
        OperationId? operation,
        string detail,
        ScenarioNodePresentation presentation,
        params ImmutableArray<EvidenceRef>[] evidenceSources)
    {
        var evidence = Combine(evidenceSources);
        return CreateNode(
            profileId,
            entryPointId,
            kind,
            key,
            method,
            operation,
            detail,
            evidence,
            evidence.Min(item => item.Certainty),
            presentation: presentation);
    }

    private static ScenarioNode CreateNodeWithPresentation(
        CompilationProfileId profileId,
        EntryPointId entryPointId,
        ScenarioNodeKind kind,
        string key,
        MethodId? method,
        OperationId? operation,
        string detail,
        ScenarioNodePresentation presentation,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty,
        int sequenceOrdinal = 0)
        => CreateNode(profileId, entryPointId, kind, key, method, operation, detail, evidence, certainty,
            sequenceOrdinal, presentation);

    private static ScenarioNode CreateNode(
        CompilationProfileId profileId,
        EntryPointId entryPointId,
        ScenarioNodeKind kind,
        string key,
        MethodId? method,
        OperationId? operation,
        string detail,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty,
        int sequenceOrdinal = 0,
        ScenarioNodePresentation? presentation = null)
    {
        var id = StableIdentity.CreateScenarioNodeId(new ScenarioNodeIdentityDescriptor(
            profileId,
            entryPointId,
            kind.ToString(),
            key));
        return new ScenarioNode(
            id,
            kind,
            key,
            method,
            operation,
            detail,
            evidence,
            certainty,
            sequenceOrdinal,
            presentation);
    }

    private static ScenarioEdge CreateEdge(
        CompilationProfileId profileId,
        EntryPointId entryPointId,
        ScenarioNode source,
        ScenarioNode target,
        ScenarioEdgeKind kind,
        string detail,
        int sequenceOrdinal,
        params ImmutableArray<EvidenceRef>[] evidenceSources)
    {
        var evidence = Combine(evidenceSources);
        return CreateEdge(
            profileId,
            entryPointId,
            source,
            target,
            kind,
            detail,
            evidence,
            evidence.Min(item => item.Certainty),
            sequenceOrdinal);
    }

    private static ScenarioEdge CreateEdge(
        CompilationProfileId profileId,
        EntryPointId entryPointId,
        ScenarioNode source,
        ScenarioNode target,
        ScenarioEdgeKind kind,
        string detail,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty,
        int sequenceOrdinal = 0)
    {
        var id = StableIdentity.CreateScenarioEdgeId(new ScenarioEdgeIdentityDescriptor(
            profileId,
            entryPointId,
            source.Id.Value,
            target.Id.Value,
            kind.ToString(),
            0));
        return new ScenarioEdge(
            id,
            source.Id,
            target.Id,
            kind,
            detail,
            evidence,
            certainty,
            sequenceOrdinal);
    }

    private static ScenarioEdge CreateEdge(
        CompilationProfileId profileId,
        EntryPointId entryPointId,
        ScenarioNode source,
        ScenarioNode target,
        ScenarioEdgeKind kind,
        string detail,
        params ImmutableArray<EvidenceRef>[] evidenceSources)
        => CreateEdge(profileId, entryPointId, source, target, kind, detail, 0, evidenceSources);

    private static ImmutableArray<EvidenceRef> Combine(params ImmutableArray<EvidenceRef>[] sources)
        => sources
            .SelectMany(source => source)
            .Where(item => item is not null)
            .DistinctBy(item => item.Id.Value)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();

    private static CertaintyLevel LeastConfident(
        CertaintyLevel explicitCertainty,
        ImmutableArray<EvidenceRef> evidence,
        params CertaintyLevel[] contributors)
        => new[] { explicitCertainty, evidence.Max(item => item.Certainty) }
            .Concat(contributors)
            .Max();

    private static ScenarioGraphDiagnostic CreateDiagnostic(
        CompilationProfileId profileId,
        EntryPointId entryPointId,
        string code,
        string summary,
        string detail,
        ImmutableArray<EvidenceRef> evidence = default)
    {
        var id = StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            code,
            AnalysisStage.FrameworkModel,
            profileId,
            $"{entryPointId.Value}\u001f{detail}",
            0));
        return new ScenarioGraphDiagnostic(id, code, summary, detail)
        {
            Evidence = evidence.IsDefault ? [] : evidence,
            Certainty = evidence.IsDefaultOrEmpty ? CertaintyLevel.Conservative : evidence.Max(item => item.Certainty),
        };
    }

    private static string BuildGraphDebugProjection(
        ImmutableArray<ScenarioNode> nodes,
        ImmutableArray<ScenarioEdge> edges,
        ImmutableArray<ScenarioGraphDiagnostic> diagnostics,
        ScenarioTopology topology,
        ScenarioServiceComposition? composition,
        ImmutableArray<ScenarioCallbackRegion> callbackRegions,
        ScenarioRootKind rootKind = ScenarioRootKind.HttpEntryPoint)
    {
        var lines = new List<(string Id, string Line)>();
        if (rootKind != ScenarioRootKind.HttpEntryPoint)
        {
            lines.Add(("root-kind", $"root-kind {rootKind}"));
        }
        foreach (var node in nodes)
        {
            lines.Add((node.Id.Value, $"node {node.Id.Value} kind={node.Kind.ToString()} key={node.Key} method={node.Method?.Value ?? string.Empty} detail={node.Detail}"));
        }

        foreach (var edge in edges)
        {
            lines.Add((edge.Id.Value, $"edge {edge.Id.Value} source={edge.Source.Value} target={edge.Target.Value} kind={edge.Kind.ToString()} detail={edge.Detail}"));
        }

        foreach (var diagnostic in diagnostics)
        {
            lines.Add((diagnostic.Id.Value, $"diagnostic {diagnostic.Code} summary={diagnostic.Summary} detail={diagnostic.Detail}"));
        }

        // The callback-region projection carries only identities, enums, the exact condition anchor,
        // the framework condition, and member node identities — never paths, debug text, or raw
        // captured values.
        foreach (var region in callbackRegions.OrderBy(region => region.Id.Value, StringComparer.Ordinal))
        {
            lines.Add((region.Id.Value, $"callback-region {region.Id.Value} boundary={region.BoundaryId.Value} cardinality={region.Cardinality.ToString()} trigger={region.Trigger.ToString()} condition={region.TriggerCondition?.Value ?? string.Empty} framework={region.FrameworkCondition?.ToString() ?? string.Empty} completion={region.Completion.ToString()} members={string.Join(",", region.MemberNodes.Select(node => node.Value))}"));
        }

        if (composition is not null)
        {
            lines.Add((composition.Id.Value, $"composition {composition.Id.Value} service={composition.ServiceType} key={composition.Decision.Key} condition={composition.Decision.ConditionOperation.Value} read={composition.Decision.ReadOperation.Value} decisionCertainty={composition.Decision.Certainty.ToString()} true={composition.TrueArm.RegistrationId.Value}->{composition.TrueArm.ResolvedMethod.Value} trueMembers={string.Join(",", composition.TrueArm.MemberNodes.Select(node => node.Value))} false={composition.FalseArm.RegistrationId.Value}->{composition.FalseArm.ResolvedMethod.Value} falseMembers={string.Join(",", composition.FalseArm.MemberNodes.Select(node => node.Value))}"));
            if (composition.ProfileSelection is not null)
            {
                lines.Add(($"selection:{composition.Id.Value}", $"profile-selection composition={composition.Id.Value} selectsTrue={composition.ProfileSelection.SelectsTrueArm.ToString()} source={composition.ProfileSelection.AnalysisProfileSource} certainty={composition.ProfileSelection.Certainty.ToString()}"));
            }
        }

        foreach (var decision in topology.Decisions.OrderBy(decision => decision.Id.Value, StringComparer.Ordinal))
        {
            lines.Add((decision.Id.Value, $"decision {decision.Id.Value} method={decision.Method.Value} flow={decision.ControllingFlowNode.Value} condition={decision.Condition.Value}"));
        }

        foreach (var arm in topology.Arms.OrderBy(arm => arm.Id.Value, StringComparer.Ordinal))
        {
            lines.Add((arm.Id.Value, $"arm {arm.Id.Value} decision={arm.Decision.Value} isTrue={arm.IsTrue}"));
        }

        foreach (var membership in topology.Memberships.OrderBy(membership => membership.Id.Value, StringComparer.Ordinal))
        {
            lines.Add((membership.Id.Value, $"membership {membership.Id.Value} arm={membership.Arm.Value} node={membership.ScenarioNode.Value}"));
        }

        foreach (var terminal in topology.Terminals.OrderBy(terminal => terminal.Arm.Value, StringComparer.Ordinal))
        {
            lines.Add(($"t:{terminal.Arm.Value}", $"terminal arm={terminal.Arm.Value} kind={terminal.Kind.ToString()}"));
        }

        return string.Join('\n', lines.OrderBy(line => line.Id, StringComparer.Ordinal).Select(line => line.Line));
    }

    private static string BuildSetDebugProjection(
        ScenarioAnalysisRequest request,
        ImmutableArray<ScenarioGraph> graphs)
    {
        var builder = new StringBuilder();
        builder.Append("scenario-graphs:v1").Append('\n');
        builder.Append("producer=").Append(ProducerVersion).Append('\n');
        builder.Append("profile=").Append(request.Profile.Id.Value).Append('\n');
        builder.Append("programIndexFingerprint=").Append(request.ProgramIndex.IndexFingerprint).Append('\n');
        foreach (var graph in graphs)
        {
            builder.Append(graph.DebugProjection).Append('\n');
        }

        return builder.ToString().TrimEnd('\n');
    }

    private sealed record ServiceResolution(
        MethodId ServiceMethod,
        CallSite CallSite,
        DependencyInjectionBindingFact Binding,
        DependencyInjectionRegistrationFact Registration);

    /// <summary>
    /// Outcome of one framework callback join attempt: either the exact typed region or a set of
    /// withheld member node identities for the SC014 unsupported-cache degradation. The two arrays
    /// are never both non-empty: an exact join withholds nothing and a withheld degradation produces
    /// no region.
    /// </summary>
    private sealed record FrameworkCallbackJoinResult(
        ScenarioCallbackRegion? Region,
        ImmutableArray<ScenarioNodeId> WithheldMemberNodes);
}

/// <summary>Builds the intentionally bounded expansion behind one exact dispatch fact.</summary>
#pragma warning disable IDE0011
public static class ScenarioDispatchHandlerExpansionBuilder
{
    public static ScenarioDispatchHandlerExpansion Build(ScenarioAnalysisRequest request, DispatchFact dispatch)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(dispatch);
        var evidence = dispatch.Evidence;
        var candidate = dispatch.SelectedHandler;
        var diagnostics = new List<ScenarioGraphDiagnostic>();
        void Refuse(string code, string detail) => diagnostics.Add(new ScenarioGraphDiagnostic(
            new DiagnosticId($"diagnostic:v1:{code}:{dispatch.OperationId.Value}"), code, detail, dispatch.OperationId.Value));
        if (dispatch.Profile != request.Profile.Id || dispatch.ProgramIndexFingerprint != request.ProgramIndex.IndexFingerprint)
            Refuse("SC-DISPATCH-MISMATCH", "Dispatch facts belong to another profile or Program Index fingerprint.");
        else if (dispatch.Boundary != DispatchBoundaryKind.RequestResponse || dispatch.Cardinality != DispatchCardinality.ExactlyOne)
            Refuse("SC-DISPATCH-UNRESOLVED", "Dispatch admission is not exact-single request/response.");
        else if (dispatch.Resolution == DispatchResolution.Ambiguous)
            Refuse("SC-DISPATCH-AMBIGUOUS", "Dispatch admission is not exact-single request/response.");
        else if (dispatch.Resolution == DispatchResolution.GeneratedBodyUnavailable || candidate is not null && !candidate.BodyAvailable)
            Refuse("SC-DISPATCH-BODY-UNAVAILABLE", "The selected dispatch candidate has no admitted source body.");
        else if (dispatch.Resolution != DispatchResolution.ExactSingle)
            Refuse("SC-DISPATCH-UNRESOLVED", "Dispatch admission is not exact-single request/response.");
        var method = candidate is null ? null : request.ProgramIndex.Methods.SingleOrDefault(item => item.Id == candidate.Method);
        var flow = candidate is null ? null : request.Behavior.MethodFlows.SingleOrDefault(item => item.Method == candidate.Method);
        if (candidate is not null && (method is null || flow is null)) Refuse("SC-DISPATCH-HANDLER-FLOW-MISSING", "The selected handler did not join to one Program Index method and Method Flow.");
        var calls = new List<ScenarioDispatchHandlerStep>();
        var nestedSteps = new List<(ScenarioDispatchHandlerStep Step, int NestedOrdinal)>();
        var loops = new List<ScenarioDispatchHandlerLoop>();
        ScenarioDispatchHandlerReturn? returned = null;
        if (!HasBlockingDiagnostics(diagnostics) && method is not null && flow is not null)
        {
            var loopNodes = flow.Nodes.OfType<LoopNode>().ToArray();
            var selectedLoop = loopNodes.Length == 1
                && loopNodes[0].Header is not null
                && !loopNodes[0].Body.IsDefaultOrEmpty
                && !loopNodes[0].BodyBlockOrdinals.IsDefaultOrEmpty
                && !loopNodes[0].Exits.IsDefaultOrEmpty
                ? loopNodes[0]
                : null;
            var selectedLoopBacks = selectedLoop is null
                ? []
                : flow.Edges.Where(edge => edge.Kind == FlowEdgeKind.LoopBack
                    && edge.Target == selectedLoop.Header
                    && selectedLoop.Body.Contains(edge.Source)).ToArray();
            var selectedLoopBack = selectedLoopBacks.Length == 1 ? selectedLoopBacks[0] : null;
            if (selectedLoop is not null && selectedLoopBack is null)
            {
                selectedLoop = null;
            }
            if (loopNodes.Length != 0 && selectedLoop is null)
                Refuse("SC-DISPATCH-LOOP-INCOMPLETE", "The handler does not contain one complete natural loop.");
            var invocations = flow.Nodes.OfType<InvocationFlowNode>()
                .OrderBy(node => node.BlockOrdinal)
                .ThenBy(node => node.EvaluationOrdinal)
                .ThenBy(node => node.Id.Value, StringComparer.Ordinal)
                .ToArray();
            for (var sourceOrdinal = 0; sourceOrdinal < invocations.Length; sourceOrdinal++)
            {
                var invocation = invocations[sourceOrdinal];
                // Platform calls, callback/nested-function calls, and non-source-backed invocations
                // are expected exclusions. They are not eligible dispatch claims, so do not resolve
                // their call sites or emit withholding diagnostics.
                if (!invocation.IsSourceBacked || invocation.IsInsideNestedFunction || invocation.IsPlatformTarget)
                {
                    continue;
                }
                var site = FindCanonicalCallSite(request, flow, invocation);
                if (!IsAdmittedDirectLeaf(invocation, site))
                {
                    Refuse("SC-DISPATCH-CALL-WITHHELD", $"Invocation {invocation.Operation.Value} is not one complete direct source call.");
                    continue;
                }
                var target = site!.Resolution.Candidates[0];
                var inLoop = selectedLoop is not null
                    && (selectedLoop.BodyBlockOrdinals.IsDefaultOrEmpty
                        ? selectedLoop.Body.Contains(invocation.Id)
                        : selectedLoop.BodyBlockOrdinals.Contains(invocation.BlockOrdinal));
                var step = MakeStep(candidate!.Method, invocation, target, inLoop ? selectedLoop!.Region.Value : null, 0, site.Evidence.Concat(site.Resolution.Evidence).ToImmutableArray(), sourceOrdinal);
                calls.Add(step);
                if (inLoop && selectedLoop is not null)
                {
                    // Membership is the Method Flow fact, never lexical proximity.
                    var back = selectedLoopBack;
                    if (back is not null)
                    {
                        var memberSteps = calls.Where(item => item.LoopMembershipKey == selectedLoop.Region.Value).ToImmutableArray();
                        loops.Clear();
                        loops.Add(new ScenarioDispatchHandlerLoop(selectedLoop.Region.Value, selectedLoop.Region, selectedLoop.Header!.Value, selectedLoop.Body, selectedLoop.Exits, back.Id, "each item", memberSteps, selectedLoop.Evidence, selectedLoop.Certainty));
                    }
                }
            }
            // One additional level is joined only from a top-level direct source call.
            foreach (var parent in calls.ToArray())
            {
                var nestedFlow = request.Behavior.MethodFlows.SingleOrDefault(item => item.Method == parent.TargetMethod);
                if (nestedFlow is null) continue;
                var nestedInvocations = nestedFlow.Nodes.OfType<InvocationFlowNode>()
                    .OrderBy(node => node.BlockOrdinal)
                    .ThenBy(node => node.EvaluationOrdinal)
                    .ThenBy(node => node.Id.Value, StringComparer.Ordinal)
                    .ToArray();
                for (var nestedOrdinal = 0; nestedOrdinal < nestedInvocations.Length; nestedOrdinal++)
                {
                    var invocation = nestedInvocations[nestedOrdinal];
                    var site = FindCanonicalCallSite(request, nestedFlow, invocation);
                    if (!IsAdmittedDirectLeaf(invocation, site)) continue;
                    var nestedTarget = site!.Resolution.Candidates[0];
                    var nested = MakeStep(parent.TargetMethod, invocation, nestedTarget, null, 1, site.Evidence.Concat(site.Resolution.Evidence).ToImmutableArray(), parent.SourceOrdinal, parent.Id);
                    calls.Add(nested);
                    nestedSteps.Add((nested, nestedOrdinal));
                }
            }
            var returns = flow.Nodes.OfType<ReturnFlowNode>().Where(item => item.Value is not null).ToArray();
            if (returns.Length == 1
                && TryGetEffectiveReturnType(method.ReturnType, out var effectiveReturnType)
                && string.Equals(dispatch.ResponseType, effectiveReturnType, StringComparison.Ordinal))
            {
                returned = new ScenarioDispatchHandlerReturn(returns[0].Value!.Value, ShortType(effectiveReturnType), method.Id, returns[0].Evidence, returns[0].Certainty);
            }
            else if (returns.Length == 1 && dispatch.ResponseType is not null)
            {
                Refuse("SC-DISPATCH-RETURN-MISMATCH", "The dispatch response type does not exactly match the selected compiler handler return type.");
            }
        }
        var topLevelOrder = calls.Where(item => item.ParentDepth == 0)
                .OrderBy(item => item.SourceOrdinal)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToArray();
        var ordered = topLevelOrder
                .SelectMany(parent => new[] { parent }.Concat(
                    nestedSteps.Where(item => item.Step.ParentStepId == parent.Id)
                        .OrderBy(item => item.NestedOrdinal)
                        .ThenBy(item => item.Step.Id, StringComparer.Ordinal)
                        .Select(item => item.Step)))
                .ToImmutableArray();
        var direct = calls.Where(item => item.LoopMembershipKey is null).OrderBy(item => item.ParentDepth).ThenBy(item => item.SourceOrdinal).ThenBy(item => item.Id, StringComparer.Ordinal).ToImmutableArray();
        var participants = Participants(request, candidate, ordered);
        var complete = !HasBlockingDiagnostics(diagnostics);
        return new ScenarioDispatchHandlerExpansion(candidate ?? new DispatchCandidate(new MethodId("method:v1:withheld"), "withheld handler", false, evidence, CertaintyLevel.Conservative), method is null ? "withheld handler" : MethodLabel(request, method), ordered, direct, loops.ToImmutableArray(), returned, complete, diagnostics.ToImmutableArray(), participants, evidence, complete ? CertaintyLevel.Exact : CertaintyLevel.Conservative, Projection(ordered, loops, returned));
    }

    private static bool HasBlockingDiagnostics(IEnumerable<ScenarioGraphDiagnostic> diagnostics)
        => diagnostics.Any(diagnostic => diagnostic.Code is not "SC-DISPATCH-CALL-WITHHELD" and not "SC-DISPATCH-RETURN-MISMATCH");

    private static bool IsAdmittedDirectLeaf(InvocationFlowNode invocation, CallSite? site)
        => invocation.IsSourceBacked
            && !invocation.IsInsideNestedFunction
            && !invocation.IsPlatformTarget
            && invocation.TargetContainingTypeName is not null
            && invocation.TargetMethodName is not null
            && site is not null
            && site.Resolution.Kind == CallResolutionKind.DirectExact
            && site.Resolution.IsComplete
            && site.Resolution.Candidates.Length == 1;

    private static ScenarioDispatchHandlerStep MakeStep(MethodId caller, InvocationFlowNode node, MethodId target, string? loop, int depth, ImmutableArray<EvidenceRef> evidence, int? ordinal = null, string? parentStepId = null)
    {
        if ((depth == 0 && parentStepId is not null) || (depth == 1 && string.IsNullOrWhiteSpace(parentStepId)))
        {
            throw new InvalidOperationException("A dispatch-handler step must carry a parent identity exactly when it is nested.");
        }

        return new($"step:{node.Operation.Value}:{depth}", ordinal ?? 0, depth, caller, target, node.Operation, MethodLabel(node), loop, evidence, CertaintyLevel.Exact, parentStepId, node.TargetContainingTypeName);
    }

    private static CallSite? FindCanonicalCallSite(ScenarioAnalysisRequest request, MethodFlowSnapshot flow, InvocationFlowNode invocation)
    {
        var orderedInvocations = flow.Nodes
            .OfType<InvocationFlowNode>()
            .OrderBy(node => node.Id.Value, StringComparer.Ordinal)
            .ToArray();
        var ordinal = Array.FindIndex(orderedInvocations, node => node.Id == invocation.Id);
        if (ordinal >= 0)
        {
            var id = StableIdentity.CreateCallSiteId(new CallSiteIdentityDescriptor(
                flow.Method, invocation.Operation, ordinal));
            var canonical = request.Behavior.CallGraph.CallSites
                .SingleOrDefault(site => site.ContainingMethod == flow.Method && site.Id == id);
            if (canonical is not null)
            {
                return canonical;
            }
        }

        var fallback = request.Behavior.CallGraph.CallSites
            .Where(site => site.ContainingMethod == flow.Method && site.InvocationOperation == invocation.Operation)
            .ToArray();
        return fallback.Length == 1 ? fallback[0] : null;
    }
    private static string MethodLabel(InvocationFlowNode invocation)
        => $"{ShortType(invocation.TargetContainingTypeName!)}.{invocation.TargetMethodName}";
    private static string MethodLabel(ScenarioAnalysisRequest request, ProgramMethod method)
    {
        var type = request.ProgramIndex.Types.FirstOrDefault(item => item.Id == method.ContainingType)?.MetadataName;
        return string.IsNullOrWhiteSpace(type) ? method.Name : $"{ShortType(type!)}.{method.Name}";
    }
    private static string ShortType(string type) => type[(type.LastIndexOf('.') + 1)..].Replace("`1", "", StringComparison.Ordinal);
    private static bool TryGetEffectiveReturnType(string declaredType, out string effectiveType)
    {
        effectiveType = declaredType;
        if (string.IsNullOrWhiteSpace(declaredType)) return false;
        var open = declaredType.IndexOf('<');
        if (open < 0) return !declaredType.Contains('>');
        if (!TryGetSingleGenericArgument(declaredType, open, out var argument)) return false;
        var outer = declaredType[..open];
        if (outer is not "System.Threading.Tasks.Task" and not "System.Threading.Tasks.ValueTask") return false;
        effectiveType = argument;
        return true;
    }

    private static bool TryGetSingleGenericArgument(string type, int open, out string argument)
    {
        argument = string.Empty;
        if (open <= 0 || !type.EndsWith('>')) return false;
        var depth = 0;
        var close = -1;
        for (var index = open; index < type.Length; index++)
        {
            if (type[index] == '<') depth++;
            else if (type[index] == '>')
            {
                depth--;
                if (depth < 0) return false;
                if (depth == 0)
                {
                    close = index;
                    if (index != type.Length - 1) return false;
                }
            }
            else if (type[index] == ',' && depth == 1) return false;
        }
        if (depth != 0 || close <= open + 1) return false;
        argument = type[(open + 1)..close];
        return !string.IsNullOrWhiteSpace(argument);
    }

    private static ImmutableArray<ScenarioDispatchParticipant> Participants(ScenarioAnalysisRequest request, DispatchCandidate? candidate, IEnumerable<ScenarioDispatchHandlerStep> steps)
    {
        var result = new List<ScenarioDispatchParticipant> { new("request", "Request"), new("dispatch", "Dispatcher"), new("handler", candidate is null ? "Handler" : "Handler") };
        var groups = steps
            .Where(step => !string.IsNullOrWhiteSpace(step.TargetParticipantIdentity))
            .GroupBy(step => step.TargetParticipantIdentity!, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
        foreach (var group in groups)
        {
            var shortType = ShortType(group.Key);
            var collides = groups.Count(other => ShortType(other.Key) == shortType) > 1;
            var label = collides ? MinimalQualifiedType(group.Key, groups.Select(item => item.Key)) : shortType;
            var key = MermaidSafe(collides ? label : shortType).ToLowerInvariant();
            result.Add(new ScenarioDispatchParticipant(key, label, group.Key));
        }
        foreach (var group in steps.Where(step => step.TargetParticipantIdentity is null).GroupBy(step => ShortType(step.Label.Split('.')[0]), StringComparer.Ordinal))
        {
            if (groups.Any(item => ShortType(item.Key) == group.Key) || group.Count() != 1) continue;
            var key = MermaidSafe(group.Key).ToLowerInvariant();
            if (!result.Any(item => item.Key == key)) result.Add(new ScenarioDispatchParticipant(key, group.Key));
        }
        return result.ToImmutableArray();
    }
    private static string MinimalQualifiedType(string identity, IEnumerable<string> identities)
    {
        var parts = identity.Split('.');
        var others = identities.Where(item => item != identity).Select(item => item.Split('.')).ToArray();
        for (var take = 2; take <= parts.Length; take++)
        {
            var suffix = string.Join('.', parts[^take..]);
            if (others.All(other => !suffix.Equals(string.Join('.', other[^Math.Min(take, other.Length)..]), StringComparison.Ordinal))) return suffix;
        }
        return identity;
    }
    private static string MermaidSafe(string value)
        => new(value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
    private static string Projection(IEnumerable<ScenarioDispatchHandlerStep> steps, IEnumerable<ScenarioDispatchHandlerLoop> loops, ScenarioDispatchHandlerReturn? returned)
    {
        var lines = steps.Select(step => $"step {step.Id} ordinal={step.SourceOrdinal} depth={step.ParentDepth} parent={step.ParentStepId ?? string.Empty} caller={step.CallerMethod.Value} callee={step.TargetMethod.Value} operation={step.Operation.Value} label={step.Label} targetIdentity={step.TargetParticipantIdentity ?? string.Empty} loop={step.LoopMembershipKey ?? string.Empty}").ToList();
        lines.AddRange(loops.Select(loop => $"loop {loop.Key} members={string.Join(',', loop.MemberSteps.Select(step => step.Id))}"));
        if (returned is not null) lines.Add($"return operation={returned.Operation.Value} type={returned.TypeName}");
        return string.Join('\n', lines);
    }
}
#pragma warning restore IDE0011
