using System.Collections.Immutable;
using SeqDoc.Analysis.Behavior;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using Xunit;

namespace SeqDoc.Behavior.Tests;

public sealed class BehaviorAnalyzerTests
{
    private static readonly CompilationProfile Profile = CompilationProfile.Create("Branching.csproj", "Release", "net10.0");

    [Fact]
    public async Task AnalyzeAsyncProducesDeterministicSnapshot()
    {
        var input = new ExtractedBehaviorInput(
            Profile,
            "index-fingerprint",
            [],
            new ExtractedTypeHierarchy([], true),
            [],
            [],
            [],
            [],
            string.Empty);
        var request = new BehaviorAnalysisRequest(CreateEmptyIndex(), input);

        var analyzer = new BehaviorAnalyzer();
        var first = await analyzer.AnalyzeAsync(request, CancellationToken.None);
        var second = await analyzer.AnalyzeAsync(request, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.NotNull(first.Value);
        Assert.Equal(1, first.Value!.SchemaVersion);
        Assert.Equal(Profile.Id, first.Value.Profile.Id);
        Assert.Equal(first.Value.BehaviorFingerprint, second.Value!.BehaviorFingerprint);
    }

    [Fact]
    public async Task AnalyzeAsyncNormalizesExtractedBodiesIntoMethodFlows()
    {
        var input = new ExtractedBehaviorInput(
            Profile,
            "index-fingerprint",
            ImmutableArray.Create(CreateBranchingBody()),
            new ExtractedTypeHierarchy([], true),
            [],
            [],
            [],
            [],
            string.Empty);
        var request = new BehaviorAnalysisRequest(CreateEmptyIndex(), input);

        var result = await new BehaviorAnalyzer().AnalyzeAsync(request, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.MethodFlows);
        var flow = result.Value.MethodFlows[0];
        Assert.Equal(64, flow.FlowFingerprint.Length);
        Assert.Contains(flow.Nodes, node => node.Kind == FlowNodeKind.Entry);
        Assert.Contains(flow.Nodes, node => node.Kind == FlowNodeKind.Exit);
        Assert.Contains(flow.Outcomes, outcome => outcome.Kind == FlowOutcomeKind.NormalCompletion);
        Assert.Contains(flow.Outcomes, outcome => outcome.Kind == FlowOutcomeKind.ExplicitReturn);
        Assert.Equal(64, result.Value.BehaviorFingerprint.Length);
    }

    private static ExtractedMethodBody CreateBranchingBody()
    {
        var methodId = new MethodId("method:v1:test");
        var literalId = new OperationId("behavior-operation:v1:literal");
        var conditionId = new OperationId("behavior-operation:v1:condition");
        var returnValueId = new OperationId("behavior-operation:v1:return-value");
        return new ExtractedMethodBody(
            methodId,
            "body-fingerprint",
            [],
            [],
            ImmutableArray.Create(
                new ExtractedOperation(
                    literalId,
                    methodId,
                    ExtractedOperationKind.Literal,
                    null,
                    [],
                    0,
                    "System.Int32",
                    "1",
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
                    CertaintyLevel.Exact),
                new ExtractedOperation(
                    conditionId,
                    methodId,
                    ExtractedOperationKind.Binary,
                    null,
                    [],
                    1,
                    "System.Boolean",
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
                    CertaintyLevel.Exact),
                new ExtractedOperation(
                    returnValueId,
                    methodId,
                    ExtractedOperationKind.LocalReference,
                    null,
                    [],
                    2,
                    "System.Int32",
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
                    "total",
                    null,
                    [],
                    CertaintyLevel.Exact)),
            ImmutableArray.Create(
                new ExtractedBasicBlock(
                    0,
                    [literalId],
                    null,
                    1,
                    [],
                    [],
                    ExtractedBlockTerminalKind.None,
                    false,
                    [],
                    [],
                    [],
                    CertaintyLevel.Exact),
                new ExtractedBasicBlock(
                    1,
                    [],
                    conditionId,
                    2,
                    [3],
                    [0],
                    ExtractedBlockTerminalKind.Conditional,
                    false,
                    [],
                    [],
                    [],
                    CertaintyLevel.Exact),
                new ExtractedBasicBlock(
                    2,
                    [returnValueId],
                    null,
                    3,
                    [],
                    [1],
                    ExtractedBlockTerminalKind.Return,
                    false,
                    [],
                    [],
                    [],
                    CertaintyLevel.Exact),
                new ExtractedBasicBlock(
                    3,
                    [],
                    null,
                    null,
                    [],
                    [1],
                    ExtractedBlockTerminalKind.Exit,
                    false,
                    [],
                    [],
                    [],
                    CertaintyLevel.Exact)),
            ImmutableArray.Create(new ExtractedExceptionRegion(
                new FlowRegionId("flow-region:v1:root"),
                ExtractedRegionKind.Root,
                null,
                0,
                0,
                3,
                null,
                [],
                CertaintyLevel.Exact)),
            []);
    }

    [Fact]
    public async Task AnalyzeAsyncCarriesExtractionDiagnosticsIntoSnapshot()
    {
        var input = new ExtractedBehaviorInput(
            Profile,
            "index-fingerprint",
            [],
            new ExtractedTypeHierarchy([], true),
            [],
            [],
            [],
            [],
            string.Empty);
        var request = new BehaviorAnalysisRequest(CreateEmptyIndex(), input);

        var result = await new BehaviorAnalyzer().AnalyzeAsync(request, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(64, result.Value!.BehaviorFingerprint.Length);
    }

    [Fact]
    public async Task AnalyzeAsyncAllowsOnlyTheRatifiedWithholdDiagnostics()
    {
        string[] allowedCodes = ["BD2001", "BD2002", "BD2003", "BD2010", "BD2011", "BD2020", "BD3001"];
        var diagnostics = allowedCodes.Select(CreateDiagnostic).ToImmutableArray();
        var input = new ExtractedBehaviorInput(
            Profile, "index-fingerprint", [], new ExtractedTypeHierarchy([], true), [], [], [], diagnostics, string.Empty);

        var result = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(CreateEmptyIndex(), input), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(allowedCodes, result.Value!.Diagnostics.Select(diagnostic => diagnostic.Code));
        Assert.Equal(diagnostics.Select(diagnostic => diagnostic.Id), result.Value.Diagnostics.Select(diagnostic => diagnostic.Id));
        Assert.Equal(diagnostics.Select(diagnostic => diagnostic.Certainty), result.Value.Diagnostics.Select(diagnostic => diagnostic.Certainty));
        Assert.Equal(diagnostics.SelectMany(diagnostic => diagnostic.Evidence), result.Value.Diagnostics.SelectMany(diagnostic => diagnostic.Evidence));
    }

    [Fact]
    public async Task AnalyzeAsyncTreatsUnknownFutureBehaviorDiagnosticsAsBlocking()
    {
        var diagnostic = CreateDiagnostic("BD9999");
        var input = new ExtractedBehaviorInput(
            Profile, "index-fingerprint", [], new ExtractedTypeHierarchy([], true), [], [], [], [diagnostic], string.Empty);

        var result = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(CreateEmptyIndex(), input), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationOutcome.AnalysisFailure, result.Outcome);
        Assert.Null(result.Value);
        var retained = Assert.Single(result.Diagnostics);
        Assert.Equal(diagnostic.Id, retained.Id);
        Assert.Equal(diagnostic.Code, retained.Code);
        Assert.Equal(diagnostic.Certainty, retained.Certainty);
    }

    [Fact]
    public async Task AnalyzeAsyncDiagnosticInputOrderDoesNotChangeFingerprintOrLoseDiagnostics()
    {
        var tiedId = new DiagnosticId("diagnostic:v1:tied");
        var earlier = CreateDiagnostic(tiedId, "BD2001", "A", "detail-a");
        var later = CreateDiagnostic(tiedId, "BD3001", "B", "different detail");
        var diagnostics = ImmutableArray.Create(later, earlier);
        var reversed = diagnostics.Reverse().ToImmutableArray();

        var first = await AnalyzeWithDiagnostics(diagnostics);
        var second = await AnalyzeWithDiagnostics(reversed);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value!.BehaviorFingerprint, second.Value!.BehaviorFingerprint);
        Assert.Equal("A", first.Value.Diagnostics[0].Summary);
        Assert.Equal("B", first.Value.Diagnostics[1].Summary);
        Assert.Equal("detail-a", first.Value.Diagnostics[0].InternalDetail);
        Assert.Equal("different detail", first.Value.Diagnostics[1].InternalDetail);
        Assert.Equal(first.Value.Diagnostics.Select(diagnostic => diagnostic.Id), second.Value.Diagnostics.Select(diagnostic => diagnostic.Id));
        Assert.Equal(first.Value.Diagnostics.Select(diagnostic => diagnostic.Code), second.Value.Diagnostics.Select(diagnostic => diagnostic.Code));
        Assert.Equal(first.Value.Diagnostics.Select(diagnostic => diagnostic.Summary), second.Value.Diagnostics.Select(diagnostic => diagnostic.Summary));
        Assert.Equal(first.Value.Diagnostics.Select(diagnostic => diagnostic.InternalDetail), second.Value.Diagnostics.Select(diagnostic => diagnostic.InternalDetail));
    }

