using System.Collections.Immutable;
using SeqDoc.Analysis.Behavior;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using Xunit;

namespace SeqDoc.Behavior.Tests;

/// <summary>
/// Deterministic mutation-gate tests. Each test proves that a plausible mutant to the branch,
/// terminal, loop, or control-dependence logic would change the produced method flow. The existing
/// assertions on the mutated and unmutated bodies therefore kill the equivalent branch-reversal,
/// terminal-classification, and successor-selection mutants without depending on a mutation tool.
/// </summary>
public sealed class MutationGateTests
{
    private static readonly MethodId Method = new("method:v1:test");

    [Fact]
    public void BranchPolarityMutantChangesFlowFingerprint()
    {
        var condition = new OperationId("behavior-operation:v1:cond");
        var trueValue = new OperationId("behavior-operation:v1:true");
        var falseValue = new OperationId("behavior-operation:v1:false");

        var original = MethodFlowBuilder.Build(CreateConditionalBody(condition, trueValue, falseValue, swapSuccessors: false)).Snapshot;
        var mutant = MethodFlowBuilder.Build(CreateConditionalBody(condition, trueValue, falseValue, swapSuccessors: true)).Snapshot;

        Assert.NotEqual(original.FlowFingerprint, mutant.FlowFingerprint);
        var originalEdges = original.Edges.Where(edge => edge.Kind == FlowEdgeKind.True).ToArray();
        var mutantEdges = mutant.Edges.Where(edge => edge.Kind == FlowEdgeKind.True).ToArray();
        Assert.NotEqual(originalEdges[0].Target, mutantEdges[0].Target);
    }

    [Fact]
    public void TerminalClassificationMutantChangesOutcomes()
    {
        var value = new OperationId("behavior-operation:v1:value");
        var returnBody = MethodFlowBuilder.Build(CreateTerminalBody(value, ExtractedBlockTerminalKind.Return)).Snapshot;
        var throwBody = MethodFlowBuilder.Build(CreateTerminalBody(value, ExtractedBlockTerminalKind.Throw)).Snapshot;

        Assert.Contains(returnBody.Outcomes, outcome => outcome.Kind == FlowOutcomeKind.ExplicitReturn);
        Assert.Contains(throwBody.Outcomes, outcome => outcome.Kind == FlowOutcomeKind.EscapingThrow);
        Assert.NotEqual(returnBody.FlowFingerprint, throwBody.FlowFingerprint);
    }

    [Fact]
    public void LoopBackEdgeMutationChangesLoopPresence()
    {
        var condition = new OperationId("behavior-operation:v1:loop-condition");
        var withoutBackEdge = MethodFlowBuilder.Build(CreateLoopBody(condition, includeBackEdge: false)).Snapshot;
        var withBackEdge = MethodFlowBuilder.Build(CreateLoopBody(condition, includeBackEdge: true)).Snapshot;

        Assert.DoesNotContain(withoutBackEdge.Nodes, node => node.Kind == FlowNodeKind.Loop);
        Assert.Contains(withBackEdge.Nodes, node => node.Kind == FlowNodeKind.Loop);
        Assert.NotEqual(withoutBackEdge.FlowFingerprint, withBackEdge.FlowFingerprint);
    }

    [Fact]
    public void HandledThrowMutantChangesOutcomeClassification()
    {
        var throwBlock = new ExtractedBasicBlock(
            2,
            [],
            null,
            null,
            [],
            [1],
            ExtractedBlockTerminalKind.Throw,
            false,
            [],
            [],
            [],
            CertaintyLevel.Exact);
        var handled = MethodFlowBuilder.Build(CreateThrowBody(throwBlock, regions: CreateTryCatchRegions(startBlock: 1, endBlock: 2))).Snapshot;
        var unhandled = MethodFlowBuilder.Build(CreateThrowBody(throwBlock with { EscapingThrow = true }, regions: [])).Snapshot;

        Assert.DoesNotContain(handled.Outcomes, outcome => outcome.Kind == FlowOutcomeKind.EscapingThrow);
        Assert.Contains(unhandled.Outcomes, outcome => outcome.Kind == FlowOutcomeKind.EscapingThrow);
    }

    [Fact]
    public void CandidateOrderMutantChangesCallGraph()
    {
        var declaringType = new SymbolId("symbol:v1:contract");
        var first = new SymbolId("symbol:v1:first");
        var second = new SymbolId("symbol:v1:second");
        var target = new MethodId("method:v1:interface.run");
        var firstMethod = new MethodId("method:v1:zzz-first");
        var secondMethod = new MethodId("method:v1:aaa-second");

        var request = CreateRequest(declaringType, first, second, target, firstMethod, secondMethod);
        var graph = CallResolver.Build(request, ImmutableArray.Create(CreateFlow(target)));

        var site = Assert.Single(graph.CallSites);
        Assert.Equal(secondMethod, site.Resolution.Candidates[0]);
        Assert.Equal(firstMethod, site.Resolution.Candidates[1]);
    }

