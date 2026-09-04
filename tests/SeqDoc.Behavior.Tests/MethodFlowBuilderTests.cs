using System.Collections.Immutable;
using SeqDoc.Analysis.Behavior;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using Xunit;

namespace SeqDoc.Behavior.Tests;

public sealed class MethodFlowBuilderTests
{
    private static readonly MethodId Method = new("method:v1:test");
    private static readonly ExtractedBlockTerminalKind None = ExtractedBlockTerminalKind.None;
    private static readonly ExtractedBlockTerminalKind Conditional = ExtractedBlockTerminalKind.Conditional;
    private static readonly ExtractedBlockTerminalKind Return = ExtractedBlockTerminalKind.Return;
    private static readonly ExtractedBlockTerminalKind Throw = ExtractedBlockTerminalKind.Throw;
    private static readonly ExtractedBlockTerminalKind Exit = ExtractedBlockTerminalKind.Exit;

    [Fact]
    public void StraightLineBodyProducesEntryExitAndNormalCompletion()
    {
        var body = CreateBody(
            blocks:
            [
                Block(0, [], fallThrough: 1, terminal: None),
                Block(1, [], fallThrough: null, terminal: Exit),
            ]);

        var result = MethodFlowBuilder.Build(body);

        Assert.Empty(result.Diagnostics);
        Assert.Contains(result.Snapshot.Nodes, node => node.Kind == FlowNodeKind.Entry);
        Assert.Contains(result.Snapshot.Nodes, node => node.Kind == FlowNodeKind.Exit);
        Assert.Contains(result.Snapshot.Outcomes, outcome => outcome.Kind == FlowOutcomeKind.NormalCompletion);
        Assert.Equal(64, result.Snapshot.FlowFingerprint.Length);
    }

    [Fact]
    public void InvocationProjectsEverySupportedPositionalConstant()
    {
        var invocation = new OperationId("behavior-operation:v1:invoke");
        var arguments = new[] { Arg("a", 0, "System.Int32", "1"), Arg("b", 1, "System.Boolean", "true"), Arg("c", 2, "System.String", "ok") };
        var result = MethodFlowBuilder.Build(CreateInvocationBody(invocation, arguments, ["a", "b", "c"]));

        Assert.Equal([(0, "1"), (1, "true"), (2, "ok")],
            Assert.Single(result.Snapshot.Nodes.OfType<InvocationFlowNode>()).ConstantArguments
                .Select(argument => (argument.Ordinal, argument.Value)));
    }

    [Fact]
    public void InvocationUsesCompilerParameterOrdinalsForNamedReorderedArguments()
    {
        var invocation = new OperationId("behavior-operation:v1:invoke-reordered");
        var arguments = new[] { Arg("second", 2, "System.String", "two"), Arg("first", 0, "System.Int32", "1") };
        var result = MethodFlowBuilder.Build(CreateInvocationBody(invocation, arguments, ["second", "first"]));

        Assert.Equal([0, 2], Assert.Single(result.Snapshot.Nodes.OfType<InvocationFlowNode>()).ConstantArguments.Select(argument => argument.Ordinal));
    }

    [Fact]
    public void InvocationWithUnsupportedArgumentWithholdsTheEntireSummary()
    {
        var invocation = new OperationId("behavior-operation:v1:invoke-partial");
        var arguments = new[] { Arg("supported", 0, "System.Int32", "1"), Arg("unknown", 1, "System.Object", null) };
        var result = MethodFlowBuilder.Build(CreateInvocationBody(invocation, arguments, ["supported", "unknown"]));

        Assert.Empty(Assert.Single(result.Snapshot.Nodes.OfType<InvocationFlowNode>()).ConstantArguments);
    }

    [Fact]
    public void InvocationCarriesTypedNullWithoutConfusingItWithUnsupportedValue()
    {
        var invocation = new OperationId("behavior-operation:v1:invoke-null");
        var result = MethodFlowBuilder.Build(CreateInvocationBody(invocation,
            [Arg("null", 0, "System.String", null, hasConstantValue: true)], ["null"]));

        var argument = Assert.Single(Assert.Single(result.Snapshot.Nodes.OfType<InvocationFlowNode>()).ConstantArguments);
        Assert.True(argument.IsNull);
        Assert.Null(argument.Value);
        Assert.Equal("System.String", argument.FullyQualifiedType);
    }

