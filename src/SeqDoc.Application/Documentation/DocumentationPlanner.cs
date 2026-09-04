using System.Collections.Immutable;
using System.Text;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Configuration;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Core.Semantics;
using SeqDoc.Core.Wording;

namespace SeqDoc.Application.Documentation;

public static class PredicateWordingFormatter
{
    public static string Format(PredicateExpression root) => Format(root, 0);
    public static string FormatComplement(PredicateExpression root)
        => TryFormatComplement(root, out var text) ? text : "Otherwise";
    public static bool TryFormatComplement(PredicateExpression root, out string text)
    {
        if (root.Kind == PredicateExpressionKind.Negation)
        {
            text = Format(root.Children[0]);
            return true;
        }

        if (root.Kind != PredicateExpressionKind.Comparison)
        {
            text = "";
            return false;
        }
        var op = root.ComparisonOperator!.Value switch
        {
            PredicateComparisonOperatorKind.Equal => PredicateComparisonOperatorKind.NotEqual,
            PredicateComparisonOperatorKind.NotEqual => PredicateComparisonOperatorKind.Equal,
            PredicateComparisonOperatorKind.LessThan => PredicateComparisonOperatorKind.GreaterThanOrEqual,
            PredicateComparisonOperatorKind.LessThanOrEqual => PredicateComparisonOperatorKind.GreaterThan,
            PredicateComparisonOperatorKind.GreaterThan => PredicateComparisonOperatorKind.LessThanOrEqual,
            _ => PredicateComparisonOperatorKind.LessThan,
        };
        text = Format(new PredicateExpression(PredicateExpressionKind.Comparison, root.Children, root.TypeName, op)); return true;
    }
    public static string FormatSubordinate() => "Otherwise";
    private static string Format(PredicateExpression e, int parent)
    {
        int p = e.Kind switch { PredicateExpressionKind.LogicalOr => 1, PredicateExpressionKind.LogicalAnd => 2, PredicateExpressionKind.Comparison => 3, PredicateExpressionKind.BinaryArithmetic => e.ArithmeticOperator is PredicateArithmeticOperatorKind.Multiply or PredicateArithmeticOperatorKind.Divide or PredicateArithmeticOperatorKind.Remainder ? 5 : 4, PredicateExpressionKind.Negation or PredicateExpressionKind.BooleanTruth => 6, _ => 7 };
        string s = e.Kind switch
        {
            PredicateExpressionKind.NullConstant => "null",
            PredicateExpressionKind.BooleanConstant => e.ConstantValue!,
            PredicateExpressionKind.NumericConstant or PredicateExpressionKind.EnumConstant => e.ConstantValue!,
            PredicateExpressionKind.StringConstant => "\"" + Escape(e.ConstantValue!) + "\"",
            PredicateExpressionKind.CharacterConstant => "'" + Escape(e.ConstantValue!) + "'",
            PredicateExpressionKind.SymbolValue => e.DisplayName!,
            PredicateExpressionKind.OpaqueValue => "typed predicate unavailable",
            PredicateExpressionKind.BooleanTruth => Format(e.Children[0], p),
            PredicateExpressionKind.Negation => "!" + (e.Children[0].Kind is PredicateExpressionKind.SymbolValue ? Format(e.Children[0], p) : "(" + Format(e.Children[0], 0) + ")"),
            PredicateExpressionKind.Comparison => Format(e.Children[0], p) + " " + Comparison(e.ComparisonOperator!.Value) + " " + Format(e.Children[1], p),
            PredicateExpressionKind.LogicalAnd or PredicateExpressionKind.LogicalOr => Format(e.Children[0], p) + (e.Kind == PredicateExpressionKind.LogicalAnd ? " && " : " || ") + Format(e.Children[1], p),
            PredicateExpressionKind.BinaryArithmetic => Format(e.Children[0], p) + " " + Arithmetic(e.ArithmeticOperator!.Value) + " " + Format(e.Children[1], p),
            _ => "typed predicate unavailable",
        };
        if (e.Kind == PredicateExpressionKind.Comparison && e.Children[0].Kind == PredicateExpressionKind.SymbolValue && e.Children[1].Kind == PredicateExpressionKind.NullConstant && e.ComparisonOperator == PredicateComparisonOperatorKind.Equal)
        {
            s = e.Children[0].DisplayName + " is null";
        }
        if (e.Kind == PredicateExpressionKind.LogicalAnd && parent == 1)
        {
            return "(" + s + ")";
        }
        return p < parent ? "(" + s + ")" : s;
    }
    private static string Comparison(PredicateComparisonOperatorKind op) => op switch { PredicateComparisonOperatorKind.Equal => "==", PredicateComparisonOperatorKind.NotEqual => "!=", PredicateComparisonOperatorKind.LessThan => "<", PredicateComparisonOperatorKind.LessThanOrEqual => "<=", PredicateComparisonOperatorKind.GreaterThan => ">", _ => ">=" };
    private static string Arithmetic(PredicateArithmeticOperatorKind op) => op switch { PredicateArithmeticOperatorKind.Add => "+", PredicateArithmeticOperatorKind.Subtract => "-", PredicateArithmeticOperatorKind.Multiply => "*", PredicateArithmeticOperatorKind.Divide => "/", _ => "%" };
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t").Replace("\r", "\\r").Replace("`", "\\u0060");
}

/// <summary>Carries the wording and diagram plan produced for one scenario graph.</summary>
public sealed record DocumentationPlan(WordingDocument Wording, DiagramPlan Diagram);

/// <summary>
/// The only ScenarioGraph-to-wording/DiagramPlan semantic authority. The planner translates each
/// node, edge, and diagnostic into conservative user-facing phrases and renderer-neutral diagram
/// elements, retaining the graph's evidence and certainty on every element without promotion.
/// Unsupported or degraded facts become explicit technical-fallback phrases with visible
/// conservative certainty; the planner never invents domain meaning beyond what the typed scenario
/// graph proves. Renderers serialize the resulting plans and never inspect scenario graphs.
/// </summary>
public static class DocumentationPlanner
{
    private const string EntryPhraseKey = "entry";
    private const string ActionPhraseKey = "action";
    private const string MethodCallPhraseKey = "method-call";
    private const string ServiceCallPhraseKey = "service-call";
    private const string ClientOperationInvocationPhraseKey = "client-operation-invocation";
    private const string OutboundHttpRequestPhraseKey = "outbound-http-request";
    private const string HttpBoundaryParticipantKey = "http-boundary";
    private const string EntityQueryPhraseKey = "entity-query";
    private const string StateAssignmentPhraseKey = "state-assignment";
    private const string EntityMutationPhraseKey = "entity-mutation";
    private const string EntitySavePhraseKey = "entity-save";
    private const string SourceObservationPhraseKey = "source-observation";
    private const string ResultSuccessPhraseKey = "result-success";
    private const string ResultFailurePhraseKey = "result-failure";
    private const string ResultPhraseKey = "result";
    private const string OutcomePhraseKey = "outcome";
    private const string FallbackPhraseKeyPrefix = "fallback";
    private static readonly string[] ReservedParticipantKeys = ["client", "action", "dispatch", "handler", "service", "data"];

    private sealed record PresentationFilter(
        HashSet<ScenarioNodeId> HiddenNodes,
        HashSet<ScenarioEdgeId> HiddenEdges,
        int FilteredInteractionCount,
        ImmutableArray<EvidenceRef> FilteredInteractionEvidence)
    {
        public static PresentationFilter Create(
            ScenarioGraph graph,
            ImmutableSortedSet<string>? participants,
            ImmutableSortedSet<string>? calls)
        {
            participants ??= ImmutableSortedSet.Create<string>(StringComparer.Ordinal);
            calls ??= ImmutableSortedSet.Create<string>(StringComparer.Ordinal);
            var hiddenNodes = new HashSet<ScenarioNodeId>();
            foreach (var node in graph.Nodes)
            {
                string? type = NodeContainingType(node);
                bool configuredParticipant = type is not null && participants.Contains(type);
                bool builtInLogging = IsRecognizedLoggingCall(node);
                bool configuredCall = node.Kind is ScenarioNodeKind.MethodCall or ScenarioNodeKind.ClientOperationInvocation
                    && CallMatches(node, calls);
                if (configuredParticipant || configuredCall || builtInLogging)
                {
                    hiddenNodes.Add(node.Id);
                }
            }

            var hiddenEdges = new HashSet<ScenarioEdgeId>();
            var countedInteractions = new HashSet<string>(StringComparer.Ordinal);
            var filteredEvidence = new List<(string Key, EvidenceRef Evidence)>();
            foreach (var edge in graph.Edges)
            {
                var source = graph.Nodes.FirstOrDefault(node => node.Id == edge.Source);
                var target = graph.Nodes.FirstOrDefault(node => node.Id == edge.Target);
                bool hidden = hiddenNodes.Contains(edge.Source) || hiddenNodes.Contains(edge.Target);
                if (edge.Kind == ScenarioEdgeKind.Call && target is not null)
                {
                    hidden |= hiddenNodes.Contains(target.Id);
                }
                if (hidden && edge.Kind is ScenarioEdgeKind.Call or ScenarioEdgeKind.Query or ScenarioEdgeKind.Mutation
                    or ScenarioEdgeKind.Save or ScenarioEdgeKind.Dispatch or ScenarioEdgeKind.ResultSuccess
                    or ScenarioEdgeKind.ResultFailure or ScenarioEdgeKind.ResultStatus or ScenarioEdgeKind.OutcomeSuccess
                    or ScenarioEdgeKind.OutcomeFailure)
                {
                    hiddenEdges.Add(edge.Id);
                    bool hasHiddenInteractionNode = graph.Nodes.Any(node =>
                        (node.Id == edge.Source || node.Id == edge.Target)
                        && hiddenNodes.Contains(node.Id)
                        && IsFilteredInteractionNode(node));
                    if (!hasHiddenInteractionNode)
                    {
                        countedInteractions.Add("edge:" + edge.Id.Value);
                        filteredEvidence.AddRange(edge.Evidence.Select(evidence => ("edge:" + edge.Id.Value + ":" + evidence.Id.Value, evidence)));
                    }
                }
            }

            foreach (var node in graph.Nodes.Where(node => hiddenNodes.Contains(node.Id) && IsFilteredInteractionNode(node)))
            {
                countedInteractions.Add("node:" + node.Id.Value);
                filteredEvidence.AddRange(node.Evidence.Select(evidence => ("node:" + node.Id.Value + ":" + evidence.Id.Value, evidence)));
            }

            return new PresentationFilter(
                hiddenNodes,
                hiddenEdges,
                countedInteractions.Count,
                filteredEvidence.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => ConservativeCopy(item.Evidence)).ToImmutableArray());
        }
    }

    public static DocumentationPlan Plan(
        ScenarioGraph graph,
        ImmutableSortedSet<string>? excludeParticipants = null,
        ImmutableSortedSet<string>? excludeCalls = null,
        DiagramBudget? diagramBudget = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ValidateStructuralExclusions(graph, excludeParticipants);
        var hostedTopology = ValidateHostedWorkerTopology(graph);
        var filter = PresentationFilter.Create(graph, excludeParticipants, excludeCalls);
        if (hostedTopology is not null)
        {
            filter.HiddenNodes.UnionWith(hostedTopology.HiddenNodes);
        }

        var phrases = BuildPhrases(graph, filter, hostedTopology);
        var diagram = BuildDiagram(graph, filter, diagramBudget, hostedTopology);

        var wording = new WordingDocument(
            graph.EntryPoint,
            graph.Profile,
            OperationKey(graph),
            graph.RootKind is ScenarioRootKind.ConfiguredMethod or ScenarioRootKind.HostedWorker
                || IsServiceOperationRoot(graph)
                ? OperationKey(graph)
                : $"{HttpMethodCanonicalToken.Get(graph.HttpMethod)} {graph.CanonicalRoute}",
            phrases,
            BuildWordingDebugProjection(phrases));

        return new DocumentationPlan(wording, diagram);
    }

    private static ImmutableArray<WordingPhrase> BuildPhrases(
        ScenarioGraph graph, PresentationFilter filter, HostedTopologyValidation? hostedTopology)
    {
        var phrases = new List<WordingPhrase>();
        var phraseOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
        var callbackMemberNodes = graph.CallbackRegions.SelectMany(region => region.MemberNodes).ToHashSet();
        // The planner owns semantic phrase order: entry, action, service call, then the unified
        // source-ordered facts (query/assignment/mutation/save by compiler ordinal), then
        // observations, then failure result/outcome before success result/outcome. Within one rank
        // the stable node identity breaks ties deterministically. Renderers preserve this order
        // verbatim.
        foreach (var node in graph.Nodes
                     .OrderBy(node => NodeOrderKey(graph, node).Segment)
                     .ThenBy(node => NodeOrderKey(graph, node).Ordinal)
                     .ThenBy(node => NodeOrderKey(graph, node).Rank)
                     .ThenBy(node => node.Id.Value, StringComparer.Ordinal))
        {
            if (filter.HiddenNodes.Contains(node.Id))
            {
                continue;
            }
            switch (node.Kind)
            {
                case ScenarioNodeKind.EntryPoint:
                    CreatePhrase(
                        graph,
                        phraseOrdinals,
                        phrases,
                        WordingPhraseKind.Statement,
                        EntryPhraseKey,
                         graph.RootKind == ScenarioRootKind.ConfiguredMethod
                             ? "Configured method entry point."
                             : graph.RootKind == ScenarioRootKind.HostedWorker
                             ? "Hosted worker lifecycle entry point."
                             : IsServiceOperationRoot(graph)
                             ? $"Service contract operation entry point {OperationKey(graph)}."
                             : $"HTTP {HttpMethodCanonicalToken.Get(graph.HttpMethod)} entry point at route \"{graph.CanonicalRoute}\".",
                        node.Evidence,
                        node.Certainty);
                    break;
                case ScenarioNodeKind.Action:
                    CreatePhrase(
                        graph,
                        phraseOrdinals,
                        phrases,
                        WordingPhraseKind.Statement,
                        ActionPhraseKey,
                         graph.RootKind == ScenarioRootKind.ConfiguredMethod
                             ? $"The selected method {node.Presentation?.ConfiguredDisplaySignature ?? graph.OperationKey} executes."
                            : node.Presentation?.ActionKind == ScenarioActionKind.HostedWorker
                            ? $"The hosted worker {ShortTypeName(node.Presentation?.HostedWorkerTypeName ?? graph.OperationKey)} lifecycle is analyzed."
                            : node.Presentation?.ActionKind == ScenarioActionKind.MinimalApiHandler
                            ? "The Minimal API handler executes."
                            : node.Presentation?.ActionKind == ScenarioActionKind.ServiceOperation
                            ? "The service contract operation executes."
                            : "The controller action executes.",
                        node.Evidence,
                        node.Certainty);
                    break;
                case ScenarioNodeKind.ServiceCall:
                    CreatePhrase(
                        graph,
                        phraseOrdinals,
                        phrases,
                        WordingPhraseKind.Statement,
                        ServiceCallPhraseKey,
                         callbackMemberNodes.Contains(node.Id)
                             ? "The source callback operation is documented from the callback body."
                             : BuildServiceCallText(node),
                        node.Evidence,
                        node.Certainty);
                    break;
                case ScenarioNodeKind.ClientOperationInvocation:
                    CreatePhrase(
                        graph,
                        phraseOrdinals,
                        phrases,
                        WordingPhraseKind.Statement,
                        ClientOperationInvocationPhraseKey,
                        BuildClientOperationInvocationText(node),
                        node.Evidence,
                        node.Certainty);
                    break;
                case ScenarioNodeKind.OutboundHttpRequest:
                    switch (node.Presentation?.OutboundHttpRequestKind)
                    {
                        case OutboundHttpRequestKind.Get:
                            CreatePhrase(
                                graph, phraseOrdinals, phrases, WordingPhraseKind.Statement,
                                OutboundHttpRequestPhraseKey,
                                "The method calls HttpClient.GetAsync at an outbound HTTP GET request boundary.",
                                node.Evidence, node.Certainty);
                            break;
                        case OutboundHttpRequestKind.Post:
                            CreatePhrase(
                                graph, phraseOrdinals, phrases, WordingPhraseKind.Statement,
                                OutboundHttpRequestPhraseKey,
                                "The method calls HttpClient.PostAsync at an outbound HTTP POST request boundary.",
                                node.Evidence, node.Certainty);
                            break;
                        default:
                            // Unknown/unkinded outbound HTTP node: withhold, no phrase.
                            break;
                    }

                    break;
                case ScenarioNodeKind.MethodCall:
                    if (IsRecognizedLoggingCall(node))
                    {
                        // Recognized logging-framework calls are hidden from presentation by
                        // default; the node and its evidence remain in the graph for audit.
                        break;
                    }
                    CreatePhrase(
                        graph,
                        phraseOrdinals,
                        phrases,
                        WordingPhraseKind.Statement,
                        MethodCallPhraseKey,
                         callbackMemberNodes.Contains(node.Id)
                             ? "The source callback operation is documented from the callback body."
                             : BuildMethodCallText(graph, node),
                        node.Evidence,
                        node.Certainty);
                    break;
                case ScenarioNodeKind.Dispatch:
                    var requestTypeName = node.Presentation?.RequestTypeName;
                    CreatePhrase(graph, phraseOrdinals, phrases, WordingPhraseKind.Statement, "dispatch",
                        $"Dispatches {(!string.IsNullOrWhiteSpace(requestTypeName) ? ShortTypeName(requestTypeName) : "a request")}",
                        node.Evidence, node.Certainty);
                    break;
                case ScenarioNodeKind.Handler:
                    var dispatchNode = graph.Nodes.FirstOrDefault(candidate => candidate.Kind == ScenarioNodeKind.Dispatch);
                    var dispatchPresentation = dispatchNode?.Presentation;
                    CreatePhrase(graph, phraseOrdinals, phrases, WordingPhraseKind.Statement, "handler",
                        node.Presentation?.HandlerBodyAvailable == false
                            ? "The handler body is unavailable"
                            : $"Routes to {node.Presentation?.HandlerTypeName ?? ((dispatchPresentation?.RequestTypeName) is { } request ? request + "Handler" : "handler")}", node.Evidence, node.Certainty);
                    break;
                case ScenarioNodeKind.EntityQuery:
                    CreatePhrase(
                        graph,
                        phraseOrdinals,
                        phrases,
                        WordingPhraseKind.Statement,
                        EntityQueryPhraseKey,
                        BuildQueryPhraseText(node),
                        node.Evidence,
                        node.Certainty);
                    break;
                case ScenarioNodeKind.StateAssignment:
                    CreatePhrase(
                        graph,
                        phraseOrdinals,
                        phrases,
                        WordingPhraseKind.Statement,
                        StateAssignmentPhraseKey,
                        $"The service assigns: {node.Detail}.",
                        node.Evidence,
                        node.Certainty);
                    break;
                case ScenarioNodeKind.EntityMutation:
                    if (IsSaveNode(node))
                    {
                        CreatePhrase(
                            graph,
                            phraseOrdinals,
                            phrases,
                            WordingPhraseKind.Statement,
                            EntitySavePhraseKey,
                            BuildSavePhraseText(node),
                            node.Evidence,
                            node.Certainty);
                    }
                    else
                    {
                        CreatePhrase(
                            graph,
                            phraseOrdinals,
                            phrases,
                            WordingPhraseKind.Statement,
                            EntityMutationPhraseKey,
                            BuildMutationPhraseText(node),
                            node.Evidence,
                            node.Certainty);
                    }

                    break;
                case ScenarioNodeKind.SourceObservation:
                    CreatePhrase(
                        graph,
                        phraseOrdinals,
                        phrases,
                        WordingPhraseKind.Statement,
                        SourceObservationPhraseKey,
                        BuildSourceObservationText(node),
                        node.Evidence,
                        node.Certainty);
                    break;
                case ScenarioNodeKind.Result:
                    if (string.Equals(node.Key, "result-success", StringComparison.Ordinal))
                    {
                        CreatePhrase(
                            graph,
                            phraseOrdinals,
                            phrases,
                            WordingPhraseKind.Statement,
                            ResultSuccessPhraseKey,
                            $"On success the service returns: {ResultSuccessLabel()}.",
                            node.Evidence,
                            node.Certainty);
                    }
                    else if (string.Equals(node.Key, "result-failure", StringComparison.Ordinal))
                    {
                        CreatePhrase(
                            graph,
                            phraseOrdinals,
                            phrases,
                            WordingPhraseKind.Statement,
                            ResultFailurePhraseKey,
                            $"On failure the service returns: {ResultFailureLabel(node)}.",
                            node.Evidence,
                            node.Certainty);
                    }
                    else
                    {
                        CreatePhrase(
                            graph,
                            phraseOrdinals,
                            phrases,
                            WordingPhraseKind.Statement,
                            ResultPhraseKey,
                            "The service result is a status outcome.",
                            node.Evidence,
                            node.Certainty);
                    }

                    break;
                case ScenarioNodeKind.Outcome:
                    CreatePhrase(
                        graph,
                        phraseOrdinals,
                        phrases,
                        WordingPhraseKind.Statement,
                        OutcomePhraseKey,
                         node.Presentation?.ActionKind == ScenarioActionKind.MinimalApiHandler
                             ? $"The Minimal API handler responds with HTTP {node.Presentation.OutcomeStatusCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "an unknown status"}."
                             : $"The controller responds: {OutcomeReadableLabel(node) ?? node.Detail}.",
                        node.Evidence,
                        node.Certainty);
                    break;
                case ScenarioNodeKind.Delay:
                    CreatePhrase(
                        graph,
                        phraseOrdinals,
                        phrases,
                        WordingPhraseKind.Statement,
                        "handler-delay",
                        FormatHandlerDelayPhrase(node.Detail),
                        node.Evidence,
                        node.Certainty);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(graph),
                        $"Undefined scenario node kind '{node.Kind}'.");
            }
        }

        // Unsupported or degraded joins are visible fallbacks, never invented business meaning. Each
        // fallback is grounded in the closest relevant typed node's evidence (for example the
        // entity-query node for a degraded predicate), falling back to the entry-point evidence only
        // when no typed node exists. Fallback certainty is always conservative because the graph
        // withheld a confident claim.
        var entryEvidence = graph.Nodes
            .FirstOrDefault(node => node.Kind == ScenarioNodeKind.EntryPoint)
            ?.Evidence ?? SourceEvidenceFallback(graph);
        foreach (var diagnostic in graph.Diagnostics.OrderBy(diagnostic => diagnostic.Id.Value, StringComparer.Ordinal))
        {
            ScenarioNodeKind? closestKind = DiagnosticNodeKind(diagnostic.Code);
            var closestNode = closestKind is null
                ? null
                : graph.Nodes
                     .OrderBy(node => SemanticNodeRank(graph, node))
                     .ThenBy(node => node.SequenceOrdinal)
                     .ThenBy(node => node.Id.Value, StringComparer.Ordinal)
                    .FirstOrDefault(node => node.Kind == closestKind);
            CreatePhrase(
                graph,
                phraseOrdinals,
                phrases,
                WordingPhraseKind.TechnicalFallback,
                $"{FallbackPhraseKeyPrefix}:{diagnostic.Code}",
                BuildFallbackText(diagnostic),
                diagnostic.Evidence.IsDefaultOrEmpty
                    ? closestNode?.Evidence ?? entryEvidence
                    : diagnostic.Evidence.Select(ConservativeCopy).ToImmutableArray(),
                CertaintyLevel.Conservative);
        }

        // Withheld decision and callback boundaries retain their unsupported status as explicit
        // technical fallbacks: a decision without exact compiler-evidenced owner predicate wording
        // is never presented with a generic label, and a source-condition callback region has no
        // exact framework-condition wording. Every phrase is grounded in the affected decision's or
        // region's own evidence (conservatively) with a stable condition-grounded key, and absorbed
        // owner-group subordinates are excluded because their boundary is presented by the exact
        // owner.
        foreach (var decision in graph.Topology.Decisions
                     .Where(decision => !TryGetExactPredicateLabel(decision, out _)
                          && DecisionHasVisibleMember(graph, decision, filter)
                         && !IsAbsorbedSubordinate(graph, decision))
                     .OrderBy(decision => decision.Id.Value, StringComparer.Ordinal))
        {
            CreatePhrase(
                graph,
                phraseOrdinals,
                phrases,
                WordingPhraseKind.TechnicalFallback,
                $"{FallbackPhraseKeyPrefix}:DP005:{decision.Condition.Value}",
                "Technical fallback: a conditional boundary lacks exact compiler-evidenced predicate wording; the guarded behavior is withheld rather than labeled generically.",
                decision.Evidence.Select(ConservativeCopy).ToImmutableArray(),
                CertaintyLevel.Conservative);
        }

        foreach (var region in graph.CallbackRegions
                     .Where(region => region.FrameworkCondition != FrameworkCallbackConditionKind.CacheMiss
                           && RegionHasVisibleMember(graph, region, filter)
                           && !(hostedTopology?.IsValid == true
                               && HostedCallbackRegionIsRepresented(graph, region, filter, hostedTopology)))
                     .OrderBy(region => region.Id.Value, StringComparer.Ordinal))
        {
            CreatePhrase(
                graph,
                phraseOrdinals,
                phrases,
                WordingPhraseKind.TechnicalFallback,
                $"{FallbackPhraseKeyPrefix}:DP003:{region.Id.Value}",
                "Technical fallback: a source-condition callback region has no exact framework-condition wording; the guarded behavior is withheld.",
                region.Evidence.Select(ConservativeCopy).ToImmutableArray(),
                CertaintyLevel.Conservative);
        }

        if (filter.FilteredInteractionCount > 0)
        {
            CreatePhrase(graph, phraseOrdinals, phrases, WordingPhraseKind.TechnicalFallback,
                "fallback:DP-FILTER",
                $"Technical fallback: {filter.FilteredInteractionCount} interaction(s) were filtered from presentation.",
                filter.FilteredInteractionEvidence, CertaintyLevel.Conservative);
        }

        return phrases.ToImmutableArray();
    }

    private static bool HostedCallbackRegionIsRepresented(
        ScenarioGraph graph,
        ScenarioCallbackRegion region,
        PresentationFilter filter,
        HostedTopologyValidation hostedTopology)
    {
        var placements = graph.Topology.FlowPlacements;
        var containers = graph.Topology.FlowContainers;
        return region.MemberNodes.All(member =>
        {
            var node = graph.Nodes.FirstOrDefault(candidate => candidate.Id == member);
            if (node is null || filter.HiddenNodes.Contains(member) || hostedTopology.HiddenNodes.Contains(member))
            {
                return false;
            }

            var owned = placements.Where(placement => placement.ScenarioNode == member).ToArray();
            return owned.Length == 1
                && owned[0].Method == graph.RootMethod
                && owned[0].Containers.Length > 0
                && owned[0].Containers.All(container => containers.Count(candidate =>
                    candidate.Region == container && candidate.Method == graph.RootMethod) == 1);
        });
    }

    /// <summary>
    /// Maps each known scenario diagnostic to the closest relevant typed scenario node so fallback
    /// evidence reflects the affected fact rather than the entry point. Unknown codes stay untyped
    /// and fall back to entry-point evidence.
    /// </summary>
    private static ScenarioNodeKind? DiagnosticNodeKind(string code) => code switch
    {
        "SC001" => ScenarioNodeKind.ServiceCall,
        "SC003" => ScenarioNodeKind.EntityQuery,
        "SC004" => ScenarioNodeKind.Outcome,
        "SC005" => ScenarioNodeKind.EntityQuery,
        "SC006" => ScenarioNodeKind.Result,
        "SC007" => ScenarioNodeKind.Result,
        "SC010" => ScenarioNodeKind.Outcome,
        "SC014" => ScenarioNodeKind.EntityQuery,
        _ => null,
    };

    private static DiagramPlan BuildDiagram(ScenarioGraph graph, PresentationFilter filter, DiagramBudget? diagramBudget,
        HostedTopologyValidation? hostedTopology)
    {
        var participants = new Dictionary<string, DiagramParticipant>(StringComparer.Ordinal);
        var messages = new List<DiagramMessage>();
        var successMessages = new List<string>();
        var failureMessages = new List<string>();
        var successEvidence = new List<EvidenceRef>();
        var failureEvidence = new List<EvidenceRef>();

        var entryNode = graph.Nodes.FirstOrDefault(node => node.Kind == ScenarioNodeKind.EntryPoint);
        var actionNode = graph.Nodes.FirstOrDefault(node => node.Kind == ScenarioNodeKind.Action);
        var serviceNode = graph.Nodes.FirstOrDefault(node => node.Kind == ScenarioNodeKind.ServiceCall);
        var dataNode = graph.Nodes.FirstOrDefault(node =>
            node.Kind is ScenarioNodeKind.EntityQuery or ScenarioNodeKind.EntityMutation);
        var methodCallParticipantKeys = BuildMethodCallParticipantKeys(graph, actionNode);
        bool configuredRoot = graph.RootKind == ScenarioRootKind.ConfiguredMethod;
        bool hostedWorkerRoot = graph.RootKind == ScenarioRootKind.HostedWorker;
        bool serviceOperationRoot = actionNode?.Presentation?.ActionKind == ScenarioActionKind.ServiceOperation;
        string callerKey = configuredRoot || hostedWorkerRoot ? "caller" : "client";

        // Participant labels come from typed presentation facts only. The client is a fixed role;
        // the controller, DI-resolved implementation, and DbContext use concise names resolved with
        // deterministic minimal qualification so same-short-name collisions stay distinct without
        // leaking full application namespaces. Missing presentation facts fall back to neutral role
        // labels; display naming never parses detail strings. A configured root never invents a
        // caller/client participant: the diagram begins at the selected method, so the root
        // participant carries the concise deterministic type.member label while the full signature
        // stays in behavior text and evidence.
        var participantSources = new List<(string Key, ScenarioNode? Source, string? FullTypeName, string FallbackLabel, DiagramParticipantKind Kind)>();
        if (!configuredRoot && !hostedWorkerRoot)
        {
            participantSources.Add((callerKey, entryNode, null, serviceOperationRoot ? "Service client" : "API client", DiagramParticipantKind.Client));
        }
        participantSources.Add(("action", actionNode, actionNode?.Presentation?.ControllerTypeName ?? actionNode?.Presentation?.HostedWorkerTypeName ?? actionNode?.Presentation?.ImplementationTypeName,
                 configuredRoot
                     ? ConfiguredActionDisplayName(actionNode?.Presentation) ?? "Selected method"
                     : hostedWorkerRoot
                     ? "Hosted worker lifecycle"
                     : actionNode?.Presentation?.ActionKind == ScenarioActionKind.MinimalApiHandler
                     ? "Minimal API handler"
                     : serviceOperationRoot
                     ? "Service operation"
                     : "Controller action",
                  configuredRoot || hostedWorkerRoot
                     ? DiagramParticipantKind.Unknown
                     : DiagramParticipantKind.Controller));
        if (hostedWorkerRoot && actionNode?.Presentation?.HostedWorkerTypeName is { Length: > 0 } hostedType)
        {
            participantSources.Add(("worker", actionNode, hostedType, ShortTypeName(hostedType), DiagramParticipantKind.Service));
        }
        participantSources.AddRange([
            ("dispatch", graph.Nodes.FirstOrDefault(node => node.Kind == ScenarioNodeKind.Dispatch), null,
                "Dispatcher", DiagramParticipantKind.Service),
            ("handler", graph.Nodes.FirstOrDefault(node => node.Kind == ScenarioNodeKind.Handler),
                graph.Nodes.FirstOrDefault(node => node.Kind == ScenarioNodeKind.Handler)?.Presentation?.HandlerTypeName,
                "Handler", DiagramParticipantKind.Service),
            ("service", serviceNode, serviceNode?.Presentation?.ImplementationTypeName, "Service", DiagramParticipantKind.Service),
            ("data", dataNode, dataNode?.Presentation?.DbContextTypeName, "Data store", DiagramParticipantKind.Data),
        ]);
        var outboundHttpNode = graph.Nodes.FirstOrDefault(node =>
            node.Kind == ScenarioNodeKind.OutboundHttpRequest
            && node.Presentation?.OutboundHttpRequestKind is OutboundHttpRequestKind.Get or OutboundHttpRequestKind.Post
            && !filter.HiddenNodes.Contains(node.Id));
        if (outboundHttpNode is not null)
        {
            participantSources.Add((HttpBoundaryParticipantKey, outboundHttpNode, null, "HTTP boundary", DiagramParticipantKind.Unknown));
        }
        foreach (var group in graph.Nodes
                     .Where(node => node.Kind is ScenarioNodeKind.MethodCall or ScenarioNodeKind.ClientOperationInvocation
                         && !string.IsNullOrWhiteSpace(node.Presentation?.TargetContainingTypeName)
                          && !filter.HiddenNodes.Contains(node.Id))
                     .GroupBy(node => node.Presentation!.TargetContainingTypeName!, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var node = group.OrderBy(item => item.Id.Value, StringComparer.Ordinal).First();
            participantSources.Add((
                methodCallParticipantKeys[group.Key],
                node,
                group.Key,
                ShortTypeName(group.Key),
                DiagramParticipantKind.Unknown));
        }
        var participantLabels = ResolveParticipantLabels(
            participantSources.Select(source => (source.Key, source.FullTypeName, source.FallbackLabel)));
        var collidingMethodTypes = methodCallParticipantKeys.Keys
            .GroupBy(NormalizeParticipantKey, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .ToHashSet(StringComparer.Ordinal);
        foreach (string type in collidingMethodTypes)
        {
            participantLabels[methodCallParticipantKeys[type]] = type;
        }
        if (actionNode?.Presentation is { ActionKind: ScenarioActionKind.ControllerAction, ControllerTypeName: { Length: > 0 } controllerType, ActionMethodName: { Length: > 0 } actionMethod })
        {
            participantLabels["action"] = $"{ShortTypeName(controllerType)}.{actionMethod}";
        }
        else if (actionNode?.Presentation is { ActionKind: ScenarioActionKind.MinimalApiHandler, ControllerTypeName: { Length: > 0 } minimalControllerType, ActionMethodName: { Length: > 0 } minimalActionMethod })
        {
            participantLabels["action"] = $"{ShortTypeName(minimalControllerType)}.{minimalActionMethod}";
        }
        else if (actionNode?.Presentation is { ActionKind: ScenarioActionKind.MinimalApiHandler, ControllerTypeName: { Length: > 0 } legacyAction }
                 && legacyAction.Contains('.', StringComparison.Ordinal))
        {
            // Preserve the pre-typed hand-authored fixture shape; compiler-projected graphs use the
            // two typed fields above, while generic lambdas still arrive without either field.
            participantLabels["action"] = legacyAction;
        }
        else if (actionNode?.Presentation is { ActionKind: ScenarioActionKind.ServiceOperation, ImplementationTypeName: { Length: > 0 } serviceImplementationType, ActionMethodName: { Length: > 0 } serviceOperationMethod })
        {
            participantLabels["action"] = $"{ShortTypeName(serviceImplementationType)}.{serviceOperationMethod}";
        }
        else if (configuredRoot)
        {
            participantLabels["action"] = ConfiguredActionDisplayName(actionNode?.Presentation)
                ?? actionNode?.Presentation?.ConfiguredDisplaySignature
                ?? "Selected method";
        }
        else if (hostedWorkerRoot)
        {
            participantLabels["action"] = "Hosted worker lifecycle";
            if (actionNode?.Presentation?.HostedWorkerTypeName is { Length: > 0 } hostedWorkerType)
            {
                participantLabels["worker"] = ShortTypeName(hostedWorkerType);
            }
        }
        // A typed composition owns the service participant's role: the contract type (for example
        // ICustomerService) humanized to its namespace-free role ("Customer service"), never the
        // first encountered implementation name. The label is resolved before participants are
        // emitted so reversed node order can never change the participant identity or evidence.
        if (graph.Composition is not null && participantLabels.ContainsKey("service"))
        {
            participantLabels["service"] = BuildCompositionServiceLabel(graph.Composition.ServiceType);
        }
        foreach (var typeName in methodCallParticipantKeys)
        {
            // The resolver may already have assigned a concise collision-safe label (or the
            // configured root action label). Never replace that deterministic presentation with a
            // canonical type name.
            participantLabels.TryAdd(typeName.Value, typeName.Key);
        }

        // The concise HTTP action label is allowed to be short only while it remains unique. A
        // collision can be introduced by a later typed-role override, so qualify the action with
        // its compiler-proven controller type and member after every label override has completed.
        if (actionNode?.Presentation is { ActionKind: ScenarioActionKind.ControllerAction, ControllerTypeName: { Length: > 0 } qualifiedControllerType, ActionMethodName: { Length: > 0 } qualifiedActionMethod }
            && participantLabels
                .Where(item => item.Key != "action")
                .Any(item => string.Equals(item.Value, participantLabels["action"], StringComparison.Ordinal)))
        {
            participantLabels["action"] = $"{qualifiedControllerType}.{qualifiedActionMethod}";
        }
        else if (actionNode?.Presentation is { ActionKind: ScenarioActionKind.ServiceOperation, ImplementationTypeName: { Length: > 0 } qualifiedServiceType, ActionMethodName: { Length: > 0 } qualifiedServiceMethod }
            && participantLabels
                .Where(item => item.Key != "action")
                .Any(item => string.Equals(item.Value, participantLabels["action"], StringComparison.Ordinal)))
        {
            participantLabels["action"] = $"{qualifiedServiceType}.{qualifiedServiceMethod}";
        }

        foreach (var source in participantSources)
        {
            if (source.Source is not null && !filter.HiddenNodes.Contains(source.Source.Id))
            {
                AddParticipant(participants, graph, source.Key, participantLabels[source.Key], source.Kind, source.Source);
            }
        }

        // The planner owns semantic edge order: client request, action call, then the unified
        // source-ordered fact messages (query/assignment/mutation/save by compiler ordinal), then
        // failure result/outcome before success result/outcome. Renderers serialize the resulting
        // message array verbatim; the fragment tree reuses the same edge order so every message
        // reference and arm partition stay deterministic.
        var orderedMessageRefs = new List<(ScenarioNodeId Node, DiagramPlanElementId Ref)>();
        var topologyWithheldPersistenceNodes = graph.Nodes
            .Where(node => node.Kind is ScenarioNodeKind.EntityQuery
                or ScenarioNodeKind.StateAssignment
                or ScenarioNodeKind.EntityMutation)
            .Where(node => !graph.Topology.Memberships.Any(item => item.ScenarioNode == node.Id))
            .Where(node => node.Operation is { } operation
                && graph.Diagnostics.Any(diagnostic => diagnostic.Code is "SC011" or "SC012" or "SC013"
                    && diagnostic.Detail.Contains($"\u001f{operation.Value}\u001f", StringComparison.Ordinal)))
            .Select(node => node.Id)
            .ToHashSet();
        foreach (var edge in graph.Edges
                     .OrderBy(edge => DirectCallOrder(graph, edge))
                     .ThenBy(edge => EdgeOrderKey(edge).Segment)
                     .ThenBy(edge => EdgeOrderKey(edge).Ordinal)
                     .ThenBy(edge => EdgeOrderKey(edge).Rank)
                     .ThenBy(edge => edge.Id.Value, StringComparer.Ordinal))
        {
            if (filter.HiddenEdges.Contains(edge.Id))
            {
                continue;
            }
            if (topologyWithheldPersistenceNodes.Contains(edge.Target))
            {
                continue;
            }
            switch (edge.Kind)
            {
                case ScenarioEdgeKind.Entry:
                    if (configuredRoot || hostedWorkerRoot)
                    {
                        // A configured root begins at the selected method: no invented caller
                        // participant or entry request message is planned.
                        break;
                    }
                    messages.Add(CreateMessage(
                        graph,
                        edge,
                         callerKey,
                         "action",
                        OperationKey(graph),
                        DiagramMessageKind.Request));
                    orderedMessageRefs.Add((edge.Target, CreateMessageRef(edge)));
                    break;
                case ScenarioEdgeKind.Call:
                    var callTarget = graph.Nodes.FirstOrDefault(node => node.Id == edge.Target);
                    var callSource = graph.Nodes.FirstOrDefault(node => node.Id == edge.Source);
                    if (callTarget is null || filter.HiddenNodes.Contains(callTarget.Id))
                    {
                        break;
                    }
                    if (callTarget.Kind == ScenarioNodeKind.OutboundHttpRequest)
                    {
                        var httpKind = callTarget.Presentation?.OutboundHttpRequestKind;
                        if (httpKind is OutboundHttpRequestKind.Get or OutboundHttpRequestKind.Post)
                        {
                            messages.Add(CreateMessage(
                                graph,
                                edge,
                                "action",
                                HttpBoundaryParticipantKey,
                                httpKind == OutboundHttpRequestKind.Get ? "HTTP GET request" : "HTTP POST request",
                                DiagramMessageKind.Request));
                            orderedMessageRefs.Add((edge.Target, CreateMessageRef(edge)));
                        }

                        break;
                    }
                    var sourceKey = callSource?.Kind is ScenarioNodeKind.MethodCall or ScenarioNodeKind.ClientOperationInvocation
                        && callSource.Presentation?.TargetContainingTypeName is { Length: > 0 } sourceType
                        && methodCallParticipantKeys.TryGetValue(sourceType, out var sourceParticipantKey)
                        ? sourceParticipantKey
                        : "action";
                    var targetKey = callTarget?.Presentation?.ActionKind == ScenarioActionKind.HostedWorker
                        ? "worker"
                        : callTarget?.Kind is ScenarioNodeKind.MethodCall or ScenarioNodeKind.ClientOperationInvocation
                        && callTarget.Presentation?.TargetContainingTypeName is { Length: > 0 } targetType
                        && methodCallParticipantKeys.TryGetValue(targetType, out var targetParticipantKey)
                        ? targetParticipantKey
                        : "service";
                    // Configurable exclusion: skip calls whose target type+member matches an exclude pattern.
                    messages.Add(CreateMessage(
                        graph,
                        edge,
                         sourceKey,
                        targetKey,
                        CallMessageLabel(callTarget, ServiceCalledMemberLabel(graph, edge) ?? edge.Detail),
                        DiagramMessageKind.Request));
                    orderedMessageRefs.Add((edge.Target, CreateMessageRef(edge)));
                    break;
                case ScenarioEdgeKind.Dispatch:
                    var dispatchSource = graph.Nodes.FirstOrDefault(node => node.Id == edge.Source);
                    var dispatchTarget = graph.Nodes.FirstOrDefault(node => node.Id == edge.Target);
                    messages.Add(CreateMessage(graph, edge,
                        dispatchSource?.Kind == ScenarioNodeKind.Action ? "action" : "dispatch",
                        dispatchTarget?.Kind == ScenarioNodeKind.Handler ? "handler" : "dispatch",
                        DispatchMessageLabel(dispatchSource, dispatchTarget),
                        DiagramMessageKind.Request));
                    orderedMessageRefs.Add((edge.Target, CreateMessageRef(edge)));
                    break;
                case ScenarioEdgeKind.Query:
                    messages.Add(CreateMessage(
                        graph,
                        edge,
                        "service",
                        "data",
                        BuildQueryLabel(graph, edge) ?? "Query data",
                        DiagramMessageKind.Request));
                    orderedMessageRefs.Add((edge.Target, CreateMessageRef(edge)));
                    break;
                case ScenarioEdgeKind.StateAssignment:
                case ScenarioEdgeKind.Observation:
                    // State assignments and source observations are non-interaction facts; they order
                    // wording phrases but never produce diagram messages or interactions.
                    break;
                case ScenarioEdgeKind.Mutation:
                    messages.Add(CreateMessage(
                        graph,
                        edge,
                        "service",
                        "data",
                        BuildMutationLabel(graph, edge) ?? "Update data",
                        DiagramMessageKind.Request));
                    orderedMessageRefs.Add((edge.Target, CreateMessageRef(edge)));
                    break;
                case ScenarioEdgeKind.Save:
                    messages.Add(CreateMessage(
                        graph,
                        edge,
                        "service",
                        "data",
                        BuildSaveLabel(graph, edge) ?? edge.Detail,
                        DiagramMessageKind.Request));
                    orderedMessageRefs.Add((edge.Target, CreateMessageRef(edge)));
                    break;
                case ScenarioEdgeKind.ResultSuccess:
                    messages.Add(CreateMessage(
                        graph,
                        edge,
                        "service",
                        "action",
                        ResultSuccessLabel(),
                        DiagramMessageKind.Response));
                    orderedMessageRefs.Add((edge.Target, CreateMessageRef(edge)));
                    successMessages.Add(MessageKey(edge));
                    successEvidence.AddRange(edge.Evidence);
                    break;
                case ScenarioEdgeKind.ResultFailure:
                    messages.Add(CreateMessage(
                        graph,
                        edge,
                        "service",
                        "action",
                        ResultFailureLabel(graph.Nodes.FirstOrDefault(node => node.Id == edge.Target)),
                        DiagramMessageKind.Response));
                    orderedMessageRefs.Add((edge.Target, CreateMessageRef(edge)));
                    failureMessages.Add(MessageKey(edge));
                    failureEvidence.AddRange(edge.Evidence);
                    break;
                case ScenarioEdgeKind.ResultStatus:
                    // The status result precedes both outcome paths; it belongs to both branches so
                    // failure and success diagrams both show the status result response. The label is
                    // the conservative typed status wording, never the compiler "status result" text.
                    messages.Add(CreateMessage(
                        graph,
                        edge,
                        "service",
                        "action",
                        ResultStatusLabel(),
                        DiagramMessageKind.Response));
                    orderedMessageRefs.Add((edge.Target, CreateMessageRef(edge)));
                    successMessages.Add(MessageKey(edge));
                    successEvidence.AddRange(edge.Evidence);
                    failureMessages.Add(MessageKey(edge));
                    failureEvidence.AddRange(edge.Evidence);
                    break;
                case ScenarioEdgeKind.OutcomeSuccess:
                    if (configuredRoot || hostedWorkerRoot)
                    {
                        break;
                    }
                    messages.Add(CreateMessage(
                        graph,
                        edge,
                        "action",
                         callerKey,
                        OutcomeLabel(graph, edge),
                        DiagramMessageKind.Response));
                    orderedMessageRefs.Add((edge.Target, CreateMessageRef(edge)));
                    successMessages.Add(MessageKey(edge));
                    successEvidence.AddRange(edge.Evidence);
                    break;
                case ScenarioEdgeKind.OutcomeFailure:
                    if (configuredRoot || hostedWorkerRoot)
                    {
                        break;
                    }
                    messages.Add(CreateMessage(
                        graph,
                        edge,
                         "action",
                         callerKey,
                        OutcomeLabel(graph, edge),
                        DiagramMessageKind.Response));
                    orderedMessageRefs.Add((edge.Target, CreateMessageRef(edge)));
                    failureMessages.Add(MessageKey(edge));
                    failureEvidence.AddRange(edge.Evidence);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(graph),
                        $"Undefined scenario edge kind '{edge.Kind}'.");
            }
        }

        var sequence = DiagramSequence.Empty;
        var diagnostics = ImmutableArray<DiagramPlanDiagnostic>.Empty;
        if (hostedTopology is not null)
        {
            if (hostedTopology.Diagnostic is { } hostedDiagnostic)
            {
                diagnostics = diagnostics.Add(hostedDiagnostic);
            }
        }

        // A selected dispatch handler owns a separate bounded expansion authority. It is appended
        // only when the expansion is complete and every nested step joins an exact top-level parent.
        var hasDispatchExpansion = false;
        if (graph.DispatchHandlerExpansion is { IsComplete: true } validatedExpansion)
        {
            var topLevelSteps = validatedExpansion.SourceSteps.Where(step => step.ParentDepth == 0).ToArray();
            var topLevelIds = topLevelSteps.Select(step => step.Id).ToHashSet(StringComparer.Ordinal);
            var orphanNested = validatedExpansion.SourceSteps.Any(step => step.ParentDepth == 1
                && (string.IsNullOrWhiteSpace(step.ParentStepId) || !topLevelIds.Contains(step.ParentStepId)));
            var invalidDepth = validatedExpansion.SourceSteps.Any(step => step.ParentDepth is < 0 or > 1);
            if (orphanNested || invalidDepth)
            {
                diagnostics = [new DiagramPlanDiagnostic(
                    new DiagnosticId($"diagnostic:v1:DP-DISPATCH-ORPHAN:{graph.EntryPoint.Value}"),
                    "DP-DISPATCH-ORPHAN",
                    "The selected dispatch expansion contains an unjoined nested step and was withheld.",
                     OperationKey(graph))];
            }
            else
            {
                hasDispatchExpansion = true;
            }
        }

        if (hasDispatchExpansion && graph.DispatchHandlerExpansion is { } expansion)
        {
            foreach (var participant in expansion.Participants
                         .Where(item => item.Key != "request")
                         .Where(item => !participants.ContainsKey(item.Key)))
            {
                participants.Add(participant.Key, new DiagramParticipant(
                    CreateElementId(graph, "participant", participant.Key), participant.Key, participant.Label,
                    participant.Key is "request" ? DiagramParticipantKind.Client : DiagramParticipantKind.Service,
                    expansion.Evidence, expansion.Certainty));
            }

            var expansionMessageRefs = new Dictionary<string, DiagramPlanElementId>(StringComparer.Ordinal);
            foreach (var step in expansion.SourceSteps)
            {
                var source = step.ParentDepth == 0 ? "handler" : ParentParticipantKey(step, expansion);
                var target = ParticipantKey(step, expansion);
                if (!participants.ContainsKey(target))
                {
                    target = "handler";
                }
                var message = new DiagramMessage(
                    CreateElementId(graph, "message", $"dispatch-step:{step.Id}"), $"dispatch-step:{step.Id}",
                    source, target, step.Label, DiagramMessageKind.Request, step.Evidence, step.Certainty);
                messages.Add(message);
                expansionMessageRefs[step.Id] = message.Id;
            }

            if (expansion.Return is { } handlerReturn)
            {
                var message = new DiagramMessage(
                    CreateElementId(graph, "message", "dispatch-return"), "dispatch-return", "handler", "action",
                    $"return {handlerReturn.TypeName}", DiagramMessageKind.Response, handlerReturn.Evidence, handlerReturn.Certainty);
                messages.Add(message);
                expansionMessageRefs["return"] = message.Id;
            }

            var expansionElements = new List<DiagramSequenceElement>();
            var loop = expansion.Loops.SingleOrDefault();
            var loopMembers = loop?.MemberSteps.Select(step => step.Id).ToHashSet(StringComparer.Ordinal) ?? [];
            var childrenOf = (string parentId) => expansion.SourceSteps
                .Where(step => step.ParentDepth == 1 && step.ParentStepId == parentId)
                .OrderBy(step => step.SourceOrdinal)
                .ThenBy(step => step.Id, StringComparer.Ordinal)
                .ToArray();
            var loopRefs = loop is null ? [] : loop.MemberSteps
                .SelectMany(member => new[] { member }.Concat(childrenOf(member.Id)))
                .Select(step => expansionMessageRefs[step.Id])
                .ToImmutableArray();
            var emittedLoop = false;
            foreach (var step in expansion.SourceSteps.Where(step => step.ParentDepth == 0))
            {
                if (loop is not null && !emittedLoop && step.Id == loop.MemberSteps[0].Id)
                {
                    expansionElements.Add(DiagramSequenceElement.Fragment(new DiagramFragment(
                        CreateElementId(graph, "fragment", $"dispatch-loop:{loop.Key}"), loop.Key, loop.Label,
                        DiagramFragmentKind.Loop, [], loopRefs, [], loop.Evidence, loop.Certainty)));
                    emittedLoop = true;
                }

                if (loopMembers.Contains(step.Id))
                {
                    continue;
                }

                if (expansionMessageRefs.TryGetValue(step.Id, out var reference))
                {
                    expansionElements.Add(DiagramSequenceElement.MessageRef(reference));
                    expansionElements.AddRange(childrenOf(step.Id).Select(child =>
                        DiagramSequenceElement.MessageRef(expansionMessageRefs[child.Id])));
                }
            }
            if (loop is not null && !emittedLoop)
            {
                expansionElements.Add(DiagramSequenceElement.Fragment(new DiagramFragment(
                    CreateElementId(graph, "fragment", $"dispatch-loop:{loop.Key}"), loop.Key, loop.Label,
                    DiagramFragmentKind.Loop, [], loopRefs, [], loop.Evidence, loop.Certainty)));
            }
            if (expansionMessageRefs.TryGetValue("return", out var returnReference))
            {
                expansionElements.Add(DiagramSequenceElement.MessageRef(returnReference));
            }

            var prefix = orderedMessageRefs.Select(item => DiagramSequenceElement.MessageRef(item.Ref));
            sequence = new DiagramSequence(prefix.Concat(expansionElements).ToImmutableArray());
        }

        var branches = new List<DiagramBranch>();
        if (graph.HandlerTopology is { } handler)
        {
            var handlerOperations = handler.Delays
                .Select(delay => (SourceOrdinal: delay.SourceOrdinal,
                    Evidence: delay.Evidence, Certainty: delay.Certainty,
                    Message: new DiagramMessage(
                        CreateElementId(graph, "message", $"handler-delay:{delay.SourceOrdinal}"),
                        $"handler-delay:{delay.SourceOrdinal}", "action", "action",
                        FormatHandlerDelay(delay.Milliseconds), DiagramMessageKind.Request,
                        delay.Evidence, delay.Certainty)))
                .Concat(handler.Outcomes.Select(outcome => (SourceOrdinal: outcome.SourceOrdinal,
                    Evidence: outcome.Evidence, Certainty: outcome.Certainty,
                    Message: new DiagramMessage(
                        CreateElementId(graph, "message", $"handler-outcome:{outcome.SourceOrdinal}"),
                        $"handler-outcome:{outcome.SourceOrdinal}", "action", "client",
                        $"HTTP {outcome.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                        DiagramMessageKind.Response, outcome.Evidence, outcome.Certainty))))
                .OrderBy(item => item.SourceOrdinal)
                .ToArray();
            foreach (var operation in handlerOperations)
            {
                messages.Add(operation.Message);
            }
        }

        // A reviewed topology or a typed service composition owns the nested fragment tree; a graph
        // with neither keeps the accepted flat failure/success branch output byte-stable with an
        // empty sequence. A composition graph (accepted contract/accepted contract) is never emitted through the legacy
        // branches because its alternatives are represented exactly by the one configuration Alt.
        bool hasTopology = graph.Topology.Decisions.Length > 0;
        bool hasHandlerTopology = graph.HandlerTopology is not null;
        bool hasComposition = graph.Composition is not null;
        if (hasDispatchExpansion)
        {
            // Sequence was built from the exact expansion above.
        }
        else if (hasHandlerTopology)
        {
            sequence = BuildHandlerSequence(graph, graph.HandlerTopology!, messages);
        }
        else if (hasTopology)
        {
            (sequence, diagnostics, var withheldRefs) = BuildFragmentSequence(graph, orderedMessageRefs);
            if (!withheldRefs.IsDefaultOrEmpty)
            {
                // A withheld message is never claimed by the sequence tree, so it must also leave
                // the planned message array; DiagramPlan coverage validation requires every planned
                // message to be referenced exactly once by a non-empty sequence.
                messages.RemoveAll(message => withheldRefs.Contains(message.Id));
            }
        }
        else if (hasComposition)
        {
            (sequence, diagnostics, var withheldRefs) = BuildCompositionSequence(graph, orderedMessageRefs);
            if (!withheldRefs.IsDefaultOrEmpty)
            {
                messages.RemoveAll(message => withheldRefs.Contains(message.Id));
            }
        }
        else if (graph.DirectCallExpansion.Steps.Length > 0
            && (!hostedWorkerRoot || graph.CallbackRegions.IsEmpty))
        {
            sequence = new DiagramSequence(orderedMessageRefs
                .Select(item => DiagramSequenceElement.MessageRef(item.Ref)).ToImmutableArray());
        }
        else if (hostedWorkerRoot)
        {
            sequence = BuildHostedWorkerSequence(graph, orderedMessageRefs, hostedTopology?.IsValid ?? true);
        }

        // Branches are ordered failure-first; renderers serialize the branch array verbatim. They
        // exist only for legacy graphs with neither topology nor composition; a structured plan owns
        // placement in the sequence tree so no message is ever duplicated across both representations.
        if (!hasTopology && !hasComposition && !hasHandlerTopology && !hasDispatchExpansion)
        {
            if (failureMessages.Count > 0)
            {
                branches.Add(CreateBranch(
                    graph,
                    "failure",
                    "Failure path",
                    DiagramBranchKind.Failure,
                    failureMessages,
                    failureEvidence));
            }

            if (successMessages.Count > 0)
            {
                branches.Add(CreateBranch(
                    graph,
                    "success",
                    "Success path",
                    DiagramBranchKind.Success,
                    successMessages,
                    successEvidence));
            }

            if (graph.Nodes.Any(node => node.Kind == ScenarioNodeKind.Dispatch))
            {
                sequence = new DiagramSequence(
                    orderedMessageRefs.Select(item => item.Ref).ToImmutableArray(), []);
            }
        }

        var orderedParticipants = participants.Values
            .OrderBy(participant => ParticipantRank(participant.Key))
            .ThenBy(participant => participant.Key, StringComparer.Ordinal)
            .ToImmutableArray();
        var orderedMessages = messages.ToImmutableArray();
        var orderedBranches = branches.ToImmutableArray();

        if (diagramBudget is not null)
        {
            var limit = diagramBudget;
            if (orderedMessages.Length <= limit.MaxMaterialMessages && orderedParticipants.Length <= limit.MaxParticipants)
            {
                goto SkipMaterialBudget;
            }
            var admitted = new List<DiagramMessage>();
            var admittedParticipants = new HashSet<string>(StringComparer.Ordinal);
            bool messageLimitReached = false;
            bool participantLimitReached = false;
            foreach (var message in orderedMessages)
            {
                if (admitted.Count >= limit.MaxMaterialMessages)
                {
                    messageLimitReached = true;
                    break;
                }
                var newParticipants = new[] { message.Source, message.Target }
                    .Where(item => !admittedParticipants.Contains(item)).Distinct(StringComparer.Ordinal).ToArray();
                if (admittedParticipants.Count + newParticipants.Length > limit.MaxParticipants)
                {
                    participantLimitReached = true;
                    break;
                }
                admitted.Add(message);
                foreach (var participant in newParticipants)
                {
                    admittedParticipants.Add(participant);
                }
            }
            var materialMessages = admitted.ToImmutableArray();
            var refs = materialMessages.Select(item => item.Id).ToHashSet();
            if (!sequence.Elements.IsEmpty)
            {
                sequence = TrimSequence(sequence, refs);
                orderedMessages = materialMessages.Where(item => SequenceContains(sequence, item.Id)).ToImmutableArray();
            }
            else
            {
                orderedMessages = materialMessages;
            }
            var used = orderedMessages.SelectMany(item => new[] { item.Source, item.Target }).ToHashSet(StringComparer.Ordinal);
            orderedParticipants = orderedParticipants.Where(item => used.Contains(item.Key)).ToImmutableArray();
            var keptKeys = orderedMessages.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
            orderedBranches = orderedBranches
                .Where(branch => branch.MessageKeys.Any(keptKeys.Contains))
                .Select(branch => new DiagramBranch(branch.Id, branch.Key, branch.Label, branch.Kind,
                    branch.MessageKeys.Where(keptKeys.Contains).ToImmutableArray(), branch.Evidence, branch.Certainty))
                .ToImmutableArray();
            if (messageLimitReached || participantLimitReached)
            {
                var dimensions = string.Join(",", new[]
                    { messageLimitReached ? $"messages={limit.MaxMaterialMessages}" : null,
                      participantLimitReached ? $"participants={limit.MaxParticipants}" : null }.Where(item => item is not null));
                diagnostics = diagnostics.Add(new DiagramPlanDiagnostic(
                    StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
                        "DP-BUDGET-TRUNCATED", AnalysisStage.CommandLine, graph.Profile,
                        $"{graph.EntryPoint.Value}:material", 0)),
                    "DP-BUDGET-TRUNCATED", "The diagram was truncated to its configured material budget.",
                    dimensions));
            }
        }

    SkipMaterialBudget:

        return new DiagramPlan(
            graph.EntryPoint,
            graph.Profile,
            OperationKey(graph),
            orderedParticipants,
            orderedMessages,
            orderedBranches,
            BuildDiagramDebugProjection(orderedParticipants, orderedMessages, sequence, orderedBranches, diagnostics)
                + (filter.FilteredInteractionCount > 0
                    ? $"\nfiltered interaction count: {filter.FilteredInteractionCount}"
                    : string.Empty),
            sequence,
            diagnostics);
    }

    private static DiagramSequence TrimSequence(DiagramSequence sequence, HashSet<DiagramPlanElementId> refs)
        => new(sequence.Elements.Select(element => element.IsMessageRef
                ? refs.Contains(element.MessageRefId!.Value) ? element : null
                : TrimFragment(element.NestedFragment!, refs))
            .Where(element => element is not null).Select(element => element!).ToImmutableArray());

    private static DiagramSequenceElement? TrimFragment(DiagramFragment fragment, HashSet<DiagramPlanElementId> refs)
    {
        var messages = fragment.MessageRefs.Where(refId => refs.Contains(refId)).ToImmutableArray();
        var nested = fragment.Fragments.Select(item => TrimFragment(item, refs)).Where(item => item is not null).Select(item => item!.NestedFragment!).ToImmutableArray();
        var arms = fragment.Arms.Select(arm =>
        {
            var armMessages = arm.MessageRefs.Where(refId => refs.Contains(refId)).ToImmutableArray();
            var armFragments = arm.Fragments.Select(item => TrimFragment(item, refs)).Where(item => item is not null).Select(item => item!.NestedFragment!).ToImmutableArray();
            return new DiagramAltArm(arm.Id, arm.Key, arm.Label, arm.IsElse, armMessages, armFragments, arm.Evidence, arm.Certainty);
        }).Where(arm => arm.MessageRefs.Length > 0 || arm.Fragments.Length > 0).ToImmutableArray();
        if (messages.Length == 0 && nested.Length == 0 && arms.Length == 0) { return null; }
        if (arms.Length == 1)
        {
            var arm = arms[0];
            return DiagramSequenceElement.Fragment(new DiagramFragment(fragment.Id, fragment.Key, fragment.Label,
                DiagramFragmentKind.Opt, [], arm.MessageRefs, arm.Fragments, fragment.Evidence, fragment.Certainty));
        }
        return DiagramSequenceElement.Fragment(new DiagramFragment(fragment.Id, fragment.Key, fragment.Label, fragment.Kind,
            arms, messages, nested, fragment.Evidence, fragment.Certainty));
    }

    private static bool SequenceContains(DiagramSequence sequence, DiagramPlanElementId id)
        => sequence.Elements.Any(element => element.IsMessageRef
            ? element.MessageRefId == id
            : FragmentContains(element.NestedFragment!, id));

    private static bool FragmentContains(DiagramFragment fragment, DiagramPlanElementId id)
        => fragment.MessageRefs.Contains(id)
            || fragment.Arms.Any(arm => arm.MessageRefs.Contains(id) || arm.Fragments.Any(item => FragmentContains(item, id)))
            || fragment.Fragments.Any(item => FragmentContains(item, id));

    /// <summary>Default maximum nested fragment depth; deeper unambiguous topology fails closed to a flat fallback.</summary>
    private const int MaxFragmentDepthLimit = 3;

    /// <summary>Canonical topology maps shared by the fragment-tree derivation.</summary>
    private sealed record FragmentContext(
        Dictionary<ScenarioDecisionId, ScenarioArm[]> ArmsByDecision,
        Dictionary<ScenarioArmId, HashSet<ScenarioNodeId>> ArmNodes,
        Dictionary<ScenarioArmId, List<DiagramPlanElementId>> ArmMessageRefs,
        Dictionary<ScenarioArmId, ImmutableArray<EvidenceRef>> ArmMembershipEvidence,
        Dictionary<ScenarioArmId, ScenarioArmTerminal> ArmTerminals,
        Dictionary<ScenarioArmId, ScenarioDecision> DecisionByArm,
        Dictionary<ScenarioArmId, List<ScenarioDecision>> ChildrenByArm,
        HashSet<ScenarioDecisionId> TransparentDecisions,
        Dictionary<ScenarioDecisionId, List<ImmutableArray<EvidenceRef>>> NormalizedEvidence,
        Dictionary<ScenarioArmId, List<DiagramFragment>> AbsorbedFragmentsByArm);

    /// <summary>
    /// Derives the ordered fragment tree from the reviewed accepted contract topology. Nesting requires proper,
    /// unique minimal membership containment; equal membership sets never prove guard containment,
    /// and ambiguous multiple minimal parents hoist the child to the enclosing sequence level while
    /// every message is emitted exactly once. A tree deeper than <see cref="MaxFragmentDepthLimit"/>
    /// emits the stable DP001 diagnostic and a non-truncated flat fallback instead of a partial
    /// tree. Automatic loop inference is intentionally absent: raw LoopNode facts are never mapped
    /// to Mermaid loops here.
    /// </summary>
    private static (DiagramSequence Sequence, ImmutableArray<DiagramPlanDiagnostic> Diagnostics, ImmutableArray<DiagramPlanElementId> WithheldRefs) BuildFragmentSequence(
        ScenarioGraph graph,
        IReadOnlyList<(ScenarioNodeId Node, DiagramPlanElementId Ref)> orderedMessageRefs)
    {
        var topology = graph.Topology;

        // Canonical topology maps keyed by stable semantic identities so reversed construction
        // yields identical fragments.
        var armsByDecision = topology.Arms
            .GroupBy(arm => arm.Decision)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(arm => arm.IsTrue ? 0 : 1)
                    .ThenBy(arm => arm.Id.Value, StringComparer.Ordinal)
                    .ToArray());

        var armNodes = new Dictionary<ScenarioArmId, HashSet<ScenarioNodeId>>();
        var armMembershipEvidence = new Dictionary<ScenarioArmId, ImmutableArray<EvidenceRef>>();
        foreach (var group in topology.Memberships.GroupBy(membership => membership.Arm))
        {
            var nodes = new HashSet<ScenarioNodeId>();
            foreach (var membership in group)
            {
                nodes.Add(membership.ScenarioNode);
            }

            armNodes[group.Key] = nodes;
            armMembershipEvidence[group.Key] = group
                .SelectMany(membership => membership.Evidence)
                .DistinctBy(item => item.Id.Value)
                .ToImmutableArray();
        }

        var armTerminals = new Dictionary<ScenarioArmId, ScenarioArmTerminal>();
        foreach (var terminal in topology.Terminals)
        {
            armTerminals[terminal.Arm] = terminal;
        }

        // Per-arm message references in planner edge order so every arm partition stays
        // deterministic and matches the flat message array.
        var armMessageRefs = new Dictionary<ScenarioArmId, List<DiagramPlanElementId>>();
        foreach (var (node, reference) in orderedMessageRefs)
        {
            foreach (var (armId, nodes) in armNodes)
            {
                if (nodes.Contains(node))
                {
                    if (!armMessageRefs.TryGetValue(armId, out var refs))
                    {
                        refs = new List<DiagramPlanElementId>();
                        armMessageRefs.Add(armId, refs);
                    }

                    refs.Add(reference);
                }
            }
        }

        // Decision node sets (union of both semantic arms) used for unambiguous containment.
        var decisionNodes = new Dictionary<ScenarioDecisionId, HashSet<ScenarioNodeId>>();
        var orderedDecisions = topology.Decisions
            .OrderBy(decision => decision.Id.Value, StringComparer.Ordinal)
            .ToArray();
        foreach (var decision in orderedDecisions)
        {
            var set = new HashSet<ScenarioNodeId>();
            if (armsByDecision.TryGetValue(decision.Id, out var arms))
            {
                foreach (var arm in arms)
                {
                    if (armNodes.TryGetValue(arm.Id, out var nodes))
                    {
                        set.UnionWith(nodes);
                    }
                }
            }

            decisionNodes[decision.Id] = set;
        }

        var decisionByArm = new Dictionary<ScenarioArmId, ScenarioDecision>();
        foreach (var decision in orderedDecisions)
        {
            foreach (var arm in armsByDecision[decision.Id])
            {
                decisionByArm[arm.Id] = decision;
            }
        }

        var context = new FragmentContext(
            armsByDecision,
            armNodes,
            armMessageRefs,
            armMembershipEvidence,
            armTerminals,
            decisionByArm,
            new Dictionary<ScenarioArmId, List<ScenarioDecision>>(),
            new HashSet<ScenarioDecisionId>(),
            new Dictionary<ScenarioDecisionId, List<ImmutableArray<EvidenceRef>>>(),
            new Dictionary<ScenarioArmId, List<DiagramFragment>>());

        var supported = orderedDecisions
            .Where(decision => IsSupportedDecision(decision, context))
            .ToHashSet();
        // A decision renders a fragment only when it carries exact compiler-evidenced owner
        // predicate wording whose normalized expression contains no opaque value (the formatted
        // label is never the generic "Condition" token). Subordinate decisions stay structurally
        // supported so safe owner groups still absorb them and merge their evidence, but they are
        // not renderable and never produce a fragment of their own.
        var renderable = supported
            .Where(decision => TryGetExactPredicateLabel(decision, out _))
            .ToHashSet();
        // Decisions admitted by the terminal/classification/wording contract before the equal-set
        // hoisting rule removes shared-set candidates. Hoisted renderable decisions still own their
        // guarded messages (F6 flat behavior), so withholding applies only to messages whose every
        // owning decision is genuinely non-renderable (for example SC013 exception-region, loop
        // guards, or decisions without exact predicate wording).
        var renderableIds = renderable
            .Select(decision => decision.Id)
            .ToHashSet();

        // F6: equal full membership sets never prove guard containment and can never nest under
        // proper containment. Such decisions cannot exclusively own the shared messages, so all of
        // them are hoisted flat (removed from the supported set) and their messages stay at the
        // enclosing sequence level exactly once.
        var equalSetGroups = orderedDecisions
            .Where(decision => supported.Contains(decision))
            .GroupBy(decision => NodeSetKey(decisionNodes[decision.Id]));
        foreach (var group in equalSetGroups)
        {
            if (group.Count() <= 1)
            {
                continue;
            }

            // Predicate owner/subordinate candidates are resolved by the group normalizer. Do not
            // let the generic equal-membership hoist erase their original topology before that
            // validator can reject an unsafe group.
            bool predicateAmbiguous = group.Any(decision => decision.PredicateWording is not null
                && (decision.PredicateWording.Role == ScenarioPredicateWordingRole.Subordinate
                    || orderedDecisions.Count(candidate => candidate.PredicateWording?.PredicateId == decision.PredicateWording.PredicateId
                        && candidate.PredicateWording.Role == ScenarioPredicateWordingRole.Owner) != 1));
            if (predicateAmbiguous)
            {
                continue;
            }

            foreach (var decision in group)
            {
                supported.Remove(decision);
            }
        }

        // Hoisted decisions are not renderable (F6 flat behavior); the pre-hoist snapshot above
        // keeps their guarded messages owned.
        renderable.RemoveWhere(decision => !supported.Contains(decision));

        // Parent selection: a child decision nests only inside the unique minimal arm whose node
        // set properly contains the child's full membership set. Equal membership sets never prove
        // containment, so an equal child set is never a candidate parent arm. Equal or multiple
        // minimal containers are ambiguous and keep the child at the enclosing sequence level.
        var parentArm = new Dictionary<ScenarioDecisionId, ScenarioArmId?>();
        foreach (var decision in orderedDecisions)
        {
            var set = decisionNodes[decision.Id];
            var candidates = new List<ScenarioArmId>();
            foreach (var other in orderedDecisions)
            {
                if (other.Id == decision.Id || !renderable.Contains(other))
                {
                    continue;
                }

                if (!armsByDecision.TryGetValue(other.Id, out var otherArms))
                {
                    continue;
                }

                foreach (var arm in otherArms)
                {
                    if (!armNodes.TryGetValue(arm.Id, out var armSet) || !set.IsProperSubsetOf(armSet))
                    {
                        continue;
                    }

                    candidates.Add(arm.Id);
                }
            }

            var minimal = candidates
                .Where(candidate => !candidates.Any(other =>
                    other != candidate && armNodes[other].IsProperSubsetOf(armNodes[candidate])))
                .ToArray();
            parentArm[decision.Id] = minimal.Length == 1 ? minimal[0] : null;
        }

        NormalizePredicateGroups(orderedDecisions, supported, parentArm, decisionByArm, context);

        // Children of a supported decision arm; a supported decision whose semantic parent renders
        // no fragment is hoisted to the enclosing sequence level so its structure is never lost.
        var rootDecisions = new List<ScenarioDecision>();
        foreach (var decision in orderedDecisions)
        {
            if (!renderable.Contains(decision) || context.TransparentDecisions.Contains(decision.Id))
            {
                continue;
            }

            var parent = parentArm[decision.Id];
            while (parent is not null
                && decisionByArm.TryGetValue(parent.Value, out var transparentParent)
                && context.TransparentDecisions.Contains(transparentParent.Id))
            {
                parent = parentArm[transparentParent.Id];
            }
            if (parent is not null
                && decisionByArm.TryGetValue(parent.Value, out var parentDecision)
                && renderable.Contains(parentDecision))
            {
                if (!context.ChildrenByArm.TryGetValue(parent.Value, out var children))
                {
                    children = new List<ScenarioDecision>();
                    context.ChildrenByArm.Add(parent.Value, children);
                }

                children.Add(decision);
            }
            else
            {
                rootDecisions.Add(decision);
            }
        }

        // Root fragments share one claimed set so overlapping root scopes (ambiguous parents that
        // hoist to the root level) never duplicate a message; a root with nothing left to claim is
        // dropped instead of emitting an empty shell.
        var claimed = new HashSet<DiagramPlanElementId>();
        var fragments = new List<DiagramFragment>();
        foreach (var decision in rootDecisions)
        {
            var fragment = BuildFragment(graph, decision, context, claimed);
            if (fragment is not null)
            {
                fragments.Add(fragment);
            }
        }

        // Guarded-but-unplaceable messages: a message whose node has exact arm membership (the
        // topology proves it is guarded by at least one decision) but whose every owning decision is
        // non-renderable (for example an SC013 exception-region or loop guard, or a decision
        // without exact predicate wording) cannot be rendered inside a continuing arm. Emitting it
        // as an unconditional top-level message before the guards would overclaim unconditional
        // execution, so the planner fails closed: the message is withheld from the diagram and DP002
        // records the withholding. Messages with no arm membership are truly unscoped and keep the
        // accepted flat behavior.
        var withheldRefs = new HashSet<DiagramPlanElementId>();
        foreach (var (node, reference) in orderedMessageRefs)
        {
            if (claimed.Contains(reference))
            {
                continue;
            }

            var owningDecisions = armNodes
                .Where(pair => pair.Value.Contains(node))
                .Select(pair => decisionByArm[pair.Key].Id)
                .ToHashSet();
            if (owningDecisions.Count == 0
                || owningDecisions.Any(owner => renderableIds.Contains(owner)))
            {
                continue;
            }

            withheldRefs.Add(reference);
        }

        var sequenceRefs = orderedMessageRefs
            .Select(item => item.Ref)
            .Where(reference => !claimed.Contains(reference) && !withheldRefs.Contains(reference))
            .ToImmutableArray();
        var sequence = new DiagramSequence(sequenceRefs, fragments.ToImmutableArray());

        if (MaxFragmentDepth(sequence) > MaxFragmentDepthLimit)
        {
            // Depth-limit fallback: never emit a partial or invalid tree. Every renderable message
            // stays visible exactly once in the flat sequence and the stable DP001 diagnostic
            // explains why; guarded-but-unplaceable messages stay withheld with DP002.
            ImmutableArray<DiagramPlanDiagnostic> depthDiagnostics = withheldRefs.Count == 0
                ? [CreateDepthDiagnostic(graph)]
                : [CreateDepthDiagnostic(graph), CreateWithheldDiagnostic(graph)];
            return (
                new DiagramSequence(
                    orderedMessageRefs
                        .Select(item => item.Ref)
                        .Where(reference => !withheldRefs.Contains(reference))
                        .ToImmutableArray(),
                    []),
                depthDiagnostics,
                withheldRefs.ToImmutableArray());
        }

        ImmutableArray<DiagramPlanDiagnostic> withheldDiagnostics = withheldRefs.Count == 0
            ? []
            : [CreateWithheldDiagnostic(graph)];
        return (
            sequence,
            withheldDiagnostics,
            withheldRefs.ToImmutableArray());
    }

    /// <summary>
    /// Makes an exact compiler predicate group transparent only in the presentation tree. The graph
    /// remains untouched; supporting evidence is moved into the owner's proven containing arm only
    /// for fully validated groups. Groups that cannot be validated in full remain untouched.
    /// </summary>
    private static void NormalizePredicateGroups(
        ScenarioDecision[] decisions,
        HashSet<ScenarioDecision> supported,
        Dictionary<ScenarioDecisionId, ScenarioArmId?> parentArm,
        Dictionary<ScenarioArmId, ScenarioDecision> decisionByArm,
        FragmentContext context)
    {
        var groups = decisions
            .Where(decision => decision.PredicateWording is not null)
            .GroupBy(decision => decision.PredicateWording!.PredicateId.Value, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var owners = group.Where(decision => decision.PredicateWording!.Role == ScenarioPredicateWordingRole.Owner).ToArray();
            var subordinates = group.Where(decision => decision.PredicateWording!.Role == ScenarioPredicateWordingRole.Subordinate).ToArray();
            if (subordinates.Length == 0 || owners.Length == 0)
            {
                continue;
            }

            if (owners.Length != 1)
            {
                continue;
            }

            var owner = owners[0];
            // Validate every subordinate arm before changing any presentation map. A terminal,
            // unknown, or missing subordinate terminal means collapsing the decision could turn a
            // conditional Break into an unconditional one (or lose an unknown control boundary).
            bool valid = supported.Contains(owner)
                && subordinates.All(s => supported.Contains(s)
                    && IsDescendantOf(s, owner, parentArm, decisionByArm)
                    && context.ArmsByDecision.TryGetValue(s.Id, out var subordinateArms)
                    && subordinateArms.All(arm => context.ArmTerminals.TryGetValue(arm.Id, out var terminal)
                        && terminal.Kind is not ScenarioTerminalKind.Terminates
                        and not ScenarioTerminalKind.Unknown));
            if (!valid)
            {
                continue;
            }

            // All containing arms and references are resolved up front. Commit the transparent
            // group only after this complete validation so a later malformed arm cannot leave a
            // partially hoisted group behind.
            var placements = subordinates
                .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
                .SelectMany(subordinate => context.ArmsByDecision[subordinate.Id]
                    .Select(arm => (subordinate, arm, containingArm: ContainingOwnerArm(arm.Id, owner, parentArm, decisionByArm))))
                .ToArray();
            if (placements.Any(item => item.containingArm is null))
            {
                continue;
            }

            foreach (var subordinate in subordinates.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
            {
                context.TransparentDecisions.Add(subordinate.Id);
                context.NormalizedEvidence.TryAdd(owner.Id, []);
                context.NormalizedEvidence[owner.Id].Add(subordinate.Evidence);
                context.NormalizedEvidence[owner.Id].Add(subordinate.PredicateWording!.Evidence);
                foreach (var arm in context.ArmsByDecision[subordinate.Id])
                {
                    var containingArm = placements.Single(item => item.subordinate.Id == subordinate.Id && item.arm.Id == arm.Id).containingArm!.Value;

                    MergeRefs(context, containingArm, context.ArmMessageRefs.TryGetValue(arm.Id, out var refs) ? refs : []);
                    if (context.ArmMembershipEvidence.TryGetValue(arm.Id, out var evidence))
                    {
                        context.NormalizedEvidence[owner.Id].Add(evidence);
                    }

                }
            }
        }

    }

    private static bool IsDescendantOf(
        ScenarioDecision candidate,
        ScenarioDecision owner,
        Dictionary<ScenarioDecisionId, ScenarioArmId?> parentArm,
        Dictionary<ScenarioArmId, ScenarioDecision> decisionByArm)
    {
        var current = candidate.Id;
        var seen = new HashSet<ScenarioDecisionId>();
        while (parentArm.TryGetValue(current, out var parent) && parent is not null && seen.Add(current))
        {
            if (decisionByArm[parent.Value].Id == owner.Id)
            {
                return true;
            }

            current = decisionByArm[parent.Value].Id;
        }

        return false;
    }

    private static ScenarioArmId? ContainingOwnerArm(
        ScenarioArmId subordinateArm,
        ScenarioDecision owner,
        Dictionary<ScenarioDecisionId, ScenarioArmId?> parentArm,
        Dictionary<ScenarioArmId, ScenarioDecision> decisionByArm)
    {
        var current = decisionByArm[subordinateArm].Id;
        var seen = new HashSet<ScenarioDecisionId>();
        while (parentArm.TryGetValue(current, out var parent) && parent is not null && seen.Add(current))
        {
            if (decisionByArm[parent.Value].Id == owner.Id)
            {
                return parent;
            }

            current = decisionByArm[parent.Value].Id;
        }

        return null;
    }

    /// <summary>
    /// Builds the ordered fragment tree for a topology-empty graph that carries a typed service
    /// composition (accepted contract) and optional callback regions (accepted contract/accepted contract). The one configuration decision
    /// becomes one Alt whose two arms hold exactly the composition arms' canonical member-node refs
    /// in planner edge order; unscoped message refs before the first arm ref (the entry request)
    /// stay flat before the Alt and any later unscoped refs stay flat after it. Each callback region
    /// wholly inside one arm replaces its member refs with one Opt fragment nested on that arm; a
    /// framework CacheMiss region is labeled exactly "On cache miss" and a source-condition region
    /// reuses the existing technical "Condition" label. Regions that are not conditional, are only
    /// partially inside an arm, span both arms, share member refs, or occur more than once per arm
    /// fail closed with the stable DP003 diagnostic and withhold the affected refs rather than
    /// duplicate or guess placement. Legacy flat branches are never produced for a composition graph.
    /// </summary>
    private static (DiagramSequence Sequence, ImmutableArray<DiagramPlanDiagnostic> Diagnostics, ImmutableArray<DiagramPlanElementId> WithheldRefs) BuildCompositionSequence(
        ScenarioGraph graph,
        List<(ScenarioNodeId Node, DiagramPlanElementId Ref)> orderedMessageRefs)
    {
        var composition = graph.Composition!;
        var trueNodes = composition.TrueArm.MemberNodes.ToHashSet();
        var falseNodes = composition.FalseArm.MemberNodes.ToHashSet();

        // Arm refs are exactly the canonical member nodes in planner edge order; a node claimed by
        // both arms (defensively impossible after the builder's disjoint-arm join) fails closed.
        var trueRefs = new List<DiagramPlanElementId>();
        var falseRefs = new List<DiagramPlanElementId>();
        var withheldRefs = new HashSet<DiagramPlanElementId>();
        foreach (var (node, reference) in orderedMessageRefs)
        {
            bool inTrue = trueNodes.Contains(node);
            bool inFalse = falseNodes.Contains(node);
            if (inTrue && inFalse)
            {
                withheldRefs.Add(reference);
            }
            else if (inTrue)
            {
                trueRefs.Add(reference);
            }
            else if (inFalse)
            {
                falseRefs.Add(reference);
            }
        }

        // Callback-region placement is all-or-nothing: every placeable region becomes exactly one
        // Opt and every unsupported region shape withholds all region member refs with DP003. A
        // region with no message refs contributes nothing.
        var regionInfo = graph.CallbackRegions
            .OrderBy(region => region.Id.Value, StringComparer.Ordinal)
            .Select(region =>
            {
                bool whollyTrue = region.MemberNodes.All(trueNodes.Contains);
                bool whollyFalse = region.MemberNodes.All(falseNodes.Contains);
                var refs = orderedMessageRefs
                    .Where(item => region.MemberNodes.Contains(item.Node))
                    .Select(item => item.Ref)
                    .ToImmutableArray();
                return (Region: region, WhollyTrue: whollyTrue, WhollyFalse: whollyFalse, Refs: refs);
            })
            .Where(info => !info.Refs.IsEmpty)
            .ToArray();
        bool regionShapeUnsupported =
            // A source-condition callback region has no exact framework-condition wording and would
            // otherwise render an Opt labeled with the generic "Condition" token; it is withheld
            // instead (DP003) with the boundary retained in technical fallback.
            regionInfo.Any(info => info.Region.FrameworkCondition != FrameworkCallbackConditionKind.CacheMiss)
            || regionInfo.Any(info => !info.WhollyTrue && !info.WhollyFalse)
            || regionInfo.Any(info => info.WhollyTrue && info.WhollyFalse)
            || regionInfo.GroupBy(info => info.WhollyTrue).Any(group => group.Count() > 1)
            || regionInfo.SelectMany(info => info.Refs).Distinct().Count() != regionInfo.SelectMany(info => info.Refs).Count();
        var allRegionRefs = regionInfo.SelectMany(info => info.Refs).ToHashSet();

        var trueOpts = new List<DiagramFragment>();
        var falseOpts = new List<DiagramFragment>();
        if (regionShapeUnsupported)
        {
            withheldRefs.UnionWith(allRegionRefs);
            trueRefs.RemoveAll(reference => allRegionRefs.Contains(reference));
            falseRefs.RemoveAll(reference => allRegionRefs.Contains(reference));
        }
        else
        {
            foreach (var info in regionInfo)
            {
                var opt = BuildCallbackOptFragment(graph, info.Region, info.Refs);
                if (info.WhollyTrue)
                {
                    trueOpts.Add(opt);
                    trueRefs.RemoveAll(reference => info.Refs.Contains(reference));
                }
                else
                {
                    falseOpts.Add(opt);
                    falseRefs.RemoveAll(reference => info.Refs.Contains(reference));
                }
            }
        }

        var (altEvidence, altCertainty) = CombineEvidence(
        [
            composition.Decision.Evidence,
            composition.TrueArm.Evidence,
            composition.FalseArm.Evidence,
        ]);
        var (trueArmEvidence, trueArmCertainty) = CombineEvidence([composition.Decision.Evidence, composition.TrueArm.Evidence]);
        var (falseArmEvidence, falseArmCertainty) = CombineEvidence([composition.Decision.Evidence, composition.FalseArm.Evidence]);

        var trueArm = new DiagramAltArm(
            CreateElementId(graph, "arm", CompositionArmKey(composition, isTrue: true)),
            CompositionArmKey(composition, isTrue: true),
            CompositionArmRoleLabel(composition.TrueArm.ImplementationType),
            isElse: false,
            trueRefs.ToImmutableArray(),
            trueOpts.ToImmutableArray(),
            trueArmEvidence,
            trueArmCertainty);
        var falseArm = new DiagramAltArm(
            CreateElementId(graph, "arm", CompositionArmKey(composition, isTrue: false)),
            CompositionArmKey(composition, isTrue: false),
            CompositionArmRoleLabel(composition.FalseArm.ImplementationType),
            isElse: true,
            falseRefs.ToImmutableArray(),
            falseOpts.ToImmutableArray(),
            falseArmEvidence,
            falseArmCertainty);
        var alt = new DiagramFragment(
            CreateElementId(graph, "fragment", CompositionFragmentKey(composition)),
            CompositionFragmentKey(composition),
            CompositionFragmentLabel(composition),
            DiagramFragmentKind.Alt,
            [trueArm, falseArm],
            [],
            [],
            altEvidence,
            altCertainty);

        // Top sequence: refs not in either arm and before the first arm ref (the entry request)
        // stay flat before the Alt; the Alt carries every arm ref; remaining non-arm refs stay flat
        // after the Alt. Withheld refs never enter the sequence tree.
        int firstArmRefIndex = -1;
        for (int i = 0; i < orderedMessageRefs.Count; i++)
        {
            var (node, _) = orderedMessageRefs[i];
            if (trueNodes.Contains(node) || falseNodes.Contains(node))
            {
                firstArmRefIndex = i;
                break;
            }
        }

        var preRefs = new List<DiagramPlanElementId>();
        var postRefs = new List<DiagramPlanElementId>();
        for (int i = 0; i < orderedMessageRefs.Count; i++)
        {
            var (node, reference) = orderedMessageRefs[i];
            if (trueNodes.Contains(node) || falseNodes.Contains(node) || withheldRefs.Contains(reference))
            {
                continue;
            }

            if (firstArmRefIndex < 0 || i < firstArmRefIndex)
            {
                preRefs.Add(reference);
            }
            else
            {
                postRefs.Add(reference);
            }
        }

        var elements = ImmutableArray.CreateBuilder<DiagramSequenceElement>();
        foreach (var reference in preRefs)
        {
            elements.Add(DiagramSequenceElement.MessageRef(reference));
        }

        elements.Add(DiagramSequenceElement.Fragment(alt));
        foreach (var reference in postRefs)
        {
            elements.Add(DiagramSequenceElement.MessageRef(reference));
        }

        var sequence = new DiagramSequence(elements.ToImmutable());
        return (
            sequence,
            withheldRefs.Count == 0
                ? []
                : [CreateCompositionDiagnostic(graph)],
            withheldRefs.ToImmutableArray());
    }

    /// <summary>
    /// One Opt fragment for a callback region wholly inside one composition arm. Only a
    /// <see cref="FrameworkCallbackConditionKind.CacheMiss"/> region reaches this path: a
    /// source-condition region is withheld as an unsupported shape before fragment construction so
    /// the generic "Condition" token is never rendered as useful behavior. Evidence and certainty
    /// come from the region unchanged (never promoted). The Opt never materializes arms.
    /// </summary>
    private static DiagramFragment BuildCallbackOptFragment(
        ScenarioGraph graph,
        ScenarioCallbackRegion region,
        ImmutableArray<DiagramPlanElementId> refs)
    {
        if (region.FrameworkCondition != FrameworkCallbackConditionKind.CacheMiss)
        {
            throw new InvalidOperationException(
                "A source-condition callback region must be withheld before building an Opt fragment.");
        }

        string key = "callback:" + region.Id.Value;
        const string label = "On cache miss";
        return new DiagramFragment(
            CreateElementId(graph, "fragment", key),
            key,
            label,
            DiagramFragmentKind.Opt,
            [],
            refs,
            [],
            region.Evidence,
            region.Certainty);
    }

    /// <summary>Stable semantic key of the one configuration Alt: composition plus the exact condition operation.</summary>
    private static string CompositionFragmentKey(ScenarioServiceComposition composition)
        => "composition:" + composition.Decision.ConditionOperation.Value;

    /// <summary>Stable semantic polarity key of one configuration arm.</summary>
    private static string CompositionArmKey(ScenarioServiceComposition composition, bool isTrue)
        => CompositionFragmentKey(composition) + ":arm:" + (isTrue ? "true" : "false");

    /// <summary>
    /// Readable primary label of the configuration Alt: the humanized last configuration-key
    /// segment (for example "UseSqlDatabase" becomes "Use SQL database"), never the raw key or a
    /// full namespace. The generic "Configuration" label is used when the key segment cannot be
    /// humanized.
    /// </summary>
    private static string CompositionFragmentLabel(ScenarioServiceComposition composition)
    {
        string key = composition.Decision.Key;
        int separator = key.LastIndexOf(':');
        string segment = separator >= 0 ? key[(separator + 1)..] : key;
        string humanized = HumanizeRoleName(segment);
        return string.IsNullOrWhiteSpace(humanized) ? "Configuration" : humanized;
    }

    /// <summary>
    /// Namespace-free service participant label from the composition contract role, never the first
    /// implementation name: the leading "I" is stripped and the remaining CamelCase name is
    /// humanized ("ICustomerService" -> "Customer service"). A name that humanizes to nothing keeps
    /// the generic "Service" role.
    /// </summary>
    private static string BuildCompositionServiceLabel(string serviceType)
    {
        string name = ParseTypeDisplay(serviceType).Name;
        if (name.StartsWith('I'))
        {
            name = name[1..];
        }

        string humanized = HumanizeRoleName(name);
        return string.IsNullOrWhiteSpace(humanized) ? "Service" : humanized;
    }

    /// <summary>
    /// Namespace-free humanized implementation role used for a configuration arm label
    /// ("SqlCustomerService" -> "SQL customer service", "JsonCustomerService" -> "JSON customer
    /// service"). The label never leaks the implementation namespace and never contains the raw
    /// type name.
    /// </summary>
    private static string CompositionArmRoleLabel(string implementationType)
        => BuildCompositionServiceLabel(implementationType);

    /// <summary>
    /// Sentence-case humanization of a CamelCase role name: SeqDoc-recognized technical acronym
    /// words are uppercased ("Sql" -> "SQL", "Json" -> "JSON", "Http" -> "HTTP") and the remaining
    /// words are lowercased with the first word capitalized ("CustomerService" -> "Customer
    /// service"). The rule is a fixed closed technical vocabulary (the wording baseline admits
    /// SeqDoc-specific acronym rules) and never recognizes application names or business terms.
    /// </summary>
    private static string HumanizeRoleName(string camelCaseName)
    {
        var words = SplitCamelWords(camelCaseName);
        if (words.Count == 0)
        {
            return string.Empty;
        }

        var normalized = new List<string>(words.Count);
        for (int i = 0; i < words.Count; i++)
        {
            string word = words[i];
            if (IsTechnicalAcronymWord(word))
            {
                normalized.Add(word.ToUpperInvariant());
            }
            else
            {
                normalized.Add(i == 0
                    ? char.ToUpperInvariant(word[0]) + word[1..]
                    : char.ToLowerInvariant(word[0]) + word[1..]);
            }
        }

        return string.Join(' ', normalized);
    }

    /// <summary>
    /// Closed technical acronym vocabulary recognized by role humanization. This is the wording
    /// baseline's SeqDoc-specific acronym rule: fixed technical terms only (SQL, JSON, HTTP, URL,
    /// API, DB, ID, EF), never application names or business vocabulary.
    /// </summary>
    private static bool IsTechnicalAcronymWord(string word)
        => word is "Sql" or "Json" or "Id" or "Http" or "Url" or "Api" or "Db" or "Ef";

    /// <summary>Splits a CamelCase name into words at lowercase-to-uppercase and acronym-to-word boundaries.</summary>
    private static List<string> SplitCamelWords(string value)
    {
        var words = new List<string>();
        if (string.IsNullOrEmpty(value))
        {
            return words;
        }

        int start = 0;
        for (int i = 1; i < value.Length; i++)
        {
            if (char.IsUpper(value[i])
                && (char.IsLower(value[i - 1])
                    || (i + 1 < value.Length && char.IsLower(value[i + 1]))))
            {
                words.Add(value[start..i]);
                start = i;
            }
        }

        words.Add(value[start..]);
        return words;
    }

    /// <summary>
    /// Stable DP003 planning diagnostic for a composition or callback region that cannot be
    /// represented exactly. The identity is grounded in the compilation profile and entry point so
    /// repeated planning of an unchanged graph yields the same diagnostic id.
    /// </summary>
    private static DiagramPlanDiagnostic CreateCompositionDiagnostic(ScenarioGraph graph)
    {
        var id = StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            "DP003",
            AnalysisStage.FrameworkModel,
            graph.Profile,
            $"scenario:{graph.EntryPoint.Value}",
            0));
        return new DiagramPlanDiagnostic(
            id,
            "DP003",
            "A conditional service composition or callback region could not be represented exactly; affected interactions are withheld from the diagram.",
            "Overlapping composition arms, partial or non-conditional callback membership, or multiple/overlapping callback regions have no exact placement; the affected messages are withheld rather than duplicated or guessed.");
    }

    private sealed record HostedTopologyValidation(DiagramPlanDiagnostic? Diagnostic,
        ImmutableHashSet<ScenarioNodeId> HiddenNodes, bool IsValid);

    private static HostedTopologyValidation? ValidateHostedWorkerTopology(ScenarioGraph graph)
    {
        if (graph.RootKind != ScenarioRootKind.HostedWorker) { return null; }
        var topology = graph.Topology;
        var containers = topology.FlowContainers;
        var placements = topology.FlowPlacements;
        var duplicateNodeIds = graph.Nodes.GroupBy(node => node.Id).Any(group => group.Count() > 1);
        var nodes = graph.Nodes.GroupBy(node => node.Id).ToDictionary(group => group.Key, group => group.First());
        var regionOwners = containers.GroupBy(container => container.Region)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Method).Distinct().ToArray());
        var regions = regionOwners.Keys.ToHashSet();
        var callbackMembers = graph.CallbackRegions.SelectMany(region => region.MemberNodes).ToHashSet();
        var controlNodes = graph.Nodes
            .Where(node => node.Presentation?.HostedWorkerControlKind is not null)
            .Where(node => !callbackMembers.Contains(node.Id))
            .ToArray();
        var byRegion = containers.GroupBy(container => container.Region).ToDictionary(group => group.Key, group => group.ToArray());
        var invalidContainers = containers.Where(container => !Enum.IsDefined(container.Kind)
            || string.IsNullOrWhiteSpace(container.Region.Value)
            || string.IsNullOrWhiteSpace(container.Method.Value)
            || container.Kind == ScenarioFlowContainerKind.NaturalLoop && container.Header is null
            || !byRegion.TryGetValue(container.Region, out var sameRegion)
            || sameRegion.Count(item => item.Method == container.Method) != 1
            || container.Parent == container.Region
            || container.Parent is { } parent && (!regions.Contains(parent)
                || !regionOwners.TryGetValue(parent, out var parentOwners)
                || parentOwners.Length != 1 || parentOwners[0] != container.Method))
            .Select(container => container.Region).ToHashSet();
        var duplicateRegions = regionOwners.Where(item => item.Value.Length > 1
            || containers.Count(container => container.Region == item.Key) > 1)
            .Select(item => item.Key).ToHashSet();
        invalidContainers.UnionWith(duplicateRegions);
        var invalidClosure = new HashSet<FlowRegionId>(invalidContainers);
        bool changed;
        do
        {
            changed = false;
            foreach (var container in containers.Where(item => item.Parent is { } parent && invalidClosure.Contains(parent)))
            {
                changed |= invalidClosure.Add(container.Region);
            }
        } while (changed);
        var controlPlacementCounts = placements.GroupBy(placement => placement.ScenarioNode)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var invalid = duplicateNodeIds || invalidContainers.Count > 0
            || placements.Any(placement => !callbackMembers.Contains(placement.ScenarioNode)
                && !controlNodes.Any(node => node.Id == placement.ScenarioNode))
            || controlNodes.Any(node => !controlPlacementCounts.TryGetValue(node.Id, out var owned) || owned.Length != 1)
            || controlNodes.Any(node => controlPlacementCounts.TryGetValue(node.Id, out var owned) && owned.Length == 1
                && !PlacementIsValid(node, owned[0]))
            || containers.Any(container => string.IsNullOrWhiteSpace(container.Region.Value)
            || containers.Count(item => item.Region == container.Region && item.Method == container.Method) != 1
            || (container.Parent is { } parent && (parent == container.Region || !regionOwners.TryGetValue(parent, out var owners)
                || owners.Length != 1 || owners[0] != container.Method)))
            || placements.GroupBy(placement => placement.ScenarioNode).Any(group => group.Count() != 1)
            || placements.Any(placement => !nodes.TryGetValue(placement.ScenarioNode, out var node)
                || node.Method != placement.Method
                || placement.Containers.Any(container => !regionOwners.TryGetValue(container, out var owners)
                    || owners.Length != 1 || owners[0] != placement.Method)
                || placement.GuardArms.Any(arm => !topology.Arms.Any(candidate => candidate.Id == arm))
                || (placement.Anchor is { } anchor && !nodes.Values.Any(node => node.Method == placement.Method
                    && node.Presentation?.HostedWorkerHeader == anchor)))
            || HasContainerCycle(containers);
        if (!invalid) { return new HostedTopologyValidation(null, [], true); }
        var id = StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            "DP-WORKER-INVALID-TOPOLOGY", AnalysisStage.FrameworkModel, graph.Profile,
            $"scenario:{graph.EntryPoint.Value}", 0));
        var affected = controlNodes.Where(node =>
            !controlPlacementCounts.TryGetValue(node.Id, out var owned) || owned.Length != 1
            || !PlacementIsValid(node, owned[0]))
            .Select(node => node.Id).ToImmutableHashSet();
        return new HostedTopologyValidation(new DiagramPlanDiagnostic(id, "DP-WORKER-INVALID-TOPOLOGY",
            "Hosted-worker topology is invalid; affected controls are withheld from the diagram.",
            "One or more hosted-worker containers, placements, anchors, parents, or guard references are missing, foreign, duplicated, or cyclic."), affected, false);

        bool PlacementIsValid(ScenarioNode node, ScenarioFlowPlacement placement)
        {
            if (placement.Method != node.Method
                || placement.Containers.Any(container => !regionOwners.TryGetValue(container, out var owners)
                    || owners.Length != 1 || owners[0] != placement.Method || invalidClosure.Contains(container))
                || placement.GuardArms.Any(arm => !topology.Arms.Any(candidate => candidate.Id == arm)))
            {
                return false;
            }
            var declared = placement.Containers.ToArray();
            if (declared.Distinct().Count() != declared.Length)
            {
                return false;
            }
            if (declared.Length > 0 && containers.Any(item => item.Method == placement.Method && item.Region == declared[0]
                && item.Parent is not null))
            {
                return false;
            }
            for (int i = 1; i < declared.Length; i++)
            {
                var child = containers.SingleOrDefault(item => item.Method == placement.Method && item.Region == declared[i]);
                if (child is null || child.Parent != declared[i - 1])
                {
                    return false;
                }
            }
            if (placement.Anchor is { } anchor && !graph.Nodes.Any(candidate => candidate.Method == placement.Method
                && candidate.Presentation?.HostedWorkerHeader == anchor))
            {
                return false;
            }
            return true;
        }

        static bool HasContainerCycle(ImmutableArray<ScenarioFlowContainer> items)
        {
            if (items.GroupBy(item => item.Region).Any(group => group.Count() > 1)) { return true; }
            var byRegion = items.ToDictionary(item => item.Region);
            foreach (var item in items)
            {
                var seen = new HashSet<FlowRegionId>();
                var current = item;
                while (current.Parent is { } parent)
                {
                    if (!seen.Add(current.Region) || !byRegion.TryGetValue(parent, out current)) { return true; }
                }
            }
            return false;
        }
    }

    private static DiagramSequence BuildHostedWorkerSequence(
        ScenarioGraph graph, IReadOnlyList<(ScenarioNodeId Node, DiagramPlanElementId Ref)> ordered, bool topologyValid)
    {
        if (!topologyValid || graph.Topology.FlowContainers.IsDefaultOrEmpty)
        {
            return new DiagramSequence(ordered.Select(item => DiagramSequenceElement.MessageRef(item.Ref)).ToImmutableArray());
        }

        var placements = graph.Topology.FlowPlacements.ToDictionary(item => item.ScenarioNode);
        var loops = graph.Topology.FlowContainers.Where(item => item.Kind == ScenarioFlowContainerKind.NaturalLoop)
            .ToDictionary(item => item.Region);
        var loopChildren = loops.Values.Where(item => item.Parent is { })
            .GroupBy(item => item.Parent!.Value)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Region.Value, StringComparer.Ordinal).ToArray());
        var claimed = new HashSet<DiagramPlanElementId>();
        var elements = new List<DiagramSequenceElement>();
        foreach (var item in ordered)
        {
            if (claimed.Contains(item.Ref))
            {
                continue;
            }
            if (placements.TryGetValue(item.Node, out var placement))
            {
                FlowRegionId? outer = placement.Containers.FirstOrDefault(region => loops.ContainsKey(region)
                    && (!loops.TryGetValue(region, out var loop) || loop.Parent is null || !placement.Containers.Contains(loop.Parent.Value)));
                if (outer is { } outerRegion && loops.TryGetValue(outerRegion, out var outerLoop))
                {
                    var fragment = BuildLoop(outerLoop);
                    if (fragment is not null)
                    {
                        elements.Add(DiagramSequenceElement.Fragment(fragment));
                        continue;
                    }
                }
            }
            elements.Add(DiagramSequenceElement.MessageRef(item.Ref));
            claimed.Add(item.Ref);
        }
        return new DiagramSequence(elements.ToImmutableArray());

        DiagramFragment? BuildLoop(ScenarioFlowContainer loop)
        {
            var memberRefs = ordered.Where(item => placements.TryGetValue(item.Node, out var placement)
                && placement.Containers.Contains(loop.Region)).ToArray();
            if (memberRefs.Length == 0)
            {
                return null;
            }
            var children = loopChildren.TryGetValue(loop.Region, out var nested) ? nested : [];
            var nestedFragments = children.Select(BuildLoop).Where(item => item is not null).Cast<DiagramFragment>().ToImmutableArray();
            var childRegions = children.Select(item => item.Region).ToHashSet();
            var refs = memberRefs.Where(item => !placements[item.Node].Containers.Any(region => childRegions.Contains(region)))
                .Select(item => item.Ref).ToImmutableArray();
            foreach (var reference in refs)
            {
                claimed.Add(reference);
            }
            foreach (var child in nestedFragments)
            {
                foreach (var reference in ordered.Where(item => child.MessageRefs.Contains(item.Ref)).Select(item => item.Ref))
                {
                    claimed.Add(reference);
                }
            }
            var evidence = loop.Evidence;
            return new DiagramFragment(
                new DiagramPlanElementId($"diagram-fragment:v1:worker-loop:{loop.Region.Value}"),
                 $"worker-loop:{loop.Region.Value}", graph.Nodes.Any(node =>
                     node.Presentation?.HostedWorkerControlKind == HostedWorkerControlKind.CatchLoopContinuation
                     && node.Presentation.HostedWorkerFlowRegion == loop.Region)
                     ? "Retry" : "each iteration", DiagramFragmentKind.Loop,
                 [], refs, nestedFragments, evidence,
                 new[] { loop.Certainty, evidence.Max(item => item.Certainty) }.Max());
        }
    }

    /// <summary>
    /// A decision is supported when both arms have known terminal classifications, at least one arm
    /// carries material messages, and the arms do not claim the same node. Both one-material shapes
    /// are admitted: an empty rejoining arm becomes an Opt, and an empty terminating arm becomes an
    /// Alt whose terminal arm holds exactly one Break.
    /// </summary>
    private static bool IsSupportedDecision(ScenarioDecision decision, FragmentContext context)
    {
        if (!context.ArmsByDecision.TryGetValue(decision.Id, out var arms) || arms.Length != 2)
        {
            return false;
        }

        foreach (var arm in arms)
        {
            // Unknown or missing terminal classifications never produce fragments.
            if (!context.ArmTerminals.TryGetValue(arm.Id, out var terminal)
                || terminal.Kind == ScenarioTerminalKind.Unknown)
            {
                return false;
            }
        }

        bool materialTrue = context.ArmMessageRefs.TryGetValue(arms[0].Id, out var trueRefs) && trueRefs.Count > 0;
        bool materialFalse = context.ArmMessageRefs.TryGetValue(arms[1].Id, out var falseRefs) && falseRefs.Count > 0;
        if (!materialTrue && !materialFalse)
        {
            return false;
        }

        // A node claimed by both arms of one decision is a conflicting membership; fail closed.
        if (context.ArmNodes.TryGetValue(arms[0].Id, out var trueNodes)
            && context.ArmNodes.TryGetValue(arms[1].Id, out var falseNodes)
            && trueNodes.Overlaps(falseNodes))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Builds one fragment from the decision's full supporting evidence (decision, arms,
    /// memberships, and terminals actually used) with certainty degraded to the weakest contributor,
    /// and records every message reference it claims in the shared set so no guarded message is ever
    /// duplicated. Both-material decisions and one-material decisions whose empty arm terminates
    /// become an Alt with explicit arms (failure/terminating arm first, later arms marked IsElse);
    /// one-material decisions with an empty rejoining arm become an Opt with the material messages
    /// and no invented else. A fragment with no remaining content to claim is not emitted.
    /// </summary>
    private static DiagramFragment? BuildFragment(
        ScenarioGraph graph,
        ScenarioDecision decision,
        FragmentContext context,
        HashSet<DiagramPlanElementId> claimed)
    {
        var arms = context.ArmsByDecision[decision.Id];
        bool materialTrue = context.ArmMessageRefs.TryGetValue(arms[0].Id, out var trueRefs) && trueRefs.Count > 0;
        bool materialFalse = context.ArmMessageRefs.TryGetValue(arms[1].Id, out var falseRefs) && falseRefs.Count > 0;
        string fragmentKey = FragmentKey(decision);
        // The primary fragment label is the sentence-case technical wording; condition identity
        // stays in the fragment key, never the visible label.
        string fragmentLabel = FragmentLabel(decision, context);
        var (fragmentEvidence, fragmentCertainty) = CombineFragmentEvidence(decision, context);

        bool bothMaterial = materialTrue && materialFalse;
        bool emptyTerminatingArm = materialTrue != materialFalse
            && (materialTrue
                ? context.ArmTerminals.TryGetValue(arms[1].Id, out var trueEmptyTerminal)
                    && trueEmptyTerminal.Kind == ScenarioTerminalKind.Terminates
                : context.ArmTerminals.TryGetValue(arms[0].Id, out var falseEmptyTerminal)
                    && falseEmptyTerminal.Kind == ScenarioTerminalKind.Terminates);
        if (bothMaterial || emptyTerminatingArm)
        {
            // Alt: visual failure/terminating-first order while semantic polarity keys stay fixed.
            var visualArms = arms
                .OrderBy(arm => TerminalSort(arm, context))
                .ThenBy(arm => arm.IsTrue ? 0 : 1)
                .ThenBy(arm => arm.Id.Value, StringComparer.Ordinal)
                .ToArray();
            var builtArms = new List<DiagramAltArm>();
            bool anyContent = false;
            for (int index = 0; index < visualArms.Length; index++)
            {
                builtArms.Add(BuildAltArm(
                    graph,
                    decision,
                    visualArms[index],
                    isElse: index > 0,
                    context,
                    claimed,
                    out bool armHasContent));
                anyContent |= armHasContent;
            }

            if (!anyContent)
            {
                return null;
            }

            return new DiagramFragment(
                CreateFragmentId(graph, decision),
                fragmentKey,
                fragmentLabel,
                DiagramFragmentKind.Alt,
                builtArms.ToImmutableArray(),
                [],
                [],
                fragmentEvidence,
                fragmentCertainty);
        }

        // One material arm with an empty rejoining arm: the empty arm never becomes an invented
        // else. The material arm's messages and nested children live directly on the Opt fragment.
        var materialArm = materialTrue ? arms[0] : arms[1];
        fragmentLabel = decision.PredicateWording is { Role: ScenarioPredicateWordingRole.Owner } wording
            ? materialArm.IsTrue
                ? PredicateWordingFormatter.Format(wording.Root)
                : PredicateWordingFormatter.FormatComplement(wording.Root)
            : fragmentLabel;
        var childFragments = new List<DiagramFragment>();
        foreach (var child in ChildrenOf(materialArm.Id, context))
        {
            var childFragment = BuildFragment(graph, child, context, claimed);
            if (childFragment is not null)
            {
                childFragments.Add(childFragment);
            }
        }
        AddAbsorbedFragments(materialArm.Id, context, childFragments);

        var refs = context.ArmMessageRefs.TryGetValue(materialArm.Id, out var materialRefs)
            ? materialRefs.Where(reference => !claimed.Contains(reference)).ToImmutableArray()
            : [];
        if (refs.Length == 0 && childFragments.Count == 0)
        {
            return null;
        }

        claimed.UnionWith(refs);

        return new DiagramFragment(
            CreateFragmentId(graph, decision),
            fragmentKey,
            fragmentLabel,
            DiagramFragmentKind.Opt,
            [],
            refs,
            childFragments.ToImmutableArray(),
            fragmentEvidence,
            fragmentCertainty);
    }

    private static string FragmentLabel(ScenarioDecision decision, FragmentContext context)
        => decision.PredicateWording is null
            ? "Otherwise"
            : decision.PredicateWording.Role == ScenarioPredicateWordingRole.Subordinate
                ? PredicateWordingFormatter.FormatSubordinate()
                : PredicateWordingFormatter.Format(decision.PredicateWording.Root);

    /// <summary>
    /// Builds one semantic arm of an Alt. Nested children are placed first, then the arm's own
    /// messages (shared messages are claimed by the deepest fragment), and a terminating arm ends
    /// with exactly one Break so no message ever appears after a terminal arm. Evidence combines
    /// the decision, arm, membership, and terminal support actually used with certainty degraded to
    /// the weakest contributor.
    /// </summary>
    private static DiagramAltArm BuildAltArm(
        ScenarioGraph graph,
        ScenarioDecision decision,
        ScenarioArm arm,
        bool isElse,
        FragmentContext context,
        HashSet<DiagramPlanElementId> claimed,
        out bool hasContent)
    {
        var childFragments = new List<DiagramFragment>();
        foreach (var child in ChildrenOf(arm.Id, context))
        {
            var childFragment = BuildFragment(graph, child, context, claimed);
            if (childFragment is not null)
            {
                childFragments.Add(childFragment);
            }
        }
        AddAbsorbedFragments(arm.Id, context, childFragments);

        var refs = context.ArmMessageRefs.TryGetValue(arm.Id, out var armRefs)
            ? armRefs.Where(reference => !claimed.Contains(reference)).ToImmutableArray()
            : [];

        var fragments = childFragments.ToList();
        var (armEvidence, armCertainty) = CombineArmEvidence(decision, arm, context);
        string armLabel = ArmLabel(graph, decision, arm, context);
        if (context.ArmTerminals.TryGetValue(arm.Id, out var terminal)
            && terminal.Kind == ScenarioTerminalKind.Terminates)
        {
            fragments.Add(CreateBreakFragment(graph, decision, arm, armLabel, armCertainty, context));
        }

        hasContent = refs.Length > 0 || fragments.Count > 0;

        claimed.UnionWith(refs);

        return new DiagramAltArm(
            CreateArmId(graph, decision, arm),
            ArmKey(decision, arm),
            armLabel,
            isElse,
            refs,
            fragments.ToImmutableArray(),
            armEvidence,
            armCertainty);
    }

    private static ImmutableArray<ScenarioDecision> ChildrenOf(ScenarioArmId armId, FragmentContext context)
        => context.ChildrenByArm.TryGetValue(armId, out var children)
            ? children.ToImmutableArray()
            : [];

    private static void AddAbsorbedFragments(ScenarioArmId armId, FragmentContext context, List<DiagramFragment> fragments)
    {
        if (context.AbsorbedFragmentsByArm.TryGetValue(armId, out var absorbed))
        {
            fragments.AddRange(absorbed);
        }
    }

    private static void MergeRefs(FragmentContext context, ScenarioArmId armId, IEnumerable<DiagramPlanElementId> refs)
    {
        if (!context.ArmMessageRefs.TryGetValue(armId, out var target))
        {
            target = [];
            context.ArmMessageRefs.Add(armId, target);
        }

        foreach (var reference in refs.Where(reference => !target.Contains(reference)))
        {
            target.Add(reference);
        }
    }

    /// <summary>Visual arm sort: a terminating arm renders first; semantic polarity breaks ties.</summary>
    private static int TerminalSort(ScenarioArm arm, FragmentContext context)
        => context.ArmTerminals.TryGetValue(arm.Id, out var terminal)
            && terminal.Kind == ScenarioTerminalKind.Terminates
            ? 0
            : 1;

    private static string FragmentKey(ScenarioDecision decision)
        => decision.OccurrenceScope is null
            ? "decision:" + decision.Condition.Value
            : "decision:occurrence:v1:" + EncodeIdentityTuple(decision.Condition.Value, decision.OccurrenceScope);

    private static string EncodeIdentityTuple(params string[] values)
        => string.Concat(values.Select(value => value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ":" + value));

    private static string ArmKey(ScenarioDecision decision, ScenarioArm arm)
        => FragmentKey(decision) + ":arm:" + (arm.IsTrue ? "true" : "false");

    /// <summary>
    /// Arm display label from the terminal classification and typed terminal facts, never inferred
    /// from labels. A terminating arm uses its exact typed terminal wording when exactly one unique
    /// typed terminal result/outcome exists, otherwise the sentence-case technical "Condition"; a
    /// rejoining arm always uses "Continue".
    /// </summary>
    private static string ArmLabel(ScenarioGraph graph, ScenarioDecision decision, ScenarioArm arm, FragmentContext context)
    {
        if (context.ArmTerminals.TryGetValue(arm.Id, out var terminal)
            && terminal.Kind == ScenarioTerminalKind.Terminates)
        {
            return TerminalArmLabel(graph, arm, context)
                ?? PredicateArmLabel(decision, arm)
                ?? "Terminal path";
        }

        if (decision.PredicateWording?.Role == ScenarioPredicateWordingRole.Subordinate)
        {
            return "Otherwise";
        }

        return PredicateArmLabel(decision, arm) ?? "Otherwise";
    }

    private static string? PredicateArmLabel(ScenarioDecision decision, ScenarioArm arm)
    {
        var wording = decision.PredicateWording;
        if (wording is null || wording.Role == ScenarioPredicateWordingRole.Subordinate)
        {
            return null;
        }

        return arm.IsTrue
            ? PredicateWordingFormatter.Format(wording.Root)
            : PredicateWordingFormatter.FormatComplement(wording.Root);
    }

    /// <summary>
    /// Exact typed terminal wording for a terminating arm when the arm has one unique typed terminal
    /// result or outcome; null keeps the sentence-case technical "Condition" label. A typed
    /// structural-result factory label outranks an HTTP outcome label so the proven result meaning
    /// stays primary; a factory-less status result with exactly one typed outcome renders
    /// "Return HTTP &lt;status&gt;". Multiple distinct candidates fail closed rather than inventing a
    /// single meaning.
    /// </summary>
    private static string? TerminalArmLabel(ScenarioGraph graph, ScenarioArm arm, FragmentContext context)
    {
        if (!context.ArmNodes.TryGetValue(arm.Id, out var memberIds))
        {
            return null;
        }

        var members = graph.Nodes.Where(node => memberIds.Contains(node.Id)).ToArray();
        var factoryLabels = members
            .Where(node => node.Kind == ScenarioNodeKind.Result && node.Presentation?.ResultFactoryKind is not null)
            .Select(node => ResultFactoryKindLabel(node.Presentation!.ResultFactoryKind!.Value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (factoryLabels.Length == 1)
        {
            return factoryLabels[0];
        }

        var outcomeLabels = members
            .Where(node => node.Kind == ScenarioNodeKind.Outcome)
            .Select(node => node.Presentation?.OutcomeStatusCode)
            .Where(status => status is not null)
            .Select(status => $"Return HTTP {status!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return outcomeLabels.Length == 1 ? outcomeLabels[0] : null;
    }

    /// <summary>
    /// Exact terminal-arm Break fragment with no continuation. Evidence combines the enclosing arm
    /// and the terminal record actually used; certainty never exceeds the enclosing arm's combined
    /// support. The label reuses the arm's typed terminal wording so the Break never invents a
    /// separate meaning.
    /// </summary>
    private static DiagramFragment CreateBreakFragment(
        ScenarioGraph graph,
        ScenarioDecision decision,
        ScenarioArm arm,
        string label,
        CertaintyLevel armCertainty,
        FragmentContext context)
    {
        string key = ArmKey(decision, arm) + ":break";
        var groups = new List<ImmutableArray<EvidenceRef>> { arm.Evidence };
        if (context.ArmTerminals.TryGetValue(arm.Id, out var terminal))
        {
            groups.Add(terminal.Evidence);
        }

        var (evidence, certainty) = CombineEvidence(groups);
        certainty = Weakest(certainty, armCertainty);
        return new DiagramFragment(
            CreateBreakId(graph, key),
            key,
            label,
            DiagramFragmentKind.Break,
            [],
            [],
            [],
            evidence,
            certainty);
    }

    /// <summary>Most conservative (weakest) of two certainty levels under the existing ordering.</summary>
    private static CertaintyLevel Weakest(CertaintyLevel first, CertaintyLevel second)
        => first >= second ? first : second;

    /// <summary>Canonical key of a node set used to detect equal membership groups.</summary>
    private static string NodeSetKey(HashSet<ScenarioNodeId> nodes)
        => string.Join(",", nodes.Select(node => node.Value).Order(StringComparer.Ordinal));

    /// <summary>
    /// Combines supporting evidence groups and degrades certainty to the weakest contributor under
    /// the existing ordering so a Conservative membership never promotes a fragment to Exact.
    /// </summary>
    private static (ImmutableArray<EvidenceRef> Evidence, CertaintyLevel Certainty) CombineEvidence(
        IEnumerable<ImmutableArray<EvidenceRef>> groups)
    {
        var combined = groups
            .SelectMany(group => group)
            .DistinctBy(item => item.Id.Value)
            .ToImmutableArray();
        CertaintyLevel certainty = combined.Length == 0
            ? CertaintyLevel.Unknown
            : combined.Max(item => item.Certainty);
        return (combined, certainty);
    }

    /// <summary>Fragment support: decision, every arm, every arm's memberships, and every arm's terminal.</summary>
    private static (ImmutableArray<EvidenceRef> Evidence, CertaintyLevel Certainty) CombineFragmentEvidence(
        ScenarioDecision decision,
        FragmentContext context)
    {
        var groups = new List<ImmutableArray<EvidenceRef>> { decision.Evidence };
        if (context.NormalizedEvidence.TryGetValue(decision.Id, out var normalized))
        {
            groups.AddRange(normalized);
        }
        foreach (var arm in context.ArmsByDecision[decision.Id])
        {
            groups.Add(arm.Evidence);
            if (context.ArmMembershipEvidence.TryGetValue(arm.Id, out var memberships))
            {
                groups.Add(memberships);
            }

            if (context.ArmTerminals.TryGetValue(arm.Id, out var terminal))
            {
                groups.Add(terminal.Evidence);
            }
        }

        return CombineEvidence(groups);
    }

    /// <summary>Arm support: decision, the arm, the arm's memberships, and the arm's terminal.</summary>
    private static (ImmutableArray<EvidenceRef> Evidence, CertaintyLevel Certainty) CombineArmEvidence(
        ScenarioDecision decision,
        ScenarioArm arm,
        FragmentContext context)
    {
        var groups = new List<ImmutableArray<EvidenceRef>> { decision.Evidence, arm.Evidence };
        if (context.ArmMembershipEvidence.TryGetValue(arm.Id, out var memberships))
        {
            groups.Add(memberships);
        }

        if (context.ArmTerminals.TryGetValue(arm.Id, out var terminal))
        {
            groups.Add(terminal.Evidence);
        }

        return CombineEvidence(groups);
    }

    /// <summary>Stable Diagram Plan identity for a fragment (profile + entry point + kind + semantic key).</summary>
    private static DiagramPlanElementId CreateFragmentId(ScenarioGraph graph, ScenarioDecision decision)
        => CreateElementId(graph, "fragment", FragmentKey(decision));

    /// <summary>Stable Diagram Plan identity for an Alt arm (profile + entry point + kind + polarity key).</summary>
    private static DiagramPlanElementId CreateArmId(ScenarioGraph graph, ScenarioDecision decision, ScenarioArm arm)
        => CreateElementId(graph, "arm", ArmKey(decision, arm));

    /// <summary>Stable Diagram Plan identity for a Break fragment (profile + entry point + kind + terminal key).</summary>
    private static DiagramPlanElementId CreateBreakId(ScenarioGraph graph, string key)
        => CreateElementId(graph, "break", key);

    private static int MaxFragmentDepth(DiagramSequence sequence)
        => sequence.Fragments.Length == 0 ? 0 : sequence.Fragments.Max(MaxFragmentDepth);

    private static int MaxFragmentDepth(DiagramFragment fragment)
    {
        int nestedMax = fragment.Fragments.Length == 0 ? 0 : fragment.Fragments.Max(MaxFragmentDepth);
        foreach (var arm in fragment.Arms)
        {
            int armMax = arm.Fragments.Length == 0 ? 0 : arm.Fragments.Max(MaxFragmentDepth);
            nestedMax = Math.Max(nestedMax, armMax);
        }

        return 1 + nestedMax;
    }

    /// <summary>
    /// Stable DP001 planning diagnostic for the depth-limit flat fallback. The identity is grounded
    /// in the compilation profile and entry point so repeated planning of an unchanged graph yields
    /// the same diagnostic id.
    /// </summary>
    private static DiagramPlanDiagnostic CreateDepthDiagnostic(ScenarioGraph graph)
    {
        var id = StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            "DP001",
            AnalysisStage.FrameworkModel,
            graph.Profile,
            $"scenario:{graph.EntryPoint.Value}",
            0));
        return new DiagramPlanDiagnostic(
            id,
            "DP001",
            $"Fragment nesting depth exceeds the maximum supported depth of {MaxFragmentDepthLimit}; a flat non-truncated fallback is emitted.",
            "The unambiguous decision topology nests deeper than the default maximum fragment depth; no partial fragment tree is emitted.");
    }

    /// <summary>
    /// Stable DP002 planning diagnostic for a guarded message withheld from the diagram. The message
    /// node has exact arm membership in the reviewed topology (proving it is controlled by a
    /// decision), but every owning decision has an unsupported terminal/rejoin classification (for
    /// example SC013 exception-region or loop guards), so the admitted fragment contract cannot
    /// render it inside a continuing arm. Emitting it at the top-level sequence would falsely claim
    /// unconditional execution before the guards, so the planner withholds it and records this
    /// diagnostic instead. The identity is grounded in the compilation profile and entry point so
    /// repeated planning of an unchanged graph yields the same diagnostic id; certainty is inherited
    /// from the message's own evidence, never promoted by the withholding.
    /// </summary>
    private static DiagramPlanDiagnostic CreateWithheldDiagnostic(ScenarioGraph graph)
    {
        var id = StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            "DP002",
            AnalysisStage.FrameworkModel,
            graph.Profile,
            $"scenario:{graph.EntryPoint.Value}",
            0));
        return new DiagramPlanDiagnostic(
            id,
            "DP002",
            "A guarded data interaction has exact arm membership but its owning decision cannot be represented under the admitted fragment contract; the interaction is withheld from the diagram rather than shown unconditionally.",
            "The node is a member of an arm whose decision has unsupported terminal/rejoin classification (for example SC013 exception-region or loop guards); no exact continuing-arm placement exists, so the message is withheld.");
    }

    /// <summary>Semantic node rank used for wording phrase ordering: request path first, then facts, then failure before success.</summary>
    private static int SemanticNodeRank(ScenarioGraph graph, ScenarioNode node) => node.Kind switch
    {
        ScenarioNodeKind.EntryPoint => 0,
        ScenarioNodeKind.Action => 1,
        ScenarioNodeKind.MethodCall => 2,
        ScenarioNodeKind.ServiceCall => 2,
        ScenarioNodeKind.ClientOperationInvocation => 2,
        ScenarioNodeKind.EntityQuery => 3,
        ScenarioNodeKind.StateAssignment => 4,
        ScenarioNodeKind.EntityMutation => 5,
        ScenarioNodeKind.SourceObservation => 6,
        ScenarioNodeKind.Result =>
            string.Equals(node.Key, "result-status", StringComparison.Ordinal) ? 7
            : string.Equals(node.Key, "result-failure", StringComparison.Ordinal) ? 8
            : string.Equals(node.Key, "result-success", StringComparison.Ordinal) ? 10
            : 12,
        ScenarioNodeKind.Outcome => IncomingOutcomeKind(graph, node) == ScenarioEdgeKind.OutcomeFailure ? 9 : 11,
        ScenarioNodeKind.Delay => 10,
        _ => 13,
    };

    private static ScenarioEdgeKind? IncomingOutcomeKind(ScenarioGraph graph, ScenarioNode node)
        => graph.Edges
            .FirstOrDefault(edge => edge.Target == node.Id
                && edge.Kind is ScenarioEdgeKind.OutcomeSuccess or ScenarioEdgeKind.OutcomeFailure)
            ?.Kind;

    /// <summary>Semantic edge rank used for diagram message ordering: request path first, then facts, then failure before success.</summary>
    private static int SemanticEdgeRank(ScenarioEdgeKind kind) => kind switch
    {
        ScenarioEdgeKind.Entry => 0,
        ScenarioEdgeKind.Call => 1,
        ScenarioEdgeKind.Query => 2,
        ScenarioEdgeKind.StateAssignment => 3,
        ScenarioEdgeKind.Mutation => 4,
        ScenarioEdgeKind.Save => 5,
        ScenarioEdgeKind.Observation => 6,
        ScenarioEdgeKind.ResultStatus => 7,
        ScenarioEdgeKind.ResultFailure => 8,
        ScenarioEdgeKind.OutcomeFailure => 9,
        ScenarioEdgeKind.ResultSuccess => 10,
        ScenarioEdgeKind.OutcomeSuccess => 11,
        _ => 12,
    };

    /// <summary>
    /// Node ordering key. Request-path nodes (entry/action/service) and observations are ordered by
    /// semantic rank; source-ordered facts (query/assignment/mutation/save) are ordered by their
    /// unified compiler source ordinal first so an interleaved mutation never reorders behind a
    /// query of the same method; result/outcome nodes keep failure-before-success rank order.
    /// </summary>
    private static (int Segment, int Ordinal, int Rank) NodeOrderKey(ScenarioGraph graph, ScenarioNode node)
    {
        if (node.Presentation?.HostedWorkerControlKind is not null)
        {
            return (0, node.Presentation.HostedWorkerBlockOrdinal ?? int.MaxValue, SemanticNodeRank(graph, node));
        }
        int segment = node.Kind switch
        {
            ScenarioNodeKind.EntryPoint or ScenarioNodeKind.Action or ScenarioNodeKind.MethodCall
                or ScenarioNodeKind.ServiceCall or ScenarioNodeKind.ClientOperationInvocation => 0,
            ScenarioNodeKind.EntityQuery or ScenarioNodeKind.StateAssignment or ScenarioNodeKind.EntityMutation => 1,
            ScenarioNodeKind.SourceObservation => 2,
            ScenarioNodeKind.Delay or ScenarioNodeKind.Outcome when node.Presentation?.ActionKind == ScenarioActionKind.MinimalApiHandler => 2,
            _ => 3,
        };
        int rank = SemanticNodeRank(graph, node);
        int ordinal = segment == 1
            ? node.SequenceOrdinal
            : node.Kind is ScenarioNodeKind.MethodCall or ScenarioNodeKind.ClientOperationInvocation
                ? node.SequenceOrdinal
            : segment == 2 && node.Presentation?.ActionKind == ScenarioActionKind.MinimalApiHandler
                ? node.Presentation.SourceOrdinal ?? int.MaxValue
                : rank;
        return (segment, ordinal, rank);
    }

    /// <summary>
    /// Edge ordering key mirroring <see cref="NodeOrderKey"/>: source-ordered fact edges
    /// (query/state/mutation/save) honor their unified compiler source ordinal before semantic kind.
    /// </summary>
    private static (int Segment, int Ordinal, int Rank) EdgeOrderKey(ScenarioEdge edge)
    {
        int segment = edge.Kind switch
        {
            ScenarioEdgeKind.Entry or ScenarioEdgeKind.Call => 0,
            ScenarioEdgeKind.Query or ScenarioEdgeKind.StateAssignment or ScenarioEdgeKind.Mutation or ScenarioEdgeKind.Save => 1,
            ScenarioEdgeKind.Observation => 2,
            _ => 3,
        };
        int rank = SemanticEdgeRank(edge.Kind);
        int ordinal = edge.Kind == ScenarioEdgeKind.Call ? edge.SequenceOrdinal : segment == 1 ? edge.SequenceOrdinal : rank;
        return (segment, ordinal, rank);
    }

    /// <summary>Semantic participant rank: client, controller action, service, then data store.</summary>
    private static int ParticipantRank(string key) => key switch
    {
        "client" => 0,
        "action" => 1,
        "dispatch" => 2,
        "handler" => 3,
        "service" => 4,
        "data" => 5,
        _ => 9,
    };

    private static string ParentParticipantKey(ScenarioDispatchHandlerStep step, ScenarioDispatchHandlerExpansion expansion)
    {
        var parent = expansion.SourceSteps.SingleOrDefault(item => item.Id == step.ParentStepId);
        return parent is null ? "handler" : ParticipantKey(parent, expansion);
    }

    private static string ParticipantKey(ScenarioDispatchHandlerStep step, ScenarioDispatchHandlerExpansion expansion)
    {
        if (step.TargetParticipantIdentity is { Length: > 0 } identity)
        {
            return expansion.Participants.SingleOrDefault(item => item.Identity == identity)?.Key ?? "handler";
        }
        var separator = step.Label.IndexOf('.', StringComparison.Ordinal);
        var shortName = (separator < 0 ? step.Label : step.Label[..separator]).ToLowerInvariant();
        var matches = expansion.Participants.Where(item => item.Key == shortName && item.Identity is null).ToArray();
        return matches.Length == 1 ? matches[0].Key : "handler";
    }

    private static string DispatchMessageLabel(ScenarioNode? source, ScenarioNode? target)
    {
        if (target?.Kind == ScenarioNodeKind.Handler
            && !string.IsNullOrWhiteSpace(target.Presentation?.HandlerTypeName))
        {
            return target.Presentation.HandlerTypeName!;
        }

        if (target?.Kind == ScenarioNodeKind.Dispatch
            && !string.IsNullOrWhiteSpace(target.Presentation?.RequestTypeName))
        {
            return ShortTypeName(target.Presentation.RequestTypeName!);
        }

        if (!string.IsNullOrWhiteSpace(source?.Presentation?.RequestTypeName))
        {
            return ShortTypeName(source.Presentation.RequestTypeName!);
        }

        return "Dispatch";
    }

    private static void AddParticipant(
        Dictionary<string, DiagramParticipant> participants,
        ScenarioGraph graph,
        string key,
        string label,
        DiagramParticipantKind kind,
        ScenarioNode? source)
    {
        if (source is null || participants.ContainsKey(key))
        {
            return;
        }

        participants.Add(key, new DiagramParticipant(
            CreateElementId(graph, "participant", key),
            key,
            label,
            kind,
            source.Evidence,
            source.Certainty));
    }

    /// <summary>
    /// Readable outcome message label from the target outcome node's typed presentation. Primary
    /// labels always come from typed helper/status/created-route facts; a conflicting Detail string
    /// never overrides them. When typed facts are absent the explicit neutral label "HTTP outcome" is
    /// used instead of leaking internal detail.
    /// </summary>
    private static string OutcomeLabel(ScenarioGraph graph, ScenarioEdge edge)
    {
        var outcome = graph.Nodes.FirstOrDefault(node => node.Id == edge.Target);
        return OutcomeReadableLabel(outcome) ?? "HTTP outcome";
    }

    /// <summary>
    /// Typed outcome display built from the node's presentation facts. The generic StatusCode helper
    /// vocabulary never replaces the compiler-proven HTTP status meaning; every other helper keeps its
    /// exact typed helper/status label and a CreatedAtAction terminal carries its typed created route.
    /// Null when the graph proves no typed outcome facts.
    /// </summary>
    private static string? OutcomeReadableLabel(ScenarioNode? outcome)
    {
        var presentation = outcome?.Presentation;
        if (presentation is null
            || presentation.OutcomeHelperKind is not { } helperKind
            || presentation.OutcomeStatusCode is not { } statusCode)
        {
            return null;
        }

        string label = helperKind == HttpOutcomeHelperKind.StatusCode
            ? $"HTTP {statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : $"{helperKind} -> HTTP {statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        if (helperKind == HttpOutcomeHelperKind.CreatedAtAction
            && !string.IsNullOrWhiteSpace(presentation.OutcomeCreatedRoute))
        {
            // The created route may already carry the "GET " method prefix (test fixtures) or arrive
            // as the bare canonical route (the scenario builder); normalize so the label never
            // duplicates the method token.
            string route = presentation.OutcomeCreatedRoute.Trim();
            if (route.StartsWith("GET ", StringComparison.Ordinal))
            {
                route = route[4..];
            }

            label += $" links to GET {route}";
        }

        return label;
    }

    private static string BuildSourceObservationText(ScenarioNode node)
    {
        var presentation = node.Presentation;
        if (presentation?.HandlerBindingKind is { } binding
            && !string.IsNullOrWhiteSpace(presentation.HandlerParameterName)
            && !string.IsNullOrWhiteSpace(presentation.HandlerParameterTypeName))
        {
            string type = presentation.HandlerParameterTypeName;
            string name = presentation.HandlerParameterName;
            return binding switch
            {
                HttpBindingKind.Body => $"The request body binds to {type} {name}.",
                HttpBindingKind.Route => $"Route parameter {name} binds to {type}.",
                HttpBindingKind.Query => $"Query parameter {name} binds to {type}.",
                HttpBindingKind.CancellationToken => $"The framework supplies {type} {name}.",
                _ => $"The handler parameter {type} {name} has an unknown binding.",
            };
        }

        return $"Source observation: {node.Detail}.";
    }

    /// <summary>
    /// Conservative typed success-result label. The success/data path is invariant: an exact Success
    /// factory and an unknown success factory both prove data on the success path, so the neutral
    /// "Return success data" wording never parses Detail or generic type names.
    /// </summary>
    private static string ResultSuccessLabel() => "Return success data";

    /// <summary>
    /// Typed terminal wording for one structural result factory kind. The typed Success kind renders
    /// "Return success data"; exact failure kinds render their proven behavior; an unknown/custom
    /// kind falls back to "Return a failure status" and never invents NotFound/Conflict meaning.
    /// The wording comes only from the typed kind — never from Detail, node keys, or rendered
    /// labels (review F1).
    /// </summary>
    private static string ResultFactoryKindLabel(StructuralResultFactoryKind kind) => kind switch
    {
        StructuralResultFactoryKind.Success => ResultSuccessLabel(),
        StructuralResultFactoryKind.NotFound => "Return Not Found",
        StructuralResultFactoryKind.Conflict => "Return Conflict",
        StructuralResultFactoryKind.ValidationError => "Return validation failure",
        _ => "Return a failure status",
    };

    /// <summary>
    /// Conservative typed failure-result label from the node's structural factory kind. Exact factory
    /// kinds render their proven behavior; an unknown/custom factory or a missing typed fact falls
    /// back to the neutral "Return a failure status" that never invents NotFound/Conflict meaning.
    /// </summary>
    private static string ResultFailureLabel(ScenarioNode? result) => result?.Presentation?.ResultFactoryKind switch
    {
        StructuralResultFactoryKind.NotFound => "Return Not Found",
        StructuralResultFactoryKind.Conflict => "Return Conflict",
        StructuralResultFactoryKind.ValidationError => "Return validation failure",
        _ => "Return a failure status",
    };

    /// <summary>
    /// Conservative typed status-result label for a switch-selected outcome. The status node carries
    /// no single structural factory kind, so the wording stays neutral and never exposes the
    /// compiler "status result" phrase or the status enum type.
    /// </summary>
    private static string ResultStatusLabel() => "Return a status outcome";

    /// <summary>
    /// Resolves concise participant display labels. Collision qualification is group-local: only
    /// participants that share a structurally derived short name are qualified together, minimally,
    /// using the shortest common namespace suffix that keeps that group distinct; unrelated
    /// participants keep their concise short name. Type displays are parsed structurally so generic
    /// type arguments and metadata arity never leak into a user-facing label ("Widget>" or "`1").
    /// Participants without a typed type name keep their neutral fallback label.
    /// </summary>
    private static Dictionary<string, string> ResolveParticipantLabels(
        IEnumerable<(string Key, string? FullTypeName, string FallbackLabel)> sources)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        var typed = sources
            .Where(source => !string.IsNullOrWhiteSpace(source.FullTypeName))
            .Select(source => (source.Key, Display: ParseTypeDisplay(source.FullTypeName!)))
            .ToArray();
        foreach (var group in typed.GroupBy(item => item.Display.Name, StringComparer.Ordinal))
        {
            var members = group.ToArray();
            if (members.Length == 1)
            {
                labels[members[0].Key] = members[0].Display.Name;
                continue;
            }

            int maxSegments = members.Max(item => item.Display.Namespace.Length);
            for (int suffixLength = 1; suffixLength <= maxSegments; suffixLength++)
            {
                var candidates = members
                    .Select(item => (item.Key, Label: QualifyTypeDisplay(item.Display, suffixLength)))
                    .ToArray();
                if (candidates.Select(candidate => candidate.Label).Distinct(StringComparer.Ordinal).Count() == candidates.Length)
                {
                    foreach (var candidate in candidates)
                    {
                        labels[candidate.Key] = candidate.Label;
                    }

                    break;
                }
            }

            // Distinct compiler symbols cannot share a full structurally derived name; this defensive
            // fallback only fires when even the full namespace fails to distinguish.
            foreach (var member in members)
            {
                labels.TryAdd(member.Key, QualifyTypeDisplay(member.Display, member.Display.Namespace.Length));
            }
        }

