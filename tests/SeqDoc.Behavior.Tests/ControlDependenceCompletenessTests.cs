using System.Collections.Immutable;
using SeqDoc.Analysis.Behavior;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using Xunit;

namespace SeqDoc.Behavior.Tests;

/// <summary>
/// accepted contract contract coverage for architecture decision control-dependence completeness. The accepted repair must emit a
/// deterministic direct control dependence for EVERY eligible represented node in a controlled basic
/// block — including second and later operation/invocation/await nodes and the represented
/// Return/Throw terminal of the block — while Entry, Exit, Loop, and UnknownOperation nodes stay
/// excluded. The baseline extractor records only the first matching node per block and never a
/// terminal, so the terminal and later-node assertions in this file fail RED until the repair lands.
/// These tests deliberately do not edit the preserved existing LocalFlowAnalyzerTests.
/// </summary>
public sealed class ControlDependenceCompletenessTests
{
    private static readonly MethodId Method = new("method:v1:test");

    [Fact]
    public void ControlledBlockControlsEveryEligibleNodeIncludingSecondAndLaterNodes()
    {
        var condition = new OperationId("behavior-operation:v1:cond");
        var first = new OperationId("behavior-operation:v1:first");
        var second = new OperationId("behavior-operation:v1:second");
        var invocation = new OperationId("behavior-operation:v1:invocation");
        var awaitOperation = new OperationId("behavior-operation:v1:await");
        var body = new ExtractedMethodBody(
            Method,
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            [],
            [],
            ImmutableArray.Create(
                Operation(condition, ExtractedOperationKind.Binary, "System.Boolean", null, null, null),
                Operation(first, ExtractedOperationKind.ExpressionStatement, "System.Int32", null, null, null),
                Operation(second, ExtractedOperationKind.ExpressionStatement, "System.Int32", null, null, null),
                Operation(invocation, ExtractedOperationKind.Invocation, "System.Int32", null, null, null),
                Operation(awaitOperation, ExtractedOperationKind.Await, "System.Void", null, null, null,
                    awaitPayload: new ExtractedAwaitPayload(invocation))),
            ImmutableArray.Create(
                Block(0, [], 1, None),
                Block(1, [], 4, Conditional, condition, [2], [0]),
                Block(2, [first, second, invocation, awaitOperation], 4, None, null, [], [1]),
                Block(3, [], 4, None, null, [], [1]),
                Block(4, [], null, Exit, null, [], [2, 3])),
            RootRegion(4),
            []);

        var flow = MethodFlowBuilder.Build(body).Snapshot;
        var (_, dependences, _) = LocalFlowAnalyzer.Analyze(body, flow);

        var decision = Assert.Single(flow.Nodes.OfType<DecisionFlowNode>());
        var firstNode = Assert.Single(flow.Nodes.OfType<OperationFlowNode>(), node => node.Operation == first);
        var secondNode = Assert.Single(flow.Nodes.OfType<OperationFlowNode>(), node => node.Operation == second);
        var invocationNode = Assert.Single(flow.Nodes.OfType<InvocationFlowNode>(), node => node.Operation == invocation);
        var awaitNode = Assert.Single(flow.Nodes.OfType<AwaitFlowNode>(), node => node.Operand == invocation);

        var controlled = dependences
            .Where(dependence => dependence.ControllingDecision == decision.Id)
            .ToArray();
        foreach (var nodeId in new[] { firstNode.Id, secondNode.Id, invocationNode.Id, awaitNode.Id })
        {
            Assert.Contains(controlled, dependence => dependence.ControlledNode == nodeId && dependence.ControlledOnTrue);
        }

        // Every eligible node in the controlled block is represented exactly once; a first-node-only
        // extractor produces one dependence while the contract requires all four.
        Assert.Equal(4, controlled.Length);
    }