    [Fact]
    public void InvocationWithNonContiguousOrdinalsRetainsEvidenceButPresentationCanWithholdSummary()
    {
        var invocation = new OperationId("behavior-operation:v1:invoke-gap");
        var result = MethodFlowBuilder.Build(CreateInvocationBody(invocation,
            [Arg("first", 0, "System.Int32", "1"), Arg("third", 2, "System.Int32", "3")], ["first", "third"]));

        Assert.Equal([0, 2], Assert.Single(result.Snapshot.Nodes.OfType<InvocationFlowNode>()).ConstantArguments.Select(item => item.Ordinal));
    }

    [Fact]
    public void ConditionalBlockProducesTrueAndFalseEdges()
    {
        var condition = new OperationId("behavior-operation:v1:cond");
        var body = CreateBody(
            operations: ImmutableArray.Create(Operation(condition, ExtractedOperationKind.Binary, "System.Boolean")),
            blocks:
            [
                Block(0, [], fallThrough: 1, terminal: None),
                Block(1, [], branchCondition: condition, fallThrough: 2, conditionals: [3], terminal: Conditional, predecessors: [0]),
                Block(2, [], fallThrough: 4, terminal: None, predecessors: [1]),
                Block(3, [], fallThrough: 4, terminal: None, predecessors: [1]),
                Block(4, [], fallThrough: null, terminal: Exit, predecessors: [2, 3]),
            ]);

        var result = MethodFlowBuilder.Build(body);

        Assert.Empty(result.Diagnostics);
        Assert.Contains(result.Snapshot.Edges, edge => edge.Kind == FlowEdgeKind.True);
        Assert.Contains(result.Snapshot.Edges, edge => edge.Kind == FlowEdgeKind.False);
        Assert.Contains(result.Snapshot.Nodes, node => node.Kind == FlowNodeKind.Decision);
    }

    [Fact]
    public void ReturnTerminalProducesExplicitReturnOutcome()
    {
        var value = new OperationId("behavior-operation:v1:value");
        var body = CreateBody(
            operations: ImmutableArray.Create(Operation(value, ExtractedOperationKind.LocalReference, "System.Int32")),
            blocks:
            [
                Block(0, [], fallThrough: 1, terminal: None),
                Block(1, [value], fallThrough: 2, terminal: Return, predecessors: [0]),
                Block(2, [], fallThrough: null, terminal: Exit, predecessors: [1]),
            ]);

        var result = MethodFlowBuilder.Build(body);

        Assert.Empty(result.Diagnostics);
        Assert.Contains(result.Snapshot.Nodes, node => node.Kind == FlowNodeKind.Return);
        Assert.Contains(result.Snapshot.Outcomes, outcome => outcome.Kind == FlowOutcomeKind.ExplicitReturn);
        Assert.Contains(result.Snapshot.Outcomes, outcome => outcome.Kind == FlowOutcomeKind.NormalCompletion);
    }

    [Fact]
    public void ThrowTerminalProducesEscapingThrowAndNoNormalExitWhenNoReturn()
    {
        var body = CreateBody(
            blocks:
            [
                Block(0, [], fallThrough: 1, terminal: None),
                Block(1, [], fallThrough: null, terminal: Throw, predecessors: [0], escapingThrow: true),
                Block(2, [], fallThrough: null, terminal: Exit),
            ]);

        var result = MethodFlowBuilder.Build(body);

        Assert.Empty(result.Diagnostics);
        Assert.Contains(result.Snapshot.Nodes, node => node.Kind == FlowNodeKind.Throw);
        Assert.Contains(result.Snapshot.Outcomes, outcome => outcome.Kind == FlowOutcomeKind.EscapingThrow);
        Assert.DoesNotContain(result.Snapshot.Outcomes, outcome => outcome.Kind == FlowOutcomeKind.NormalCompletion);
        Assert.Contains(result.Snapshot.Outcomes, outcome => outcome.Kind == FlowOutcomeKind.NoNormalExit);
    }

