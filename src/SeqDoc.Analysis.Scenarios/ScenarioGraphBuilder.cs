using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
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

    public static ScenarioGraphSet Build(ScenarioAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var frameworkEntries = request.FrameworkFacts.Facts
            .Where(fact => fact is HttpEntryPointFact or MinimalApiRouteFact)
            .Select(fact => fact is MinimalApiRouteFact minimal
                ? new NormalizedEntry(minimal.EntryPointId, minimal.HandlerRoot, minimal.HttpMethod, minimal.CanonicalRoute, minimal.OperationKey, ScenarioActionKind.MinimalApiHandler, minimal.Evidence)
                : new NormalizedEntry(((HttpEntryPointFact)fact).EntryPointId, ((HttpEntryPointFact)fact).RootMethod, ((HttpEntryPointFact)fact).HttpMethod, ((HttpEntryPointFact)fact).CanonicalRoute, ((HttpEntryPointFact)fact).OperationKey, ScenarioActionKind.ControllerAction, fact.Evidence))
            .ToArray();
        var serviceUnsupportedDispatchDiagnostics = new List<AnalysisDiagnostic>();
        var serviceOperationEntries = FrameworkFactsBound(request)
            ? BuildServiceOperationEntries(request, serviceUnsupportedDispatchDiagnostics)
            : [];
        var workerEntries = request.FrameworkFacts.Facts
            .OfType<HostedWorkerLifecycleFact>()
            .Where(fact => FrameworkFactsBound(request))
            .Join(
                request.FrameworkFacts.Facts.OfType<HostedWorkerRegistrationFact>(),
                fact => fact.HostedType,
                registration => registration.HostedType,
                (fact, registration) => new NormalizedEntry(
                    fact.EntryPointId,
                    fact.RootMethod,
                    HttpMethodKind.Unknown,
                    string.Empty,
                    $"Hosted worker {fact.HostedTypeName}",
                    ScenarioActionKind.HostedWorker,
                    Combine(fact.Evidence, registration.Evidence),
                    fact))
            .ToArray();
        var admittedMethods = frameworkEntries.Select(entry => entry.RootMethod)
            .Concat(workerEntries.Select(entry => entry.RootMethod))
            .Concat(serviceOperationEntries.Select(entry => entry.RootMethod))
            .ToHashSet();
        var configuredEntries = (request.ConfiguredRoots.IsDefault ? [] : request.ConfiguredRoots)
            .Where(method => !admittedMethods.Contains(method))
            .OrderBy(method => method.Value, StringComparer.Ordinal)
            .Select(method => new NormalizedEntry(
                StableIdentity.CreateConfiguredMethodEntryPointId(new ConfiguredMethodEntryPointIdentityDescriptor(request.Profile.Id, method)),
                 method, HttpMethodKind.Unknown, string.Empty,
                 ConfiguredDisplaySignature(request.ProgramIndex, method),
                ScenarioActionKind.ConfiguredMethod,
                 request.ProgramIndex.Methods.First(item => item.Id == method).Evidence,
                 null))
            .ToArray();
        var graphs = frameworkEntries
            .OrderBy(fact => fact.EntryPointId.Value, StringComparer.Ordinal)
            .Select(fact => BuildGraph(request, fact, cancellationToken))
            .Concat(workerEntries
                .OrderBy(fact => fact.EntryPointId.Value, StringComparer.Ordinal)
                .Select(fact => BuildGraph(request, fact, cancellationToken)))
            .Concat(serviceOperationEntries
                .OrderBy(fact => fact.EntryPointId.Value, StringComparer.Ordinal)
                .Select(fact => BuildGraph(request, fact, cancellationToken)))
            .Concat(configuredEntries.Select(entry => BuildGraph(request, entry, cancellationToken)))
            .OrderBy(graph => graph.EntryPoint.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        var debugProjection = BuildSetDebugProjection(request, graphs);
        return new ScenarioGraphSet(
            1,
            ProducerVersion,
            request.Profile,
            request.ProgramIndex.IndexFingerprint,
            graphs,
            serviceUnsupportedDispatchDiagnostics
                .OrderBy(diagnostic => diagnostic.Id.Value, StringComparer.Ordinal)
                .ToImmutableArray(),
            debugProjection);
    }

    /// <summary>
    /// Joins each compiler-proven <see cref="ServiceOperationCapabilityFact"/> with a matching
    /// <see cref="ServiceEndpointRegistrationFact"/> by exact (implementation type, service contract
    /// type). Attribute/implementation/body evidence proves capability only, never hosting or
    /// dispatch; a capability without a matching registration never admits an executable root — it
    /// contributes a conservative unsupported-dispatch diagnostic instead. A matched pair's combined
    /// evidence is the union of both facts' evidence, and the combined certainty is the weaker
    /// (higher-ordinal) of the two, so a Conservative registration can never let an Exact capability
    /// claim a stronger overall root.
    /// </summary>
    private static NormalizedEntry[] BuildServiceOperationEntries(
        ScenarioAnalysisRequest request, List<AnalysisDiagnostic> unsupportedDispatchDiagnostics)
    {
        var registrations = request.FrameworkFacts.Facts.OfType<ServiceEndpointRegistrationFact>()
            .OrderBy(fact => fact.Id.Value, StringComparer.Ordinal)
            .ToArray();
        var entries = new List<NormalizedEntry>();
        foreach (var capability in request.FrameworkFacts.Facts.OfType<ServiceOperationCapabilityFact>()
                     .OrderBy(fact => fact.Id.Value, StringComparer.Ordinal))
        {
            var matches = registrations
                .Where(candidate =>
                    candidate.ImplementationTypeSymbol == capability.ImplementationTypeSymbol
                    && candidate.ServiceContractTypeSymbol == capability.ServiceContractTypeSymbol)
                .ToArray();
            if (matches.Length == 0)
            {
                unsupportedDispatchDiagnostics.Add(CreateServiceUnsupportedDispatchDiagnostic(request.Profile.Id, capability));
                continue;
            }

            var combinedEvidence = capability.Evidence
                .Concat(matches.SelectMany(match => match.Evidence))
                .DistinctBy(item => item.Id.Value)
                .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
                .ToImmutableArray();
            var combinedCertainty = matches.Aggregate(
                capability.Certainty,
                (weakest, match) => match.Certainty > weakest ? match.Certainty : weakest);
            var entryPointId = StableIdentity.CreateServiceOperationEntryPointId(
                new ServiceOperationEntryPointIdentityDescriptor(request.Profile.Id, capability.RootMethod, capability.OperationKey));
            var entryFact = new ServiceOperationEntryPointFact
            {
                Id = capability.Id,
                EntryPointId = entryPointId,
                RootMethod = capability.RootMethod,
                ServiceContractType = capability.ServiceContractType,
                ServiceContractTypeSymbol = capability.ServiceContractTypeSymbol,
                ImplementationType = capability.ImplementationType,
                ImplementationTypeSymbol = capability.ImplementationTypeSymbol,
                OperationName = capability.OperationName,
                OperationKey = capability.OperationKey,
                Evidence = combinedEvidence,
                Certainty = combinedCertainty,
            };
            entries.Add(new NormalizedEntry(
                entryPointId, capability.RootMethod, HttpMethodKind.Unknown, string.Empty, capability.OperationKey,
                ScenarioActionKind.ServiceOperation, combinedEvidence, ServiceOperation: entryFact));
        }

        return entries.ToArray();
    }

    private const string ServiceUnsupportedDispatchCode = "SC-SERVICE-UNSUPPORTED-DISPATCH";

    private static AnalysisDiagnostic CreateServiceUnsupportedDispatchDiagnostic(
        CompilationProfileId profileId, ServiceOperationCapabilityFact capability)
    {
        var subject = $"{capability.RootMethod.Value}{capability.OperationKey}";
        var certainty = capability.Evidence.IsDefaultOrEmpty
            ? capability.Certainty
            : capability.Evidence.Aggregate(capability.Certainty, (weakest, item) => item.Certainty > weakest ? item.Certainty : weakest);
        return new AnalysisDiagnostic(
            StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
                ServiceUnsupportedDispatchCode, AnalysisStage.FrameworkModel, profileId, subject, Ordinal: 0)),
            ServiceUnsupportedDispatchCode,
            DiagnosticSeverity.Warning,
            AnalysisStage.FrameworkModel,
            "A compiler-proven service contract operation has no matching host-chain-proven endpoint registration.",
            new DiagnosticLocation("core wcf service operation", profileId),
            $"'{capability.ImplementationType}' proves the compiler-shape [ServiceContract]/[OperationContract] capability for '{capability.OperationKey}' with a real source body, but no exact IServiceBuilder.AddServiceEndpoint<{capability.ImplementationType}, {capability.ServiceContractType}>(Binding, string) call reachable through a proven active host chain was found.",
            "No service operation entry point or execution wording was emitted for the unregistered or host-unreachable capability.",
            "Register the implementation through the exact active host chain (generic-host construction, UseStartup<TStartup>, the selected Configure/UseServiceModel callback, and a matching AddService<TService>().AddServiceEndpoint<TService,TContract>(Binding,string) call), or remove the unused capability.",
            certainty,
            evidence: capability.Evidence,
            internalDetail: subject);
    }

    private sealed record NormalizedEntry(
        EntryPointId EntryPointId,
        MethodId RootMethod,
        HttpMethodKind HttpMethod,
        string CanonicalRoute,
        string OperationKey,
        ScenarioActionKind ActionKind,
        ImmutableArray<EvidenceRef> Evidence,
        HostedWorkerLifecycleFact? HostedWorker = null,
        ServiceOperationEntryPointFact? ServiceOperation = null)
    {
        public ScenarioRootKind RootKind => ActionKind switch
        {
            ScenarioActionKind.ConfiguredMethod => ScenarioRootKind.ConfiguredMethod,
            ScenarioActionKind.HostedWorker => ScenarioRootKind.HostedWorker,
            ScenarioActionKind.ServiceOperation => ScenarioRootKind.ServiceOperation,
            _ => ScenarioRootKind.HttpEntryPoint,
        };
    }

    private static bool FrameworkFactsBound(ScenarioAnalysisRequest request)
        => request.FrameworkFacts.ProfileId is not null
            && request.FrameworkFacts.ProfileId == request.Profile.Id
            && !string.IsNullOrWhiteSpace(request.FrameworkFacts.ProgramIndexFingerprint)
            && string.Equals(
                request.FrameworkFacts.ProgramIndexFingerprint,
                request.ProgramIndex.IndexFingerprint,
                StringComparison.Ordinal);

    private static bool BehaviorSnapshotBound(ScenarioAnalysisRequest request)
        => request.Behavior.Profile is { } profile
            && profile.Id == request.Profile.Id
            && !string.IsNullOrWhiteSpace(request.Behavior.ProgramIndexFingerprint)
            && string.Equals(request.Behavior.ProgramIndexFingerprint,
                request.ProgramIndex.IndexFingerprint, StringComparison.Ordinal);

    private static bool NonGetFactsBound(ScenarioAnalysisRequest request)
        => request.NonGetSemanticFacts?.Profile is { } profile
            && profile.Id == request.Profile.Id
            && !string.IsNullOrWhiteSpace(request.NonGetSemanticFacts.ProgramIndexFingerprint)
            && string.Equals(
                request.NonGetSemanticFacts.ProgramIndexFingerprint,
                request.ProgramIndex.IndexFingerprint,
                StringComparison.Ordinal);

    private static ScenarioGraph BuildGraph(ScenarioAnalysisRequest request, NormalizedEntry entryPoint, CancellationToken cancellationToken = default)
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
            : entryPoint.ActionKind == ScenarioActionKind.HostedWorker
            ? HostedWorkerPresentation(request.ProgramIndex, entryPoint.HostedWorker!)
            : entryPoint.ActionKind == ScenarioActionKind.MinimalApiHandler
            ? MinimalApiActionPresentation(request.ProgramIndex, entryPoint.RootMethod)
            : entryPoint.ActionKind == ScenarioActionKind.ServiceOperation
            ? ServiceOperationPresentation(entryPoint.ServiceOperation!)
            : ControllerActionPresentation(request.ProgramIndex, entryPoint.RootMethod);
        var actionNode = CreateNodeWithPresentation(
            profileId,
            entryPointId,
            ScenarioNodeKind.Action,
            $"action:{entryPoint.RootMethod.Value}",
            entryPoint.RootMethod,
            null,
                 entryPoint.ActionKind == ScenarioActionKind.ConfiguredMethod ? "configured method" : entryPoint.ActionKind == ScenarioActionKind.HostedWorker ? "hosted worker lifecycle" : entryPoint.ActionKind == ScenarioActionKind.MinimalApiHandler ? "minimal API handler" : entryPoint.ActionKind == ScenarioActionKind.ServiceOperation ? "CoreWCF service operation" : "controller action",
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
        JoinEdmxMetadata(request, profileId, entryPointId, entryPoint.RootMethod, actionNode, nodes, edges);

        if (entryPoint.ActionKind == ScenarioActionKind.HostedWorker)
        {
            AddHostedWorkerLifecycle(request, entryPoint, actionNode, nodes, edges, diagnostics);
            var callbackPlacements = new List<ScenarioFlowPlacement>();
            if (BehaviorSnapshotBound(request))
            {
                AddHostedWorkerCallbackMembers(request, entryPoint, actionNode, nodes, edges, diagnostics, callbackPlacements);
            }
            var hostedTopology = BuildHostedWorkerTopology(request, entryPoint, actionNode, nodes, edges, diagnostics, callbackPlacements);
            return FinalizeGraph(request, entryPoint, profileId, nodes, edges, diagnostics, hostedTopology);
        }

        if (entryPoint.ActionKind == ScenarioActionKind.ConfiguredMethod)
        {
            var method = request.ProgramIndex.Methods.Single(item => item.Id == entryPoint.RootMethod);
            if (request.Behavior.Profile is not { } behaviorProfile
                || behaviorProfile.Id != request.Profile.Id
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

            // Mirror the HTTP controller-action (SC001) path: operations independently proven as a
            // service-client invocation whose caller is this configured root are presented by
            // AddServiceClientInvocations with protocol-neutral wording, so they are excluded from
            // the depth-1 generic MethodCall nodes AddConfiguredDirectCalls builds to avoid emitting
            // two nodes for one call site. Root-local only (CallerMethod == RootMethod), identical to
            // the HTTP path; a client call inside a callee stays a generic MethodCall.
            var configuredClientInvocationOperations = FrameworkFactsBound(request)
                ? request.FrameworkFacts.Facts.OfType<ServiceClientInvocationFact>()
                    .Where(fact => fact.CallerMethod == entryPoint.RootMethod)
                    .Select(fact => fact.InvocationOperation)
                    .ToHashSet()
                : new HashSet<OperationId>();
            var directExpansion = AddConfiguredDirectCalls(request, entryPoint, profileId, actionNode, nodes, edges, diagnostics,
                configuredClientInvocationOperations);
            AddServiceClientInvocations(request, entryPoint, profileId, actionNode, nodes, edges, diagnostics);
            AddOutboundHttpRequests(request, entryPoint, profileId, actionNode, nodes, edges, diagnostics, cancellationToken);

            JoinEntityQueries(request, profileId, entryPointId, entryPoint.RootMethod, actionNode, nodes, edges, diagnostics);
            JoinStateAssignments(request, profileId, entryPointId, entryPoint.RootMethod, actionNode, nodes, edges);
            JoinEntityMutations(request, profileId, entryPointId, entryPoint.RootMethod, actionNode, nodes, edges);
            JoinSourceObservations(
                request,
                profileId,
                entryPointId,
                entryPoint.RootMethod,
                entryPoint.RootMethod,
                actionNode,
                actionNode,
                nodes,
                edges);

            var topologyNodes = nodes.Where(node => node.Kind != ScenarioNodeKind.MethodCall
                || directExpansion.Steps.Any(step => step.ScenarioNodeId == node.Id && step.Depth == 1)).ToImmutableArray();
            var withheldPersistenceAssignments = new HashSet<ScenarioNodeId>();
            var configuredTopology = BuildTopology(request, profileId, entryPointId, entryPoint, entryPoint.RootMethod,
                topologyNodes, diagnostics, withheldPersistenceAssignments);
            RemoveWithheldPersistenceAssignments(nodes, edges, ref configuredTopology, withheldPersistenceAssignments);
            directExpansion = InheritDirectCallMembership(configuredTopology, directExpansion);
            configuredTopology = AddDirectCallMemberships(configuredTopology, directExpansion, entryPoint.RootMethod, profileId);
            var calleeTopology = ComposeCalleeOccurrenceTopology(
                request, profileId, entryPointId, entryPoint, directExpansion, configuredTopology, diagnostics);
            configuredTopology = calleeTopology.Topology;
            if (!calleeTopology.WithheldOccurrenceIds.IsEmpty)
            {
                var parentByOccurrence = directExpansion.Steps
                    .ToDictionary(step => step.Id, step => step.ParentStepId, StringComparer.Ordinal);
                var withheldOccurrences = calleeTopology.WithheldOccurrenceIds
                    .ToHashSet(StringComparer.Ordinal);
                // Compute the complete transitive closure once. The same occurrence set is the
                // authority for expansion, graph nodes/edges, and every occurrence-scoped topology
                // claim; no consumer may accidentally use only the initially diagnosed occurrences.
                foreach (var step in directExpansion.Steps)
                {
                    if (IsWithheldOccurrence(step, withheldOccurrences, parentByOccurrence))
                    {
                        withheldOccurrences.Add(step.Id);
                    }
                }
                var withheldSteps = directExpansion.Steps
                    .Where(step => withheldOccurrences.Contains(step.Id))
                    .Select(step => step.Id)
                    .ToHashSet(StringComparer.Ordinal);
                var withheldNodes = directExpansion.Steps
                    .Where(step => withheldSteps.Contains(step.Id))
                    .Select(step => step.ScenarioNodeId)
                    .ToHashSet();
                nodes.RemoveAll(node => withheldNodes.Contains(node.Id));
                edges.RemoveAll(edge => withheldNodes.Contains(edge.Source) || withheldNodes.Contains(edge.Target));
                directExpansion = directExpansion with
                {
                    Steps = directExpansion.Steps
                        .Where(step => !withheldSteps.Contains(step.Id))
                        .ToImmutableArray()
                };
                configuredTopology = RemoveWithheldOccurrenceTopology(configuredTopology, withheldOccurrences, withheldNodes);
            }
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
            var clientInvocationOperations = FrameworkFactsBound(request)
                ? request.FrameworkFacts.Facts.OfType<ServiceClientInvocationFact>()
                    .Where(fact => fact.CallerMethod == entryPoint.RootMethod)
                    .Select(fact => fact.InvocationOperation)
                    .ToHashSet()
                : new HashSet<OperationId>();
            AddRootDirectCalls(request, entryPoint, profileId, actionNode, nodes, edges, clientInvocationOperations);
            AddServiceClientInvocations(request, entryPoint, profileId, actionNode, nodes, edges, diagnostics);
            AddOutboundHttpRequests(request, entryPoint, profileId, actionNode, nodes, edges, diagnostics, cancellationToken);
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

        var rootWithheldPersistenceAssignments = new HashSet<ScenarioNodeId>();
        AddOutboundHttpRequests(request, entryPoint, profileId, actionNode, nodes, edges, diagnostics, cancellationToken);
        var topology = BuildTopology(
            request,
            profileId,
            entryPointId,
            entryPoint,
            resolution.ServiceMethod,
            nodes.ToImmutableArray(),
            diagnostics,
            rootWithheldPersistenceAssignments);
        RemoveWithheldPersistenceAssignments(nodes, edges, ref topology, rootWithheldPersistenceAssignments);
        return FinalizeGraph(request, entryPoint, profileId, nodes, edges, diagnostics, topology);
    }

    private static void AddHostedWorkerCallbackMembers(
        ScenarioAnalysisRequest request,
        NormalizedEntry entry,
        ScenarioNode actionNode,
        List<ScenarioNode> nodes,
        List<ScenarioEdge> edges,
        List<ScenarioGraphDiagnostic> diagnostics,
        List<ScenarioFlowPlacement> callbackPlacements)
    {
        var facts = request.CallbackBoundaryFacts;
        if (!BehaviorSnapshotBound(request) || facts is null || facts.Profile.Id != request.Profile.Id
            || !string.Equals(facts.ProgramIndexFingerprint, request.ProgramIndex.IndexFingerprint, StringComparison.Ordinal))
        {
            return;
        }

        var method = request.ProgramIndex.Methods.SingleOrDefault(item => item.Id == entry.RootMethod);
        var flow = request.Behavior.MethodFlows.SingleOrDefault(item => item.Method == entry.RootMethod);
        if (method is null || flow is null)
        {
            return;
        }

        foreach (var boundary in facts.Boundaries.Where(item => item.CallerMethod == entry.RootMethod)
                     .OrderBy(item => item.Id.Value, StringComparer.Ordinal))
        {
            // The outer operation is the only placement authority. A callback member is never
            // treated as an operation of the worker method: its node owns the callback boundary's
            // identity, while its flow placement reuses the exact caller block/container chain.
            var outer = flow.Nodes.OfType<InvocationFlowNode>()
                .Where(item => item.Operation == boundary.OuterInvocationOperation)
                .ToArray();
            if (outer.Length != 1)
            {
                var outerEvidence = Combine(
                    boundary.Evidence,
                    outer.SelectMany(candidate => candidate.Evidence).ToImmutableArray());
                foreach (var memberOperation in boundary.MemberOperations.Order(StringComparer.Ordinal))
                {
                    diagnosticsForCallbackPlacement(request, entry, boundary, memberOperation, outerEvidence);
                }
                continue;
            }

            var matchingLoopNodes = flow.Nodes.OfType<LoopNode>()
                .Where(loop => loop.HeaderBlockOrdinal == outer[0].BlockOrdinal
                    || loop.BodyBlockOrdinals.Contains(outer[0].BlockOrdinal))
                .ToArray();
            var loopRegions = matchingLoopNodes
                .Select(loop => loop.Region)
                .Distinct()
                .ToArray();
            var enclosingRegions = flow.Regions
                    .Where(region => region.StartBlockOrdinal is { } start
                        && region.EndBlockOrdinal is { } end
                        && start <= outer[0].BlockOrdinal && outer[0].BlockOrdinal <= end
                        && region.Kind is not FlowRegionKind.Root and not FlowRegionKind.NaturalLoop)
                    .OrderBy(region => region.StartBlockOrdinal)
                    .ToArray();
            var placement = loopRegions
                .Concat(enclosingRegions.Select(region => region.Id))
                .Distinct()
                .ToImmutableArray();
            var supportedPlacement = loopRegions.Length == 1
                && enclosingRegions.All(region => region.Kind is FlowRegionKind.Try or FlowRegionKind.TryAndCatch)
                && enclosingRegions.Any(region => region.Kind is FlowRegionKind.Try or FlowRegionKind.TryAndCatch);
            var ordinal = 0;
            var supportedPresentation = boundary.Cardinality == CallbackCardinality.ExactlyOnce
                && boundary.Trigger == CallbackTriggerKind.Unconditional
                && boundary.TriggerCondition is null;
            if (!supportedPresentation || !supportedPlacement)
            {
                var placementEvidence = Combine(
                    boundary.Evidence,
                    outer[0].Evidence,
                    matchingLoopNodes.SelectMany(loop => loop.Evidence).ToImmutableArray(),
                    enclosingRegions.SelectMany(region => region.Evidence).ToImmutableArray());
                foreach (var memberOperation in boundary.MemberOperations.Order(StringComparer.Ordinal))
                {
                    diagnosticsForCallbackPlacement(request, entry, boundary, memberOperation, placementEvidence,
                        supportedPresentation
                            ? "callback placement was missing or unsupported"
                            : "unsupported exact hosted callback presentation: only ExactlyOnce unconditional callbacks are currently representable");
                }
                continue;
            }
            var overlappingNodes = boundary.MemberOperations
                .SelectMany(memberOperation => nodes.Where(node => node.Operation?.Value == memberOperation
                    && !node.Key.StartsWith("callback:", StringComparison.Ordinal)))
                .DistinctBy(node => node.Id)
                .ToArray();
            if (overlappingNodes.Length > 0
                || boundary.MemberOperations.Contains(boundary.OuterInvocationOperation.Value, StringComparer.Ordinal))
            {
                var overlapEvidence = Combine(boundary.Evidence, outer[0].Evidence,
                    overlappingNodes.SelectMany(node => node.Evidence).ToImmutableArray());
                foreach (var memberOperation in boundary.MemberOperations.Order(StringComparer.Ordinal))
                {
                    diagnosticsForCallbackPlacement(request, entry, boundary, memberOperation, overlapEvidence,
                        "callback presentation was withheld because member ownership overlaps outer worker work",
                        "SC-CALLBACK-OUTER-OVERLAP");
                }
                continue;
            }
            foreach (var memberOperation in boundary.MemberOperations.Order(StringComparer.Ordinal))
            {
                if (nodes.Any(node => node.Key == $"callback:{boundary.Id.Value}:{memberOperation}"))
                {
                    continue;
                }

                var evidence = boundary.Evidence;
                var certainty = boundary.Certainty;
                var presentation = new ScenarioNodePresentation();
                var invocation = request.ProgramIndex.Invocations
                    .Where(item => item.Id.Value == memberOperation)
                    .ToArray();
                if (invocation.Length == 1 && invocation[0].BoundTarget is { } boundTarget)
                {
                    var targetMethods = request.ProgramIndex.Methods
                        .Where(item => item.Id == boundTarget)
                        .ToArray();
                    if (targetMethods.Length == 1)
                    {
                        var targetTypes = request.ProgramIndex.Types
                            .Where(item => item.Id == targetMethods[0].ContainingType)
                            .ToArray();
                        presentation = new ScenarioNodePresentation(
                            TargetContainingTypeName: targetTypes.Length == 1 ? targetTypes[0].MetadataName : null,
                            TargetMemberName: targetMethods[0].Name);
                        evidence = Combine(evidence, invocation[0].Evidence);
                        certainty = LeastConfident(certainty, invocation[0].Evidence, invocation[0].Certainty);
                    }
                }
                var node = CreateNodeWithPresentation(
                    request.Profile.Id,
                    entry.EntryPointId,
                    ScenarioNodeKind.MethodCall,
                    $"callback:{boundary.Id.Value}:{memberOperation}",
                    boundary.CallerMethod,
                    new OperationId(memberOperation),
                    "source callback operation",
                    presentation,
                    evidence,
                    certainty,
                    outer[0].BlockOrdinal * 1000 + ordinal++);
                nodes.Add(node);
                edges.Add(CreateEdge(request.Profile.Id, entry.EntryPointId, actionNode, node,
                    ScenarioEdgeKind.Call, "source callback operation", evidence, certainty, ordinal));
                // Callback-local operations are placed in the caller's proven control containers,
                // but retain the caller method only as the graph owner; no callback operation is
                // added to the worker Method Flow or given an outer terminal.
                if (!placement.IsDefaultOrEmpty)
                {
                    callbackPlacements.Add(new ScenarioFlowPlacement(
                        node.Id,
                        boundary.CallerMethod,
                        null,
                        placement,
                        [],
                        evidence,
                        certainty));
                }
            }
        }

        void diagnosticsForCallbackPlacement(ScenarioAnalysisRequest currentRequest, NormalizedEntry currentEntry,
            CallbackBoundaryFact currentBoundary, string memberOperation, ImmutableArray<EvidenceRef> evidence,
            string? reason = null, string code = "SC-WORKER-UNSUPPORTED-PLACEMENT")
        {
            // This local helper is called before a callback node is created, so the member identity
            // must be carried directly in the diagnostic rather than inferred by a later layer.
            var detail = $"callback-boundary={currentBoundary.Id.Value}; member-operation={memberOperation}; "
                + $"member-node=callback:{currentBoundary.Id.Value}:{memberOperation}; {reason ?? "callback placement was missing or unsupported"}.";
            diagnostics.Add(CreateDiagnostic(currentRequest.Profile.Id, currentEntry.EntryPointId,
                code,
                code == "SC-CALLBACK-OUTER-OVERLAP"
                    ? "A hosted-worker callback boundary was withheld because callback ownership overlapped outer work."
                    : "A hosted-worker callback member was withheld because its exact placement was not representable.",
                detail, evidence, CertaintyLevel.Conservative));
        }

    }

    private static void AddRootDirectCalls(
        ScenarioAnalysisRequest request,
        NormalizedEntry entryPoint,
        CompilationProfileId profileId,
        ScenarioNode actionNode,
        List<ScenarioNode> nodes,
        List<ScenarioEdge> edges,
        HashSet<OperationId>? excludedOperations = null)
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
                && !string.IsNullOrWhiteSpace(invocation.TargetMethodName)
                // Operations independently admitted as a service-client invocation are presented by
                // AddServiceClientInvocations with protocol-neutral wording instead of as a generic
                // MethodCall node, so they are excluded here to avoid emitting two nodes for one call site.
                && (excludedOperations is null || !excludedOperations.Contains(invocation.Operation)))
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

    private const string ClientUnsupportedInvocationCode = "SC-CLIENT-UNSUPPORTED-INVOCATION";
    private const string ClientConflictingBoundaryCode = "SC-CLIENT-CONFLICTING-BOUNDARY";
    private const string ClientConflictingInvocationCode = "SC-CLIENT-CONFLICTING-INVOCATION";

    /// <summary>
    /// Joins each compiler-proven <see cref="ServiceClientInvocationFact"/> reachable as a root-level
    /// direct call from <paramref name="entryPoint"/>'s own method with a matching
    /// <see cref="ServiceClientBoundaryFact"/> (exact client type and service contract type, classified
    /// <see cref="ServiceClientKind.SourceClient"/> or <see cref="ServiceClientKind.GeneratedClient"/> —
    /// never <see cref="ServiceClientKind.Unknown"/>) and an optional matching
    /// <see cref="ServiceFaultContractFact"/> (exact operation symbol). Mirrors
    /// <see cref="BuildServiceOperationEntries"/>'s capability/registration join pattern: the invocation
    /// alone never proves the receiver's client classification, so a proven invocation without a
    /// matching admitted boundary contributes a conservative unsupported-invocation diagnostic instead
    /// of a node, exactly like an unregistered service capability. Only invocations that also pass the
    /// same exact/source-backed/<see cref="CallResolutionKind.DirectExact"/> admission
    /// <see cref="DirectCalls"/> already applies to ordinary direct calls are considered, so a
    /// disconnected/unreachable call site never admits a node here either. The resulting node replaces
    /// the generic <see cref="ScenarioNodeKind.MethodCall"/> node <see cref="AddRootDirectCalls"/> would
    /// otherwise build for the same call site (see its <c>excludedOperations</c> parameter).
    /// </summary>
    private static void AddServiceClientInvocations(
        ScenarioAnalysisRequest request,
        NormalizedEntry entryPoint,
        CompilationProfileId profileId,
        ScenarioNode actionNode,
        List<ScenarioNode> nodes,
        List<ScenarioEdge> edges,
        List<ScenarioGraphDiagnostic> diagnostics)
    {
        if (!FrameworkFactsBound(request))
        {
            return;
        }

        var invocationFacts = request.FrameworkFacts.Facts.OfType<ServiceClientInvocationFact>()
            .Where(fact => fact.CallerMethod == entryPoint.RootMethod)
            .ToArray();
        if (invocationFacts.Length == 0)
        {
            return;
        }

        var directCalls = DirectCalls(request, entryPoint.RootMethod)
            .ToDictionary(item => item.Invocation.Operation, item => item);
        var clientBoundaries = request.FrameworkFacts.Facts.OfType<ServiceClientBoundaryFact>().ToArray();
        var faultFacts = request.FrameworkFacts.Facts.OfType<ServiceFaultContractFact>().ToArray();

        // Grouped by InvocationOperation (the real identity of a compiler call site): if two or more
        // ServiceClientInvocationFacts ever land on the same call site (duplicate emission or a genuine
        // conflicting re-analysis), exactly one node is admitted per call site rather than one per fact.
        // A group whose facts disagree on any field material to the node's identity or presentation is
        // withheld and reported via ClientConflictingInvocationCode instead of arbitrarily picking one —
        // the same fail-closed posture ClientConflictingBoundaryCode below applies to a disagreeing
        // client-kind boundary set.
        var admitted = invocationFacts
            .Where(fact => directCalls.ContainsKey(fact.InvocationOperation))
            .GroupBy(fact => fact.InvocationOperation)
            .Select(group => (
                Operation: group.Key,
                Facts: group.OrderBy(f => f.Id.Value, StringComparer.Ordinal).ToArray(),
                Call: directCalls[group.Key]))
            .OrderBy(item => item.Call.Invocation.BlockOrdinal)
            .ThenBy(item => item.Call.Invocation.EvaluationOrdinal)
            .ThenBy(item => item.Operation.Value, StringComparer.Ordinal)
            .ToArray();

        for (var ordinal = 0; ordinal < admitted.Length; ordinal++)
        {
            var facts = admitted[ordinal].Facts;
            var call = admitted[ordinal].Call;

            if (facts.Length > 1 && !ClientInvocationFactsAgree(facts))
            {
                var first = facts[0];
                diagnostics.Add(CreateDiagnostic(
                    profileId,
                    entryPoint.EntryPointId,
                    ClientConflictingInvocationCode,
                    "Multiple compiler-proven service-client invocation facts disagree for the same call site.",
                    $"Call site '{first.InvocationOperation.Value}' has {facts.Length} admitted ServiceClientInvocationFacts that disagree on operation, contract, client, or result-claim shape; no single coherent invocation can be admitted for this call site.",
                    Combine(facts.Select(f => f.Evidence).ToArray()),
                    facts.Select(f => f.Certainty).Max()));
                continue;
            }

            var fact = facts[0];

            var boundaries = clientBoundaries
                .Where(boundary => boundary.ClientTypeSymbol == fact.ClientTypeSymbol
                    && boundary.ServiceContractTypeSymbol == fact.ServiceContractTypeSymbol
                    && boundary.ClientKind is ServiceClientKind.SourceClient or ServiceClientKind.GeneratedClient)
                .OrderBy(boundary => boundary.Id.Value, StringComparer.Ordinal)
                .ToArray();
            if (boundaries.Length == 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    profileId,
                    entryPoint.EntryPointId,
                    ClientUnsupportedInvocationCode,
                    "A compiler-proven service-client invocation has no admitted source/generated client boundary.",
                    $"'{fact.ClientType}' proves an exact invocation of '{fact.OperationName}' on service contract '{fact.ServiceContractType}', but no admitted client boundary classified SourceClient or GeneratedClient matches this exact client/contract pair.",
                    fact.Evidence,
                    fact.Certainty));
                continue;
            }

            var distinctClientKinds = boundaries.Select(boundary => boundary.ClientKind).Distinct().ToArray();
            if (distinctClientKinds.Length > 1)
            {
                diagnostics.Add(CreateDiagnostic(
                    profileId,
                    entryPoint.EntryPointId,
                    ClientConflictingBoundaryCode,
                    "Conflicting client-kind boundaries match the same client/contract pair.",
                    $"'{fact.ClientType}' proves an exact invocation of '{fact.OperationName}' on service contract '{fact.ServiceContractType}', but the admitted client boundaries for this exact client/contract pair disagree on ClientKind ({string.Join(", ", distinctClientKinds.Select(kind => kind.ToString()).OrderBy(name => name, StringComparer.Ordinal))}); no single coherent client kind can be admitted.",
                    Combine(Combine(facts.Select(f => f.Evidence).ToArray()), Combine(boundaries.Select(boundary => boundary.Evidence).ToArray())),
                    boundaries.Select(boundary => boundary.Certainty).Append(facts.Select(f => f.Certainty).Max()).Max()));
                continue;
            }

            var faults = faultFacts
                .Where(candidate => candidate.OperationSymbol == fact.OperationSymbol)
                .OrderBy(candidate => candidate.FaultType, StringComparer.Ordinal)
                .ToArray();
            var declaredFaultTypeNames = faults.Length == 0
                ? null
                : string.Join(", ", faults.Select(f => f.FaultType).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal));

            var evidence = Combine(
                Combine(facts.Select(f => f.Evidence).ToArray()),
                call.Invocation.Evidence,
                call.Site.Evidence,
                call.Site.Resolution.Evidence,
                Combine(boundaries.Select(boundary => boundary.Evidence).ToArray()),
                Combine(faults.Select(f => f.Evidence).ToArray()));
            var certainty = LeastConfident(
                facts.Select(f => f.Certainty).Max(),
                evidence,
                boundaries.Select(boundary => boundary.Certainty).Append(fact.Certainty).Max(),
                faults.Select(f => f.Certainty).DefaultIfEmpty(fact.Certainty).Max());

            var node = CreateNodeWithPresentation(
                profileId,
                entryPoint.EntryPointId,
                ScenarioNodeKind.ClientOperationInvocation,
                $"client-invocation:{fact.InvocationOperation.Value}",
                fact.CallerMethod,
                fact.InvocationOperation,
                $"invokes {fact.ClientType}.{fact.OperationName}",
                new ScenarioNodePresentation(
                    ContractTypeName: fact.ServiceContractType,
                    ClientTypeName: fact.ClientType,
                    CalledMemberName: fact.OperationName,
                    // Dual-populated with the same TargetContainingTypeName/TargetMemberName shape
                    // ordinary MethodCall nodes use, so this node reuses the existing dynamic
                    // per-type diagram-participant machinery (BuildMethodCallParticipantKeys and the
                    // message source/target resolution it feeds) instead of colliding with the
                    // reserved "client" participant key already used for the inbound caller.
                    TargetContainingTypeName: fact.ClientType,
                    TargetMemberName: fact.OperationName,
                    ClientKind: distinctClientKinds[0],
                    ResultClaimKind: fact.ResultClaim,
                    ResultIsAwaited: fact.IsAwaited,
                    ResultBindingName: fact.ResultBindingName,
                    DeclaredResultTypeName: fact.DeclaredResultType,
                    DeclaredFaultTypeNames: declaredFaultTypeNames),
                evidence,
                certainty,
                ordinal);
            nodes.Add(node);
            edges.Add(CreateEdge(profileId, entryPoint.EntryPointId, actionNode, node, ScenarioEdgeKind.Call,
                "outbound service-client call", evidence, certainty, ordinal));
        }
    }

    private const string OutboundHttpConflictCode = "SC-HTTP-CONFLICT";

    /// <summary>
    /// Joins each compiler-proven <see cref="OutboundHttpRequestFact"/> whose caller is the scenario
    /// root's own method with exactly one Method Flow platform invocation and one Call Graph site for
    /// its operation. Unlike <see cref="DirectCalls"/>, this dedicated admission REQUIRES
    /// <c>IsPlatformTarget</c>. Zero flow candidates for the operation is silent; a flow invocation that
    /// exists but cannot be admitted exactly (ambiguous/incomplete resolution, not a platform target)
    /// is withheld under the existing <c>SC013</c> topology diagnostic; agreeing duplicate facts merge
    /// to one node with unioned evidence and the weakest certainty; conflicting facts for one operation
    /// withhold with one deterministic <see cref="OutboundHttpConflictCode"/>. The typed node replaces
    /// any generic direct-call node for the same site (a platform call never becomes one anyway).
    /// </summary>
    private static void AddOutboundHttpRequests(
        ScenarioAnalysisRequest request,
        NormalizedEntry entryPoint,
        CompilationProfileId profileId,
        ScenarioNode actionNode,
        List<ScenarioNode> nodes,
        List<ScenarioEdge> edges,
        List<ScenarioGraphDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (!FrameworkFactsBound(request))
        {
            return;
        }

        var facts = request.FrameworkFacts.Facts.OfType<OutboundHttpRequestFact>()
            .Where(fact => fact.CallerMethod == entryPoint.RootMethod
                && fact.RequestKind != OutboundHttpRequestKind.Unknown
                && !fact.Evidence.IsDefaultOrEmpty
                && fact.Certainty != CertaintyLevel.Unknown)
            .ToArray();
        if (facts.Length == 0)
        {
            return;
        }

        var flow = request.Behavior.MethodFlows.SingleOrDefault(item => item.Method == entryPoint.RootMethod);
        var flowOperations = flow is null
            ? new HashSet<OperationId>()
            : flow.Nodes.OfType<InvocationFlowNode>().Select(invocation => invocation.Operation).ToHashSet();

        var platformCalls = flow is null
            ? new Dictionary<OperationId, (InvocationFlowNode Invocation, CallSite Site)>()
            : flow.Nodes.OfType<InvocationFlowNode>()
                .GroupBy(invocation => invocation.Operation)
                .Where(group => InvocationFactsAgree(group))
                .Select(group => group.OrderBy(invocation => invocation.Id.Value, StringComparer.Ordinal).First())
                .Select(invocation => (Invocation: invocation, Site: CanonicalSite(request, flow, invocation)))
                .Where(item => item.Site is not null && IsPlatformDirectExact(item.Invocation, item.Site!))
                .ToDictionary(item => item.Invocation.Operation, item => (Invocation: item.Invocation, Site: item.Site!));

        var groups = facts
            .GroupBy(fact => fact.InvocationOperation)
            .OrderBy(group => group.Key.Value, StringComparer.Ordinal)
            .ToArray();

        var ordinal = 0;
        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = group.Key;
            var groupFacts = group.OrderBy(fact => fact.Id.Value, StringComparer.Ordinal).ToArray();

            if (groupFacts.Length > 1 && !OutboundHttpFactsAgree(groupFacts))
            {
                diagnostics.Add(CreateDiagnostic(
                    profileId,
                    entryPoint.EntryPointId,
                    OutboundHttpConflictCode,
                    "Multiple compiler-proven outbound HTTP request facts disagree for the same call site.",
                    $"operation={operation.Value}; rows={string.Join(" | ", groupFacts.Select(OutboundHttpFactRow).Distinct(StringComparer.Ordinal).OrderBy(row => row, StringComparer.Ordinal))}",
                    Combine(groupFacts.Select(fact => fact.Evidence).ToArray()),
                    groupFacts.Select(fact => fact.Certainty).Max()));
                continue;
            }

            if (!platformCalls.TryGetValue(operation, out var call))
            {
                if (flowOperations.Contains(operation))
                {
                    // The canonical EmitOnce("SC013", ...) topology withhold emitter lives inside
                    // BuildTopology's callee-composition closure and cannot be reached from this
                    // root-local join. Emit the same code with the canonical
                    // methodoperationreason detail shape used by the bespoke SC013 emits in
                    // BuildTopology, and attach the fact-group evidence, so an outbound-HTTP
                    // withhold is indistinguishable in form from any other SC013 topology withhold.
                    var withholdEvidence = Combine(groupFacts.Select(fact => fact.Evidence).ToArray());
                    diagnostics.Add(CreateDiagnostic(
                        profileId,
                        entryPoint.EntryPointId,
                        "SC013",
                        "The material scenario node is withheld because its unsupported call-site topology cannot place the claim safely.",
                        $"{entryPoint.RootMethod.Value}{operation.Value}outbound-http-not-platform-direct-exact",
                        withholdEvidence,
                        LeastConfident(groupFacts.Select(fact => fact.Certainty).Max(), withholdEvidence)));
                }

                continue;
            }

            var factEvidence = Combine(groupFacts.Select(fact => fact.Evidence).ToArray());
            var evidence = Combine(
                factEvidence,
                call.Invocation.Evidence,
                call.Site.Evidence,
                call.Site.Resolution.Evidence);
            var certainty = LeastConfident(groupFacts.Select(fact => fact.Certainty).Max(), evidence);
            var kind = groupFacts[0].RequestKind;

            var node = CreateNodeWithPresentation(
                profileId,
                entryPoint.EntryPointId,
                ScenarioNodeKind.OutboundHttpRequest,
                $"outbound-http:{operation.Value}",
                groupFacts[0].CallerMethod,
                operation,
                "outbound HTTP request boundary",
                new ScenarioNodePresentation(OutboundHttpRequestKind: kind),
                evidence,
                certainty,
                ordinal);
            nodes.Add(node);
            edges.Add(CreateEdge(
                profileId,
                entryPoint.EntryPointId,
                actionNode,
                node,
                ScenarioEdgeKind.Call,
                "outbound HTTP request",
                evidence,
                certainty,
                ordinal));
            ordinal++;
        }
    }

    private static bool IsPlatformDirectExact(InvocationFlowNode invocation, CallSite site)
        => invocation.Certainty == CertaintyLevel.Exact && !invocation.Evidence.IsDefaultOrEmpty && invocation.IsSourceBacked
            && invocation.IsPlatformTarget
            && invocation.Target is not null
            && !invocation.IsInsideNestedFunction && !invocation.IsDynamic
            && !invocation.IsDelegateOrEventInvoke && !invocation.IsConstructor
            && site.DeclaredTarget == invocation.Target && site.Certainty == CertaintyLevel.Exact && !site.Evidence.IsDefaultOrEmpty
            && site.Resolution.Kind == CallResolutionKind.DirectExact && site.Resolution.IsComplete
            && site.Resolution.Candidates.Length == 1 && site.Resolution.Candidates[0] == invocation.Target
            && !site.Resolution.Evidence.IsDefaultOrEmpty
            && invocation.Evidence.All(item => item.Certainty == CertaintyLevel.Exact)
            && site.Evidence.All(item => item.Certainty == CertaintyLevel.Exact)
            && site.Resolution.Evidence.All(item => item.Certainty == CertaintyLevel.Exact);

    private static bool OutboundHttpFactsAgree(OutboundHttpRequestFact[] candidates)
    {
        var first = OutboundHttpFactRow(candidates[0]);
        return candidates.All(candidate => string.Equals(OutboundHttpFactRow(candidate), first, StringComparison.Ordinal));
    }

    private static string OutboundHttpFactRow(OutboundHttpRequestFact fact)
    {
        var identity = fact.FrameworkMethodIdentity;
        var parameters = identity.Parameters.IsDefaultOrEmpty
            ? string.Empty
            : string.Join(",", identity.Parameters.Select(parameter => $"{parameter.RefKind} {parameter.FullyQualifiedType}"));
        return string.Join(
            "|",
            fact.RequestKind.ToString(),
            identity.AssemblyIdentity,
            identity.ContainingMetadataType,
            identity.MethodMetadataName,
            identity.GenericArity.ToString(System.Globalization.CultureInfo.InvariantCulture),
            identity.ReturnType,
            identity.AssemblyVersion,
            identity.AssemblyPublicKeyToken,
            parameters);
    }

    private static ScenarioDirectCallExpansion AddConfiguredDirectCalls(
        ScenarioAnalysisRequest request,
        NormalizedEntry entryPoint,
        CompilationProfileId profileId,
        ScenarioNode actionNode,
        List<ScenarioNode> nodes,
        List<ScenarioEdge> edges,
        List<ScenarioGraphDiagnostic> diagnostics,
        HashSet<OperationId>? excludedOperations = null)
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
            // Operations independently admitted as a service-client invocation are presented by
            // AddServiceClientInvocations with protocol-neutral wording instead of as a generic
            // MethodCall node, so the depth-1 push is skipped to avoid two nodes for one call site.
            // Only depth-1 root calls are excluded; deeper calls are unaffected.
            if (excludedOperations is not null && excludedOperations.Contains(root.Invocation.Operation))
            {
                continue;
            }
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
                // A locally guarded child is admitted like every other DirectExact child; its
                // callee-local guard topology is composed later from the target Method Flow so the
                // call renders inside exact nested fragments instead of being withheld or becoming
                // unconditional (guarded callee topology contract).
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
        // Parents precede children by contract, so each step inherits the ALREADY-inherited parent
        // arms; a snapshot of the untouched input steps would lose every arm beyond the first level.
        var inheritedById = new Dictionary<string, ImmutableArray<ScenarioArmId>>(StringComparer.Ordinal);
        var steps = expansion.Steps.Select(step =>
        {
            var inherited = step.ParentStepId is { } parent && inheritedById.TryGetValue(parent, out var parentStepArms)
                ? parentStepArms
                : memberships.GetValueOrDefault(step.ScenarioNodeId, []);
            inheritedById[step.Id] = inherited;
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

    /// <summary>
    /// Composes exact callee-local decision topology for every complete direct-call expansion
    /// occurrence. Each complete step whose target resolves to the unique loaded Method Flow gains
    /// decisions and true/false arms scoped to that occurrence (<c>OccurrenceScope</c> = the expansion
    /// step identity), so repeated and diamond-shaped calls compose distinct occurrence topology while
    /// root identities stay unchanged. Child-call scenario nodes created inside the occurrence join
    /// arms through the target flow's own operation anchors and control dependences with the same
    /// duplicate-anchor agreement and dual-polarity conflict rules as the root join; a child call not
    /// controlled by any callee-local decision keeps its flat/inherited behavior. A child node also
    /// inherits the caller occurrence's locally proven arms so guarded nesting stays provable by
    /// proper membership containment. Terminal classification reuses the exact arm classifier;
    /// unsupported loop/switch/exception/mixed shapes fail closed with SC013. Unsupported or
    /// ambiguous membership never invents placement: SC011/SC012 withhold it exactly like the root
    /// join. Evidence is the canonical union of the call and control-dependence contributors each
    /// claim uses; certainty is the least confident contributor everywhere.
    /// </summary>
    private sealed record CalleeTopologyCompositionResult(
        ScenarioTopology Topology,
        ImmutableArray<string> WithheldOccurrenceIds);

    private static bool IsWithheldOccurrence(
        ScenarioDirectCallExpansionStep step,
        HashSet<string> withheldOccurrences,
        IReadOnlyDictionary<string, string?> parentByOccurrence)
    {
        for (string? current = step.Id; current is not null;)
        {
            if (withheldOccurrences.Contains(current))
            {
                return true;
            }
            current = parentByOccurrence.GetValueOrDefault(current);
        }
        return false;
    }

    private static ScenarioTopology RemoveWithheldOccurrenceTopology(ScenarioTopology topology,
        HashSet<string> withheldOccurrences, HashSet<ScenarioNodeId> withheldNodes)
    {
        var decisions = topology.Decisions.Where(decision => decision.OccurrenceScope is null
            || !withheldOccurrences.Contains(decision.OccurrenceScope)).ToImmutableArray();
        var decisionIds = decisions.Select(decision => decision.Id).ToHashSet();
        var arms = topology.Arms.Where(arm => decisionIds.Contains(arm.Decision)).ToImmutableArray();
        var armIds = arms.Select(arm => arm.Id).ToHashSet();
        return topology with
        {
            Decisions = decisions,
            Arms = arms,
            Memberships = topology.Memberships.Where(item => armIds.Contains(item.Arm)
                && !withheldNodes.Contains(item.ScenarioNode)).ToImmutableArray(),
            Terminals = topology.Terminals.Where(item => armIds.Contains(item.Arm)).ToImmutableArray()
        };
    }

    private static CalleeTopologyCompositionResult ComposeCalleeOccurrenceTopology(
        ScenarioAnalysisRequest request,
        CompilationProfileId profileId,
        EntryPointId entryPointId,
        NormalizedEntry entryPoint,
        ScenarioDirectCallExpansion expansion,
        ScenarioTopology topology,
        List<ScenarioGraphDiagnostic> diagnostics)
    {
        if (!expansion.Steps.Any(step => step.IsComplete))
        {
            return new CalleeTopologyCompositionResult(topology, []);
        }

        var decisions = topology.Decisions.ToList();
        var arms = topology.Arms.ToList();
        var memberships = topology.Memberships.ToList();
        var terminals = topology.Terminals.ToList();
        // Membership pairs already claimed before this pass (root join plus inherited caller/root
        // propagation) are never duplicated by the occurrence join.
        var claimedPairs = memberships
            .Select(item => (item.Arm.Value, item.ScenarioNode.Value))
            .ToHashSet();
        // Locally proven placements of one occurrence's own call node; they become the inherited
        // caller-local arms for that occurrence's children in DFS chronology order.
        var localPlacementsByNode = new Dictionary<string, List<ScenarioMembership>>(StringComparer.Ordinal);
        // Occurrence diagnostics carry no per-occurrence discriminator, so N occurrences reaching
        // the same unsupported shape must report one deterministic first-occurrence boundary
        // instead of N byte-identical diagnostics. Composition itself is never deduped.
        var emittedDiagnosticKeys = new HashSet<string>(StringComparer.Ordinal);
        var withheldOccurrenceIds = new HashSet<string>(StringComparer.Ordinal);
        void EmitOnce(string code, string summary, string detail,
            ImmutableArray<EvidenceRef> evidence = default, CertaintyLevel? certainty = null)
        {
            if (!emittedDiagnosticKeys.Add(code + "\u001f" + detail))
            {
                return;
            }

            diagnostics.Add(CreateDiagnostic(profileId, entryPointId, code, summary, detail, evidence, certainty));
        }

        var flowsByMethod = request.Behavior.MethodFlows
            .GroupBy(flow => flow.Method)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var childStepsByParent = expansion.Steps
            .Where(step => step.ParentStepId is not null)
            .GroupBy(step => step.ParentStepId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        foreach (var step in expansion.Steps)
        {
            if (!step.IsComplete || step.IsCycleBoundary)
            {
                continue;
            }

            var targetFlows = flowsByMethod.TryGetValue(step.TargetMethod, out var matchingFlows)
                ? matchingFlows.Take(2).ToArray()
                : [];
            if (targetFlows.Length != 1)
            {
                continue;
            }

            var flow = targetFlows[0];
            var flowNodesById = flow.Nodes
                .GroupBy(node => node.Id)
                .ToDictionary(group => group.Key, group => group.First());
            var anchorsByOperation = BuildOperationAnchors(flow);
            var dependencesByControlled = flow.ControlDependences
                .GroupBy(dependence => dependence.ControlledNode)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(dependence => dependence.ControllingDecision.Value, StringComparer.Ordinal)
                        .ThenBy(dependence => dependence.ControlledOnTrue)
                        .ToImmutableArray());

            var childSteps = childStepsByParent.GetValueOrDefault(step.Id, []);
            var unsupportedChildren = new HashSet<string>(StringComparer.Ordinal);
            foreach (var child in childSteps)
            {
                var childAnchors = anchorsByOperation.TryGetValue(child.Operation.Value, out var exactAnchors) ? exactAnchors : [];
                var unsupportedTopology = childAnchors.Where(flowNodesById.ContainsKey)
                    .Select(anchorId => flowNodesById[anchorId]).SelectMany(anchor =>
                    {
                        var loops = flow.Nodes.OfType<LoopNode>().Where(loop => loop.Body.Contains(anchor.Id)
                            || (anchor is InvocationFlowNode invocationAnchor && !loop.BodyBlockOrdinals.IsDefaultOrEmpty
                                && loop.BodyBlockOrdinals.Contains(invocationAnchor.BlockOrdinal))).ToArray();
                        var regions = flow.Regions.Where(region =>
                            (region.Kind is FlowRegionKind.Try or FlowRegionKind.Catch or FlowRegionKind.Filter or FlowRegionKind.Finally)
                            && region.Nodes.Contains(anchor.Id)).ToArray();
                        var switches = dependencesByControlled.GetValueOrDefault(anchor.Id, [])
                            .SelectMany(dependence =>
                            {
                                if (!flowNodesById.TryGetValue(dependence.ControllingDecision, out var controlling)
                                    || controlling is not DecisionFlowNode decision)
                                {
                                    return [];
                                }

                                return flow.Edges
                                    .Where(edge => edge.Source == decision.Id
                                        && edge.Kind is FlowEdgeKind.SwitchCase or FlowEdgeKind.SwitchDefault)
                                    .OrderBy(edge => edge.Id.Value, StringComparer.Ordinal)
                                    .Select(edge => (
                                        Kind: "switch",
                                        Id: $"{decision.Id.Value}:{edge.Id.Value}",
                                        Evidence: Combine(dependence.Evidence, decision.Evidence, edge.Evidence),
                                        Certainty: LeastConfident(dependence.Certainty,
                                            Combine(dependence.Evidence, decision.Evidence, edge.Evidence),
                                            decision.Certainty, edge.Certainty)));
                            });
                        return loops.Select(loop => (Kind: "loop", Id: loop.Id.Value, Evidence: loop.Evidence, Certainty: loop.Certainty))
                            .Concat(regions.Select(region => (Kind: "exception", Id: region.Id.Value, Evidence: region.Evidence, Certainty: region.Certainty)))
                            .Concat(switches);
                    }).OrderBy(item => item.Kind, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
                if (unsupportedTopology.Length == 0)
                {
                    continue;
                }
                unsupportedChildren.Add(child.Id);
                var anchorEvidence = childAnchors.Where(flowNodesById.ContainsKey).SelectMany(anchorId => flowNodesById[anchorId].Evidence).ToImmutableArray();
                var boundaryEvidence = Combine(child.Evidence, anchorEvidence, Combine(unsupportedTopology.Select(item => item.Evidence).ToArray()));
                withheldOccurrenceIds.Add(child.Id);
                EmitOnce("SC013", "The direct child call has unsupported loop, switch, or exception topology; the child occurrence is withheld.",
                    $"{flow.Method.Value}\u001f{string.Join("\u001f", unsupportedTopology.Select(item => item.Kind + ":" + item.Id))}",
                    boundaryEvidence, LeastConfident(child.Certainty, boundaryEvidence, unsupportedTopology.Select(item => item.Certainty).ToArray()));
            }

            var flowDecisions = flow.Nodes.OfType<DecisionFlowNode>().OrderBy(node => node.Id.Value, StringComparer.Ordinal).ToArray();
            if (flowDecisions.Length == 0)
            {
                continue;
            }

            // One occurrence-scoped decision per controlling flow node, with the same evidence,
            // certainty, predicate wording, arm polarity, and terminal classification conventions
            // as the root/service topology composition.
            var occurrenceArmsByDecision = new Dictionary<FlowNodeId, (ScenarioArm TrueArm, ScenarioArm FalseArm)>();
            foreach (var decision in flowDecisions)
            {
                var decisionId = StableIdentity.CreateScenarioDecisionId(new ScenarioDecisionIdentityDescriptor(
                    profileId, entryPoint.RootMethod, flow.Method, decision.Id, step.Id));
                decisions.Add(new ScenarioDecision(
                    decisionId,
                    flow.Method,
                    decision.Id,
                    decision.Condition,
                    decision.Evidence,
                    decision.Certainty,
                    PredicateWording(request, flow.Method, decision.Condition), step.Id));
                var trueArm = new ScenarioArm(
                    StableIdentity.CreateScenarioArmId(new ScenarioArmIdentityDescriptor(
                        profileId, entryPoint.RootMethod, decisionId, IsTrue: true)),
                    decisionId,
                    IsTrue: true,
                    decision.Evidence,
                    decision.Certainty);
                var falseArm = new ScenarioArm(
                    StableIdentity.CreateScenarioArmId(new ScenarioArmIdentityDescriptor(
                        profileId, entryPoint.RootMethod, decisionId, IsTrue: false)),
                    decisionId,
                    IsTrue: false,
                    decision.Evidence,
                    decision.Certainty);
                arms.Add(trueArm);
                arms.Add(falseArm);
                occurrenceArmsByDecision[decision.Id] = (trueArm, falseArm);
            }

            foreach (var decision in flowDecisions)
            {
                var (trueArm, falseArm) = occurrenceArmsByDecision[decision.Id];
                var trueClassification = ClassifyArmTerminal(flow, flowNodesById, decision, isTrue: true);
                var falseClassification = ClassifyArmTerminal(flow, flowNodesById, decision, isTrue: false);
                if (trueClassification.UnsupportedReason is not null || falseClassification.UnsupportedReason is not null)
                {
                    EmitOnce(
                        "SC013",
                        "The decision has unsupported or incomplete terminal/rejoin topology; exact arm classification is withheld.",
                        $"{flow.Method.Value}\u001f{decision.Id.Value}\u001f{trueClassification.UnsupportedReason ?? falseClassification.UnsupportedReason}");
                }

                terminals.Add(BuildArmTerminal(trueArm.Id, trueClassification, decision));
                terminals.Add(BuildArmTerminal(falseArm.Id, falseClassification, decision));
            }

            // Child-call placement inside this occurrence, in DFS expansion chronology.
            var callerLocalMemberships = localPlacementsByNode.TryGetValue(step.ScenarioNodeId.Value, out var placed)
                ? placed
                : [];
            foreach (var child in childSteps)
            {
                if (unsupportedChildren.Contains(child.Id))
                {
                    continue;
                }
                // Inherited caller-local arms: the whole occurrence executes under these locally
                // proven arms, so its child calls inherit them alongside their own local guards.
                foreach (var callerMembership in callerLocalMemberships)
                {
                    if (claimedPairs.Add((callerMembership.Arm.Value, child.ScenarioNodeId.Value)))
                    {
                        AddOccurrenceMembership(memberships, callerMembership.Arm, child,
                            Combine(callerMembership.Evidence, child.Evidence),
                            LeastConfident(callerMembership.Certainty, child.Evidence, child.Certainty),
                            profileId, entryPoint.RootMethod);
                    }
                }

                if (!anchorsByOperation.TryGetValue(child.Operation.Value, out var anchorIds))
                {
                    EmitOnce(
                        "SC011",
                        "The scenario node has no exact eligible Method Flow operation anchor; arm membership is withheld.",
                        $"{flow.Method.Value}\u001f{child.Operation.Value}\u001f{child.ScenarioNodeId.Value}");
                    continue;
                }

                // Every eligible anchor must agree on control memberships; disagreement never
                // silently prefers one anchor (same rule as the root join).
                var membershipSets = anchorIds
                    .Select(anchorId => AnchorMembershipSet(anchorId, dependencesByControlled))
                    .ToArray();
                var firstSet = membershipSets[0];
                if (membershipSets.Skip(1).Any(candidate => !MembershipSetsEqual(firstSet, candidate)))
                {
                    EmitOnce(
                        "SC011",
                        "The scenario node's operation anchors disagree on control membership; arm membership is withheld.",
                        $"{flow.Method.Value}\u001f{child.Operation.Value}\u001f{child.ScenarioNodeId.Value}");
                    continue;
                }

                // Same-decision dual-polarity conflicts are reported deterministically and only
                // their memberships are withheld (same rule as the root join).
                var conflictDecisions = firstSet
                    .GroupBy(membership => membership.ControllingDecision.Value, StringComparer.Ordinal)
                    .Where(group => group.Any(membership => membership.ControlledOnTrue)
                        && group.Any(membership => !membership.ControlledOnTrue))
                    .Select(group => group.Key)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                foreach (var conflict in conflictDecisions)
                {
                    EmitOnce(
                        "SC012",
                        "The scenario node is directly controlled by both semantic arms of the same decision; arm membership is withheld.",
                        $"{flow.Method.Value}\u001f{child.Operation.Value}\u001f{conflict}");
                }

                var withheld = conflictDecisions.ToHashSet(StringComparer.Ordinal);
                foreach (var dependenceGroup in firstSet
                             .GroupBy(membership => (membership.ControllingDecision.Value, membership.ControlledOnTrue))
                             .OrderBy(group => group.Key.Item1, StringComparer.Ordinal)
                             .ThenBy(group => group.Key.Item2))
                {
                    if (withheld.Contains(dependenceGroup.Key.Item1)
                        || !occurrenceArmsByDecision.TryGetValue(dependenceGroup.First().ControllingDecision, out var armPair))
                    {
                        continue;
                    }

                    var arm = dependenceGroup.Key.Item2 ? armPair.TrueArm.Id : armPair.FalseArm.Id;
                    if (!claimedPairs.Add((arm.Value, child.ScenarioNodeId.Value)))
                    {
                        continue;
                    }

                    var dependenceEvidence = Combine(dependenceGroup.Select(item => item.Evidence).ToArray());
                    var membership = AddOccurrenceMembership(memberships, arm, child,
                        Combine(child.Evidence, dependenceEvidence),
                        LeastConfident(child.Certainty, dependenceEvidence),
                        profileId, entryPoint.RootMethod);
                    // Record this locally proven placement so the child's own occurrence (if any,
                    // later in DFS chronology) inherits it as a caller-local arm.
                    if (!localPlacementsByNode.TryGetValue(child.ScenarioNodeId.Value, out var placements))
                    {
                        placements = [];
                        localPlacementsByNode.Add(child.ScenarioNodeId.Value, placements);
                    }

                    placements.Add(membership);
                }
            }
        }

        return new CalleeTopologyCompositionResult(CanonicalizeTopology(decisions, arms, memberships, terminals),
            withheldOccurrenceIds.Order(StringComparer.Ordinal).ToImmutableArray());
    }

    /// <summary>Creates one evidence-backed occurrence membership and records it.</summary>
    private static ScenarioMembership AddOccurrenceMembership(
        List<ScenarioMembership> memberships,
        ScenarioArmId arm,
        ScenarioDirectCallExpansionStep childStep,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty,
        CompilationProfileId profileId,
        MethodId rootMethod)
    {
        var membership = new ScenarioMembership(
            StableIdentity.CreateScenarioMembershipId(new ScenarioMembershipIdentityDescriptor(
                profileId, rootMethod, arm, childStep.ScenarioNodeId)),
            arm,
            childStep.ScenarioNodeId,
            evidence,
            certainty);
        memberships.Add(membership);
        return membership;
    }

    /// <summary>Applies the canonical semantic ordering shared by every topology composition.</summary>
    private static ScenarioTopology CanonicalizeTopology(
        List<ScenarioDecision> decisions,
        List<ScenarioArm> arms,
        List<ScenarioMembership> memberships,
        List<ScenarioArmTerminal> terminals)
    {
        var decisionById = decisions.GroupBy(decision => decision.Id).ToDictionary(group => group.Key, group => group.First());
        var armById = arms.GroupBy(arm => arm.Id).ToDictionary(group => group.Key, group => group.First());
        return new ScenarioTopology(
            decisions
                .GroupBy(decision => decision.Id).Select(group => group.First())
                .OrderBy(decision => decision.ControllingFlowNode.Value, StringComparer.Ordinal)
                .ToImmutableArray(),
            arms
                .GroupBy(arm => arm.Id).Select(group => group.First())
                .OrderBy(arm => decisionById[arm.Decision].ControllingFlowNode.Value, StringComparer.Ordinal)
                .ThenBy(arm => arm.IsTrue)
                .ToImmutableArray(),
            memberships
                .GroupBy(item => (item.Arm.Value, item.ScenarioNode.Value)).Select(group => group.First())
                .OrderBy(item => decisionById[armById[item.Arm].Decision].ControllingFlowNode.Value, StringComparer.Ordinal)
                .ThenBy(item => armById[item.Arm].IsTrue)
                .ThenBy(item => item.ScenarioNode.Value, StringComparer.Ordinal)
                .ToImmutableArray(),
            terminals
                .GroupBy(terminal => terminal.Arm).Select(group => group.First())
                .OrderBy(terminal => decisionById[armById[terminal.Arm].Decision].ControllingFlowNode.Value, StringComparer.Ordinal)
                .ThenBy(terminal => armById[terminal.Arm].IsTrue)
                .ToImmutableArray());
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

    /// <summary>
    /// True when every <see cref="ServiceClientInvocationFact"/> admitted for the same
    /// <see cref="ServiceClientInvocationFact.InvocationOperation"/> (the real identity of one compiler
    /// call site) agrees on every field material to the node's identity, evidence join, and observable
    /// presentation. Mirrors <see cref="InvocationFactsAgree"/>'s duplicate-anchor coherence check for
    /// direct calls, applied to this fact's own required fields instead. Unlike
    /// <see cref="InvocationFactsAgree"/>, this deliberately does not compare Certainty across candidates,
    /// because Certainty is folded via the weakest-contributor rule (Max of Certainty values) regardless of
    /// agreement, so differing certainty among otherwise-agreeing facts cannot strengthen the resulting claim.
    /// </summary>
    private static bool ClientInvocationFactsAgree(ServiceClientInvocationFact[] candidates)
    {
        var first = candidates[0];
        return candidates.All(candidate => candidate.ServiceContractType == first.ServiceContractType
            && candidate.ServiceContractTypeSymbol == first.ServiceContractTypeSymbol
            && candidate.ClientType == first.ClientType
            && candidate.ClientTypeSymbol == first.ClientTypeSymbol
            && candidate.OperationName == first.OperationName
            && candidate.OperationSymbol == first.OperationSymbol
            && candidate.OperationKey == first.OperationKey
            && candidate.ResultClaim == first.ResultClaim
            && candidate.IsAwaited == first.IsAwaited
            && candidate.ResultBindingName == first.ResultBindingName
            && candidate.DeclaredResultType == first.DeclaredResultType);
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
        if (!HasEfSlice(request, composition.TrueArm.ResolvedMethod))
        {
            JoinStateAssignments(
                request,
                profileId,
                entryPointId,
                composition.TrueArm.ResolvedMethod,
                trueServiceNode,
                nodes,
                edges);
        }
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
        if (!HasEfSlice(request, composition.FalseArm.ResolvedMethod))
        {
            JoinStateAssignments(
                request,
                profileId,
                entryPointId,
                composition.FalseArm.ResolvedMethod,
                falseServiceNode,
                nodes,
                edges);
        }
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
        if (!FrameworkFactsBound(request))
        {
            return;
        }

        var efFacts = request.FrameworkFacts.Facts
            .OfType<EntityFrameworkQueryFact>()
            .Where(fact => fact.Method == serviceMethod)
            .ToArray();

        var queryOrder = NonGetFactsBound(request) ? request.NonGetSemanticFacts!.EfOperationSequence
            .Where(item => item.Method == serviceMethod && item.Kind == EfOperationSequenceKind.QueryTerminal)
            .GroupBy(item => item.Operation.Value)
            .ToDictionary(group => group.Key, group => group.Min(item => item.Ordinal), StringComparer.Ordinal) : null;

        var ordered = efFacts
            .OrderBy(fact => queryOrder?.GetValueOrDefault(fact.Operation.Value, int.MaxValue) ?? int.MaxValue)
            .ThenBy(fact => fact.Operation.Value, StringComparer.Ordinal)
            .ToArray();

        foreach (var fact in ordered)
        {
            int ordinal = queryOrder?.GetValueOrDefault(fact.Operation.Value, int.MaxValue) ?? int.MaxValue;
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

    /// <summary>Joins every exact property state assignment of the service method in source order.</summary>
    private static void JoinStateAssignments(
        ScenarioAnalysisRequest request,
        CompilationProfileId profileId,
        EntryPointId entryPointId,
        MethodId serviceMethod,
        ScenarioNode serviceNode,
        List<ScenarioNode> nodes,
        List<ScenarioEdge> edges)
    {
        if (!NonGetFactsBound(request))
        {
            return;
        }

        var methodMutations = request.NonGetSemanticFacts.EntityFrameworkMutations
            .Where(fact => fact.Method == serviceMethod)
            .ToArray();
        var entityMutations = methodMutations
            .Where(fact => fact.MutationKind is not (EntityFrameworkMutationKind.SaveChangesAsync or EntityFrameworkMutationKind.SaveChanges))
            .ToArray();
        var saves = methodMutations
            .Where(fact => fact.MutationKind is EntityFrameworkMutationKind.SaveChangesAsync or EntityFrameworkMutationKind.SaveChanges)
            .ToArray();

        var assignments = request.NonGetSemanticFacts.StateAssignments
            .Where(fact => fact.Method == serviceMethod);
        if (methodMutations.Length > 0)
        {
            assignments = assignments.Where(fact => HasCompatibleMutationSaveRequest(fact, entityMutations, saves));
        }

        foreach (var assignment in assignments
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

        static bool HasCompatibleMutationSaveRequest(
            StateAssignmentSemanticFact assignment,
            IReadOnlyCollection<EntityFrameworkMutationFact> entityMutations,
            IReadOnlyCollection<EntityFrameworkMutationFact> saves)
        {
            var separator = assignment.TargetMember.LastIndexOf('.');
            if (separator <= 0)
            {
                return entityMutations.Count == 0 && saves.Count == 0;
            }

            var containingType = assignment.TargetMember[..separator];
            foreach (var mutation in entityMutations
                         .Where(candidate => string.Equals(candidate.EntityType, containingType, StringComparison.Ordinal))
                         .Where(candidate => candidate.SequenceOrdinal > assignment.SequenceOrdinal))
            {
                if (saves.Any(save => string.Equals(save.DbContextType, mutation.DbContextType, StringComparison.Ordinal)
                    && save.SequenceOrdinal > mutation.SequenceOrdinal
                    && save.SequenceOrdinal > assignment.SequenceOrdinal))
                {
                    return true;
                }
            }

            return false;
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
        if (!NonGetFactsBound(request) && !FrameworkFactsBound(request))
        {
            return;
        }

        var semanticMutations = NonGetFactsBound(request)
            ? request.NonGetSemanticFacts!.EntityFrameworkMutations.Where(fact => fact.Method == serviceMethod)
            : [];
        var frameworkMutations = FrameworkFactsBound(request)
            ? request.FrameworkFacts.Facts.OfType<EntityFrameworkMutationFact>().Where(fact => fact.Method == serviceMethod)
            : [];
        foreach (var mutation in semanticMutations
                     .Concat(frameworkMutations)
                     .GroupBy(fact => fact.Operation.Value, StringComparer.Ordinal)
                     .Select(group => MergeCompatibleMutations(group))
                     .Where(fact => fact is not null)
                     .Select(fact => fact!)
                     .OrderBy(fact => fact.SequenceOrdinal)
                     .ThenBy(fact => fact.Operation.Value, StringComparer.Ordinal))
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
            var edgeKind = mutation.MutationKind is EntityFrameworkMutationKind.SaveChangesAsync or EntityFrameworkMutationKind.SaveChanges
                ? ScenarioEdgeKind.Save
                : ScenarioEdgeKind.Mutation;
            edges.Add(CreateEdge(
                profileId,
                entryPointId,
                serviceNode,
                mutationNode,
                edgeKind,
                edgeKind == ScenarioEdgeKind.Save ? "calls SaveChanges" : "mutates tracked entities",
                mutation.Evidence,
                mutation.Certainty,
                mutation.SequenceOrdinal));
        }

        static EntityFrameworkMutationFact? MergeCompatibleMutations(IEnumerable<EntityFrameworkMutationFact> candidates)
        {
            var facts = candidates.OrderBy(fact => fact.Id.Value, StringComparer.Ordinal).ToArray();
            var first = facts[0];
            if (facts.Any(fact => fact.MutationKind != first.MutationKind
                || !string.Equals(fact.DbContextType, first.DbContextType, StringComparison.Ordinal)
                || !string.Equals(fact.EntityType, first.EntityType, StringComparison.Ordinal)
                || !string.Equals(fact.TargetMember, first.TargetMember, StringComparison.Ordinal)
                || fact.ArgumentOperation != first.ArgumentOperation))
            {
                return null;
            }

            var certainty = facts.Max(fact => fact.Certainty);
            return first with
            {
                Evidence = facts.SelectMany(fact => fact.Evidence).DistinctBy(evidence => evidence.Id.Value).OrderBy(evidence => evidence.Id.Value, StringComparer.Ordinal).ToImmutableArray(),
                Certainty = certainty,
            };
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
        if (!NonGetFactsBound(request))
        {
            return;
        }

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

    private static void JoinEdmxMetadata(
        ScenarioAnalysisRequest request,
        CompilationProfileId profileId,
        EntryPointId entryPointId,
        MethodId actionMethod,
        ScenarioNode actionNode,
        List<ScenarioNode> nodes,
        List<ScenarioEdge> edges)
    {
        if (!FrameworkFactsBound(request))
        {
            return;
        }

        var owningProject = request.ProgramIndex.Methods.FirstOrDefault(method => method.Id == actionMethod) is { } method
            ? request.ProgramIndex.Types.FirstOrDefault(type => type.Id == method.ContainingType)?.Project
            : null;
        if (owningProject is not { } project)
        {
            return;
        }

        foreach (var metadata in request.FrameworkFacts.Facts
                     .OfType<EntityFrameworkEdmxMetadataFact>()
                     .Where(fact => fact.Project == project)
                     .OrderBy(fact => fact.RepositoryRelativePath, StringComparer.Ordinal)
                     .ThenBy(fact => fact.ContentFingerprint, StringComparer.Ordinal))
        {
            var metadataNode = CreateNode(
                profileId,
                entryPointId,
                ScenarioNodeKind.SourceObservation,
                $"observation:{metadata.Id.Value}",
                actionMethod,
                null,
                $"EDMX metadata boundary: {metadata.RepositoryRelativePath}; FunctionImport declaration present: {metadata.HasFunctionImport}; store-function declaration present: {metadata.HasStoreFunction}; unsupported declaration-only metadata boundary; database mapping and runtime behavior are not inferred.",
                metadata.Evidence,
                metadata.Certainty);
            nodes.Add(metadataNode);
            edges.Add(CreateEdge(profileId, entryPointId, actionNode, metadataNode,
                ScenarioEdgeKind.Observation, "independent metadata boundary", metadata.Evidence, metadata.Certainty));
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
            EntityFrameworkMutationKind.SaveChangesAsync or EntityFrameworkMutationKind.SaveChanges => $"saves changes to {ShortTypeName(mutation.DbContextType)}",
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

    private static string? AssignmentContainingType(string targetMember)
    {
        var separator = targetMember.LastIndexOf('.');
        return separator > 0 ? targetMember[..separator] : null;
    }

    private static bool HasEfSlice(ScenarioAnalysisRequest request, MethodId method)
        => request.NonGetSemanticFacts.EntityFrameworkMutations.Any(fact => fact.Method == method);

    private static void RemoveWithheldPersistenceAssignments(
        List<ScenarioNode> nodes,
        List<ScenarioEdge> edges,
        ref ScenarioTopology topology,
        HashSet<ScenarioNodeId> withheld)
    {
        if (withheld.Count == 0)
        {
            return;
        }

        nodes.RemoveAll(node => withheld.Contains(node.Id));
        edges.RemoveAll(edge => withheld.Contains(edge.Source) || withheld.Contains(edge.Target));
        topology = topology with
        {
            Memberships = topology.Memberships
                .Where(membership => !withheld.Contains(membership.ScenarioNode))
                .ToImmutableArray(),
        };
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
    /// <see cref="EntityFrameworkQueryOperatorKind.FirstOrDefaultAsync"/> (and the supported
    /// synchronous FirstOrDefault terminal). The CountAsync aggregation
    /// has no terminal predicate, so it is deliberately excluded from the single-value set and keeps
    /// its count-only handling everywhere in this builder.
    /// </summary>
    private static bool IsSingleValueQueryTerminal(EntityFrameworkQueryOperatorKind? terminal)
        => terminal is EntityFrameworkQueryOperatorKind.SingleOrDefaultAsync
            or EntityFrameworkQueryOperatorKind.FirstOrDefaultAsync
            or EntityFrameworkQueryOperatorKind.FirstOrDefault;

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
        var presentationOperator = IsSingleValueQueryTerminal(terminal)
            || terminal is EntityFrameworkQueryOperatorKind.CountAsync or EntityFrameworkQueryOperatorKind.Count
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

    private static ScenarioNodePresentation ServiceOperationPresentation(ServiceOperationEntryPointFact serviceOperation)
        => new(
            ContractTypeName: serviceOperation.ServiceContractType,
            ImplementationTypeName: serviceOperation.ImplementationType,
            ActionMethodName: serviceOperation.OperationName);

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

    private static ScenarioNodePresentation HostedWorkerPresentation(
        ProgramIndexSnapshot index,
        HostedWorkerLifecycleFact fact)
        => new(
            HostedWorkerTypeName: fact.HostedTypeName,
            HostedWorkerStartMethodName: fact.StartMethod is { } start ? MethodConciseName(index, start) : null,
            HostedWorkerExecuteMethodName: fact.ExecuteMethod is { } execute ? MethodConciseName(index, execute) : null,
            HostedWorkerStopMethodName: fact.StopMethod is { } stop ? MethodConciseName(index, stop) : null,
            ActionKind: ScenarioActionKind.HostedWorker);

    private static void AddHostedWorkerLifecycle(
        ScenarioAnalysisRequest request,
        NormalizedEntry entry,
        ScenarioNode actionNode,
        List<ScenarioNode> nodes,
        List<ScenarioEdge> edges,
        List<ScenarioGraphDiagnostic> diagnostics)
    {
        var fact = entry.HostedWorker!;
        var cancellationSourceMethod = fact.ExecuteMethod ?? fact.StartMethod ?? fact.StopMethod;
        var lifecycle = new[]
        {
            (Step: HostedWorkerLifecycleStep.Start, Method: fact.StartMethod),
            (Step: HostedWorkerLifecycleStep.Execute, Method: fact.ExecuteMethod),
            (Step: HostedWorkerLifecycleStep.Stop, Method: fact.StopMethod),
        };
        var ordinal = 0;
        foreach (var item in lifecycle)
        {
            if (item.Method is not { } method)
            {
                continue;
            }

            var memberName = item.Step switch
            {
                HostedWorkerLifecycleStep.Start => "StartAsync",
                HostedWorkerLifecycleStep.Execute => "ExecuteAsync",
                HostedWorkerLifecycleStep.Stop => "StopAsync",
                _ => throw new InvalidOperationException("An impossible hosted-worker lifecycle step was encountered."),
            };
            var node = CreateNodeWithPresentation(
                request.Profile.Id,
                entry.EntryPointId,
                ScenarioNodeKind.MethodCall,
                $"hosted-worker:{ordinal:000}:{item.Step}:{method.Value}",
                method,
                null,
                memberName,
                new ScenarioNodePresentation(
                    TargetContainingTypeName: fact.HostedTypeName,
                    TargetMemberName: memberName,
                    HostedWorkerTypeName: fact.HostedTypeName,
                    HostedWorkerLifecycleStep: item.Step,
                    HostedWorkerCancellationParameterName: item.Method == cancellationSourceMethod
                        ? fact.CancellationParameterName
                        : null,
                    ActionKind: ScenarioActionKind.HostedWorker),
                entry.Evidence,
                entry.Evidence.Max(item => item.Certainty),
                ordinal++);
            nodes.Add(node);
            edges.Add(CreateEdge(
                request.Profile.Id,
                entry.EntryPointId,
                actionNode,
                node,
                ScenarioEdgeKind.Call,
                memberName,
                ordinal,
                entry.Evidence));
        }

        foreach (var scheduler in request.FrameworkFacts.Facts
                     .OfType<SchedulerJobFact>()
                     .Where(_ => FrameworkFactsBound(request))
                     .Where(_ => BehaviorSnapshotBound(request))
                     .Where(item => lifecycle.Any(lifecycleItem => lifecycleItem.Method == item.RegistrationMethod))
                     .OrderBy(item => item.SourceStart)
                     .ThenBy(item => item.Id.Value, StringComparer.Ordinal))
        {
            var placement = ClassifySchedulerPlacement(request, scheduler);
            if (placement.Kind != SchedulerPlacementKind.Admitted)
            {
                diagnostics.Add(CreateDiagnostic(
                    request.Profile.Id,
                    entry.EntryPointId,
                    "SC-WORKER-UNSUPPORTED-PLACEMENT",
                    $"The scheduler registration was withheld at the {placement.Description} boundary.",
                    $"boundary={placement.Token} ({placement.Description}); registrationOperation={scheduler.RegistrationOperation.Value}",
                    placement.Evidence,
                    CertaintyLevel.Conservative));
                continue;
            }
            var callback = request.ProgramIndex.Methods.FirstOrDefault(method => method.Id == scheduler.JobMethod);
            if (callback is null)
            {
                continue;
            }

            var callbackType = request.ProgramIndex.Types.FirstOrDefault(type => type.Id == callback.ContainingType);
            var node = CreateNodeWithPresentation(
                request.Profile.Id,
                entry.EntryPointId,
                ScenarioNodeKind.MethodCall,
                $"timer-job:{scheduler.Id.Value}",
                scheduler.JobMethod,
                scheduler.RegistrationOperation,
                "timer registration",
                new ScenarioNodePresentation(
                    TargetContainingTypeName: callbackType?.MetadataName,
                    TargetMemberName: "Timer registration",
                    HostedWorkerTypeName: fact.HostedTypeName,
                    ActionKind: ScenarioActionKind.HostedWorker,
                    HostedWorkerSchedulerRegistration: true),
                Combine(entry.Evidence, scheduler.Evidence),
                Combine(entry.Evidence, scheduler.Evidence).Max(item => item.Certainty),
                ordinal++);
            var source = nodes.SingleOrDefault(item => item.Method == scheduler.RegistrationMethod
                && item.Presentation?.HostedWorkerLifecycleStep is not null) ?? actionNode;
            nodes.Add(node);
            edges.Add(CreateEdge(
                request.Profile.Id,
                entry.EntryPointId,
                source,
                node,
                ScenarioEdgeKind.Call,
                "timer registration",
                ordinal,
                Combine(entry.Evidence, scheduler.Evidence)));
        }
    }

    private static ScenarioTopology BuildHostedWorkerTopology(
        ScenarioAnalysisRequest request, NormalizedEntry entry, ScenarioNode actionNode, List<ScenarioNode> nodes,
        List<ScenarioEdge> edges, List<ScenarioGraphDiagnostic> diagnostics, List<ScenarioFlowPlacement> callbackPlacements)
    {
        var containers = new List<ScenarioFlowContainer>();
        var placements = new List<ScenarioFlowPlacement>();
        var controlCandidates = new List<WorkerControlCandidate>();
        var wrapperParentOverrides = new Dictionary<(MethodId Method, FlowRegionId Wrapper), FlowRegionId>();
        var conflictingOverrides = new HashSet<(MethodId Method, FlowRegionId Wrapper)>();
        var ambiguousLoops = new HashSet<(MethodId Method, FlowRegionId Region)>();
        var worker = entry.HostedWorker!;
        var behaviorBound = request.Behavior.Profile is { } behaviorProfile
            && behaviorProfile.Id == request.Profile.Id
            && !string.IsNullOrWhiteSpace(request.Behavior.ProgramIndexFingerprint)
            && string.Equals(request.Behavior.ProgramIndexFingerprint, request.ProgramIndex.IndexFingerprint, StringComparison.Ordinal);
        if (!behaviorBound)
        {
            diagnostics.Add(CreateDiagnostic(request.Profile.Id, entry.EntryPointId,
                "SC-WORKER-UNSUPPORTED-PLACEMENT",
                "Hosted-worker controls were withheld because the behavior snapshot does not match the active analysis.",
                "behavior identity mismatch: behavior profile or Program Index fingerprint did not match for the hosted worker graph.",
                entry.Evidence, CertaintyLevel.Conservative));
            return new ScenarioTopology([], [], [], [], [], []);
        }
        void Observe(MethodId method, HostedWorkerControlKind kind, string detail, FlowNodeId? anchor,
            FlowRegionId? region, int block, ImmutableArray<FlowRegionId> containers, ImmutableArray<EvidenceRef> evidence)
            => controlCandidates.Add(new(method, kind, detail, anchor, region, block, containers, evidence));
        bool RecordWrapperParent(MethodId method, FlowRegionId wrapper, FlowRegionId loop,
            HostedWorkerControlKind controlKind, ImmutableArray<EvidenceRef> evidence)
        {
            var key = (method, wrapper);
            if (conflictingOverrides.Contains(key))
            {
                return false;
            }
            if (wrapperParentOverrides.TryGetValue(key, out var existing))
            {
                if (existing == loop)
                {
                    return true;
                }
                conflictingOverrides.Add(key);
                diagnostics.Add(CreateDiagnostic(request.Profile.Id, entry.EntryPointId,
                    "SC-WORKER-UNSUPPORTED-PLACEMENT",
                    "A hosted-worker exception wrapper has conflicting exact loop ancestry.",
                    $"method={method.Value}; control={controlKind}; wrapper={wrapper.Value}; exact loop ancestry is ambiguous.",
                    evidence, CertaintyLevel.Conservative));
                return false;
            }
            wrapperParentOverrides.Add(key, loop);
            return true;
        }
        foreach (var method in new[] { worker.StartMethod, worker.ExecuteMethod, worker.StopMethod }
            .Where(item => item is not null).Select(item => item!.Value).Distinct().OrderBy(item => item.Value, StringComparer.Ordinal))
        {
            var flows = request.Behavior.MethodFlows.Where(flow => flow.Method == method).ToArray();
            // Hand-built framework-only requests can intentionally omit the behavior snapshot;
            // there is no lifecycle mapping claim to diagnose in that case.
            if (request.Behavior.MethodFlows.IsDefaultOrEmpty)
            {
                continue;
            }
            if (flows.Length != 1)
            {
                var mappedMethod = request.ProgramIndex.Methods.FirstOrDefault(item => item.Id == method);
                var requiresFlow = !string.IsNullOrWhiteSpace(mappedMethod?.BodyFingerprint)
                    && request.FrameworkFacts.Facts.OfType<SchedulerJobFact>()
                        .Any(fact => fact.RegistrationMethod == method);
                if (flows.Length == 0 && !requiresFlow)
                {
                    continue;
                }
                var flowEvidence = flows.SelectMany(flow => flow.Nodes.SelectMany(node => node.Evidence))
                    .ToImmutableArray();
                diagnostics.Add(CreateDiagnostic(request.Profile.Id, entry.EntryPointId,
                    "SC-WORKER-UNSUPPORTED-PLACEMENT",
                    "Hosted-worker method flow mapping is not unique.",
                    $"method={method.Value}; flow-count={flows.Length}; {(flows.Length == 0 ? "no method flow was available" : "multiple method flows were available")}.",
                    Combine(entry.Evidence, flowEvidence), CertaintyLevel.Conservative));
                continue;
            }
            var flow = flows[0];
            var programMethod = request.ProgramIndex.Methods.FirstOrDefault(item => item.Id == method);
            var cancellationOrdinal = programMethod?.Parameters
                .Select((parameter, ordinal) => (parameter, ordinal))
                .Where(item => item.parameter.FullyQualifiedType == "System.Threading.CancellationToken")
                .Select(item => (int?)item.ordinal).SingleOrDefault();
            if (cancellationOrdinal is not null)
            {
                var recognizedCancellation = flow.Nodes.OfType<InvocationFlowNode>().Where(item =>
                    item.TargetIdentity is { } identity
                    && identity.AssemblyIdentity == "System.Runtime"
                    && identity.AssemblyVersion == "10.0.0.0"
                    && identity.ContainingMetadataType == "System.Threading.CancellationToken"
                    && identity.MethodMetadataName == "ThrowIfCancellationRequested"
                    && identity.GenericArity == 0 && identity.Parameters.IsEmpty
                     && identity.ReturnType == "System.Void"
                     && item.IsPlatformTarget && !item.IsDynamic
                     && item.TargetAssemblyFullIdentity == "System.Runtime, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"
                     && item.ReceiverOriginalTypeIdentity == new FrameworkTypeIdentity("System.Runtime", "10.0.0.0", "System.Threading.CancellationToken")
                     && item.ReceiverOriginalTypeFullAssemblyIdentity == "System.Runtime, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")
                    .ToArray();
                var cancellationCandidates = recognizedCancellation.Where(item => item.ReceiverParameterOrdinal == cancellationOrdinal
                        && item.ReceiverIdentity == $"{method.Value}:parameter:{cancellationOrdinal.Value}")
                    .Select(item => (Node: item,
                        Containers: ContainingChain(flow, item.BlockOrdinal, null)))
                    .GroupBy(item => item.Node.Operation)
                    .ToArray();
                if (recognizedCancellation.Length > 0 && cancellationCandidates.Length == 0)
                {
                    diagnostics.Add(CreateDiagnostic(request.Profile.Id, entry.EntryPointId,
                        "SC-WORKER-UNSUPPORTED-PLACEMENT",
                        "A recognized cancellation operation could not be mapped to the lifecycle cancellation parameter.",
                        $"method={method.Value}; control=CancellationCheck; exact receiver ordinal or symbol was missing or ambiguous.",
                        Combine(entry.Evidence, recognizedCancellation.SelectMany(item => item.Evidence).ToImmutableArray()),
                        CertaintyLevel.Conservative));
                }
                foreach (var group in cancellationCandidates)
                {
                    var candidates = group.ToArray();
                    var first = candidates[0];
                    var placementAgrees = candidates.All(candidate =>
                        candidate.Node.Method == first.Node.Method
                        && candidate.Node.ReceiverParameterOrdinal == first.Node.ReceiverParameterOrdinal
                        && candidate.Node.ReceiverIdentity == first.Node.ReceiverIdentity
                        && candidate.Node.TargetAssemblyFullIdentity == first.Node.TargetAssemblyFullIdentity
                        && candidate.Node.TargetIdentity == first.Node.TargetIdentity
                        && candidate.Node.BlockOrdinal == first.Node.BlockOrdinal
                        && candidate.Containers.SequenceEqual(first.Containers));
                    if (!placementAgrees)
                    {
                        diagnostics.Add(CreateDiagnostic(request.Profile.Id, entry.EntryPointId,
                            "SC-WORKER-UNSUPPORTED-PLACEMENT",
                            "A recognized cancellation check has conflicting exact anchors.",
                            $"method={method.Value}; control=CancellationCheck; operation={group.Key.Value}; exact receiver, identity, block, evidence, or control placement disagreed.",
                            Combine(candidates.SelectMany(candidate => candidate.Node.Evidence).ToImmutableArray(), entry.Evidence),
                            CertaintyLevel.Conservative));
                        continue;
                    }
                    var evidence = candidates.SelectMany(candidate => candidate.Node.Evidence)
                        .DistinctBy(item => item.Id)
                        .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
                        .ToImmutableArray();
                    Observe(method,
                        HostedWorkerControlKind.CancellationCheck, "cancellation check", first.Node.Id, null,
                        first.Node.BlockOrdinal, first.Containers, evidence);
                }
            }
            foreach (var loop in flow.Nodes.OfType<LoopNode>().OrderBy(item => item.Id.Value, StringComparer.Ordinal))
            {
                if (loop.Header is null || loop.HeaderBlockOrdinal < 0 || loop.BodyBlockOrdinals.IsDefaultOrEmpty) { continue; }
                var region = flow.Regions.FirstOrDefault(item => item.Id == loop.Region);
                if (region is null) { continue; }
                var containingLoops = flow.Nodes.OfType<LoopNode>()
                    .Where(other => other.Id != loop.Id && other.HeaderBlockOrdinal >= 0
                        && other.BodyBlockOrdinals.Concat([other.HeaderBlockOrdinal]).Contains(loop.HeaderBlockOrdinal)
                        && loop.BodyBlockOrdinals.Concat([loop.HeaderBlockOrdinal]).All(other.BodyBlockOrdinals.Concat([other.HeaderBlockOrdinal]).Contains))
                    .OrderBy(other => other.BodyBlockOrdinals.Length)
                    .ToArray();
                var smallestLength = containingLoops.Select(other => other.BodyBlockOrdinals.Length).DefaultIfEmpty(-1).Min();
                var smallestLoops = containingLoops.Where(other => other.BodyBlockOrdinals.Length == smallestLength).ToArray();
                if (smallestLoops.Length > 1)
                {
                    ambiguousLoops.Add((method, loop.Region));
                }
                containers.Add(new ScenarioFlowContainer(loop.Region, method, ScenarioFlowContainerKind.NaturalLoop,
                    loop.Header, smallestLoops.Length == 1 ? smallestLoops[0].Region : null, loop.Evidence, loop.Certainty));
                var members = flow.Nodes.Where(node => node is InvocationFlowNode invocation
                    && (loop.BodyBlockOrdinals.Contains(invocation.BlockOrdinal) || invocation.BlockOrdinal == loop.HeaderBlockOrdinal))
                    .OfType<InvocationFlowNode>().ToArray();
                var hasAwait = flow.Nodes.OfType<AwaitFlowNode>().Any(awaitNode => members.Any(invocation => invocation.Operation == awaitNode.Operand));
                var kind = loop.LoopKind == ExtractedLoopKind.ForEachLoop
                    ? HostedWorkerControlKind.EnumerationLoop
                    : (loop.LoopKind is ExtractedLoopKind.ForLoop or ExtractedLoopKind.WhileLoop or ExtractedLoopKind.DoWhileLoop) && hasAwait
                        ? HostedWorkerControlKind.AwaitedRepeatingLoop : (HostedWorkerControlKind?)null;
                if (kind is null) { continue; }
                if (ambiguousLoops.Contains((method, loop.Region)))
                {
                    diagnostics.Add(CreateDiagnostic(request.Profile.Id, entry.EntryPointId,
                        "SC-WORKER-UNSUPPORTED-PLACEMENT",
                        "A hosted-worker loop has ambiguous exact natural-loop ancestry.",
                        $"method={method.Value}; control={kind.Value}; region={loop.Region.Value}; multiple equal smallest containing loops were proven.",
                        loop.Evidence, CertaintyLevel.Conservative));
                    continue;
                }
                Observe(method, kind.Value,
                    kind == HostedWorkerControlKind.EnumerationLoop ? "enumeration loop" : "awaited repeating loop",
                    loop.Header, loop.Region, loop.HeaderBlockOrdinal, [loop.Region], loop.Evidence);
                foreach (var continuation in (flow.CatchContinuations.IsDefault ? [] : flow.CatchContinuations)
                    .Where(item => item.LoopRegion == loop.Region))
                {
                    var wrapperId = flow.Regions.FirstOrDefault(region => region.Id == continuation.TryRegion)?.Parent
                        ?? continuation.TryRegion;
                    var wrapper = flow.Regions.FirstOrDefault(region => region.Id == wrapperId
                        && region.Kind == FlowRegionKind.TryAndCatch);
                    if (wrapper is null || !RecordWrapperParent(method, wrapper.Id, loop.Region,
                        HostedWorkerControlKind.CatchLoopContinuation, continuation.Evidence))
                    {
                        continue;
                    }
                    Observe(method,
                        HostedWorkerControlKind.CatchLoopContinuation, "catch-to-loop continuation boundary",
                        loop.Header, loop.Region, continuation.DestinationBlockOrdinal,
                        [loop.Region, wrapperId, continuation.CatchRegion], continuation.Evidence);
                }
            }
            foreach (var exceptionRegion in flow.Regions.Where(item =>
                item.Kind is FlowRegionKind.Try or FlowRegionKind.Catch or FlowRegionKind.Finally or FlowRegionKind.TryAndCatch or FlowRegionKind.TryAndFinally))
            {
                var kind = exceptionRegion.Kind switch
                {
                    FlowRegionKind.Catch => ScenarioFlowContainerKind.CatchRegion,
                    FlowRegionKind.Finally => ScenarioFlowContainerKind.FinallyRegion,
                    FlowRegionKind.Try => ScenarioFlowContainerKind.TryRegion,
                    FlowRegionKind.TryAndCatch => ScenarioFlowContainerKind.TryAndCatchRegion,
                    _ => ScenarioFlowContainerKind.TryAndFinallyRegion,
                };
                var containingLoopRegions = flow.Nodes.OfType<LoopNode>()
                    .Where(loop => exceptionRegion.StartBlockOrdinal is { } start && exceptionRegion.EndBlockOrdinal is { } end
                        && loop.HeaderBlockOrdinal >= 0
                        && Enumerable.Range(start, end - start + 1).All(block => loop.BodyBlockOrdinals.Contains(block) || block == loop.HeaderBlockOrdinal))
                    .OrderBy(loop => loop.BodyBlockOrdinals.Length).ToArray();
                var parent = exceptionRegion.Kind == FlowRegionKind.TryAndCatch || exceptionRegion.Kind == FlowRegionKind.TryAndFinally
                    ? containingLoopRegions.Length == 1 ? containingLoopRegions[0].Region : null
                    : exceptionRegion.Parent;
                containers.Add(new ScenarioFlowContainer(exceptionRegion.Id, method, kind, null,
                    parent, exceptionRegion.Evidence, exceptionRegion.Certainty));
            }

            var invocations = flow.Nodes.OfType<InvocationFlowNode>().ToArray();
            var acquireCandidates = invocations.Where(IsSemaphoreAcquire).Where(item =>
                flow.Nodes.OfType<AwaitFlowNode>().Any(awaitNode => awaitNode.Operand == item.Operation))
                .SelectMany(acquire =>
                {
                    var loops = flow.Nodes.OfType<LoopNode>().Where(loop => loop.BodyBlockOrdinals.Contains(acquire.BlockOrdinal)).ToArray();
                    var releases = invocations.Where(IsSemaphoreRelease)
                    .Where(release => release.ReceiverIdentity == acquire.ReceiverIdentity)
                    .Where(release => loops.Any(loop =>
                         (loop.BodyBlockOrdinals.Contains(release.BlockOrdinal) && IsDirectPair(flow, acquire, release, loop))
                         || IsExactFinallyPair(flow, acquire, release, loop) is not null)
                     && loops.Any(loop => CanonicalLoopForRelease(flow, release)?.Id == loop.Id))
                    .ToArray();
                    return loops.Length == 1 ? releases.Select(release => (Acquire: acquire, Release: release, Loop: loops[0])) : [];
                }).ToArray();
            foreach (var candidate in acquireCandidates.Where(item =>
                acquireCandidates.Count(other => other.Acquire.Id == item.Acquire.Id) == 1
                && acquireCandidates.Count(other => other.Release.Id == item.Release.Id) == 1))
            {
                var finallyRegion = IsExactFinallyPair(flow, candidate.Acquire, candidate.Release, candidate.Loop);
                if (finallyRegion is not null
                    && (finallyRegion.Parent is not { } wrapperId
                        || !RecordWrapperParent(method, wrapperId, candidate.Loop.Region,
                            HostedWorkerControlKind.SemaphoreBoundary,
                            candidate.Acquire.Evidence.Concat(candidate.Release.Evidence).ToImmutableArray())))
                {
                    continue;
                }
                var wrapperIdForChain = finallyRegion?.Parent;
                ImmutableArray<FlowRegionId> chain = finallyRegion is null
                    ? [candidate.Loop.Region]
                    : [candidate.Loop.Region, wrapperIdForChain!.Value, finallyRegion.Id];
                Observe(method, HostedWorkerControlKind.SemaphoreBoundary,
                    "semaphore synchronization boundary", candidate.Acquire.Id, candidate.Loop.Region, candidate.Acquire.BlockOrdinal,
                     chain, candidate.Acquire.Evidence.Concat(candidate.Release.Evidence).ToImmutableArray());
            }
            foreach (var acquire in invocations.Where(IsRecognizedSemaphoreAcquire)
                .Where(acquire => !acquireCandidates.Any(candidate => candidate.Acquire.Id == acquire.Id
                    && acquireCandidates.Count(other => other.Acquire.Id == acquire.Id) == 1
                    && acquireCandidates.Count(other => other.Release.Id == candidate.Release.Id) == 1)))
            {
                var related = acquireCandidates.Where(candidate => candidate.Acquire.Id == acquire.Id)
                    .SelectMany(candidate => candidate.Release.Evidence).ToImmutableArray();
                diagnostics.Add(CreateDiagnostic(request.Profile.Id, entry.EntryPointId,
                    "SC-WORKER-UNSUPPORTED-PLACEMENT",
                    "A recognized semaphore operation has no unique exact acquire/release placement.",
                    $"method={method.Value}; control=SemaphoreBoundary; acquire={acquire.Id.Value}",
                    Combine(acquire.Evidence, related), CertaintyLevel.Conservative));
            }

            var claimedTerminalNodes = flow.Outcomes
                .Where(item => item.Kind is FlowOutcomeKind.ExplicitReturn or FlowOutcomeKind.EscapingThrow)
                .Where(item => item.TerminalNode is not null)
                .Select(item => item.TerminalNode!.Value)
                .ToHashSet();
            foreach (var returnGroup in flow.Nodes.OfType<ReturnFlowNode>()
                         .Where(item => item.BlockOrdinal is not null && !claimedTerminalNodes.Contains(item.Id))
                         .GroupBy(item => item.BlockOrdinal!.Value)
                         .OrderBy(group => group.Key))
            {
                var candidates = returnGroup
                    .Select(node => (Node: node, Containers: ContainingChain(flow, node.BlockOrdinal!.Value, null)))
                    .OrderBy(item => item.Node.Id.Value, StringComparer.Ordinal)
                    .ToArray();
                var first = candidates[0];
                if (candidates.Any(item => !item.Containers.SequenceEqual(first.Containers)))
                {
                    diagnostics.Add(CreateDiagnostic(request.Profile.Id, entry.EntryPointId,
                        "SC-WORKER-UNSUPPORTED-PLACEMENT",
                        "A return boundary has conflicting exact block containment.",
                        $"method={method.Value}; block={returnGroup.Key}; duplicate return anchors disagreed.",
                        Combine(candidates.SelectMany(item => item.Node.Evidence).ToImmutableArray(), entry.Evidence),
                        CertaintyLevel.Conservative));
                    continue;
                }

                var evidence = Combine(
                    candidates.SelectMany(item => item.Node.Evidence).ToImmutableArray(),
                    first.Containers.SelectMany(regionId => flow.Regions.Where(region => region.Id == regionId)
                        .SelectMany(region => region.Evidence)).ToImmutableArray());
                Observe(method, HostedWorkerControlKind.ReturnBoundary, "return boundary", first.Node.Id, null,
                    returnGroup.Key, first.Containers, evidence);
            }

            foreach (var outcome in flow.Outcomes.Where(item => item.Kind == FlowOutcomeKind.EscapingThrow))
            {
                if (outcome.TerminalNode is null) { continue; }
                Observe(method,
                    outcome.Kind == FlowOutcomeKind.ExplicitReturn ? HostedWorkerControlKind.ReturnBoundary : HostedWorkerControlKind.ThrowBoundary,
                    outcome.Kind == FlowOutcomeKind.ExplicitReturn ? "return boundary" : "throw boundary",
                    outcome.TerminalNode, null, outcome.BlockOrdinal ?? int.MaxValue,
                    ContainingChain(flow, outcome.BlockOrdinal ?? int.MaxValue, null), outcome.Evidence);
            }
        }

        var distinctContainers = containers.GroupBy(item => (item.Method, item.Region)).Select(group => group.First())
            .ToArray();
        var canonicalContainers = distinctContainers.Select(container => container with
        {
            Parent = wrapperParentOverrides.TryGetValue((container.Method, container.Region), out var exactParent)
                ? exactParent
                : FindCanonicalParent(container, distinctContainers)
        }).ToImmutableArray();
        var normalizedCandidates = new List<WorkerControlCandidate>();
        var rejectedCandidateKinds = new HashSet<(MethodId Method, HostedWorkerControlKind Kind)>();
        foreach (var candidate in controlCandidates.OrderBy(item => item.Method.Value, StringComparer.Ordinal)
            .ThenBy(item => item.Block).ThenBy(item => item.Kind).ThenBy(item => item.Anchor?.Value, StringComparer.Ordinal))
        {
            if (NormalizeCandidate(candidate) is { } normalized)
            {
                normalizedCandidates.Add(normalized);
            }
            else if (rejectedCandidateKinds.Add((candidate.Method, candidate.Kind)))
            {
                diagnostics.Add(CreateDiagnostic(request.Profile.Id, entry.EntryPointId,
                    "SC-WORKER-UNSUPPORTED-PLACEMENT",
                    "A hosted-worker control has no unique canonical container ancestry.",
                    $"method={candidate.Method.Value}; control={candidate.Kind}; declared container ancestry was missing, foreign, cyclic, or inconsistent.",
                    candidate.Evidence, CertaintyLevel.Conservative));
            }
        }
        distinctContainers = canonicalContainers
            .OrderBy(item => item.Method.Value, StringComparer.Ordinal).ThenBy(item => item.Region.Value, StringComparer.Ordinal).ToArray();
        var canonicalByRegion = distinctContainers
            .GroupBy(item => (item.Method, item.Region))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var validCallbackPlacements = new List<ScenarioFlowPlacement>();
        var withheldCallbackNodes = new HashSet<ScenarioNodeId>();
        foreach (var placement in callbackPlacements)
        {
            var matches = placement.Containers
                .Select(region => canonicalByRegion.TryGetValue((placement.Method, region), out var candidates)
                    && candidates.Length == 1 ? candidates[0] : null)
                .ToArray();
            var matchedContainers = matches.Where(item => item is not null).Cast<ScenarioFlowContainer>().ToArray();
            var closure = new Dictionary<FlowRegionId, ScenarioFlowContainer>();
            var loopAnchors = matchedContainers.Where(item => item.Kind == ScenarioFlowContainerKind.NaturalLoop).ToArray();
            var ancestryValid = loopAnchors.Select(container =>
            {
                var current = container;
                while (true)
                {
                    if (!closure.TryAdd(current.Region, current))
                    {
                        break;
                    }
                    if (current.Parent is not { } parent)
                    {
                        break;
                    }
                    if (!canonicalByRegion.TryGetValue((placement.Method, parent), out var parents)
                        || parents.Length != 1)
                    {
                        return false;
                    }
                    current = parents[0];
                }
                return true;
            }).All(item => item);
            var descendantsAdded = true;
            while (descendantsAdded)
            {
                descendantsAdded = false;
                foreach (var container in matchedContainers.Where(item => item.Parent is { }
                    && closure.ContainsKey(item.Parent.Value)))
                {
                    descendantsAdded |= closure.TryAdd(container.Region, container);
                }
            }
            var canonicalChain = closure.Values.ToArray();
            var roots = canonicalChain.Where(item => item.Parent is null).ToArray();
            var valid = placement.Method == entry.HostedWorker!.ExecuteMethod
                && ancestryValid
                && matches.Length > 0
                && matchedContainers.Select(item => item.Region).Distinct().Count() == matchedContainers.Length
                && matchedContainers.All(item => closure.ContainsKey(item.Region))
                && canonicalChain.Any(item => item.Kind == ScenarioFlowContainerKind.NaturalLoop)
                && canonicalChain.Any(item => item.Kind is ScenarioFlowContainerKind.TryRegion or ScenarioFlowContainerKind.TryAndCatchRegion)
                && canonicalChain.All(item => item.Kind is ScenarioFlowContainerKind.NaturalLoop
                    or ScenarioFlowContainerKind.TryRegion
                    or ScenarioFlowContainerKind.TryAndCatchRegion
                    or ScenarioFlowContainerKind.CatchRegion)
                && roots.Length == 1;
            if (valid)
            {
                var chain = new List<ScenarioFlowContainer> { roots[0] };
                while (chain.Count < canonicalChain.Length)
                {
                    var children = canonicalChain.Where(item => item.Parent == chain[^1].Region).ToArray();
                    if (children.Length != 1)
                    {
                        valid = false;
                        break;
                    }
                    chain.Add(children[0]);
                }
                valid = valid && chain.Count == canonicalChain.Length;
                if (valid)
                {
                    validCallbackPlacements.Add(placement with { Containers = chain.Select(item => item.Region).ToImmutableArray() });
                    continue;
                }
            }

            withheldCallbackNodes.Add(placement.ScenarioNode);
            var callbackNode = nodes.FirstOrDefault(node => node.Id == placement.ScenarioNode);
            var memberOperation = callbackNode?.Operation?.Value ?? placement.ScenarioNode.Value;
            var callbackKey = callbackNode?.Key ?? string.Empty;
            var operationMarker = callbackKey.IndexOf(":operation:", StringComparison.Ordinal);
            var boundaryIdentity = callbackKey.StartsWith("callback:", StringComparison.Ordinal)
                ? callbackKey["callback:".Length..(operationMarker >= 0 ? operationMarker : callbackKey.Length)]
                : "unknown";
            diagnostics.Add(CreateDiagnostic(request.Profile.Id, entry.EntryPointId,
                "SC-WORKER-UNSUPPORTED-PLACEMENT",
                "A callback member was withheld because its exact hosted-worker container ancestry was not representable.",
                $"callback-boundary={boundaryIdentity}; member-operation={memberOperation}; member-node={placement.ScenarioNode.Value}; "
                    + $"method={placement.Method.Value}; callback placement referenced a missing, duplicate, foreign, or unsupported container.",
                placement.Evidence, CertaintyLevel.Conservative));
        }
        callbackPlacements.Clear();
        callbackPlacements.AddRange(validCallbackPlacements);
        if (withheldCallbackNodes.Count > 0)
        {
            nodes.RemoveAll(node => withheldCallbackNodes.Contains(node.Id));
            edges.RemoveAll(edge => withheldCallbackNodes.Contains(edge.Source) || withheldCallbackNodes.Contains(edge.Target));
        }
        foreach (var candidate in normalizedCandidates)
        {
            var node = AddWorkerControl(request, entry, actionNode, nodes, edges, candidate.Method, candidate.Kind,
                candidate.Detail, candidate.Anchor, candidate.Region, candidate.Block, candidate.Containers, candidate.Evidence);
            placements.Add(new ScenarioFlowPlacement(node.Id, candidate.Method, candidate.Anchor,
                candidate.Containers, [], node.Evidence, node.Certainty));
        }
        placements.AddRange(callbackPlacements);
        return new ScenarioTopology([], [], [], [], distinctContainers.ToImmutableArray(), placements.OrderBy(item => item.ScenarioNode.Value, StringComparer.Ordinal).ToImmutableArray());

        static bool IsSemaphoreAcquire(InvocationFlowNode node)
            => node.TargetIdentity is { } identity && identity.AssemblyIdentity == "System.Threading"
                && identity.AssemblyVersion == "10.0.0.0" && node.IsPlatformTarget
                && identity.ContainingMetadataType == "System.Threading.SemaphoreSlim"
                && identity.MethodMetadataName == "WaitAsync" && identity.GenericArity == 0
                && identity.Parameters.Length == 1 && identity.Parameters[0].RefKind == ParameterRefKind.None
                 && identity.Parameters[0].FullyQualifiedType == "System.Threading.CancellationToken"
                 && identity.ReturnType == "System.Threading.Tasks.Task"
                 && node.TargetAssemblyFullIdentity == "System.Threading, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"
                 && node.ReceiverOriginalTypeIdentity == new FrameworkTypeIdentity("System.Threading", "10.0.0.0", "System.Threading.SemaphoreSlim")
                 && node.ReceiverOriginalTypeFullAssemblyIdentity == "System.Threading, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"
                 && node.ReceiverIdentity is not null;
        static bool IsRecognizedSemaphoreAcquire(InvocationFlowNode node)
            => node.TargetIdentity is { } identity && identity.AssemblyIdentity == "System.Threading"
                && identity.AssemblyVersion == "10.0.0.0" && node.IsPlatformTarget
                && identity.ContainingMetadataType == "System.Threading.SemaphoreSlim"
                && identity.MethodMetadataName == "WaitAsync" && identity.GenericArity == 0
                && identity.Parameters.Length == 1
                && identity.Parameters[0].RefKind == ParameterRefKind.None
                && identity.Parameters[0].FullyQualifiedType == "System.Threading.CancellationToken"
                && identity.ReturnType == "System.Threading.Tasks.Task"
                && node.TargetAssemblyFullIdentity == "System.Threading, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"
                && node.ReceiverIdentity is not null;
        static bool IsSemaphoreRelease(InvocationFlowNode node)
            => node.TargetIdentity is { } identity && identity.AssemblyIdentity == "System.Threading"
                && identity.AssemblyVersion == "10.0.0.0" && node.IsPlatformTarget
                && identity.ContainingMetadataType == "System.Threading.SemaphoreSlim"
                && identity.MethodMetadataName == "Release" && identity.GenericArity == 0
                 && identity.Parameters.IsEmpty && identity.ReturnType == "System.Int32"
                 && node.TargetAssemblyFullIdentity == "System.Threading, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"
                 && node.ReceiverOriginalTypeIdentity == new FrameworkTypeIdentity("System.Threading", "10.0.0.0", "System.Threading.SemaphoreSlim")
                 && node.ReceiverOriginalTypeFullAssemblyIdentity == "System.Threading, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"
                 && node.ReceiverIdentity is not null;

        static FlowRegion? IsExactFinallyPair(MethodFlowSnapshot flow, InvocationFlowNode acquire,
            InvocationFlowNode release, LoopNode loop)
        {
            var finallyRegions = flow.Regions.Where(region => region.Kind == FlowRegionKind.Finally
                && region.Parent is not null
                && region.StartBlockOrdinal <= release.BlockOrdinal
                && region.EndBlockOrdinal >= release.BlockOrdinal).ToArray();
            if (finallyRegions.Length != 1) { return null; }
            var finallyRegion = finallyRegions[0];
            var wrapper = flow.Regions.Where(region => region.Id == finallyRegion.Parent
                && region.Kind == FlowRegionKind.TryAndFinally).ToArray();
            if (wrapper.Length != 1) { return null; }
            var tryRegions = flow.Regions.Where(region => region.Kind == FlowRegionKind.Try
                && region.Parent == wrapper[0].Id).ToArray();
            if (tryRegions.Length != 1 || tryRegions[0].StartBlockOrdinal is null
                || tryRegions[0].EndBlockOrdinal is null || wrapper[0].StartBlockOrdinal is null
                || wrapper[0].EndBlockOrdinal is null || finallyRegion.StartBlockOrdinal is null
                || finallyRegion.EndBlockOrdinal is null) { return null; }
            var loopMembers = loop.BodyBlockOrdinals.Append(loop.HeaderBlockOrdinal).ToHashSet();
            static bool InLoop(FlowRegion region, HashSet<int> members) =>
                region.StartBlockOrdinal is { } start && region.EndBlockOrdinal is { } end
                && Enumerable.Range(start, end - start + 1).All(members.Contains);
            if (!InLoop(tryRegions[0], loopMembers)
                || wrapper[0].StartBlockOrdinal is not { } wrapperStart
                || !loopMembers.Contains(wrapperStart)) { return null; }
            var branches = flow.OrdinaryBranches.IsDefault ? [] : flow.OrdinaryBranches;
            var incoming = branches.Where(branch => branch.SourceBlockOrdinal == acquire.BlockOrdinal
                && branch.DestinationBlockOrdinal == wrapper[0].StartBlockOrdinal
                && branch.DestinationBlockOrdinal != branch.SourceBlockOrdinal).ToArray();
            return incoming.Length == 1 && release.BlockOrdinal >= finallyRegion.StartBlockOrdinal
                && release.BlockOrdinal <= finallyRegion.EndBlockOrdinal
                && acquire.EvaluationOrdinal < release.EvaluationOrdinal
                && ControlsCompatible(flow, acquire, release, allowFinallyRelease: true) ? finallyRegion : null;
        }

        static FlowRegionId? FindCanonicalParent(ScenarioFlowContainer container, IReadOnlyList<ScenarioFlowContainer> all)
        {
            var sameMethod = all.Where(item => item.Method == container.Method && item.Region != container.Region).ToArray();
            if (container.Kind == ScenarioFlowContainerKind.NaturalLoop)
            {
                return container.Parent;
            }
            return container.Parent is { } parent && sameMethod.Any(item => item.Region == parent)
                ? parent : null;
        }

        WorkerControlCandidate? NormalizeCandidate(WorkerControlCandidate candidate)
        {
            var declared = candidate.Containers;
            if (declared.IsDefaultOrEmpty)
            {
                return candidate with { Containers = [] };
            }
            var byKey = canonicalContainers.Where(item => item.Method == candidate.Method)
                .ToDictionary(item => item.Region);
            var chain = new List<FlowRegionId>();
            var seen = new HashSet<FlowRegionId>();
            var current = declared[^1];
            while (true)
            {
                if (!seen.Add(current) || !byKey.TryGetValue(current, out var container))
                {
                    return null;
                }
                chain.Insert(0, current);
                if (container.Parent is not { } parent)
                {
                    break;
                }
                current = parent;
            }
            var previous = -1;
            foreach (var semantic in declared)
            {
                var index = chain.IndexOf(semantic);
                if (index <= previous || index != previous + 1 && previous >= 0)
                {
                    return null;
                }
                previous = index;
            }
            return candidate with { Containers = chain.ToImmutableArray() };
        }

        static ImmutableArray<FlowRegionId> ContainingChain(MethodFlowSnapshot flow, int block, FlowRegionId? exact)
        {
            if (exact is { } region)
            {
                return [region];
            }
            var candidates = flow.Regions.Where(item => item.StartBlockOrdinal is { } start
                && item.EndBlockOrdinal is { } end && start <= block && block <= end).ToArray();
            var loops = flow.Nodes.OfType<LoopNode>()
                .Where(loop => loop.HeaderBlockOrdinal >= 0
                    && (loop.HeaderBlockOrdinal == block || loop.BodyBlockOrdinals.Contains(block)))
                .OrderBy(loop => loop.BodyBlockOrdinals.Length).Select(loop => loop.Region).ToList();
            var exceptions = candidates.Where(item => item.Kind is FlowRegionKind.Try or FlowRegionKind.Catch
                or FlowRegionKind.Filter or FlowRegionKind.Finally or FlowRegionKind.TryAndCatch or FlowRegionKind.TryAndFinally)
                .OrderBy(item => item.StartBlockOrdinal).Select(item => item.Id);
            loops.AddRange(exceptions);
            return loops.Distinct().ToImmutableArray();
        }

        static bool IsDirectPair(MethodFlowSnapshot flow, InvocationFlowNode acquire, InvocationFlowNode release, LoopNode loop)
        {
            if (acquire.BlockOrdinal == release.BlockOrdinal)
            {
                return acquire.EvaluationOrdinal < release.EvaluationOrdinal && ControlsCompatible(flow, acquire, release);
            }
            var exits = loop.Exits.ToHashSet();
            var pending = new Queue<FlowNodeId>([acquire.Id]);
            var reachable = new HashSet<FlowNodeId> { acquire.Id };
            while (pending.TryDequeue(out var current))
            {
                foreach (var edge in flow.Edges.Where(edge => edge.Source == current
                    && edge.Kind is FlowEdgeKind.Normal or FlowEdgeKind.True or FlowEdgeKind.False))
                {
                    if (edge.Kind == FlowEdgeKind.LoopBack || edge.Target == loop.Header || exits.Contains(edge.Target))
                    {
                        continue;
                    }
                    if (reachable.Add(edge.Target))
                    {
                        pending.Enqueue(edge.Target);
                    }
                }
            }
            if (!reachable.Contains(release.Id)) { return false; }
            return ControlsCompatible(flow, acquire, release);
        }

        static LoopNode? CanonicalLoopForBlock(MethodFlowSnapshot flow, int block)
        {
            var loops = flow.Nodes.OfType<LoopNode>()
                .Where(item => item.HeaderBlockOrdinal == block || item.BodyBlockOrdinals.Contains(block))
                .OrderBy(item => item.BodyBlockOrdinals.Length)
                .ToArray();
            return loops.Length == 0 ? null : loops[0];
        }

        static LoopNode? CanonicalLoopForRelease(MethodFlowSnapshot flow, InvocationFlowNode release)
        {
            var direct = CanonicalLoopForBlock(flow, release.BlockOrdinal);
            if (direct is not null) { return direct; }
            var finallyRegions = flow.Regions.Where(region => region.Kind == FlowRegionKind.Finally
                && region.StartBlockOrdinal <= release.BlockOrdinal
                && region.EndBlockOrdinal >= release.BlockOrdinal).ToArray();
            if (finallyRegions.Length != 1) { return null; }
            var region = finallyRegions[0];
            var wrapper = flow.Regions.FirstOrDefault(item => item.Id == region.Parent
                && item.Kind == FlowRegionKind.TryAndFinally);
            var tryRegion = wrapper is null ? null : flow.Regions.FirstOrDefault(item => item.Kind == FlowRegionKind.Try
                && item.Parent == wrapper.Id);
            if (wrapper is null || tryRegion is null) { return null; }
            var loops = flow.Nodes.OfType<LoopNode>().Where(loop => region.StartBlockOrdinal is { } start
                && region.EndBlockOrdinal is { } end
                && tryRegion.StartBlockOrdinal is { } tryStart
                && tryRegion.EndBlockOrdinal is { } tryEnd
                && Enumerable.Range(tryStart, tryEnd - tryStart + 1).All(block => loop.BodyBlockOrdinals.Contains(block)
                    || block == loop.HeaderBlockOrdinal)
                && wrapper.StartBlockOrdinal is { } wrapperStart
                && loop.BodyBlockOrdinals.Contains(wrapperStart))
                .OrderBy(loop => loop.BodyBlockOrdinals.Length).ToArray();
            return loops.Length == 0 ? null : loops[0];
        }

        static bool ControlsCompatible(MethodFlowSnapshot flow, InvocationFlowNode acquire, InvocationFlowNode release,
            bool allowFinallyRelease = false)
        {
            var acquireControls = flow.ControlDependences.Where(item => item.ControlledNode == acquire.Id).ToArray();
            var releaseControls = flow.ControlDependences.Where(item => item.ControlledNode == release.Id).ToArray();
            return acquireControls.Length == 0 && releaseControls.Length == 0
                || allowFinallyRelease && acquireControls.Length == 1 && releaseControls.Length == 0
                || acquireControls.Length == 1 && releaseControls.Length == 1
                    && acquireControls[0].ControllingDecision == releaseControls[0].ControllingDecision
                    && acquireControls[0].ControlledOnTrue == releaseControls[0].ControlledOnTrue;
        }
    }

    private sealed record WorkerControlCandidate(MethodId Method, HostedWorkerControlKind Kind, string Detail,
        FlowNodeId? Anchor, FlowRegionId? Region, int Block, ImmutableArray<FlowRegionId> Containers,
        ImmutableArray<EvidenceRef> Evidence);

    private static ScenarioNode AddWorkerControl(ScenarioAnalysisRequest request, NormalizedEntry entry, ScenarioNode actionNode,
        List<ScenarioNode> nodes, List<ScenarioEdge> edges,
        MethodId method, HostedWorkerControlKind kind, string detail, FlowNodeId? anchor, FlowRegionId? region, int block,
        ImmutableArray<FlowRegionId> containers,
        ImmutableArray<EvidenceRef> evidence)
    {
        var identity = $"{method.Value}\u001f{anchor?.Value ?? block.ToString(CultureInfo.InvariantCulture)}\u001f{kind}";
        var ordinal = StableOrdinal(identity);
        var canonicalEvidence = evidence.IsDefaultOrEmpty
                ? entry.Evidence
                : evidence.DistinctBy(item => item.Id).OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToImmutableArray();
        var node = CreateNodeWithPresentation(request.Profile.Id, entry.EntryPointId, ScenarioNodeKind.MethodCall,
            $"worker-control:{method.Value}:{kind}:{anchor?.Value ?? block.ToString(CultureInfo.InvariantCulture)}", method, null, detail,
            new ScenarioNodePresentation(TargetContainingTypeName: entry.HostedWorker!.HostedTypeName, TargetMemberName: detail,
                HostedWorkerTypeName: entry.HostedWorker.HostedTypeName, ActionKind: ScenarioActionKind.HostedWorker,
                 HostedWorkerControlKind: kind, HostedWorkerFlowRegion: region, HostedWorkerHeader: anchor,
                 HostedWorkerBlockOrdinal: block), canonicalEvidence, canonicalEvidence.Max(item => item.Certainty), ordinal);
        nodes.Add(node);
        edges.Add(CreateEdge(request.Profile.Id, entry.EntryPointId, actionNode, node, ScenarioEdgeKind.Call,
            detail, ordinal, canonicalEvidence));
        return node;

        static int StableOrdinal(string value)
        {
            var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
            return ((hash[0] << 24) | (hash[1] << 16) | (hash[2] << 8) | hash[3]) & int.MaxValue;
        }
    }

    private enum SchedulerPlacementKind
    {
        Admitted,
        BehaviorIdentityMismatch,
        MissingFlow,
        AmbiguousFlow,
        MissingAnchor,
        AmbiguousAnchor,
        DirectControlDependence,
        NonRootRegion,
    }

    private sealed record SchedulerPlacement(
        SchedulerPlacementKind Kind,
        string Token,
        string Description,
        ImmutableArray<EvidenceRef> Evidence);

    private static SchedulerPlacement ClassifySchedulerPlacement(
        ScenarioAnalysisRequest request,
        SchedulerJobFact scheduler)
    {
        if (request.Behavior.Profile is null
            || request.Behavior.Profile.Id != request.Profile.Id
            || string.IsNullOrWhiteSpace(request.Behavior.ProgramIndexFingerprint)
            || !string.Equals(request.Behavior.ProgramIndexFingerprint, request.ProgramIndex.IndexFingerprint, StringComparison.Ordinal))
        {
            return Placement(SchedulerPlacementKind.BehaviorIdentityMismatch, "behavior-identity", "behavior identity mismatch", scheduler.Evidence);
        }

        var flows = request.Behavior.MethodFlows
            .Where(candidate => candidate.Method == scheduler.RegistrationMethod)
            .OrderBy(candidate => candidate.FlowFingerprint, StringComparer.Ordinal)
            .ToArray();
        if (flows.Length == 0)
        {
            return Placement(SchedulerPlacementKind.MissingFlow, "missing-flow", "missing flow", scheduler.Evidence);
        }
        if (flows.Length > 1)
        {
            return Placement(SchedulerPlacementKind.AmbiguousFlow, "ambiguous-flow", "ambiguous flow", Combine(scheduler.Evidence, flows.SelectMany(flow => FlowEvidence(flow)).ToImmutableArray()));
        }
        var flow = flows[0];
        var anchors = BuildOperationAnchors(flow);
        if (!anchors.TryGetValue(scheduler.RegistrationOperation.Value, out var anchorIds) || anchorIds.Length == 0)
        {
            return Placement(SchedulerPlacementKind.MissingAnchor, "missing-anchor", "missing anchor", Combine(scheduler.Evidence, FlowEvidence(flow)));
        }
        if (anchorIds.Length > 1)
        {
            return Placement(SchedulerPlacementKind.AmbiguousAnchor, "ambiguous-anchor", "ambiguous anchor", Combine(scheduler.Evidence, FlowEvidence(flow), flow.Nodes.Where(node => anchorIds.Contains(node.Id)).SelectMany(node => node.Evidence).ToImmutableArray()));
        }

        var anchor = anchorIds[0];
        var dependences = flow.ControlDependences.Where(dependence => dependence.ControlledNode == anchor).ToArray();
        if (dependences.Length > 0)
        {
            return Placement(SchedulerPlacementKind.DirectControlDependence, "direct-control-dependence", "direct control dependence", Combine(scheduler.Evidence, FlowEvidence(flow), dependences.SelectMany(dependence => dependence.Evidence).ToImmutableArray()));
        }

        var regions = flow.Regions.Where(region => region.Kind is not FlowRegionKind.Root && region.Nodes.Contains(anchor)).ToArray();
        return regions.Length > 0
            ? Placement(SchedulerPlacementKind.NonRootRegion, "non-root-region", "non-root region", Combine(scheduler.Evidence, FlowEvidence(flow), regions.SelectMany(region => region.Evidence).ToImmutableArray()))
            : Placement(SchedulerPlacementKind.Admitted, "admitted", "admitted", Combine(scheduler.Evidence, FlowEvidence(flow), flow.Nodes.Where(node => node.Id == anchor).SelectMany(node => node.Evidence).ToImmutableArray()));

        static SchedulerPlacement Placement(SchedulerPlacementKind kind, string token, string description, IEnumerable<EvidenceRef> evidence)
            => new(kind, token, description, Combine(evidence.ToImmutableArray()));

        static ImmutableArray<EvidenceRef> FlowEvidence(MethodFlowSnapshot flow)
            => flow.Nodes.SelectMany(node => node.Evidence)
                .Concat(flow.Regions.SelectMany(region => region.Evidence))
                .Concat(flow.ControlDependences.SelectMany(dependence => dependence.Evidence))
                .ToImmutableArray();
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
        List<ScenarioGraphDiagnostic> diagnostics,
        HashSet<ScenarioNodeId>? withheldPersistenceAssignments = null)
    {
        var decisions = new List<ScenarioDecision>();
        var arms = new List<ScenarioArm>();
        var memberships = new List<ScenarioMembership>();
        var terminals = new List<ScenarioArmTerminal>();
        var unsupportedDecisions = new HashSet<FlowNodeId>();
        var validOperationPlacements = new Dictionary<string, ImmutableHashSet<string>>(StringComparer.Ordinal);
        var conflictingOperationPlacements = new HashSet<string>(StringComparer.Ordinal);
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
                    unsupportedDecisions.Add(decision.Id);
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
                var withholdUnsupportedTopology = IsPersistenceTopologyNode(node);
                if (withholdUnsupportedTopology
                    && firstSet.Any(dependence => unsupportedDecisions.Contains(dependence.ControllingDecision)))
                {
                    diagnostics.Add(CreateDiagnostic(
                        profileId,
                        entryPointId,
                        "SC013",
                        "The material scenario node is withheld because its unsupported decision topology cannot place the claim safely.",
                        $"{node.Method!.Value}\u001f{operation.Value}\u001funsupported-decision-topology"));
                }
                if (conflictDecisions.Length > 0
                    || (withholdUnsupportedTopology
                        && firstSet.Any(dependence => unsupportedDecisions.Contains(dependence.ControllingDecision))))
                {
                    conflictingOperationPlacements.Add(operation.Value);
                }
                else
                {
                    var placement = firstSet
                        .Select(dependence => $"{dependence.ControllingDecision.Value}\u001f{dependence.ControlledOnTrue}")
                        .ToImmutableHashSet(StringComparer.Ordinal);
                    if (validOperationPlacements.TryGetValue(operation.Value, out var existingPlacement)
                        && !existingPlacement.SetEquals(placement))
                    {
                        conflictingOperationPlacements.Add(operation.Value);
                    }
                    else
                    {
                        validOperationPlacements[operation.Value] = placement;
                    }
                }
                foreach (var dependence in firstSet
                             .Where(dependence => !withholdUnsupportedTopology
                                 || !unsupportedDecisions.Contains(dependence.ControllingDecision))
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

        if (withheldPersistenceAssignments is not null
            && decisions.Count > 0
            && request.NonGetSemanticFacts.EntityFrameworkMutations.Length > 0)
        {
            var stateNodes = nodes.Where(node => node.Kind == ScenarioNodeKind.StateAssignment && node.Operation is not null);
            foreach (var stateNode in stateNodes)
            {
                var matchingAssignments = request.NonGetSemanticFacts.StateAssignments
                    .Where(fact => fact.Method == serviceMethod && fact.Operation == stateNode.Operation)
                    .ToArray();
                if (matchingAssignments.Length != 1 || conflictingOperationPlacements.Contains(stateNode.Operation!.Value.Value)
                    || !validOperationPlacements.TryGetValue(stateNode.Operation.Value.Value, out var assignmentPlacement))
                {
                    withheldPersistenceAssignments.Add(stateNode.Id);
                    continue;
                }
                var assignment = matchingAssignments[0];

                var compatible = request.NonGetSemanticFacts.EntityFrameworkMutations
                    .Where(fact => fact.Method == serviceMethod
                        && fact.MutationKind is not (EntityFrameworkMutationKind.SaveChangesAsync or EntityFrameworkMutationKind.SaveChanges)
                        && string.Equals(fact.EntityType, AssignmentContainingType(assignment.TargetMember), StringComparison.Ordinal)
                        && fact.SequenceOrdinal > assignment.SequenceOrdinal
                        && !conflictingOperationPlacements.Contains(fact.Operation.Value)
                        && validOperationPlacements.TryGetValue(fact.Operation.Value, out var mutationPlacement)
                        && mutationPlacement.SetEquals(assignmentPlacement))
                    .Any(mutation => request.NonGetSemanticFacts.EntityFrameworkMutations.Any(save =>
                        save.Method == serviceMethod
                        && save.MutationKind is EntityFrameworkMutationKind.SaveChangesAsync or EntityFrameworkMutationKind.SaveChanges
                        && string.Equals(save.DbContextType, mutation.DbContextType, StringComparison.Ordinal)
                        && save.SequenceOrdinal > mutation.SequenceOrdinal
                        && validOperationPlacements.TryGetValue(save.Operation.Value, out var savePlacement)
                        && savePlacement.SetEquals(assignmentPlacement)
                        && !conflictingOperationPlacements.Contains(save.Operation.Value)));
                if (!compatible)
                {
                    withheldPersistenceAssignments.Add(stateNode.Id);
                }
            }
            memberships.RemoveAll(membership => withheldPersistenceAssignments.Contains(membership.ScenarioNode));
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
            or ScenarioNodeKind.ClientOperationInvocation
            or ScenarioNodeKind.OutboundHttpRequest
            or ScenarioNodeKind.MethodCall
            or ScenarioNodeKind.EntityQuery
            or ScenarioNodeKind.StateAssignment
            or ScenarioNodeKind.EntityMutation
            or ScenarioNodeKind.Result
            or ScenarioNodeKind.Outcome;

    private static bool IsPersistenceTopologyNode(ScenarioNode node)
        => node.Kind is ScenarioNodeKind.EntityQuery
            or ScenarioNodeKind.StateAssignment
            or ScenarioNodeKind.EntityMutation;

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
        // Hosted-worker lifecycle nodes carry an explicit compiler-backed chronology. Stable IDs
        // remain the identity source, but their hash ordering cannot represent start/execute/stop
        // order, so worker graphs preserve the assigned sequence ordinal first.
        var orderedNodes = (entryPoint.RootKind == ScenarioRootKind.HostedWorker
                ? nodes.OrderBy(node => node.SequenceOrdinal).ThenBy(node => node.Id.Value, StringComparer.Ordinal)
                : nodes.OrderBy(node => node.Id.Value, StringComparer.Ordinal))
            .ToImmutableArray();
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
            entryPoint.RootKind,
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
        var prunedTopology = withheld.Count == 0
            ? topology
            : topology with { FlowPlacements = topology.FlowPlacements.Where(placement => !withheld.Contains(placement.ScenarioNode)).ToImmutableArray() };
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
            BuildGraphDebugProjection(prunedNodes, prunedEdges, orderedDiagnostics, prunedTopology, prunedComposition, callbackRegions, entryPoint.RootKind),
            prunedTopology,
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
        ScenarioRootKind rootKind,
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
        var eligibleGenericBoundaries = facts.Boundaries
            .Where(boundary => !(boundary.TargetKind == CallbackTargetKind.AnonymousFunction
                && boundary.ContractProvenance == CallbackContractProvenance.Unknown))
            .Where(boundary => nodes.Any(node => node.Method == boundary.CallerMethod))
            .Where(boundary => boundary.ContractMethod is not { } contractMethod
                || request.ProgramIndex.Methods.Count(method => method.Id == contractMethod) == 1)
            .Where(boundary => boundary.TargetKind != CallbackTargetKind.MethodGroup
                || boundary.TargetMethod is not { } targetMethod
                || request.ProgramIndex.Methods.Count(method => method.Id == targetMethod) == 1)
            .ToArray();
        var ambiguousOperations = eligibleGenericBoundaries
            .SelectMany(boundary => boundary.MemberOperations.Select(operation => (boundary, operation)))
            .GroupBy(item => item.operation, StringComparer.Ordinal)
            .Where(group => group.Select(item => item.boundary.Id).Distinct().Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        var ambiguousBoundaries = eligibleGenericBoundaries
            .Where(boundary => boundary.MemberOperations.Any(ambiguousOperations.Contains))
            .ToArray();
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

            var isEligibleGenericBoundary = eligibleGenericBoundaries.Any(candidate => candidate.Id == boundary.Id);
            var isAmbiguousBoundary = isEligibleGenericBoundary
                && ambiguousBoundaries.Any(candidate => candidate.Id == boundary.Id);
            if (isAmbiguousBoundary)
            {
                var ambiguousMembers = boundary.MemberOperations.Order(StringComparer.Ordinal).ToArray();
                var ambiguousNodes = nodes
                    .Where(node => node.Operation is { } operation && ambiguousMembers.Contains(operation.Value, StringComparer.Ordinal))
                    .Select(node => node.Id)
                    .ToArray();
                withheldNodeIds.AddRange(ambiguousNodes);
                foreach (var operation in ambiguousMembers)
                {
                    diagnostics.Add(CreateDiagnostic(profileId, entryPointId,
                        "SC-CALLBACK-AMBIGUOUS-OWNERSHIP",
                        "A callback boundary was withheld because exact member-operation ownership was ambiguous.",
                        $"callback-boundary={boundary.Id.Value}; member-operation={operation}; exact member-operation ownership overlapped another eligible callback boundary.",
                        boundary.Evidence, CertaintyLevel.Conservative));
                }
                continue;
            }

            if (!nodes.Any(node => node.Method == boundary.CallerMethod))
            {
                // The boundary's caller method is not represented by a generated graph node, so the
                // boundary cannot join any member node.
                continue;
            }

            if (boundary.ContractMethod is { } contractMethod
                && request.ProgramIndex.Methods.Count(method => method.Id == contractMethod) != 1)
            {
                continue;
            }
            if (boundary.TargetKind == CallbackTargetKind.MethodGroup
                && boundary.TargetMethod is { } targetMethod
                && request.ProgramIndex.Methods.Count(method => method.Id == targetMethod) != 1)
            {
                continue;
            }

            var memberOperations = boundary.MemberOperations.ToHashSet(StringComparer.Ordinal);
            if (rootKind == ScenarioRootKind.HostedWorker)
            {
                var overlappingNodes = nodes
                    .Where(node => node.Operation is { } operation
                        && memberOperations.Contains(operation.Value)
                        && !node.Key.StartsWith($"callback:{boundary.Id.Value}:", StringComparison.Ordinal)
                        && !node.Key.StartsWith("callback:", StringComparison.Ordinal))
                    .OrderBy(node => node.Key, StringComparer.Ordinal)
                    .ToArray();
                if (overlappingNodes.Length > 0)
                {
                    var overlapEvidence = Combine(boundary.Evidence,
                        overlappingNodes.SelectMany(node => node.Evidence).ToImmutableArray());
                    foreach (var operation in boundary.MemberOperations.Order(StringComparer.Ordinal))
                    {
                        var alreadyDiagnosed = diagnostics.Any(diagnostic =>
                            diagnostic.Code == "SC-CALLBACK-OUTER-OVERLAP"
                            && diagnostic.Detail.StartsWith(
                                $"callback-boundary={boundary.Id.Value}; member-operation={operation};",
                                StringComparison.Ordinal));
                        if (!alreadyDiagnosed)
                        {
                            diagnostics.Add(CreateDiagnostic(profileId, entryPointId,
                                "SC-CALLBACK-OUTER-OVERLAP",
                                "A hosted-worker callback boundary was withheld because callback ownership overlapped outer work.",
                                $"callback-boundary={boundary.Id.Value}; member-operation={operation}; "
                                    + $"member-node=callback:{boundary.Id.Value}:{operation}; callback presentation was withheld because member ownership overlaps non-callback node(s): {string.Join(",", overlappingNodes.Select(node => node.Key))}.",
                                overlapEvidence,
                                CertaintyLevel.Conservative));
                        }
                    }
                    withheldNodeIds.AddRange(nodes
                        .Where(node => node.Key.StartsWith($"callback:{boundary.Id.Value}:", StringComparison.Ordinal))
                        .Select(node => node.Id));
                    continue;
                }
            }
            var memberNodes = nodes
                .Where(node => node.Operation is { } operation && memberOperations.Contains(operation.Value)
                    && (rootKind != ScenarioRootKind.HostedWorker
                        || node.Key == $"callback:{boundary.Id.Value}:{operation.Value}"))
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
        ImmutableArray<EvidenceRef> evidence = default,
        CertaintyLevel? certaintyOverride = null)
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
            Certainty = certaintyOverride ?? (evidence.IsDefaultOrEmpty ? CertaintyLevel.Conservative : evidence.Max(item => item.Certainty)),
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