    [Fact]
    public async Task AnalyzeAsyncFailsOnMalformedExtraction()
    {
        var malformedBlock = new ExtractedBasicBlock(
            0,
            [],
            null,
            99,
            [],
            [],
            ExtractedBlockTerminalKind.None,
            false,
            [],
            [],
            [],
            CertaintyLevel.Exact);
        var body = new ExtractedMethodBody(
            new MethodId("method:v1:malformed"),
            "body-fingerprint",
            [],
            [],
            [],
            ImmutableArray.Create(malformedBlock),
            ImmutableArray.Create(new ExtractedExceptionRegion(
                new FlowRegionId("flow-region:v1:root"),
                ExtractedRegionKind.Root,
                null,
                0,
                0,
                0,
                null,
                [],
                CertaintyLevel.Exact)),
            []);
        var input = new ExtractedBehaviorInput(
            Profile,
            "index-fingerprint",
            ImmutableArray.Create(body),
            new ExtractedTypeHierarchy([], true),
            [],
            [],
            [],
            [],
            string.Empty);
        var request = new BehaviorAnalysisRequest(CreateEmptyIndex(), input);

        var result = await new BehaviorAnalyzer().AnalyzeAsync(request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationOutcome.AnalysisFailure, result.Outcome);
        Assert.Null(result.Value);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "BD1009");
    }

    [Fact]
    public async Task AnalyzeAsyncCompletesWhenFlowBuildingWithholdsANaturalLoop()
    {
        // BD2011 is a withhold-class code: MethodFlowBuilder skips the one malformed natural loop
        // with `continue`, the method flow is still produced and fingerprinted. It must not escalate
        // to a whole-profile AnalysisFailure.
        var withheldLoop = new ExtractedNaturalLoop(
            new OperationId("behavior-operation:v1:condition"),
            ExtractedLoopKind.WhileLoop,
            HeaderBlockOrdinal: 1,
            LatchBlockOrdinals: [],
            BodyBlockOrdinals: ImmutableArray.Create(2),
            ExitBlockOrdinals: ImmutableArray.Create(3),
            BackEdges: [],
            Evidence: [],
            Certainty: CertaintyLevel.Exact);
        var body = CreateBranchingBody() with { NaturalLoops = ImmutableArray.Create(withheldLoop) };
        var input = new ExtractedBehaviorInput(
            Profile,
            "index-fingerprint",
            ImmutableArray.Create(body),
            new ExtractedTypeHierarchy([], true),
            [],
            [],
            [],
            [],
            string.Empty);
        var request = new BehaviorAnalysisRequest(CreateEmptyIndex(), input);

        var analyzer = new BehaviorAnalyzer();
        var first = await analyzer.AnalyzeAsync(request, CancellationToken.None);
        var second = await analyzer.AnalyzeAsync(request, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.NotNull(first.Value);
        Assert.Contains(first.Value!.Diagnostics, diagnostic => diagnostic.Code == "BD2011");
        Assert.Single(first.Value.MethodFlows);
        Assert.Equal(64, first.Value.BehaviorFingerprint.Length);
        Assert.Equal(first.Value.BehaviorFingerprint, second.Value!.BehaviorFingerprint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnalyzeAsyncCompletesWhenCatchContinuationMappingIsAmbiguous(bool countAmbiguity)
    {
        var body = CreateAmbiguousCatchContinuationBody(countAmbiguity);
        var request = new BehaviorAnalysisRequest(
            CreateEmptyIndex(),
            new ExtractedBehaviorInput(
                Profile,
                "index-fingerprint",
                [body],
                new ExtractedTypeHierarchy([], true),
                [],
                [],
                [],
                [],
                string.Empty));

        var analyzer = new BehaviorAnalyzer();
        var first = await analyzer.AnalyzeAsync(request, CancellationToken.None);
        var second = await analyzer.AnalyzeAsync(request, CancellationToken.None);

        Assert.True(
            first.IsSuccess,
            string.Join(Environment.NewLine, first.Diagnostics.Select(item => $"{item.Code}: {item.Summary}")));
        var snapshot = Assert.IsType<BehaviorSnapshot>(first.Value);
        var flow = Assert.Single(snapshot.MethodFlows);
        Assert.True(flow.CatchContinuations.IsDefaultOrEmpty);
        var diagnostic = Assert.Single(snapshot.Diagnostics, item => item.Code == "BD2020");
        string[] expectedEvidence = countAmbiguity
            ? ["branch", "catch-a", "catch-b", "loop"]
            : ["branch", "catch-a", "loop", "try-a", "try-b"];
        Assert.Equal(
            expectedEvidence.Select(name => $"evidence:v1:{name}"),
            diagnostic.Evidence.Select(item => item.Id.Value));
        Assert.Equal(CertaintyLevel.Heuristic, diagnostic.Certainty);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(AnalysisStage.BaselineIndex, diagnostic.Stage);
        Assert.Equal(64, flow.FlowFingerprint.Length);
        Assert.Equal(snapshot.BehaviorFingerprint, second.Value!.BehaviorFingerprint);
        Assert.Equal(diagnostic.Id, Assert.Single(second.Value.Diagnostics, item => item.Code == "BD2020").Id);
    }

    [Fact]
    public async Task AnalyzeAsyncFailsOnBd1xxxExtractionInvariantAtExtractionCallSite()
    {
        // BD1009 (extraction-structural invariant) must still block - guards gate 4 / previous-valid-state.
        var input = new ExtractedBehaviorInput(
            Profile,
            "index-fingerprint",
            ImmutableArray.Create(new ExtractedMethodBody(
                new MethodId("method:v1:bad-successor"),
                "body-fingerprint",
                [],
                [],
                [],
                ImmutableArray.Create(new ExtractedBasicBlock(
                    0,
                    [],
                    null,
                    99,
                    [],
                    [],
                    ExtractedBlockTerminalKind.None,
                    false,
                    [],
                    [],
                    [],
                    CertaintyLevel.Exact)),
                ImmutableArray.Create(new ExtractedExceptionRegion(
                    new FlowRegionId("flow-region:v1:root"),
                    ExtractedRegionKind.Root,
                    null,
                    0,
                    0,
                    0,
                    null,
                    [],
                    CertaintyLevel.Exact)),
                [])),
            new ExtractedTypeHierarchy([], true),
            [],
            [],
            [],
            [],
            string.Empty);
        var request = new BehaviorAnalysisRequest(CreateEmptyIndex(), input);

        var result = await new BehaviorAnalyzer().AnalyzeAsync(request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationOutcome.AnalysisFailure, result.Outcome);
        Assert.Null(result.Value);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "BD1009");
    }

    [Fact]
    public async Task AnalyzeAsyncFailsWhenFlowBuildingReportsNoExitBlock()
    {
        // BD2004 ("method flow has no exit block") is deliberately kept blocking: terminal
        // reconciliation has no exit node to resolve against, so the flow is not safe to consume.
        var operationId = new OperationId("behavior-operation:v1:literal");
        var methodId = new MethodId("method:v1:no-exit");
        var body = new ExtractedMethodBody(
            methodId,
            "body-fingerprint",
            [],
            [],
            ImmutableArray.Create(new ExtractedOperation(
                operationId,
                methodId,
                ExtractedOperationKind.Literal,
                null,
                [],
                0,
                "System.Int32",
                "1",
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
                CertaintyLevel.Exact)),
            ImmutableArray.Create(
                new ExtractedBasicBlock(
                    0,
                    ImmutableArray.Create(operationId),
                    null,
                    1,
                    [],
                    [],
                    ExtractedBlockTerminalKind.None,
                    false,
                    [],
                    [],
                    [],
                    CertaintyLevel.Exact),
                new ExtractedBasicBlock(
                    1,
                    [],
                    null,
                    null,
                    [],
                    [0],
                    ExtractedBlockTerminalKind.Return,
                    false,
                    [],
                    [],
                    [],
                    CertaintyLevel.Exact)),
            ImmutableArray.Create(new ExtractedExceptionRegion(
                new FlowRegionId("flow-region:v1:root"),
                ExtractedRegionKind.Root,
                null,
                0,
                0,
                1,
                null,
                [],
                CertaintyLevel.Exact)),
            []);
        var input = new ExtractedBehaviorInput(
            Profile,
            "index-fingerprint",
            ImmutableArray.Create(body),
            new ExtractedTypeHierarchy([], true),
            [],
            [],
            [],
            [],
            string.Empty);
        var request = new BehaviorAnalysisRequest(CreateEmptyIndex(), input);

        var result = await new BehaviorAnalyzer().AnalyzeAsync(request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationOutcome.AnalysisFailure, result.Outcome);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "BD2004");
    }

    [Fact]
    public async Task AnalyzeAsyncWithheldNaturalLoopDoesNotFlattenGuardStructure()
    {
        // Gate 3 (monotonic claims): withholding one malformed natural loop must degrade locally, not
        // linearise the method. The guarded branch that the loop body sat under must survive in the flow.
        var withheldLoop = new ExtractedNaturalLoop(
            new OperationId("behavior-operation:v1:condition"),
            ExtractedLoopKind.WhileLoop,
            HeaderBlockOrdinal: 1,
            LatchBlockOrdinals: [],
            BodyBlockOrdinals: ImmutableArray.Create(2),
            ExitBlockOrdinals: ImmutableArray.Create(3),
            BackEdges: [],
            Evidence: [],
            Certainty: CertaintyLevel.Exact);
        var body = CreateBranchingBody() with { NaturalLoops = ImmutableArray.Create(withheldLoop) };
        var input = new ExtractedBehaviorInput(
            Profile,
            "index-fingerprint",
            ImmutableArray.Create(body),
            new ExtractedTypeHierarchy([], true),
            [],
            [],
            [],
            [],
            string.Empty);
        var request = new BehaviorAnalysisRequest(CreateEmptyIndex(), input);

        var result = await new BehaviorAnalyzer().AnalyzeAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value!.Diagnostics, diagnostic => diagnostic.Code == "BD2011");
        var flow = result.Value.MethodFlows[0];
        Assert.Contains(flow.Nodes, node => node.Kind == FlowNodeKind.Decision);
        Assert.Contains(flow.Edges, edge => edge.Kind == FlowEdgeKind.True);
        Assert.Contains(flow.Edges, edge => edge.Kind == FlowEdgeKind.False);
        Assert.DoesNotContain(flow.Regions, region => region.Kind == FlowRegionKind.NaturalLoop);
        Assert.DoesNotContain(flow.Edges, edge => edge.Kind == FlowEdgeKind.LoopBack);
    }

    [Fact]
    public async Task AnalyzeAsyncFailsWhenLoopAnchorCollectionIsInvalid()
    {
        // BD2012 ("the compiler loop-anchor collection is invalid") is kept blocking: an anchor with
        // empty evidence signals corrupt upstream extraction, not a single recoverable local withhold.
        var invalidAnchor = new ExtractedLoopAnchor(
            new OperationId("behavior-operation:v1:condition"),
            ExtractedLoopKind.WhileLoop,
            Evidence: [],
            Certainty: CertaintyLevel.Exact);
        var body = CreateBranchingBody() with { LoopAnchors = ImmutableArray.Create(invalidAnchor) };
        var input = new ExtractedBehaviorInput(
            Profile,
            "index-fingerprint",
            ImmutableArray.Create(body),
            new ExtractedTypeHierarchy([], true),
            [],
            [],
            [],
            [],
            string.Empty);
        var request = new BehaviorAnalysisRequest(CreateEmptyIndex(), input);

        var result = await new BehaviorAnalyzer().AnalyzeAsync(request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationOutcome.AnalysisFailure, result.Outcome);
        Assert.Null(result.Value);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "BD2012");
    }

    private static ExtractedMethodBody CreateAmbiguousCatchContinuationBody(bool countAmbiguity)
    {
        var method = new MethodId($"method:v1:ambiguous-catch-{countAmbiguity}");
        var condition = new OperationId($"behavior-operation:v1:condition-{countAmbiguity}");
        var loopAnchor = new OperationId($"behavior-operation:v1:loop-{countAmbiguity}");
        var rootId = new FlowRegionId($"flow-region:v1:root-{countAmbiguity}");
        var tryAId = new FlowRegionId($"flow-region:v1:try-a-{countAmbiguity}");
        var tryBId = new FlowRegionId($"flow-region:v1:try-b-{countAmbiguity}");
        var catchAId = new FlowRegionId($"flow-region:v1:catch-a-{countAmbiguity}");
        var catchBId = new FlowRegionId($"flow-region:v1:catch-b-{countAmbiguity}");
        var branchEvidence = CreateEvidence("branch", CertaintyLevel.Exact);
        var loopEvidence = CreateEvidence("loop", CertaintyLevel.Exact);
        var tryAEvidence = CreateEvidence("try-a", CertaintyLevel.Conservative);
        var tryBEvidence = CreateEvidence("try-b", CertaintyLevel.Heuristic);
        var catchAEvidence = CreateEvidence("catch-a", CertaintyLevel.Conservative);
        var catchBEvidence = CreateEvidence("catch-b", CertaintyLevel.Heuristic);
        ImmutableArray<FlowRegionId> catchIds = countAmbiguity ? [catchAId, catchBId] : [catchAId];
        ImmutableArray<FlowRegionId> tryIds = countAmbiguity ? [tryAId] : [tryAId, tryBId];
        var backEdge = new ExtractedOrdinaryBranch(
            2,
            1,
            [],
            catchIds,
            [branchEvidence],
            CertaintyLevel.Exact);

        var regions = ImmutableArray.CreateBuilder<ExtractedExceptionRegion>();
        regions.Add(new ExtractedExceptionRegion(
            rootId, ExtractedRegionKind.Root, null, 0, 0, 3, null, [], CertaintyLevel.Exact));
        regions.Add(new ExtractedExceptionRegion(
            tryAId, ExtractedRegionKind.Try, rootId, 1, 1, 2, null, [tryAEvidence], CertaintyLevel.Conservative));
        if (!countAmbiguity)
        {
            regions.Add(new ExtractedExceptionRegion(
                tryBId, ExtractedRegionKind.Try, rootId, 2, 1, 2, null, [tryBEvidence], CertaintyLevel.Heuristic));
        }

        regions.Add(new ExtractedExceptionRegion(
            catchAId,
            ExtractedRegionKind.Catch,
            rootId,
            regions.Count,
            2,
            2,
            "System.Exception",
            [catchAEvidence],
            CertaintyLevel.Conservative));
        if (countAmbiguity)
        {
            regions.Add(new ExtractedExceptionRegion(
                catchBId,
                ExtractedRegionKind.Catch,
                rootId,
                regions.Count,
                2,
                2,
                "System.InvalidOperationException",
                [catchBEvidence],
                CertaintyLevel.Heuristic));
        }

        return new ExtractedMethodBody(
            method,
            "body-fingerprint",
            [],
            [],
            [new ExtractedOperation(
                condition,
                method,
                ExtractedOperationKind.Binary,
                null,
                [],
                0,
                "System.Boolean",
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
                [branchEvidence],
                CertaintyLevel.Exact)],
            [
                new ExtractedBasicBlock(0, [], null, 1, [], [], ExtractedBlockTerminalKind.None, false, [], [], [], CertaintyLevel.Exact),
                new ExtractedBasicBlock(1, [], condition, 2, [3], [0, 2], ExtractedBlockTerminalKind.Conditional, false, [], [], [], CertaintyLevel.Exact),
                new ExtractedBasicBlock(2, [], null, 1, [], [1], ExtractedBlockTerminalKind.None, false, [], [], [], CertaintyLevel.Exact),
                new ExtractedBasicBlock(3, [], null, null, [], [1], ExtractedBlockTerminalKind.Exit, false, [], [], [], CertaintyLevel.Exact),
            ],
            regions.ToImmutable(),
            [branchEvidence, loopEvidence, tryAEvidence, tryBEvidence, catchAEvidence, catchBEvidence],
            [new ExtractedNaturalLoop(
                loopAnchor,
                ExtractedLoopKind.WhileLoop,
                1,
                [2],
                [2],
                [3],
                [backEdge],
                [branchEvidence, loopEvidence],
                CertaintyLevel.Exact)],
            [new ExtractedLoopAnchor(loopAnchor, ExtractedLoopKind.WhileLoop, [loopEvidence], CertaintyLevel.Exact)],
            [
                new ExtractedOrdinaryBranch(0, 1, tryIds, [], [branchEvidence], CertaintyLevel.Exact),
                new ExtractedOrdinaryBranch(1, 2, catchIds, [], [branchEvidence], CertaintyLevel.Exact),
                new ExtractedOrdinaryBranch(1, 3, [], tryIds, [branchEvidence], CertaintyLevel.Exact),
                backEdge,
            ]);
    }

    private static EvidenceRef CreateEvidence(string name, CertaintyLevel certainty) =>
        new(
            new EvidenceId($"evidence:v1:{name}"),
            EvidenceKind.Source,
            "catch-continuation.cs",
            null,
            name,
            "test",
            certainty);

    private static ProgramIndexSnapshot CreateEmptyIndex() =>
        new(
            1,
            "test",
            Profile,
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            "manifest",
            "index-fingerprint");

    private static AnalysisDiagnostic CreateDiagnostic(string code) =>
        CreateDiagnostic(new DiagnosticId($"diagnostic:v1:{code}"), code, $"Test diagnostic {code}.", null);

    private static AnalysisDiagnostic CreateDiagnostic(
        DiagnosticId id, string code, string summary, string? internalDetail) =>
        new(
            id,
            code,
            DiagnosticSeverity.Warning,
            AnalysisStage.FrameworkModel,
            summary,
            new DiagnosticLocation("test"),
            "test cause",
            "test impact",
            "test action",
            CertaintyLevel.Exact,
            internalDetail: internalDetail);

    private static Task<ApplicationResult<BehaviorSnapshot>> AnalyzeWithDiagnostics(
        ImmutableArray<AnalysisDiagnostic> diagnostics)
    {
        var input = new ExtractedBehaviorInput(
            Profile, "index-fingerprint", [], new ExtractedTypeHierarchy([], true), [], [], [], diagnostics, string.Empty);
        return new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(CreateEmptyIndex(), input), CancellationToken.None);
    }
}