    [Fact]
    public void BackEdgeProducesNaturalLoopRegion()
    {
        var condition = new OperationId("behavior-operation:v1:loop-condition");
        var loopInvocation = new OperationId("behavior-operation:v1:loop-invocation");
        var loopAnchor = new OperationId("behavior-operation:v1:loop-anchor");
        var evidence = LoopEvidence("while-loop");
        var body = CreateBody(
            operations: ImmutableArray.Create(
                Operation(condition, ExtractedOperationKind.Binary, "System.Boolean") with { Evidence = [evidence] },
                Operation(loopInvocation, ExtractedOperationKind.Invocation, "System.Void") with { Evidence = [evidence] }),
            blocks:
            [
                Block(0, [], fallThrough: 1, terminal: None),
                Block(1, [], branchCondition: condition, fallThrough: 2, conditionals: [3], terminal: Conditional, predecessors: [0, 2]),
                Block(2, [loopInvocation], fallThrough: 1, terminal: None, predecessors: [1]) with { Evidence = [evidence] },
                Block(3, [], fallThrough: null, terminal: Exit, predecessors: [1]),
            ]) with
        {
            Evidence = [evidence],
            NaturalLoops = [new ExtractedNaturalLoop(loopAnchor, ExtractedLoopKind.WhileLoop, 1, [2], [2], [3],
                [new ExtractedOrdinaryBranch(2, 1, [], [], [evidence], CertaintyLevel.Exact)], [evidence], CertaintyLevel.Exact)],
            LoopAnchors = [new ExtractedLoopAnchor(loopAnchor, ExtractedLoopKind.WhileLoop, [evidence], CertaintyLevel.Exact)],
            OrdinaryBranches =
            [
                new ExtractedOrdinaryBranch(0, 1, [], [], [evidence], CertaintyLevel.Exact),
                new ExtractedOrdinaryBranch(1, 2, [], [], [evidence], CertaintyLevel.Exact),
                new ExtractedOrdinaryBranch(1, 3, [], [], [evidence], CertaintyLevel.Exact),
                new ExtractedOrdinaryBranch(2, 1, [], [], [evidence], CertaintyLevel.Exact),
            ]
        };

        var result = MethodFlowBuilder.Build(body);

        Assert.Empty(result.Diagnostics);
        Assert.Contains(result.Snapshot.Nodes, node => node.Kind == FlowNodeKind.Loop);
        Assert.Contains(result.Snapshot.Regions, region => region.Kind == FlowRegionKind.NaturalLoop);
        var loop = Assert.Single(result.Snapshot.Nodes.OfType<LoopNode>());
        var bodyInvocations = result.Snapshot.Nodes
            .OfType<InvocationFlowNode>()
            .Where(node => loop.Body.Contains(node.Id))
            .ToArray();
        Assert.Equal(
            bodyInvocations.Select(node => node.BlockOrdinal).OrderBy(ordinal => ordinal),
            bodyInvocations.Select(node => node.BlockOrdinal));
        Assert.Contains(bodyInvocations, node => node.Operation == loopInvocation && node.BlockOrdinal == 2);
    }

    [Fact]
    public void ConditionalBackEdgeProducesDoWhileNaturalLoop()
    {
        var condition = new OperationId("behavior-operation:v1:loop-condition");
        var loopAnchor = new OperationId("behavior-operation:v1:loop-anchor");
        var evidence = LoopEvidence("do-while-loop");
        var body = CreateBody(
            operations: ImmutableArray.Create(Operation(condition, ExtractedOperationKind.Binary, "System.Boolean") with { Evidence = [evidence] }),
            blocks:
            [
                Block(0, [], fallThrough: 1, terminal: None),
                Block(1, [], fallThrough: 2, terminal: None, predecessors: [0, 2]),
                Block(2, [], branchCondition: condition, fallThrough: 3, conditionals: [1], terminal: Conditional, predecessors: [1]),
                Block(3, [], fallThrough: null, terminal: Exit, predecessors: [2]),
            ]) with
        {
            Evidence = [evidence],
            NaturalLoops = [new ExtractedNaturalLoop(loopAnchor, ExtractedLoopKind.DoWhileLoop, 1, [2], [2], [3],
                [new ExtractedOrdinaryBranch(2, 1, [], [], [evidence], CertaintyLevel.Exact)], [evidence], CertaintyLevel.Exact)],
            LoopAnchors = [new ExtractedLoopAnchor(loopAnchor, ExtractedLoopKind.DoWhileLoop, [evidence], CertaintyLevel.Exact)],
            OrdinaryBranches =
            [
                new ExtractedOrdinaryBranch(0, 1, [], [], [evidence], CertaintyLevel.Exact),
                new ExtractedOrdinaryBranch(1, 2, [], [], [evidence], CertaintyLevel.Exact),
                new ExtractedOrdinaryBranch(2, 3, [], [], [evidence], CertaintyLevel.Exact),
                new ExtractedOrdinaryBranch(2, 1, [], [], [evidence], CertaintyLevel.Exact),
            ]
        };

        var result = MethodFlowBuilder.Build(body);

        Assert.Empty(result.Diagnostics);
        Assert.Contains(result.Snapshot.Nodes, node => node.Kind == FlowNodeKind.Loop);
        Assert.Contains(result.Snapshot.Regions, region => region.Kind == FlowRegionKind.NaturalLoop);
    }