    [Fact]
    public void RepresentedReturnTerminalIsControlledAndOperationDerivedReturnIsNot()
    {
        var condition = new OperationId("behavior-operation:v1:cond");
        var factory = new OperationId("behavior-operation:v1:factory");
        var value = new OperationId("behavior-operation:v1:value");
        var returnOperation = new OperationId("behavior-operation:v1:return");
        var body = new ExtractedMethodBody(
            Method,
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            [],
            [],
            ImmutableArray.Create(
                Operation(condition, ExtractedOperationKind.Binary, "System.Boolean", null, null, null),
                Operation(factory, ExtractedOperationKind.Invocation, "System.Int32", null, null, null),
                Operation(value, ExtractedOperationKind.Literal, "System.Int32", "1", null, null),
                Operation(returnOperation, ExtractedOperationKind.Return, "System.Int32", null, null, null,
                    returnPayload: new ExtractedReturnPayload(value))),
            ImmutableArray.Create(
                Block(0, [], 1, None),
                Block(1, [], 3, Conditional, condition, [2], [0]),
                Block(2, [factory, returnOperation], null, Return, null, [], [1]),
                Block(3, [], 4, None, null, [], [1]),
                Block(4, [], null, Exit, null, [], [3])),
            RootRegion(4),
            []);

        var flow = MethodFlowBuilder.Build(body).Snapshot;
        var (_, dependences, _) = LocalFlowAnalyzer.Analyze(body, flow);

        // The block contains BOTH an operation-derived return node (from the Return operation) and the
        // represented terminal (whose value is the first non-return operation in the block). Only the
        // represented terminal is eligible for control dependence.
        var terminal = Assert.Single(flow.Nodes.OfType<ReturnFlowNode>(), node => node.Value == factory);
        var operationDerived = Assert.Single(flow.Nodes.OfType<ReturnFlowNode>(), node => node.Value == value);
        var factoryNode = Assert.Single(flow.Nodes.OfType<InvocationFlowNode>(), node => node.Operation == factory);

        Assert.Contains(dependences, dependence => dependence.ControlledNode == factoryNode.Id && dependence.ControlledOnTrue);
        Assert.Contains(dependences, dependence => dependence.ControlledNode == terminal.Id && dependence.ControlledOnTrue);
        Assert.DoesNotContain(dependences, dependence => dependence.ControlledNode == operationDerived.Id);

        // Exactly one represented terminal is controlled for this block; the operation-derived return
        // never counts as the block terminal.
        var returnDependences = dependences
            .Where(dependence => flow.Nodes.OfType<ReturnFlowNode>().Any(node => node.Id == dependence.ControlledNode))
            .ToArray();
        Assert.Single(returnDependences);
        Assert.Equal(terminal.Id, returnDependences[0].ControlledNode);
    }

    [Fact]
    public void RepresentedThrowTerminalIsControlled()
    {
        var condition = new OperationId("behavior-operation:v1:cond");
        var exceptionFactory = new OperationId("behavior-operation:v1:exception-factory");
        var body = new ExtractedMethodBody(
            Method,
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            [],
            [],
            ImmutableArray.Create(
                Operation(condition, ExtractedOperationKind.Binary, "System.Boolean", null, null, null),
                Operation(exceptionFactory, ExtractedOperationKind.ObjectCreation, "System.InvalidOperationException", null, null, null)),
            ImmutableArray.Create(
                Block(0, [], 1, None),
                Block(1, [], 3, Conditional, condition, [2], [0]),
                Block(2, [exceptionFactory], null, Throw, null, [], [1], escapingThrow: true),
                Block(3, [], 4, None, null, [], [1]),
                Block(4, [], null, Exit, null, [], [3])),
            RootRegion(4),
            []);

        var flow = MethodFlowBuilder.Build(body).Snapshot;
        var (_, dependences, _) = LocalFlowAnalyzer.Analyze(body, flow);

        var throwNode = Assert.Single(flow.Nodes.OfType<ThrowFlowNode>());
        Assert.Contains(dependences, dependence => dependence.ControlledNode == throwNode.Id && dependence.ControlledOnTrue);
        Assert.Contains(dependences, dependence => dependence.ControlledNode == flow.Nodes.OfType<InvocationFlowNode>().Single().Id
            && dependence.ControlledOnTrue);
    }

    [Fact]
    public void EntryExitLoopAndUnknownNodesStayExcluded()
    {
        var condition = new OperationId("behavior-operation:v1:cond");
        var unknown = new OperationId("behavior-operation:v1:unknown");
        var body = new ExtractedMethodBody(
            Method,
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            [],
            [],
            ImmutableArray.Create(
                Operation(condition, ExtractedOperationKind.Binary, "System.Boolean", null, null, null),
                Operation(unknown, ExtractedOperationKind.Unknown, "System.Object", null, null, null)),
            ImmutableArray.Create(
                Block(0, [], 1, None),
                Block(1, [], 2, Conditional, condition, [2], [0]),
                Block(2, [unknown], 4, None, null, [], [1]),
                Block(3, [], 4, None, null, [], [1]),
                Block(4, [], null, Exit, null, [], [2, 3])),
            RootRegion(4),
            []);

        var flow = MethodFlowBuilder.Build(body).Snapshot;
        var (_, dependences, _) = LocalFlowAnalyzer.Analyze(body, flow);

        var unknownNode = Assert.Single(flow.Nodes.OfType<UnknownOperationFlowNode>());
        Assert.DoesNotContain(dependences, dependence => dependence.ControlledNode == unknownNode.Id);
        Assert.DoesNotContain(dependences, dependence => flow.Nodes.OfType<EntryFlowNode>().Any(node => node.Id == dependence.ControlledNode));
        Assert.DoesNotContain(dependences, dependence => flow.Nodes.OfType<ExitFlowNode>().Any(node => node.Id == dependence.ControlledNode));

        var loopFlow = MethodFlowBuilder.Build(CreateLoopBody(condition)).Snapshot;
        var (_, loopDependences, _) = LocalFlowAnalyzer.Analyze(CreateLoopBody(condition), loopFlow);
        var loopNode = Assert.Single(loopFlow.Nodes.OfType<LoopNode>());
        Assert.DoesNotContain(loopDependences, dependence => dependence.ControlledNode == loopNode.Id);
    }