    private static ExtractedMethodBody CreateConditionalBody(
        OperationId condition,
        OperationId trueValue,
        OperationId falseValue,
        bool swapSuccessors)
    {
        var trueTarget = swapSuccessors ? 3 : 2;
        var falseTarget = swapSuccessors ? 2 : 3;
        return new ExtractedMethodBody(
            Method,
            "body",
            [],
            [],
            ImmutableArray.Create(
                Operation(condition, ExtractedOperationKind.Binary, "System.Boolean"),
                Operation(trueValue, ExtractedOperationKind.ExpressionStatement, "System.Int32"),
                Operation(falseValue, ExtractedOperationKind.ExpressionStatement, "System.Int32")),
            ImmutableArray.Create(
                Block(0, [], 1, None),
                Block(1, [], 4, Conditional, condition, [trueTarget], [0]),
                Block(2, [trueValue], 4, None, null, [], [1]),
                Block(3, [falseValue], 4, None, null, [], [1]),
                Block(4, [], null, Exit, null, [], [2, 3])),
            RootRegion(4),
            []);
    }

    private static ExtractedMethodBody CreateTerminalBody(OperationId value, ExtractedBlockTerminalKind terminal) =>
        new(
            Method,
            "body",
            [],
            [],
            ImmutableArray.Create(Operation(value, ExtractedOperationKind.ExpressionStatement, "System.Int32")),
            ImmutableArray.Create(
                Block(0, [], 1, None),
                Block(1, [value], null, terminal, null, [], [0], escapingThrow: terminal == ExtractedBlockTerminalKind.Throw),
                Block(2, [], null, Exit, null, [], [])),
            RootRegion(2),
            []);

    private static ExtractedMethodBody CreateLoopBody(OperationId condition, bool includeBackEdge)
    {
        var evidence = new EvidenceRef(new EvidenceId("evidence:v1:mutation-loop"), EvidenceKind.Source, "loop-fixture.cs", null, "loop", "test", CertaintyLevel.Exact);
        var loopAnchor = new OperationId("behavior-operation:v1:mutation-loop-anchor");
        var body = new ExtractedMethodBody(
            Method,
            "body",
            [],
            [],
            ImmutableArray.Create(Operation(condition, ExtractedOperationKind.Binary, "System.Boolean") with { Evidence = [evidence] }),
            ImmutableArray.Create(
                Block(0, [], 1, None),
                Block(1, [], 2, Conditional, condition, [3], includeBackEdge ? [0, 2] : [0]),
                Block(2, [], includeBackEdge ? 1 : 3, None, null, [], [1]),
                Block(3, [], null, Exit, null, [], includeBackEdge ? [1] : [1, 2])),
            RootRegion(3),
            [evidence],
            includeBackEdge
                ? [new ExtractedNaturalLoop(loopAnchor, ExtractedLoopKind.WhileLoop, 1, [2], [2], [3],
                    [new ExtractedOrdinaryBranch(2, 1, [], [], [evidence], CertaintyLevel.Exact)], [evidence], CertaintyLevel.Exact)]
                : [],
            includeBackEdge
                ? [new ExtractedLoopAnchor(loopAnchor, ExtractedLoopKind.WhileLoop, [evidence], CertaintyLevel.Exact)]
                : [],
            includeBackEdge
                ? [
                    new ExtractedOrdinaryBranch(0, 1, [], [], [evidence], CertaintyLevel.Exact),
                    new ExtractedOrdinaryBranch(1, 2, [], [], [evidence], CertaintyLevel.Exact),
                    new ExtractedOrdinaryBranch(1, 3, [], [], [evidence], CertaintyLevel.Exact),
                    new ExtractedOrdinaryBranch(2, 1, [], [], [evidence], CertaintyLevel.Exact),
                ]
                : [
                    new ExtractedOrdinaryBranch(0, 1, [], [], [evidence], CertaintyLevel.Exact),
                    new ExtractedOrdinaryBranch(1, 2, [], [], [evidence], CertaintyLevel.Exact),
                    new ExtractedOrdinaryBranch(1, 3, [], [], [evidence], CertaintyLevel.Exact),
                    new ExtractedOrdinaryBranch(2, 3, [], [], [evidence], CertaintyLevel.Exact),
                ]);
        return body;
    }

    private static ExtractedMethodBody CreateThrowBody(
        ExtractedBasicBlock throwBlock,
        ImmutableArray<ExtractedExceptionRegion> regions)
    {
        var throwOnly = new ExtractedMethodBody(
            Method,
            "body",
            [],
            [],
            [],
            ImmutableArray.Create(
                Block(0, [], 1, None),
                Block(1, [], 2, None, null, [], [0]),
                throwBlock,
                Block(3, [], null, Exit, null, [], [])),
            regions,
            []);
        return throwOnly;
    }