        foreach (var source in sources)
        {
            if (!labels.ContainsKey(source.Key))
            {
                labels[source.Key] = source.FallbackLabel;
            }
        }

        return labels;
    }

    /// <summary>Joins the last namespace segments of a type display to its declared name.</summary>
    private static string QualifyTypeDisplay(TypeDisplay display, int suffixLength)
    {
        var tail = display.Namespace.TakeLast(suffixLength).ToArray();
        return tail.Length == 0 ? display.Name : string.Join(".", tail) + "." + display.Name;
    }

    /// <summary>
    /// Structurally derived display of a canonical compiler type name: the declared type name with
    /// generic type arguments and metadata arity removed, plus the namespace segments. For example
    /// <c>Acme.Api.Services.WidgetService`1&lt;Acme.Api.Models.Widget&gt;</c> yields namespace
    /// [Acme, Api, Services] and name WidgetService.
    /// </summary>
    private static TypeDisplay ParseTypeDisplay(string fullyQualifiedName)
    {
        int genericStart = fullyQualifiedName.IndexOf('<');
        string core = genericStart >= 0 ? fullyQualifiedName[..genericStart] : fullyQualifiedName;
        int arityMarker = core.IndexOf('`');
        if (arityMarker >= 0)
        {
            int end = arityMarker + 1;
            while (end < core.Length && char.IsDigit(core[end]))
            {
                end++;
            }

            core = core[..arityMarker];
        }

        var segments = core.Split(['.', '+'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return new TypeDisplay([], fullyQualifiedName);
        }

        return new TypeDisplay(segments[..^1], segments[^1]);
    }

    private static string ShortTypeName(string fullyQualifiedName)
        => ParseTypeDisplay(fullyQualifiedName).Name;

    private static Dictionary<string, string> BuildMethodCallParticipantKeys(ScenarioGraph graph, ScenarioNode? actionNode)
    {
        string? rootContainingType = actionNode?.Presentation?.ConfiguredContainingTypeName
            ?? actionNode?.Presentation?.ControllerTypeName;
        var types = graph.Nodes
            .Where(node => node.Kind is ScenarioNodeKind.MethodCall or ScenarioNodeKind.ClientOperationInvocation
                && !string.IsNullOrWhiteSpace(node.Presentation?.TargetContainingTypeName)
                && !IsRecognizedLoggingType(node.Presentation?.TargetContainingTypeName))
            .Select(node => node.Presentation!.TargetContainingTypeName!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(type => type, StringComparer.Ordinal)
            .ToArray();
        var reserved = ReservedParticipantKeys.ToHashSet(StringComparer.Ordinal);
        var bases = types.ToDictionary(type => type, NormalizeParticipantKey, StringComparer.Ordinal);
        var counts = bases.Values.GroupBy(value => value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var keys = types.ToDictionary(
            type => type,
            type => counts[bases[type]] == 1 && !reserved.Contains(bases[type])
                ? bases[type]
                : bases[type] + "_" + TypeKeySuffix(type),
            StringComparer.Ordinal);

        while (keys.Values.GroupBy(value => value, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            var collisions = keys.Values.GroupBy(value => value, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var type in types.Where(type => collisions.Contains(keys[type])))
            {
                keys[type] += "_" + TypeKeySuffix(type);
            }
        }

        if (rootContainingType is { Length: > 0 } rootType && types.Contains(rootType, StringComparer.Ordinal))
        {
            // Exact same-type calls reuse the root participant instead of duplicating it: a
            // configured root calling another method of its own type (or a controller action
            // calling another method of the same controller) renders against the single "action"
            // participant. The mapping is exact canonical type identity; participant creation and
            // edge resolution skip this key defensively through TryGetValue fallbacks.
            keys[rootType] = "action";
        }

        return keys;
    }

    private static string NormalizeParticipantKey(string fullyQualifiedName)
    {
        var key = new StringBuilder();
        foreach (var character in fullyQualifiedName)
        {
            if (char.IsLetterOrDigit(character))
            {
                key.Append(char.ToLowerInvariant(character));
            }
            else if (key.Length > 0 && key[^1] != '_')
            {
                key.Append('_');
            }
        }

        return key.ToString().TrimEnd('_');
    }

    private static string TypeKeySuffix(string canonicalTypeName)
        => Convert.ToHexString(Encoding.UTF8.GetBytes(canonicalTypeName)).ToLowerInvariant();

    /// <summary>
    /// Concise deterministic root participant label for a configured method: the namespace-free
    /// containing type and the member name (for example "TransferEngine.SubmitAsync"). The full
    /// signature stays in behavior text and evidence; the diagram label is scannable. Returns null
    /// when the typed fields are absent so callers keep the previous signature or neutral fallback.
    /// </summary>
    private static string? ConfiguredActionDisplayName(ScenarioNodePresentation? presentation)
    {
        if (presentation is { ConfiguredContainingTypeName: { Length: > 0 } type, ConfiguredMethodName: { Length: > 0 } method })
        {
            return $"{ShortTypeName(type)}.{method}";
        }

        return null;
    }

    /// <summary>
    /// Exact canonical Microsoft.Extensions.Logging surface hidden from presentation by default.
    /// Recognition is exact fully-qualified type identity plus the framework's exact member names,
    /// never name matching, so an application type named e.g. "LoggerExtensions" is never filtered.
    /// Filtered nodes, edges, and their evidence remain in the graph so the presentation filter is
    /// auditable; only messages, phrases, and participants are withheld.
    /// </summary>
    private static readonly string[] RecognizedLoggingTypes =
    [
        "Microsoft.Extensions.Logging.ILogger",
        "Microsoft.Extensions.Logging.LoggerExtensions",
    ];

    private static readonly HashSet<string> RecognizedLoggingMembers = new(StringComparer.Ordinal)
    {
        "Log", "LogTrace", "LogDebug", "LogInformation", "LogWarning", "LogError", "LogCritical",
        "BeginScope", "IsEnabled",
    };

    private static bool IsRecognizedLoggingType(string? containingType)
        => containingType is not null && RecognizedLoggingTypes.Contains(containingType);

    private static bool IsRecognizedLoggingCall(ScenarioNode? node)
        => node?.Kind == ScenarioNodeKind.MethodCall
            && IsRecognizedLoggingType(node.Presentation?.TargetContainingTypeName)
            && node.Presentation?.TargetMemberName is { Length: > 0 } member
            && RecognizedLoggingMembers.Contains(member);

    private static string? NodeContainingType(ScenarioNode node)
        => node.Presentation?.TargetContainingTypeName
            ?? node.Presentation?.ImplementationTypeName
            ?? node.Presentation?.DbContextTypeName
            ?? node.Presentation?.ConfiguredContainingTypeName
            ?? node.Presentation?.ControllerTypeName
            ?? node.Presentation?.HandlerTypeName;

    private static bool CallMatches(ScenarioNode node, ImmutableSortedSet<string> patterns)
    {
        if (node.Presentation?.TargetContainingTypeName is not { Length: > 0 } type
            || node.Presentation.TargetMemberName is not { Length: > 0 } member)
        {
            return false;
        }

        return patterns.Contains($"{type}.{member}") || patterns.Contains($"{type}.*");
    }

    private static void ValidateStructuralExclusions(
        ScenarioGraph graph,
        ImmutableSortedSet<string>? excludeParticipants)
    {
        if (excludeParticipants is null || excludeParticipants.Count == 0)
        {
            return;
        }

        var structuralTypes = graph.Nodes
            .Where(node => node.Kind == ScenarioNodeKind.Action)
            .Select(node => node.Presentation)
            .Where(presentation => presentation is not null)
            .SelectMany(presentation => new[]
            {
                presentation!.ConfiguredContainingTypeName,
                presentation.ControllerTypeName,
            })
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .ToHashSet(StringComparer.Ordinal);
        string? invalid = excludeParticipants.FirstOrDefault(structuralTypes.Contains);
        if (invalid is not null)
        {
            throw new ArgumentException(
                $"Participant exclusion '{invalid}' matches the structural root participant type and cannot be applied.",
                nameof(excludeParticipants));
        }
    }

    /// <summary>
    /// Exact renderable predicate wording for a decision: the predicate must carry Owner role and
    /// its normalized expression must contain no opaque value, so the formatted label is never the
    /// generic "Condition" token. Decisions without such wording are withheld from the diagram
    /// (their guarded messages are withheld and the boundary is retained in technical fallback)
    /// rather than presented with a meaningless label.
    /// </summary>
    private static bool TryGetExactPredicateLabel(ScenarioDecision decision, out string label)
    {
        if (decision.PredicateWording is { Role: ScenarioPredicateWordingRole.Owner } wording
            && !PredicateExpressionContainsOpaqueValue(wording.Root))
        {
            label = PredicateWordingFormatter.Format(wording.Root);
            return true;
        }

        label = string.Empty;
        return false;
    }

    private static bool PredicateExpressionContainsOpaqueValue(PredicateExpression expression)
        => expression.Kind == PredicateExpressionKind.OpaqueValue
            || expression.Children.Any(PredicateExpressionContainsOpaqueValue);

    /// <summary>True when a decision guards at least one member node that produces a diagram message.</summary>
    private static bool DecisionHasVisibleMember(ScenarioGraph graph, ScenarioDecision decision, PresentationFilter filter)
    {
        var armIds = graph.Topology.Arms
            .Where(arm => arm.Decision == decision.Id)
            .Select(arm => arm.Id)
            .ToHashSet();
        return graph.Topology.Memberships
            .Where(membership => armIds.Contains(membership.Arm))
            .Select(membership => membership.ScenarioNode)
            .Any(nodeId => graph.Nodes.Any(node => node.Id == nodeId
                && !filter.HiddenNodes.Contains(node.Id)
                && ProducesDiagramMessage(node)));
    }

    private static bool RegionHasVisibleMember(ScenarioGraph graph, ScenarioCallbackRegion region, PresentationFilter filter)
        => graph.Nodes.Any(node => region.MemberNodes.Contains(node.Id)
            && !filter.HiddenNodes.Contains(node.Id)
            && ProducesDiagramMessage(node));

    /// <summary>
    /// Node kinds that produce a diagram message or reference in the ordered sequence. State
    /// assignments and source observations order wording only and never produce diagram messages.
    /// </summary>
    private static bool ProducesDiagramMessage(ScenarioNode node) => node.Kind switch
    {
        ScenarioNodeKind.MethodCall => !IsRecognizedLoggingCall(node),
        ScenarioNodeKind.ClientOperationInvocation => true,
        ScenarioNodeKind.ServiceCall or ScenarioNodeKind.Dispatch or ScenarioNodeKind.Handler
            or ScenarioNodeKind.EntityQuery or ScenarioNodeKind.EntityMutation
            or ScenarioNodeKind.Result or ScenarioNodeKind.Outcome => true,
        _ => false,
    };

    private static bool IsFilteredInteractionNode(ScenarioNode node)
        => ProducesDiagramMessage(node) || IsRecognizedLoggingCall(node);

    /// <summary>
    /// Conservative presentation copy of an evidence reference: the same stable identity and fact,
    /// presented with conservative certainty so a withheld-boundary fallback phrase never claims
    /// more support than the planner's conservative withholding.
    /// </summary>
    private static EvidenceRef ConservativeCopy(EvidenceRef evidence)
        => new(
            evidence.Id,
            evidence.Kind,
            evidence.Artifact,
            evidence.Range,
            evidence.Symbol,
            evidence.Detail,
            CertaintyLevel.Conservative,
            evidence.UnderlyingEvidence,
            evidence.ProducerId,
            evidence.ProducerVersion);

    /// <summary>
    /// True when a subordinate decision belongs to a valid exact owner group and is therefore
    /// presented by the owner rather than withheld: exactly one owner shares the predicate id with
    /// exact wording, no subordinate arm terminates, and every member node of the subordinate is
    /// contained in the owner's arm member sets. This mirrors the fragment builder's predicate-group
    /// absorption contract for the wording fallback filter so absorbed subordinates never receive a
    /// spurious withheld-boundary phrase.
    /// </summary>
    private static bool IsAbsorbedSubordinate(ScenarioGraph graph, ScenarioDecision subordinate)
    {
        if (subordinate.PredicateWording?.Role != ScenarioPredicateWordingRole.Subordinate)
        {
            return false;
        }

        var predicateId = subordinate.PredicateWording.PredicateId;
        var owners = graph.Topology.Decisions
            .Where(candidate => candidate.PredicateWording is { Role: ScenarioPredicateWordingRole.Owner }
                && candidate.PredicateWording.PredicateId == predicateId
                && TryGetExactPredicateLabel(candidate, out _))
            .ToArray();
        if (owners.Length != 1)
        {
            return false;
        }

        var owner = owners[0];
        var ownerArmIds = graph.Topology.Arms
            .Where(arm => arm.Decision == owner.Id)
            .Select(arm => arm.Id)
            .ToHashSet();
        var ownerMembers = graph.Topology.Memberships
            .Where(membership => ownerArmIds.Contains(membership.Arm))
            .Select(membership => membership.ScenarioNode)
            .ToHashSet();
        if (ownerMembers.Count == 0)
        {
            return false;
        }

        var subordinateArmIds = graph.Topology.Arms
            .Where(arm => arm.Decision == subordinate.Id)
            .Select(arm => arm.Id)
            .ToHashSet();
        if (graph.Topology.Terminals.Any(terminal =>
                subordinateArmIds.Contains(terminal.Arm)
                && terminal.Kind == ScenarioTerminalKind.Terminates))
        {
            return false;
        }

        var subordinateMembers = graph.Topology.Memberships
            .Where(membership => subordinateArmIds.Contains(membership.Arm))
            .Select(membership => membership.ScenarioNode)
            .ToHashSet();
        return subordinateMembers.Count > 0 && subordinateMembers.IsSubsetOf(ownerMembers);
    }

    /// <summary>Structurally derived display parts of a canonical compiler type name.</summary>
    private sealed record TypeDisplay(string[] Namespace, string Name);

    /// <summary>
    /// Deterministic narrow English pluralization used only for Count/Clear labels. Only proven-safe
    /// forms are pluralized: a terminal y preceded by a consonant becomes ies (Category ->
    /// Categories), and a plain regular name that never requires an -es ending keeps the plain -s
    /// suffix (Reservation -> Reservations, Part -> Parts). Names with unsupported endings (s, x, z,
    /// ch, sh such as Box, Class, Status) return null so callers use honest neutral wording instead of
    /// visibly invalid forms (Boxs, Classs, Statuss). Broad linguistic inflection and irregular
    /// vocabulary are intentionally out of scope; the rule never infers business meaning.
    /// </summary>
    private static string? TryPluralize(string singular)
    {
        if (singular.Length >= 2
            && (singular[^1] == 'y' || singular[^1] == 'Y')
            && !IsEnglishVowel(singular[^2]))
        {
            return singular[..^1] + "ies";
        }

        if (RequiresEsPlural(singular))
        {
            return null;
        }

        return singular + "s";
    }

    /// <summary>True when the plain -s suffix would be invalid English because the name ends in s, x, z, ch, or sh.</summary>
    private static bool RequiresEsPlural(string singular)
    {
        char last = singular[^1];
        if (last is 's' or 'x' or 'z')
        {
            return true;
        }

        return last == 'h'
            && singular.Length >= 2
            && singular[^2] is 'c' or 's';
    }

    /// <summary>English vowel test used by the narrow pluralization rule; case-insensitive.</summary>
    private static bool IsEnglishVowel(char value)
        => value is 'a' or 'e' or 'i' or 'o' or 'u'
            or 'A' or 'E' or 'I' or 'O' or 'U';

    /// <summary>
    /// Concise service-call wording: the exact dependency-injection-resolved implementation name and
    /// its contract name, both derived from typed presentation facts. Concise names never leak
    /// application namespaces; the contract/implementation identity stays visible in the diagram and
    /// evidence-backed Markdown.
    /// </summary>
    private static string BuildServiceCallText(ScenarioNode node)
    {
        var presentation = node.Presentation;
        if (presentation is not null && !string.IsNullOrWhiteSpace(presentation.ImplementationTypeName))
        {
            string implementation = ShortTypeName(presentation.ImplementationTypeName);
            string contract = string.IsNullOrWhiteSpace(presentation.ContractTypeName)
                ? "its contract"
                : ShortTypeName(presentation.ContractTypeName);
            return $"The action calls the {implementation} implementation through the {contract} contract; the service is resolved through dependency injection.";
        }

        return "The action calls a service resolved through dependency injection.";
    }

    /// <summary>
    /// Protocol-neutral outbound service-client call wording. The claim describes only the call
    /// site's own compiler-proven syntax (discarded/assigned/returned/unclaimed, optionally awaited)
    /// and the operation's declared fault contract, if any; it never claims a network call executed,
    /// a response was received, or a runtime fault occurred.
    /// </summary>
    private static string BuildClientOperationInvocationText(ScenarioNode node)
    {
        var presentation = node.Presentation;
        var client = ShortTypeName(presentation?.ClientTypeName ?? "the client");
        var member = presentation?.CalledMemberName ?? "the operation";
        var contract = string.IsNullOrWhiteSpace(presentation?.ContractTypeName)
            ? null
            : ShortTypeName(presentation.ContractTypeName);
        var resultType = presentation?.DeclaredResultTypeName;

        var claim = presentation?.ResultClaimKind switch
        {
            ClientInvocationResultClaimKind.Discarded => string.IsNullOrWhiteSpace(resultType)
                ? "the result is discarded"
                : $"the result is discarded; the operation declares {ShortTypeName(resultType)}",
            ClientInvocationResultClaimKind.ResultAssigned => string.IsNullOrWhiteSpace(presentation?.ResultBindingName)
                ? "the call result is assigned"
                : $"the call result is assigned to {presentation!.ResultBindingName}",
            ClientInvocationResultClaimKind.ResultReturned => "the call result is returned",
            _ => string.IsNullOrWhiteSpace(resultType)
                ? "the call is made"
                : $"the call is made; result type {ShortTypeName(resultType)} is declared",
        };
        var awaited = presentation?.ResultIsAwaited == true ? ", awaited" : string.Empty;
        var faultText = string.IsNullOrWhiteSpace(presentation?.DeclaredFaultTypeNames)
            ? string.Empty
            : $" The operation declares fault: {presentation!.DeclaredFaultTypeNames}.";

        var callText = contract is null
            ? $"The action calls {client}.{member} through the service-client boundary"
            : $"The action calls {client}.{member} through the {contract} service-client boundary";
        return $"{callText}; {claim}{awaited}.{faultText}";
    }

    private static string BuildMethodCallText(ScenarioGraph graph, ScenarioNode node)
    {
        var presentation = node.Presentation;
        if (presentation?.ActionKind == ScenarioActionKind.HostedWorker)
        {
            return presentation.HostedWorkerLifecycleStep switch
            {
                HostedWorkerLifecycleStep.Start => presentation.HostedWorkerCancellationParameterName is { Length: > 0 } startParameter
                    ? $"The registered hosted-worker lifecycle includes StartAsync with cancellation parameter evidence: {startParameter}."
                    : "The registered hosted-worker lifecycle includes StartAsync.",
                HostedWorkerLifecycleStep.Execute => presentation.HostedWorkerCancellationParameterName is { Length: > 0 } parameter
                    ? $"The registered hosted-worker lifecycle includes ExecuteAsync with cancellation parameter evidence: {parameter}."
                    : "The registered hosted-worker lifecycle includes ExecuteAsync.",
                HostedWorkerLifecycleStep.Stop => presentation.HostedWorkerCancellationParameterName is { Length: > 0 } stopParameter
                    ? $"The registered hosted-worker lifecycle includes StopAsync with cancellation parameter evidence: {stopParameter}."
                    : "The registered hosted-worker lifecycle includes StopAsync.",
                _ => presentation.HostedWorkerSchedulerRegistration
                    ? "The hosted worker registers a timer callback."
                    : "The hosted worker has an unsupported lifecycle slot.",
            };
        }
        if (presentation is not null
            && !string.IsNullOrWhiteSpace(presentation.TargetContainingTypeName)
            && !string.IsNullOrWhiteSpace(presentation.TargetMemberName))
        {
            var source = graph.Edges
                .Where(edge => edge.Kind == ScenarioEdgeKind.Call && edge.Target == node.Id)
                .Select(edge => graph.Nodes.FirstOrDefault(candidate => candidate.Id == edge.Source))
                .FirstOrDefault(candidate => candidate is not null);
            var caller = source?.Kind == ScenarioNodeKind.MethodCall
                && source.Presentation is { TargetContainingTypeName: { Length: > 0 } parentType, TargetMemberName: { Length: > 0 } parentMember }
                ? $"The called method {ShortTypeName(parentType)}.{parentMember}"
                : graph.RootKind == ScenarioRootKind.ConfiguredMethod
                    ? $"The selected method {OperationKey(graph)}"
                    : "The action";
            return $"{caller} calls {ShortTypeName(presentation.TargetContainingTypeName)}.{presentation.TargetMemberName}.";
        }

        return graph.RootKind == ScenarioRootKind.ConfiguredMethod
            ? $"The selected method {OperationKey(graph)} makes an exact method call."
            : "The action makes an exact method call.";
    }

    private static string OperationKey(ScenarioGraph graph)
        => string.IsNullOrWhiteSpace(graph.OperationKey) ? graph.RootMethod.Value : graph.OperationKey;

    /// <summary>
    /// A service-contract operation root shares <see cref="ScenarioRootKind.HttpEntryPoint"/> with
    /// controller/Minimal API roots (it is neither a configured method nor a hosted worker), but it
    /// carries no HTTP method or route, so callers must check the action node's presentation instead
    /// of formatting <see cref="ScenarioGraph.HttpMethod"/>/<see cref="ScenarioGraph.CanonicalRoute"/>.
    /// </summary>
    private static bool IsServiceOperationRoot(ScenarioGraph graph)
        => graph.Nodes.Any(node => node.Kind == ScenarioNodeKind.Action
            && node.Presentation?.ActionKind == ScenarioActionKind.ServiceOperation);

    private static int DirectCallOrder(ScenarioGraph graph, ScenarioEdge edge)
    {
        if (edge.Kind == ScenarioEdgeKind.Entry)
        {
            return -1;
        }
        if (edge.Kind != ScenarioEdgeKind.Call)
        {
            return int.MaxValue;
        }
        var index = graph.DirectCallExpansion.Steps
            .Select((step, ordinal) => (step, ordinal))
            .FirstOrDefault(item => item.step.ScenarioNodeId == edge.Target);
        return index.step is null ? int.MaxValue : index.ordinal;
    }

    /// <summary>
    /// Sentence-case, namespace-free query wording from typed presentation facts. The phrase keeps the
    /// exact entity short name and the admitted terminal kind; the exact SingleOrDefaultAsync and
    /// FirstOrDefaultAsync lookups both read as at-most-one "finds", while a Count aggregation uses a
    /// proven-safe plural or the honest neutral "items of type" wording. Unsupported shapes fall back
    /// to the graph's conservative detail text.
    /// </summary>
    private static string BuildQueryPhraseText(ScenarioNode node)
    {
        var presentation = node.Presentation;
        if (presentation is not null && !string.IsNullOrWhiteSpace(presentation.EntityTypeName))
        {
            string entity = ShortTypeName(presentation.EntityTypeName);
            if (presentation.QueryOperatorKind is EntityFrameworkQueryOperatorKind.CountAsync or EntityFrameworkQueryOperatorKind.Count)
            {
                return TryPluralize(entity) is { } plural
                    ? $"The service queries the data store: counts {plural}."
                    : $"The service queries the data store: counts items of type {entity}.";
            }

            if (presentation.QueryOperatorKind is
                EntityFrameworkQueryOperatorKind.SingleOrDefaultAsync
                or EntityFrameworkQueryOperatorKind.FirstOrDefaultAsync
                or EntityFrameworkQueryOperatorKind.FirstOrDefault)
            {
                return $"The service queries the data store: finds at most one {entity}.";
            }
        }

        return $"The service queries the data store: {node.Detail}.";
    }

    /// <summary>Concise sentence-case query message label from typed presentation facts; null when the graph proves none.</summary>
    private static string? BuildQueryLabel(ScenarioGraph graph, ScenarioEdge edge)
    {
        var presentation = graph.Nodes.FirstOrDefault(node => node.Id == edge.Target)?.Presentation;
        if (presentation is null || string.IsNullOrWhiteSpace(presentation.EntityTypeName))
        {
            return null;
        }

        string entity = ShortTypeName(presentation.EntityTypeName);
        return presentation.QueryOperatorKind switch
        {
            EntityFrameworkQueryOperatorKind.SingleOrDefaultAsync
                or EntityFrameworkQueryOperatorKind.FirstOrDefaultAsync
                or EntityFrameworkQueryOperatorKind.FirstOrDefault => $"Find at most one {entity}",
            EntityFrameworkQueryOperatorKind.CountAsync
                or EntityFrameworkQueryOperatorKind.Count => TryPluralize(entity) is { } plural ? $"Count {plural}" : $"Count items of type {entity}",
            _ => null,
        };
    }

    /// <summary>Kind-distinct sentence-case mutation message label from typed presentation facts; null when the graph proves none.</summary>
    private static string? BuildMutationLabel(ScenarioGraph graph, ScenarioEdge edge)
    {
        var presentation = graph.Nodes.FirstOrDefault(node => node.Id == edge.Target)?.Presentation;
        if (presentation is null || string.IsNullOrWhiteSpace(presentation.EntityTypeName))
        {
            return null;
        }

        string entity = ShortTypeName(presentation.EntityTypeName);
        return presentation.MutationKind switch
        {
            EntityFrameworkMutationKind.Add => $"Add {entity}",
            EntityFrameworkMutationKind.RemoveRange => $"Remove {entity} range",
            EntityFrameworkMutationKind.Clear => TryPluralize(entity) is { } plural ? $"Clear tracked {plural}" : $"Clear tracked items of type {entity}",
            _ => null,
        };
    }

    /// <summary>
    /// Exact sentence-case save message label proven by the save node's typed SaveChangesAsync
    /// mutation kind; null when the graph proves none so the conservative edge detail remains visible.
    /// The label is a fixed known wording for the compiler-proven save operation, never the lowercase
    /// generic edge detail text.
    /// </summary>
    private static string? BuildSaveLabel(ScenarioGraph graph, ScenarioEdge edge)
    {
        var presentation = graph.Nodes.FirstOrDefault(node => node.Id == edge.Target)?.Presentation;
        if (presentation?.MutationKind is EntityFrameworkMutationKind.SaveChangesAsync or EntityFrameworkMutationKind.SaveChanges)
        {
            return "calls SaveChanges";
        }

        return null;
    }

    /// <summary>Exact called member concise name proven by the service-call node presentation; null when the graph proves none.</summary>
    private static string? ServiceCalledMemberLabel(ScenarioGraph graph, ScenarioEdge edge)
        => graph.Nodes.FirstOrDefault(node => node.Id == edge.Target)?.Presentation?.CalledMemberName;

    /// <summary>
    /// Builds the full call message label: the concise member name plus the compiler-proven
    /// argument summary when available. Strings are quoted; other constant values are bare.
    /// Falls back to the bare member name when no argument label is proven.
    /// </summary>
    private static string CallMessageLabel(ScenarioNode? targetNode, string fallback)
    {
        var memberName = targetNode?.Kind == ScenarioNodeKind.MethodCall
            ? targetNode.Presentation?.TargetMemberName
            : targetNode?.Presentation?.CalledMemberName;
        var label = memberName ?? fallback;
        var argumentLabel = targetNode?.Presentation?.ArgumentLabel;
        if (argumentLabel is not null)
        {
            return $"{label}({argumentLabel})";
        }
        return label;
    }

    /// <summary>
    /// Checks whether a call should be excluded based on configurable exclude patterns.
    /// Patterns are "Type.Method" or "Type.*" where * matches any method on that type.
    /// Matching is case-sensitive on the full containing type name and member name.
    /// </summary>
    private static bool IsExcludedByPattern(ScenarioNode? targetNode, string fallbackLabel, ImmutableSortedSet<string> excludePatterns)
    {
        if (excludePatterns.Count == 0)
        {
            return false;
        }

        string typeName = targetNode?.Presentation?.TargetContainingTypeName
            ?? targetNode?.Presentation?.CalledMemberName
            ?? string.Empty;
        string methodName = targetNode?.Presentation?.TargetMemberName
            ?? fallbackLabel;

        if (string.IsNullOrEmpty(typeName))
        {
            return false;
        }

        string fullKey = $"{typeName}.{methodName}";
        string typeAny = $"{typeName}.*";

        return excludePatterns.Contains(fullKey) || excludePatterns.Contains(typeAny);
    }

    private static string MessageKey(ScenarioEdge edge) => $"message:{edge.Id.Value}";

    private static string FormatHandlerDelay(int milliseconds)
        => milliseconds % 1000 == 0
            ? $"Wait {milliseconds / 1000} seconds"
            : $"Wait {milliseconds} milliseconds";

    private static string FormatHandlerDelayPhrase(string detail)
    {
        const string prefix = "requested delay ";
        if (!detail.StartsWith(prefix, StringComparison.Ordinal)
            || !int.TryParse(detail[prefix.Length..].Split(' ')[0], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var milliseconds))
        {
            return "The handler requests a delay.";
        }
        return milliseconds % 1000 == 0
            ? $"The handler requests a delay of {milliseconds / 1000} seconds."
            : $"The handler requests a delay of {milliseconds} milliseconds.";
    }

    private static DiagramSequence BuildHandlerSequence(
        ScenarioGraph graph, ScenarioHandlerTopology topology, List<DiagramMessage> messages)
    {
        var messageIds = messages.ToDictionary(message => message.Key, message => message.Id, StringComparer.Ordinal);
        DiagramPlanElementId Message(int source, bool delay)
            => messageIds[delay ? $"handler-delay:{source}" : $"handler-outcome:{source}"];

        var prefix = messages
            .Where(message => !message.Key.StartsWith("handler-", StringComparison.Ordinal))
            .Select(message => DiagramSequenceElement.MessageRef(message.Id))
            .ToList();

        var operations = topology.Delays
            .Select(item => (item.SourceOrdinal, item.Evidence, item.Certainty,
                item.DecisionOrdinal, item.IsTrue, Reference: Message(item.SourceOrdinal, true)))
            .Concat(topology.Outcomes
                .Select(item => (item.SourceOrdinal, item.Evidence, item.Certainty,
                    item.DecisionOrdinal, item.IsTrue, Reference: Message(item.SourceOrdinal, false))))
            .ToArray();

        (ImmutableArray<EvidenceRef> Evidence, CertaintyLevel Certainty) Support(
            ScenarioHandlerDecision decision,
            IEnumerable<(int SourceOrdinal, ImmutableArray<EvidenceRef> Evidence,
                CertaintyLevel Certainty, int DecisionOrdinal, bool IsTrue, DiagramPlanElementId Reference)> direct,
            IEnumerable<DiagramFragment> children)
        {
            var childArray = children.ToArray();
            var evidence = CombineEvidence([
                decision.Evidence,
                .. direct.Select(item => item.Evidence),
                .. childArray.Select(item => item.Evidence)]);
            var certainty = Weakest(decision.Certainty, evidence.Certainty);
            foreach (var child in childArray)
            {
                certainty = Weakest(certainty, child.Certainty);
            }

            return (evidence.Evidence, certainty);
        }

        DiagramFragment BuildDecision(ScenarioHandlerDecision decision)
        {
            DiagramAltArm BuildArm(bool isTrue)
            {
                var direct = operations
                    .Where(item => item.DecisionOrdinal == decision.Ordinal && item.IsTrue == isTrue)
                    .OrderBy(item => item.SourceOrdinal)
                    .ToArray();
                var children = topology.Decisions
                    .Where(item => item.ParentDecisionOrdinal == decision.Ordinal && item.ParentIsTrue == isTrue)
                    .OrderBy(item => item.Ordinal)
                    .Select(BuildDecision)
                    .ToArray();
                var support = Support(decision, direct, children);
                return new DiagramAltArm(
                    CreateElementId(graph, "arm", $"handler:decision:{decision.Ordinal}:{(isTrue ? "true" : "false")}"),
                    $"handler:decision:{decision.Ordinal}:{(isTrue ? "true" : "false")}",
                    isTrue ? decision.PredicateText : "Otherwise",
                    !isTrue,
                    direct.Select(item => item.Reference).ToImmutableArray(),
                    children.ToImmutableArray(),
                    support.Evidence,
                    support.Certainty);
            }

            var trueArm = BuildArm(true);
            var falseArm = BuildArm(false);
            var support = CombineEvidence([decision.Evidence, trueArm.Evidence, falseArm.Evidence]);
            var certainty = Weakest(decision.Certainty, Weakest(trueArm.Certainty, falseArm.Certainty));
            return new DiagramFragment(
                CreateElementId(graph, "fragment", $"handler:decision:{decision.Ordinal}"),
                $"handler:decision:{decision.Ordinal}", decision.PredicateText, DiagramFragmentKind.Alt,
                [trueArm, falseArm], [], [], support.Evidence, certainty);
        }

        if (topology.Decisions.IsDefaultOrEmpty)
        {
            prefix.AddRange(operations
                .OrderBy(item => item.SourceOrdinal)
                .Select(item => DiagramSequenceElement.MessageRef(item.Reference)));
            return new DiagramSequence(prefix.ToImmutableArray());
        }

        prefix.AddRange(topology.Decisions
            .Where(decision => decision.ParentDecisionOrdinal is null)
            .OrderBy(decision => decision.Ordinal)
            .Select(decision => DiagramSequenceElement.Fragment(BuildDecision(decision))));
        return new DiagramSequence(prefix.ToImmutableArray());
    }

    /// <summary>
    /// Canonical path-free message reference. The reference embeds the stable scenario edge id so
    /// sequence-tree refs and message ids are equal by construction and independent of labels,
    /// traversal order, or construction order.
    /// </summary>
    private static DiagramPlanElementId CreateMessageRef(ScenarioEdge edge)
        => new("diagram-element:v1:message:" + edge.Id.Value);

    private static DiagramMessage CreateMessage(
        ScenarioGraph graph,
        ScenarioEdge edge,
        string source,
        string target,
        string label,
        DiagramMessageKind kind)
        => new(
            CreateMessageRef(edge),
            MessageKey(edge),
            source,
            target,
            label,
            kind,
            edge.Evidence,
            edge.Certainty);

    private static DiagramBranch CreateBranch(
        ScenarioGraph graph,
        string key,
        string label,
        DiagramBranchKind kind,
        IEnumerable<string> messageKeys,
        IEnumerable<EvidenceRef> evidence)
    {
        // Message keys retain the planner's semantic insertion order (result before outcome within
        // the polarity path); distinct deduplicates without re-sorting.
        var orderedKeys = messageKeys.Distinct(StringComparer.Ordinal).ToImmutableArray();
        var combined = evidence
            .DistinctBy(item => item.Id.Value)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        return new DiagramBranch(
            CreateElementId(graph, "branch", key),
            key,
            label,
            kind,
            orderedKeys,
            combined,
            combined.Min(item => item.Certainty));
    }

    private static void CreatePhrase(
        ScenarioGraph graph,
        Dictionary<string, int> phraseOrdinals,
        List<WordingPhrase> phrases,
        WordingPhraseKind kind,
        string key,
        string text,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        // Repeated phrase keys (multiple outcomes, several fallbacks) receive increasing ordinals so
        // every phrase keeps a distinct canonical identity and stable ordering.
        int ordinal = phraseOrdinals.GetValueOrDefault(key);
        phraseOrdinals[key] = ordinal + 1;
        phrases.Add(new WordingPhrase(
            CreatePhraseId(graph, kind, key, ordinal),
            ordinal == 0 ? key : $"{key}:{ordinal}",
            kind,
            text,
            evidence,
            certainty));
    }

    private static WordingPhraseId CreatePhraseId(
        ScenarioGraph graph,
        WordingPhraseKind kind,
        string key,
        int ordinal)
        => StableIdentity.CreateWordingPhraseId(new WordingPhraseIdentityDescriptor(
            graph.Profile,
            graph.EntryPoint,
            kind.ToString(),
            key,
            ordinal));

    private static DiagramPlanElementId CreateElementId(ScenarioGraph graph, string elementKind, string key)
        => StableIdentity.CreateDiagramPlanElementId(new DiagramPlanElementIdentityDescriptor(
            graph.Profile,
            graph.EntryPoint,
            elementKind,
            key));

    private static string BuildFallbackText(ScenarioGraphDiagnostic diagnostic) => diagnostic.Code switch
    {
        "SC001" => "Technical fallback: the action call could not be joined to exactly one service implementation; service claims are withheld.",
        "SC003" => "Technical fallback: the service method contains several query facts; no single query is selected.",
        "SC004" => "Technical fallback: a decision path had no unique HTTP outcome fact; that outcome claim is withheld.",
        "SC005" => "Technical fallback: the query predicate comparison is not fully supported; query details are conservative.",
        "SC006" => "Technical fallback: no compiler-proven result decision could be joined; outcome claims are withheld.",
        "SC007" => "Technical fallback: the service result could not be fully associated with HTTP outcomes; result and outcome claims are withheld.",
        "SC010" => "Technical fallback: the created outcome could not be joined to exactly one Get entry point; the created link is withheld.",
        "SC014" => "Technical fallback: the FusionCache callback boundary has no exact supported GetOrSetAsync contract; cache-miss membership is withheld.",
        _ => $"Technical fallback: the analysis recorded an unresolved finding ({diagnostic.Code}); {diagnostic.Summary}.",
    };

    /// <summary>
    /// Distinguishes a save (SaveChangesAsync) mutation node from other EF mutations for wording.
    /// Classification comes from the typed mutation kind only; a conflicting Detail string (for
    /// example an Add node whose detail text claims a save) never overrides it.
    /// </summary>
    private static bool IsSaveNode(ScenarioNode node)
        => node.Presentation?.MutationKind is EntityFrameworkMutationKind.SaveChangesAsync or EntityFrameworkMutationKind.SaveChanges;

    /// <summary>
    /// Kind-distinct mutation wording built from typed presentation facts only; a conflicting Detail
    /// string never overrides the compiler-proven mutation kind. Unsupported shapes keep the graph's
    /// evidence-backed detail text.
    /// </summary>
    private static string BuildMutationPhraseText(ScenarioNode node)
    {
        var presentation = node.Presentation;
        if (presentation is not null && !string.IsNullOrWhiteSpace(presentation.EntityTypeName))
        {
            string entity = ShortTypeName(presentation.EntityTypeName);
            switch (presentation.MutationKind)
            {
                case EntityFrameworkMutationKind.Add:
                    return $"The service mutates the data store: adds {entity}.";
                case EntityFrameworkMutationKind.RemoveRange:
                    return $"The service mutates the data store: removes {entity} records.";
                case EntityFrameworkMutationKind.Clear:
                    return $"The service mutates the data store: clears the tracked {entity} set.";
            }
        }

        return $"The service mutates the data store: {node.Detail}.";
    }

    /// <summary>
    /// Exact save wording built from the typed SaveChangesAsync mutation kind and DbContext identity;
    /// a conflicting Detail string never overrides the save classification.
    /// </summary>
    private static string BuildSavePhraseText(ScenarioNode node)
    {
        var presentation = node.Presentation;
        if (presentation is not null && !string.IsNullOrWhiteSpace(presentation.DbContextTypeName))
        {
            return $"The service calls SaveChanges on {ShortTypeName(presentation.DbContextTypeName)}.";
        }

        return "The service calls SaveChanges.";
    }

    private static ImmutableArray<EvidenceRef> SourceEvidenceFallback(ScenarioGraph graph)
    {
        // Defensive fallback only for a graph with no entry node; normal graphs always carry one.
        var descriptor = new EvidenceIdentityDescriptor(
            EvidenceKind.Source,
            $"scenario:{graph.EntryPoint.Value}",
            null,
            null,
            null,
            "scenario-entry",
            CertaintyLevel.Conservative,
            Detail: null);
        return [new EvidenceRef(
            StableIdentity.CreateEvidenceId(descriptor),
            EvidenceKind.Source,
            descriptor.Artifact,
            null,
            null,
            "scenario entry point evidence",
            CertaintyLevel.Conservative)];
    }

    private static string BuildWordingDebugProjection(IEnumerable<WordingPhrase> phrases)
    {
        var lines = phrases
            .Select(phrase => $"phrase {phrase.Id.Value} key={phrase.Key} kind={phrase.Kind.ToString()} certainty={phrase.Certainty} text={phrase.Text}");
        return string.Join('\n', lines);
    }

    private static string BuildDiagramDebugProjection(
        IEnumerable<DiagramParticipant> participants,
        IEnumerable<DiagramMessage> messages,
        DiagramSequence sequence,
        IEnumerable<DiagramBranch> branches,
        IEnumerable<DiagramPlanDiagnostic> diagnostics)
    {
        var builder = new StringBuilder();
        foreach (var participant in participants)
        {
            builder.Append("participant ").Append(participant.Id.Value).Append(" key=").Append(participant.Key)
                .Append(" kind=").Append(participant.Kind.ToString()).Append(" certainty=").Append(participant.Certainty)
                .Append(" label=").Append(participant.Label)
                .Append(" canonical=").Append(CanonicalParticipantDebugIdentity(participant)).Append('\n');
        }

        foreach (var message in messages)
        {
            builder.Append("message ").Append(message.Id.Value).Append(" key=").Append(message.Key)
                .Append(" source=").Append(message.Source).Append(" target=").Append(message.Target)
                .Append(" kind=").Append(message.Kind.ToString()).Append(" certainty=").Append(message.Certainty)
                .Append(" label=").Append(message.Label).Append('\n');
        }

        // Ordered sequence elements expose the exact chronological placement of every message ref
        // and fragment (message-before, fragment, message-after) so renderer chronology is
        // inspectable without invoking the renderer; nested fragments follow in canonical tree order.
        AppendSequenceElementProjection(builder, sequence);

        foreach (var branch in branches)
        {
            builder.Append("branch ").Append(branch.Id.Value).Append(" key=").Append(branch.Key)
                .Append(" kind=").Append(branch.Kind.ToString()).Append(" certainty=").Append(branch.Certainty)
                .Append(" label=").Append(branch.Label)
                .Append(" messages=").Append(string.Join(",", branch.MessageKeys)).Append('\n');
        }

        foreach (var diagnostic in diagnostics)
        {
            builder.Append("diagnostic ").Append(diagnostic.Id.Value).Append(" code=").Append(diagnostic.Code)
                .Append(" summary=").Append(diagnostic.Summary).Append(" detail=").Append(diagnostic.Detail).Append('\n');
        }

        return builder.ToString().TrimEnd('\n');
    }

    private static string CanonicalParticipantDebugIdentity(DiagramParticipant participant)
        => participant.Evidence
            .Select(evidence => evidence.Symbol)
            .FirstOrDefault(symbol => !string.IsNullOrWhiteSpace(symbol))
            ?? participant.Evidence[0].Id.Value;

    /// <summary>
    /// Emits one <c>element</c> line per ordered sequence element with its exact zero-based position
    /// and kind (message ref or fragment), then recurses into nested fragments in canonical tree
    /// order.
    /// </summary>
    private static void AppendSequenceElementProjection(StringBuilder builder, DiagramSequence sequence)
    {
        int position = 0;
        foreach (var element in sequence.Elements)
        {
            if (element.IsMessageRef)
            {
                builder.Append("element ").Append(element.MessageRefId!.Value)
                    .Append(" kind=message position=").Append(position).Append('\n');
            }
            else
            {
                var fragment = element.NestedFragment!;
                builder.Append("element ").Append(fragment.Id.Value)
                    .Append(" kind=fragment position=").Append(position).Append('\n');
                AppendFragmentProjection(builder, fragment);
            }

            position++;
        }
    }

    private static void AppendFragmentProjection(StringBuilder builder, DiagramFragment fragment)
    {
        builder.Append("fragment ").Append(fragment.Id.Value).Append(" key=").Append(fragment.Key)
            .Append(" kind=").Append(fragment.Kind.ToString()).Append(" certainty=").Append(fragment.Certainty)
            .Append(" label=").Append(fragment.Label).Append('\n');
        foreach (var arm in fragment.Arms)
        {
            builder.Append("arm ").Append(arm.Id.Value).Append(" key=").Append(arm.Key)
                .Append(" isElse=").Append(arm.IsElse).Append(" certainty=").Append(arm.Certainty)
                .Append(" label=").Append(arm.Label).Append('\n');
            foreach (var nested in arm.Fragments)
            {
                AppendFragmentProjection(builder, nested);
            }
        }

        foreach (var nested in fragment.Fragments)
        {
            AppendFragmentProjection(builder, nested);
        }
    }
}