    [Fact]
    public void BackEdgesAreClassifiedLoopBack()
    {
        var condition = new OperationId("behavior-operation:v1:loop-condition");
        var body = CreateBody(
            operations: ImmutableArray.Create(Operation(condition, ExtractedOperationKind.Binary, "System.Boolean")),
            blocks:
            [
                Block(0, [], fallThrough: 1, terminal: None),
                Block(1, [], branchCondition: condition, fallThrough: 2, conditionals: [3], terminal: Conditional, predecessors: [0]),
                Block(2, [], fallThrough: 1, terminal: None, predecessors: [1]),
                Block(3, [], fallThrough: null, terminal: Exit, predecessors: [1]),
            ]);

        var result = MethodFlowBuilder.Build(body);

        Assert.Contains(result.Snapshot.Edges, edge => edge.Kind == FlowEdgeKind.LoopBack);
    }

    [Fact]
    public void RethrowTerminalProducesRethrowEdgeKind()
    {
        var body = CreateThrowBody(new ExtractedBasicBlock(
            1,
            [],
            null,
            null,
            [],
            [0],
            ExtractedBlockTerminalKind.Rethrow,
            true,
            [],
            [],
            [],
            CertaintyLevel.Exact));

        var result = MethodFlowBuilder.Build(body);

        Assert.Contains(result.Snapshot.Edges, edge => edge.Kind == FlowEdgeKind.Rethrow);
    }

    [Fact]
    public void NonEmptyEntryBlockConnectsEntryNode()
    {
        var value = new OperationId("behavior-operation:v1:value");
        var body = CreateBody(
            operations: ImmutableArray.Create(Operation(value, ExtractedOperationKind.ExpressionStatement, "System.Int32")),
            blocks:
            [
                Block(0, [value], fallThrough: 1, terminal: None),
                Block(1, [], fallThrough: null, terminal: Exit, predecessors: [0]),
            ]);

        var result = MethodFlowBuilder.Build(body);

        Assert.Empty(result.Diagnostics);
        var entry = Assert.Single(result.Snapshot.Nodes, node => node.Kind == FlowNodeKind.Entry);
        Assert.Contains(result.Snapshot.Edges, edge => edge.Source == entry.Id);
    }

    [Fact]
    public void EscapingThrowFlagControlsEscapingOutcome()
    {
        var throwing = new ExtractedBasicBlock(
            1,
            [],
            null,
            null,
            [],
            [0],
            ExtractedBlockTerminalKind.Throw,
            true,
            [],
            [],
            [],
            CertaintyLevel.Exact);
        var handled = new ExtractedBasicBlock(
            1,
            [],
            null,
            null,
            [],
            [0],
            ExtractedBlockTerminalKind.Throw,
            false,
            [],
            [],
            [],
            CertaintyLevel.Exact);

        var escaping = MethodFlowBuilder.Build(CreateThrowBody(throwing)).Snapshot;
        var caught = MethodFlowBuilder.Build(CreateThrowBody(handled)).Snapshot;

        Assert.Contains(escaping.Outcomes, outcome => outcome.Kind == FlowOutcomeKind.EscapingThrow);
        Assert.DoesNotContain(caught.Outcomes, outcome => outcome.Kind == FlowOutcomeKind.EscapingThrow);
    }

    [Fact]
    public void RethrowFlagControlsEscapingOutcome()
    {
        var rethrow = new ExtractedBasicBlock(
            1,
            [],
            null,
            null,
            [],
            [0],
            ExtractedBlockTerminalKind.Rethrow,
            false,
            [],
            [],
            [],
            CertaintyLevel.Exact);

        var caught = MethodFlowBuilder.Build(CreateThrowBody(rethrow)).Snapshot;

        Assert.DoesNotContain(caught.Outcomes, outcome => outcome.Kind == FlowOutcomeKind.EscapingThrow);
    }

