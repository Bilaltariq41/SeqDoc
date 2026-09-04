using System.Collections.Immutable;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Core.Semantics;
using Xunit;

namespace SeqDoc.Wording.Tests;

public sealed class HostedWorkerCallbackWordingTests
{
    [Fact]
    public void HostedWorkerCallbackWordingRetainsNeutralSourceEvidence()
    {
        var graph = CreateWorkerCallbackGraph();
        var region = Assert.Single(graph.CallbackRegions);
        var plan = DocumentationPlanner.Plan(graph);

        Assert.Contains(plan.Wording.Phrases, phrase => phrase.Text.Contains("hosted worker", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Wording.Phrases, phrase => phrase.Text.Contains("source callback operation", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Wording.Phrases, phrase => phrase.Text.Contains("callback", StringComparison.OrdinalIgnoreCase)
            && phrase.Text.Contains("withheld", StringComparison.OrdinalIgnoreCase));
        Assert.All(plan.Wording.Phrases, phrase =>
        {
            Assert.NotEmpty(phrase.Evidence);
            Assert.NotEqual(CertaintyLevel.Unknown, phrase.Certainty);
        });
        Assert.Contains(graph.Nodes, node => node.Id == region.MemberNodes.Single());
    }

    [Fact]
    public void HostedWorkerCallbackWordingDoesNotClaimRuntimeDeliveryPersistenceOrSuccess()
    {
        var text = string.Join("\n", DocumentationPlanner.Plan(CreateWorkerCallbackGraph()).Wording.Phrases.Select(phrase => phrase.Text));

        foreach (var forbidden in new[] { "runtime", "delivered", "invocation count", "persisted", "recovered", "retry completed", "cancellation succeeded", "timing" })
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static ScenarioGraph CreateWorkerCallbackGraph()
    {
        var evidence = new EvidenceRef(
            new EvidenceId("evidence:v1:hosted-worker-callback"), EvidenceKind.Source,
            "hosted-worker-callback", new SourceRange(new DocumentId("document:v1:test"), new SourcePosition(1, 0), new SourcePosition(1, 10)),
            "HostedWorkers.RetryWorker", null, CertaintyLevel.Exact);
        var root = new MethodId("method:v1:HostedWorkers.RetryWorker.ExecuteAsync");
        var callback = new ScenarioNodeId("scenario-node:v1:hosted-worker:callback");
        var loop = new FlowRegionId("flow-region:v1:hosted-worker:retry-loop");
        var loopHeader = new FlowNodeId("flow-node:v1:hosted-worker:retry-header");
        var action = new ScenarioNode(new ScenarioNodeId("scenario-node:v1:hosted-worker:action"), ScenarioNodeKind.Action,
            "hosted-worker:action", root, null, "retry worker", [evidence], CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(ActionKind: ScenarioActionKind.HostedWorker, HostedWorkerTypeName: "HostedWorkers.RetryWorker"));
        var callbackNode = new ScenarioNode(callback, ScenarioNodeKind.ServiceCall, "hosted-worker:callback", root,
            new OperationId("operation:v1:hosted-worker:callback"), "callback-local operation", [evidence], CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(ImplementationTypeName: "HostedWorkers.CallbackContract", CalledMemberName: "Observe",
                HostedWorkerControlKind: HostedWorkerControlKind.AwaitedRepeatingLoop,
                HostedWorkerFlowRegion: loop, HostedWorkerHeader: loopHeader, HostedWorkerBlockOrdinal: 1));
        var region = new ScenarioCallbackRegion(new ScenarioCallbackRegionId("scenario-callback-region:v1:hosted-worker"),
            new CallbackBoundaryId("callback-boundary:v1:hosted-worker"), CallbackCardinality.ExactlyOnce,
            CallbackTriggerKind.Unconditional, null, CallbackCompletionKind.RejoinsCaller, [callback], [evidence], CertaintyLevel.Exact);
        var topology = new ScenarioTopology([], [], [], [],
            [new ScenarioFlowContainer(loop, root, ScenarioFlowContainerKind.NaturalLoop,
                loopHeader, null, [evidence], CertaintyLevel.Exact)],
            [new ScenarioFlowPlacement(callback, root, null, [loop], [], [evidence], CertaintyLevel.Exact)]);
        var edge = new ScenarioEdge(new ScenarioEdgeId("scenario-edge:v1:hosted-worker:callback"), action.Id, callback,
            ScenarioEdgeKind.Call, "callback", [evidence], CertaintyLevel.Exact);
        return new ScenarioGraph(new EntryPointId("entry-point:v1:hosted-worker"), new CompilationProfileId("compilation-profile:v1:test"),
            root, HttpMethodKind.Unknown, "", "HostedWorkers.RetryWorker.ExecuteAsync", [action, callbackNode], [edge], [],
            "hosted-worker-callback", topology, callbackRegions: [region], rootKind: ScenarioRootKind.HostedWorker);
    }
}
