using System.Collections.Immutable;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Core.Semantics;
using SeqDoc.Rendering.Markdown;
using Xunit;

namespace SeqDoc.Rendering.Tests;

public sealed class HostedWorkerCallbackRenderingTests
{
    [Fact]
    public void CallbackMessageRendersOnceInsideRecoveryLoopFromScenarioGraph()
    {
        var graph = CreateWorkerCallbackGraph();
        var region = Assert.Single(graph.CallbackRegions);
        var plan = DocumentationPlanner.Plan(graph).Diagram;
        var mermaid = MermaidRenderer.Render(plan);

        Assert.Contains(graph.Nodes, node => node.Id == region.MemberNodes.Single());
        Assert.Single(plan.Messages, message => message.Label == "Observe");
        Assert.Contains("loop Retry", mermaid, StringComparison.Ordinal);
        Assert.Empty(MermaidValidator.Validate(mermaid));
        Assert.Equal(plan.Messages.Length, mermaid.Split("->>", StringSplitOptions.None).Length - 1);
        var messageLine = mermaid.Split('\n').Single(line => line.Contains("Observe", StringComparison.Ordinal));
        Assert.StartsWith("      ", messageLine, StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatedScenarioPlanningAndRenderingProducesIdenticalMarkdownAndMermaid()
    {
        var first = DocumentationPlanner.Plan(CreateWorkerCallbackGraph());
        var second = DocumentationPlanner.Plan(CreateWorkerCallbackGraph());

        Assert.Equal(first.Wording.DebugProjection, second.Wording.DebugProjection);
        Assert.Equal(first.Diagram.DebugProjection, second.Diagram.DebugProjection);
        Assert.Equal(MermaidRenderer.Render(first.Diagram), MermaidRenderer.Render(second.Diagram));
        Assert.Equal(
            MarkdownRenderer.RenderDocument(first.Wording, first.Diagram),
            MarkdownRenderer.RenderDocument(second.Wording, second.Diagram));
    }

    private static ScenarioGraph CreateWorkerCallbackGraph()
    {
        var evidence = PlanTestFactory.SourceEvidence("hosted-worker-callback");
        var root = new MethodId("method:v1:HostedWorkers.RetryWorker.ExecuteAsync");
        var callback = new ScenarioNodeId("scenario-node:v1:hosted-worker:callback");
        var loopRegion = new FlowRegionId("flow-region:v1:hosted-worker:retry-loop");
        var loopHeader = new FlowNodeId("flow-node:v1:hosted-worker:retry-header");
        var action = new ScenarioNode(
            new ScenarioNodeId("scenario-node:v1:hosted-worker:action"),
            ScenarioNodeKind.Action,
            "hosted-worker:action",
            root,
            null,
            "retry worker",
            [evidence],
            SeqDoc.Core.Evidence.CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(
                ActionKind: ScenarioActionKind.HostedWorker,
                HostedWorkerTypeName: "HostedWorkers.RetryWorker"));
        var callbackNode = new ScenarioNode(
            callback,
            ScenarioNodeKind.ServiceCall,
            "hosted-worker:callback",
            root,
            new OperationId("operation:v1:hosted-worker:callback"),
            "callback-local operation",
            [evidence],
            SeqDoc.Core.Evidence.CertaintyLevel.Exact,
            sequenceOrdinal: 1,
            presentation: new ScenarioNodePresentation(ImplementationTypeName: "HostedWorkers.CallbackContract", CalledMemberName: "Observe",
                HostedWorkerControlKind: HostedWorkerControlKind.AwaitedRepeatingLoop,
                HostedWorkerFlowRegion: loopRegion, HostedWorkerHeader: loopHeader, HostedWorkerBlockOrdinal: 1));
        var recoveryNode = new ScenarioNode(
            new ScenarioNodeId("scenario-node:v1:hosted-worker:catch-loop"),
            ScenarioNodeKind.MethodCall,
            "hosted-worker:catch-loop",
            root,
            new OperationId("operation:v1:hosted-worker:catch-loop"),
            "catch-loop continuation",
            [evidence],
            SeqDoc.Core.Evidence.CertaintyLevel.Exact,
            sequenceOrdinal: 0,
            presentation: new ScenarioNodePresentation(
                HostedWorkerControlKind: HostedWorkerControlKind.CatchLoopContinuation,
                HostedWorkerFlowRegion: loopRegion,
                HostedWorkerHeader: loopHeader,
                HostedWorkerBlockOrdinal: 0));
        var topology = new ScenarioTopology(
            [],
            [],
            [],
            [],
            [new ScenarioFlowContainer(loopRegion, root, ScenarioFlowContainerKind.NaturalLoop, loopHeader, null, [evidence], SeqDoc.Core.Evidence.CertaintyLevel.Exact)],
            [new ScenarioFlowPlacement(recoveryNode.Id, root, loopHeader, [loopRegion], [], [evidence], SeqDoc.Core.Evidence.CertaintyLevel.Exact),
             new ScenarioFlowPlacement(callback, root, null, [loopRegion], [], [evidence], SeqDoc.Core.Evidence.CertaintyLevel.Exact)]);
        var boundary = new ScenarioCallbackRegion(
            new ScenarioCallbackRegionId("scenario-callback-region:v1:hosted-worker:callback"),
            new CallbackBoundaryId("callback-boundary:v1:hosted-worker:callback"),
            CallbackCardinality.ExactlyOnce,
            CallbackTriggerKind.Unconditional,
            null,
            CallbackCompletionKind.RejoinsCaller,
            [callback],
            [evidence],
            SeqDoc.Core.Evidence.CertaintyLevel.Exact);
        var edge = new ScenarioEdge(
            new ScenarioEdgeId("scenario-edge:v1:hosted-worker:callback"),
            action.Id,
            callback,
            ScenarioEdgeKind.Call,
            "callback",
            [evidence],
            SeqDoc.Core.Evidence.CertaintyLevel.Exact);
        return new ScenarioGraph(
            new EntryPointId("entry-point:v1:hosted-worker"),
            PlanTestFactory.Profile,
            root,
            HttpMethodKind.Unknown,
            "",
            "HostedWorkers.RetryWorker.ExecuteAsync",
            [action, recoveryNode, callbackNode],
            [edge, new ScenarioEdge(new ScenarioEdgeId("scenario-edge:v1:hosted-worker:catch-loop"), action.Id, recoveryNode.Id,
                ScenarioEdgeKind.Call, "catch-loop continuation", [evidence], SeqDoc.Core.Evidence.CertaintyLevel.Exact)],
            [],
            "hosted-worker-callback",
            topology,
            callbackRegions: [boundary],
            rootKind: ScenarioRootKind.HostedWorker);
    }
}
