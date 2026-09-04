using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Analysis.Scenarios;
using System.Collections.Immutable;
using Xunit;

namespace SeqDoc.Scenarios.Tests;

public sealed class HostedWorkerCallbackScenarioTests
{
    [Theory]
    [InlineData("caller")]
    [InlineData("contract")]
    [InlineData("target")]
    public void CallbackJoinWithForeignIdentityNeverAdmitsARegion(string foreignAnchor)
    {
        var baseRequest = ScenarioTestFactory.CreateCallbackBoundaryRequest();
        var boundary = new CallbackBoundaryFact(
            ScenarioTestFactory.PrimaryCallbackBoundaryId,
            foreignAnchor == "caller" ? new MethodId("method:v1:foreign-caller") : ScenarioTestFactory.ServiceMethod,
            ScenarioTestFactory.CallbackOuterInvocationOperation,
            0,
            foreignAnchor == "target" ? CallbackTargetKind.MethodGroup : CallbackTargetKind.AnonymousFunction,
            foreignAnchor == "target" ? new MethodId("method:v1:foreign-target") : null,
            foreignAnchor == "target" ? null : ScenarioTestFactory.CallbackTargetBodyOperation,
            foreignAnchor == "contract" ? new MethodId("method:v1:foreign-contract") : ScenarioTestFactory.ServiceMethod,
            ScenarioTestFactory.CallbackContractInvokeOperation,
            CallbackCardinality.ZeroOrOne,
            CallbackTriggerKind.Conditional,
            ScenarioTestFactory.CallbackConditionOperation,
            CallbackCompletionKind.RejoinsCaller,
            CallbackContractProvenance.SourceBody,
            [ScenarioTestFactory.ServiceQueryOperation.Value],
            [ScenarioTestFactory.SourceEvidence($"foreign-{foreignAnchor}")],
            SeqDoc.Core.Evidence.CertaintyLevel.Exact);
        var facts = new CallbackBoundaryFactSet(
            1,
            "test",
            baseRequest.Profile,
            baseRequest.ProgramIndex.IndexFingerprint,
            [boundary],
            [],
            $"foreign-{foreignAnchor}");

        var graph = Assert.Single(ScenarioGraphBuilder.Build(baseRequest with { CallbackBoundaryFacts = facts }).Graphs);

        Assert.Empty(graph.CallbackRegions);
    }

    [Fact]
    public void ReversedCallbackFactsAndEvidenceKeepRegionProjectionCanonical()
    {
        var forward = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateCallbackBoundaryRequest()).Graphs.Single();
        var reversed = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateCallbackBoundaryRequest(
            reverseBoundaryConstruction: true,
            reverseMemberOrder: true)).Graphs.Single();

        Assert.Equal(forward.DebugProjection, reversed.DebugProjection);
        Assert.Equal(forward.CallbackRegions.Select(region => region.Id), reversed.CallbackRegions.Select(region => region.Id));
        Assert.Equal(
            forward.CallbackRegions.SelectMany(region => region.MemberNodes).Select(node => node.Value),
            reversed.CallbackRegions.SelectMany(region => region.MemberNodes).Select(node => node.Value));
    }

    [Fact]
    public void OverlappingBoundariesDoNotFirstSelectSharedMemberOwnership()
    {
        var request = ScenarioTestFactory.CreateCallbackBoundaryRequest();
        var first = ScenarioTestFactory.CreateCallbackBoundaryFact(new CallbackBoundaryId("callback-boundary:v1:overlap:first"),
            ScenarioTestFactory.CallbackOuterInvocationOperation, CallbackCardinality.ZeroOrOne, CallbackTriggerKind.Conditional,
            ScenarioTestFactory.CallbackConditionOperation, CallbackCompletionKind.RejoinsCaller,
            [ScenarioTestFactory.ServiceQueryOperation.Value], [ScenarioTestFactory.SourceEvidence("overlap-first")], SeqDoc.Core.Evidence.CertaintyLevel.Exact);
        var second = ScenarioTestFactory.CreateCallbackBoundaryFact(new CallbackBoundaryId("callback-boundary:v1:overlap:second"),
            ScenarioTestFactory.CallbackOuterInvocationOperation, CallbackCardinality.ZeroOrOne, CallbackTriggerKind.Conditional,
            ScenarioTestFactory.CallbackConditionOperation, CallbackCompletionKind.RejoinsCaller,
            [ScenarioTestFactory.ServiceQueryOperation.Value], [ScenarioTestFactory.SourceEvidence("overlap-second")], SeqDoc.Core.Evidence.CertaintyLevel.Exact);
        var sourceFacts = request.CallbackBoundaryFacts!;
        var forwardFacts = new CallbackBoundaryFactSet(
            1, "test", request.Profile, request.ProgramIndex.IndexFingerprint,
            [first, second], sourceFacts.Diagnostics, "callback-overlap-forward");
        var reverseFacts = new CallbackBoundaryFactSet(
            1, "test", request.Profile, request.ProgramIndex.IndexFingerprint,
            [second, first], sourceFacts.Diagnostics, "callback-overlap-reverse");
        var forward = Assert.Single(ScenarioGraphBuilder.Build(request with { CallbackBoundaryFacts = forwardFacts }).Graphs);
        var reverse = Assert.Single(ScenarioGraphBuilder.Build(request with { CallbackBoundaryFacts = reverseFacts }).Graphs);

        Assert.Empty(forward.CallbackRegions);
        Assert.Empty(reverse.CallbackRegions);
        Assert.Equal(DiagnosticProjection(forward.Diagnostics), DiagnosticProjection(reverse.Diagnostics));
    }

    private static string[] DiagnosticProjection(IEnumerable<ScenarioGraphDiagnostic> diagnostics)
        => diagnostics
            .Select(diagnostic => $"{diagnostic.Code}|{diagnostic.Detail}|{diagnostic.Certainty}|{string.Join(",", diagnostic.Evidence.Select(evidence => evidence.Id.Value).Order(StringComparer.Ordinal))}")
            .Order(StringComparer.Ordinal)
            .ToArray();

}