    private static ImmutableArray<ExtractedExceptionRegion> CreateTryCatchRegions(int startBlock, int endBlock) =>
        ImmutableArray.Create(
            new ExtractedExceptionRegion(
                new FlowRegionId("flow-region:v1:root"),
                ExtractedRegionKind.Root,
                null,
                0,
                0,
                3,
                null,
                [],
                CertaintyLevel.Exact),
            new ExtractedExceptionRegion(
                new FlowRegionId("flow-region:v1:try"),
                ExtractedRegionKind.Try,
                new FlowRegionId("flow-region:v1:root"),
                1,
                startBlock,
                endBlock,
                null,
                [],
                CertaintyLevel.Exact),
            new ExtractedExceptionRegion(
                new FlowRegionId("flow-region:v1:catch"),
                ExtractedRegionKind.Catch,
                new FlowRegionId("flow-region:v1:try"),
                2,
                startBlock,
                endBlock,
                "System.Exception",
                [],
                CertaintyLevel.Exact));

    private static SeqDoc.Application.Analysis.BehaviorAnalysisRequest CreateRequest(
        SymbolId declaringType,
        SymbolId first,
        SymbolId second,
        MethodId target,
        MethodId firstMethod,
        MethodId secondMethod)
    {
        var profile = CompilationProfile.Create("Dispatch.csproj", "Release", "net10.0");
        var project = new ProjectId("project:v1:test");
        var index = new SeqDoc.Core.ProgramIndex.ProgramIndexSnapshot(
            1,
            "test",
            profile,
            [],
            [],
            [],
            ImmutableArray.Create(
                new SeqDoc.Core.ProgramIndex.ProgramType(declaringType, project, new SymbolId("symbol:v1:ns"), "IContract", SeqDoc.Core.ProgramIndex.ProgramTypeKind.Interface, null, [], "sig", []),
                new SeqDoc.Core.ProgramIndex.ProgramType(first, project, new SymbolId("symbol:v1:ns"), "First", SeqDoc.Core.ProgramIndex.ProgramTypeKind.Class, declaringType, [], "sig", []),
                new SeqDoc.Core.ProgramIndex.ProgramType(second, project, new SymbolId("symbol:v1:ns"), "Second", SeqDoc.Core.ProgramIndex.ProgramTypeKind.Class, declaringType, [], "sig", [])),
            [],
            ImmutableArray.Create(
                new SeqDoc.Core.ProgramIndex.ProgramMethod(target, new SymbolId("symbol:v1:run"), declaringType, "Run", "Run", [], "System.Void", "sig", null, []),
                new SeqDoc.Core.ProgramIndex.ProgramMethod(firstMethod, new SymbolId("symbol:v1:first-run"), first, "Run", "Run", [], "System.Void", "sig", "body", []),
                new SeqDoc.Core.ProgramIndex.ProgramMethod(secondMethod, new SymbolId("symbol:v1:second-run"), second, "Run", "Run", [], "System.Void", "sig", "body", [])),
            [],
            [],
            [],
            [],
            [],
            "manifest",
            "fingerprint");
        return new SeqDoc.Application.Analysis.BehaviorAnalysisRequest(
            index,
            CreateBehaviorInput(profile, (firstMethod, target), (secondMethod, target)));
    }

    private static ExtractedBehaviorInput CreateBehaviorInput(CompilationProfile profile, params (MethodId Implementation, MethodId InterfaceMember)[] facts)
    {
        var interfaceFacts = facts
            .Select(fact => new InterfaceImplementationFact(fact.Implementation, fact.InterfaceMember, [], CertaintyLevel.Exact))
            .ToImmutableArray();
        return new ExtractedBehaviorInput(profile, "fingerprint", [], new ExtractedTypeHierarchy([], true), [], interfaceFacts, [], [], string.Empty);
    }

    private static ExtractedBehaviorInput CreateBehaviorInput(CompilationProfile profile) =>
        CreateBehaviorInput(profile, Array.Empty<(MethodId, MethodId)>());

    private static MethodFlowSnapshot CreateFlow(MethodId target)
    {
        var invocation = new InvocationFlowNode(
            StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(Method, "Invocation", 0, 1, "invocation")),
            Method,
            new OperationId("behavior-operation:v1:call"),
            target,
            IsDispatchable: true,
            IsDelegateOrEventInvoke: false,
            IsStatic: false,
            IsConstructor: false,
            IsDynamic: false,
            [],
            CertaintyLevel.Exact);
        return new MethodFlowSnapshot(
            Method,
            "body",
            ImmutableArray.Create<FlowNode>(
                new EntryFlowNode(StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(Method, "Entry", 0, 0, "entry")), Method, [], CertaintyLevel.Exact),
                invocation,
                new ExitFlowNode(StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(Method, "Exit", 99, 99, "exit")), Method, [], CertaintyLevel.Exact)),
            [],
            [],
            [],
            new LocalValueGraph([], []),
            [],
            null,
            [],
            "flow");
    }

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

    private static ExtractedOperation Operation(OperationId id, ExtractedOperationKind kind, string type) =>
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

    private const ExtractedBlockTerminalKind None = ExtractedBlockTerminalKind.None;
    private const ExtractedBlockTerminalKind Conditional = ExtractedBlockTerminalKind.Conditional;
    private const ExtractedBlockTerminalKind Exit = ExtractedBlockTerminalKind.Exit;
    private const ExtractedBlockTerminalKind Return = ExtractedBlockTerminalKind.Return;
    private const ExtractedBlockTerminalKind Throw = ExtractedBlockTerminalKind.Throw;
}