    [Fact]
    public void TryCatchFinallyRegionsArePreserved()
    {
        var body = CreateBody(
            blocks:
            [
                Block(0, [], fallThrough: 1, terminal: None),
                Block(1, [], fallThrough: 2, terminal: None, predecessors: [0]),
                Block(2, [], fallThrough: null, terminal: Throw, predecessors: [1]),
                Block(3, [], fallThrough: 5, terminal: None, predecessors: [1]),
                Block(4, [], fallThrough: 5, terminal: None, predecessors: [1]),
                Block(5, [], fallThrough: null, terminal: Exit, predecessors: [3, 4]),
            ],
            regions:
            [
                new ExtractedExceptionRegion(
                    new FlowRegionId("flow-region:v1:root"),
                    ExtractedRegionKind.Root,
                    null,
                    0,
                    0,
                    5,
                    null,
                    [],
                    CertaintyLevel.Exact),
                new ExtractedExceptionRegion(
                    new FlowRegionId("flow-region:v1:try"),
                    ExtractedRegionKind.Try,
                    new FlowRegionId("flow-region:v1:root"),
                    1,
                    1,
                    2,
                    null,
                    [],
                    CertaintyLevel.Exact),
                new ExtractedExceptionRegion(
                    new FlowRegionId("flow-region:v1:catch"),
                    ExtractedRegionKind.Catch,
                    new FlowRegionId("flow-region:v1:try"),
                    2,
                    4,
                    4,
                    "System.Exception",
                    [],
                    CertaintyLevel.Exact),
                new ExtractedExceptionRegion(
                    new FlowRegionId("flow-region:v1:finally"),
                    ExtractedRegionKind.Finally,
                    new FlowRegionId("flow-region:v1:try"),
                    3,
                    3,
                    4,
                    null,
                    [],
                    CertaintyLevel.Exact),
            ]);

        var result = MethodFlowBuilder.Build(body);

        Assert.Empty(result.Diagnostics);
        Assert.Contains(result.Snapshot.Regions, region => region.Kind == FlowRegionKind.Try);
        Assert.Contains(result.Snapshot.Regions, region => region.Kind == FlowRegionKind.Catch);
        Assert.Contains(result.Snapshot.Regions, region => region.Kind == FlowRegionKind.Finally);
    }