    private static ExtractedMethodBody CreateLoopBody(OperationId condition)
    {
        var evidence = new EvidenceRef(new EvidenceId("evidence:v1:control-loop"), EvidenceKind.Source, "loop-fixture.cs", null, "loop", "test", CertaintyLevel.Exact);
        var loopAnchor = new OperationId("behavior-operation:v1:control-loop-anchor");
        var body = new ExtractedMethodBody(
            Method,
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            [],
            [],
            ImmutableArray.Create(Operation(condition, ExtractedOperationKind.Binary, "System.Boolean", null, null, null) with { Evidence = [evidence] }),
            ImmutableArray.Create(
                Block(0, [], 1, None),
                Block(1, [], 2, Conditional, condition, [3], [0, 2]),
                Block(2, [], 1, None, null, [], [1]),
                Block(3, [], null, Exit, null, [], [1])),
            RootRegion(3),
            [evidence],
            [new ExtractedNaturalLoop(loopAnchor, ExtractedLoopKind.WhileLoop, 1, [2], [2], [3],
                [new ExtractedOrdinaryBranch(2, 1, [], [], [evidence], CertaintyLevel.Exact)], [evidence], CertaintyLevel.Exact)],
            [new ExtractedLoopAnchor(loopAnchor, ExtractedLoopKind.WhileLoop, [evidence], CertaintyLevel.Exact)],
            [
                new ExtractedOrdinaryBranch(0, 1, [], [], [evidence], CertaintyLevel.Exact),
                new ExtractedOrdinaryBranch(1, 2, [], [], [evidence], CertaintyLevel.Exact),
                new ExtractedOrdinaryBranch(1, 3, [], [], [evidence], CertaintyLevel.Exact),
                new ExtractedOrdinaryBranch(2, 1, [], [], [evidence], CertaintyLevel.Exact),
            ]);
        return body;
    }

    private static ImmutableArray<ExtractedExceptionRegion> RootRegion(int lastBlock) =>
        ImmutableArray.Create(new ExtractedExceptionRegion(
            new FlowRegionId("flow-region:v1:root"),
            ExtractedRegionKind.Root,
            null,
            0,
            0,
            lastBlock,
            null,
            [],
            CertaintyLevel.Exact));

    private static ExtractedBasicBlock Block(
        int ordinal,
        ImmutableArray<OperationId> operations,
        int? fallThrough,
        ExtractedBlockTerminalKind terminal,
        OperationId? branchCondition = null,
        ImmutableArray<int> conditionals = default,
        ImmutableArray<int> predecessors = default,
        bool escapingThrow = false) =>
        new(
            ordinal,
            operations,
            branchCondition,
            fallThrough,
            conditionals.IsDefault ? [] : conditionals,
            predecessors.IsDefault ? [] : predecessors,
            terminal,
            escapingThrow,
            [],
            [],
            [],
            CertaintyLevel.Exact);

    private static ExtractedOperation Operation(
        OperationId id,
        ExtractedOperationKind kind,
        string type,
        string? constantValue,
        string? localName,
        int? parameterOrdinal,
        ExtractedAssignmentPayload? assignment = null,
        ExtractedInvocationPayload? invocation = null,
        ExtractedConversionPayload? conversion = null,
        ExtractedAwaitPayload? awaitPayload = null,
        ExtractedReturnPayload? returnPayload = null,
        ImmutableArray<MethodId> referencedMethods = default,
        ImmutableArray<SymbolId> referencedTypes = default,
        ImmutableArray<SymbolId> referencedMembers = default,
        ImmutableArray<OperationId> referencedOperands = default) =>
        new(
            id,
            Method,
            kind,
            null,
            referencedOperands.IsDefault ? [] : referencedOperands,
            0,
            type,
            constantValue,
            false,
            true,
            referencedMethods.IsDefault ? [] : referencedMethods,
            referencedTypes.IsDefault ? [] : referencedTypes,
            referencedMembers.IsDefault ? [] : referencedMembers,
            invocation,
            assignment,
            conversion,
            awaitPayload,
            returnPayload,
            null,
            localName,
            parameterOrdinal,
            [],
            CertaintyLevel.Exact);

    private const ExtractedBlockTerminalKind None = ExtractedBlockTerminalKind.None;
    private const ExtractedBlockTerminalKind Conditional = ExtractedBlockTerminalKind.Conditional;
    private const ExtractedBlockTerminalKind Return = ExtractedBlockTerminalKind.Return;
    private const ExtractedBlockTerminalKind Throw = ExtractedBlockTerminalKind.Throw;
    private const ExtractedBlockTerminalKind Exit = ExtractedBlockTerminalKind.Exit;
}