    [Fact]
    public void MissingExitBlockProducesDiagnostic()
    {
        var body = CreateBody(
            blocks:
            [
                Block(0, [], fallThrough: 1, terminal: None),
                Block(1, [], fallThrough: null, terminal: None, predecessors: [0]),
            ]);

        var result = MethodFlowBuilder.Build(body);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "BD2004");
    }

    [Fact]
    public void BuildIsDeterministicAcrossRuns()
    {
        var body = CreateBody(
            blocks:
            [
                Block(0, [], fallThrough: 1, terminal: None),
                Block(1, [], fallThrough: 2, terminal: None, predecessors: [0]),
                Block(2, [], fallThrough: null, terminal: Exit, predecessors: [1]),
            ]);

        var first = MethodFlowBuilder.Build(body).Snapshot;
        var second = MethodFlowBuilder.Build(body).Snapshot;

        Assert.Equal(first.FlowFingerprint, second.FlowFingerprint);
        Assert.Equal(
            first.Nodes.Select(node => node.Id.Value).Order(StringComparer.Ordinal),
            second.Nodes.Select(node => node.Id.Value).Order(StringComparer.Ordinal));
        Assert.Equal(
            first.Edges.Select(edge => edge.Id.Value).Order(StringComparer.Ordinal),
            second.Edges.Select(edge => edge.Id.Value).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void InvocationFlowRetainsTypedTargetSourceAndNestedMarkers()
    {
        var operationId = new OperationId("behavior-operation:v1:typed-call");
        var body = CreateBody(
            operations: [new ExtractedOperation(
                operationId,
                Method,
                ExtractedOperationKind.Invocation,
                null,
                [],
                7,
                "System.Void",
                null,
                false,
                true,
                [],
                [],
                [],
                new ExtractedInvocationPayload(new MethodId("method:v1:Nested.Target"), false, false, false, false, false, [], "Nested", "Target", IsLoadedProjectTarget: true, TargetAssemblyName: "Fixture.Application", IsPlatformTarget: false),
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                [new EvidenceRef(new EvidenceId("evidence:v1:typed-call"), EvidenceKind.Source, "typed.cs", null, "call", "test", CertaintyLevel.Exact)],
                CertaintyLevel.Exact)],
            blocks:
            [
                Block(0, [operationId], fallThrough: 1, terminal: None),
                Block(1, [], fallThrough: null, terminal: Exit),
            ]);

        var extracted = body with
        {
            Operations = [body.Operations[0] with { }],
        };
        var result = MethodFlowBuilder.Build(extracted);
        var node = Assert.IsType<InvocationFlowNode>(Assert.Single(result.Snapshot.Nodes, item => item.Kind == FlowNodeKind.Invocation));

        Assert.Equal(new MethodId("method:v1:Nested.Target"), node.Target);
        Assert.True(node.IsSourceBacked);
        Assert.True(node.IsLoadedProjectTarget);
        Assert.Equal("Fixture.Application", node.TargetAssemblyName);
        Assert.False(node.IsPlatformTarget);
        Assert.Equal(0, node.BlockOrdinal);
        Assert.Equal(7, node.EvaluationOrdinal);
    }

    private static ExtractedMethodBody CreateThrowBody(ExtractedBasicBlock terminalBlock) =>
        new(
            Method,
            "body-fingerprint",
            [],
            [],
            [],
            [
                Block(0, [], fallThrough: 1, terminal: None),
                terminalBlock,
                Block(2, [], fallThrough: null, terminal: Exit, predecessors: [1]),
            ],
            ImmutableArray.Create(new ExtractedExceptionRegion(
                new FlowRegionId("flow-region:v1:root"),
                ExtractedRegionKind.Root,
                null,
                0,
                0,
                2,
                null,
                [],
                CertaintyLevel.Exact)),
            []);

    private static ExtractedMethodBody CreateBody(
        ImmutableArray<ExtractedOperation>? operations = null,
        ImmutableArray<ExtractedBasicBlock>? blocks = null,
        ImmutableArray<ExtractedExceptionRegion>? regions = null) =>
        new(
            Method,
            "body-fingerprint",
            [],
            [],
            operations ?? [],
            blocks ?? [],
            regions ?? ImmutableArray.Create(new ExtractedExceptionRegion(
                new FlowRegionId("flow-region:v1:root"),
                ExtractedRegionKind.Root,
                null,
                0,
                0,
                (blocks?.Length ?? 1) - 1,
                null,
                [],
                CertaintyLevel.Exact)),
            []);

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
        string type) =>
        new(
            id,
            Method,
            kind,
            null,
            [],
            0,
            type,
            null,
            false,
            true,
            [],
            [],
            [],
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            CertaintyLevel.Exact);

    private static ExtractedMethodBody CreateInvocationBody(OperationId invocation, ExtractedOperation[] arguments, string[] argumentNames)
    {
        var argumentIds = argumentNames.Select(name => new OperationId($"behavior-operation:v1:{name}")).ToImmutableArray();
        var call = new ExtractedOperation(invocation, Method, ExtractedOperationKind.Invocation, null, argumentIds, 0,
            "System.Void", null, false, true, [], [], [],
            new ExtractedInvocationPayload(new MethodId("method:v1:target"), false, false, false, false, false, argumentIds),
            null, null, null, null, null, null, null, [], CertaintyLevel.Exact);
        return CreateBody(
            operations: [call, .. arguments],
            blocks: [Block(0, [invocation], fallThrough: 1, terminal: None), Block(1, [], fallThrough: null, terminal: Exit)]);
    }

    private static ExtractedOperation Arg(string name, int parameterOrdinal, string type, string? value, bool hasConstantValue = false) =>
        new(new OperationId($"behavior-operation:v1:{name}"), Method, ExtractedOperationKind.Literal, null, [], 1,
            type, value, false, true, [], [], [], null, null, null, null, null, null,
            LocalName: null, ParameterOrdinal: parameterOrdinal, Evidence: [], Certainty: CertaintyLevel.Exact,
            HasConstantValue: hasConstantValue || value is not null);

    private static EvidenceRef LoopEvidence(string name) =>
        new(new EvidenceId($"evidence:v1:{name}"), EvidenceKind.Source, "loop-fixture.cs", null, name, "test", CertaintyLevel.Exact);
}
