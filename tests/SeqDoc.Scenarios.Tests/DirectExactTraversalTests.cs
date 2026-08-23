using System.Collections.Immutable;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.Configuration;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using Xunit;

namespace SeqDoc.Scenarios.Tests;

/// <summary>
/// CT-6 write-first contract.  DirectExactTraversalFixture is deliberately a neutral, reusable
/// call-tree fixture: its partitions vary compiler facts, not product names.  The expected seam is
/// ScenarioGraph.DirectCallExpansion, populated by ScenarioGraphBuilder for a configured root.
/// </summary>
public sealed class DirectExactTraversalTests
{
    [Fact]
    public void ExpansionContractRejectsChildBeforeParentAndCompleteCycleBoundary()
    {
        var evidence = ImmutableArray.Create(ScenarioTestFactory.SourceEvidence("direct-contract"));
        var parent = new ScenarioDirectCallExpansionStep("parent", null, 1,
            new MethodId("caller"), new MethodId("target"), new OperationId("parent-operation"),
            new ScenarioNodeId("parent-node"), 0, evidence, SeqDoc.Core.Evidence.CertaintyLevel.Exact, true);
        var child = new ScenarioDirectCallExpansionStep("child", "parent", 2,
            new MethodId("target"), new MethodId("leaf"), new OperationId("child-operation"),
            new ScenarioNodeId("child-node"), 0, evidence, SeqDoc.Core.Evidence.CertaintyLevel.Exact, true);

        Assert.Throws<ArgumentException>(() => new ScenarioDirectCallExpansion([child, parent], true, []));
        var cycle = new ScenarioDirectCallExpansionStep("cycle", null, 1,
            new MethodId("caller"), new MethodId("target"), new OperationId("cycle-operation"),
            new ScenarioNodeId("cycle-node"), 0, evidence, SeqDoc.Core.Evidence.CertaintyLevel.Exact, true,
            isCycleBoundary: true);
        Assert.Throws<ArgumentException>(() => new ScenarioDirectCallExpansion([cycle], true, []));
    }

    [Fact]
    public void ExactRootAndAvailableChildrenAreTypedDepthFirstStepsAndCalls()
    {
        var graph = DirectExactTraversalFixture.BuildGraph("depth-two");
        var expansion = graph.DirectCallExpansion;

        Assert.Equal([1, 2, 1], expansion.Steps.Select(step => step.Depth));
        Assert.All(expansion.Steps, step => Assert.Equal(ScenarioNodeKind.MethodCall,
            graph.Nodes.Single(node => node.Id == step.ScenarioNodeId).Kind));
        Assert.Equal(expansion.Steps.Length, graph.Edges.Count(edge => edge.Kind == ScenarioEdgeKind.Call));
        Assert.Equal(["operation:v1:root.first", "operation:v1:child.first", "operation:v1:root.second"],
            expansion.Steps.Select(step => step.Operation.Value));
        Assert.All(expansion.Steps, step =>
        {
            Assert.NotEmpty(step.Evidence);
            Assert.NotEqual(SeqDoc.Core.Evidence.CertaintyLevel.Unknown, step.Certainty);
        });
    }

    [Fact]
    public void ReversedFactsHaveIdenticalExpansionIdentityDebugProjectionAndPlan()
    {
        var normal = DirectExactTraversalFixture.BuildGraph("depth-two");
        var reversed = DirectExactTraversalFixture.BuildGraph("depth-two-reversed");

        Assert.Equal(normal.DebugProjection, reversed.DebugProjection);
        Assert.Equal(normal.DirectCallExpansion!.Steps.Select(step => step.Id),
            reversed.DirectCallExpansion!.Steps.Select(step => step.Id));
        Assert.Equal(DirectExactTraversalFixture.Plan(normal), DirectExactTraversalFixture.Plan(reversed));
    }

    [Fact]
    public void DuplicateInvocationAnchorsRequireAgreementBeforeOneCanonicalStep()
    {
        var agreeing = DirectExactTraversalFixture.BuildGraph("duplicate-agreeing");
        var disagreeing = DirectExactTraversalFixture.BuildGraph("duplicate-disagreeing");

        Assert.Single(agreeing.DirectCallExpansion!.Steps,
            step => step.Operation.Value == "operation:v1:root.first");
        Assert.Empty(disagreeing.Nodes.Where(node => node.Kind == ScenarioNodeKind.MethodCall));
        Assert.Empty(disagreeing.DirectCallExpansion!.Steps);
        Assert.False(disagreeing.DirectCallExpansion.IsComplete);
        Assert.Contains(disagreeing.DirectCallExpansion.Diagnostics,
            diagnostic => diagnostic.Code == "SC-DIRECT-DUPLICATE" && !diagnostic.Evidence.IsDefaultOrEmpty);
    }

    [Fact]
    public void DeepChainExpandsInDeterministicDepthFirstChronologyWithoutDepthBoundary()
    {
        var expansion = DirectExactTraversalFixture.BuildGraph("deep-chain").DirectCallExpansion;

        Assert.Equal(1024, expansion.Steps.Length);
        Assert.Equal(Enumerable.Range(1, 1024), expansion.Steps.Select(step => step.Depth));
        Assert.Equal(Enumerable.Range(0, 1024).Select(index => $"operation:v1:chain.{index:D3}"),
            expansion.Steps.Select(step => step.Operation.Value));
        Assert.False(expansion.IsComplete);
        Assert.False(expansion.Steps[^1].IsComplete);
        Assert.Equal("operation:v1:chain.1023", expansion.Steps[^1].Operation.Value);
        var budgetDiagnostic = Assert.Single(expansion.Diagnostics, diagnostic => diagnostic.Code == "SC-DIRECT-METHOD-BUDGET");
        Assert.Contains("1024", budgetDiagnostic.Detail, StringComparison.Ordinal);
        Assert.NotEmpty(budgetDiagnostic.Evidence);
        Assert.DoesNotContain(expansion.Diagnostics, diagnostic => diagnostic.Code == "SC-DIRECT-DEPTH");
    }

    [Fact]
    public void DeepChainReversedFactsProduceIdenticalExpansionAndPlan()
    {
        var normal = DirectExactTraversalFixture.BuildGraph("deep-chain");
        var reversed = DirectExactTraversalFixture.BuildGraph("deep-chain-reversed");

        Assert.Equal(normal.DebugProjection, reversed.DebugProjection);
        Assert.Equal(normal.DirectCallExpansion!.Steps.Select(step => step.Id), reversed.DirectCallExpansion!.Steps.Select(step => step.Id));
        Assert.Equal(normal.DirectCallExpansion.Steps.Select(step => (step.Depth, step.Operation.Value, step.IsComplete, step.IsCycleBoundary)),
            reversed.DirectCallExpansion.Steps.Select(step => (step.Depth, step.Operation.Value, step.IsComplete, step.IsCycleBoundary)));
        Assert.Equal(normal.DirectCallExpansion.Diagnostics.Select(item => $"{item.Code}|{item.Detail}|{string.Join(',', item.Evidence.Select(evidence => evidence.Id.Value))}"),
            reversed.DirectCallExpansion.Diagnostics.Select(item => $"{item.Code}|{item.Detail}|{string.Join(',', item.Evidence.Select(evidence => evidence.Id.Value))}"));
        Assert.Equal(DirectExactTraversalFixture.Plan(normal), DirectExactTraversalFixture.Plan(reversed));
    }

    [Fact]
    public void CallBudgetKeepsExactDeterministicPrefixAndNamesConfiguredLimit()
    {
        var expansion = DirectExactTraversalFixture.BuildGraph("deep-chain", new DiagramBudget(1024, 4, 1024, 256, 45_000)).DirectCallExpansion;

        Assert.Equal(4, expansion.Steps.Length);
        Assert.Equal(["operation:v1:chain.000", "operation:v1:chain.001", "operation:v1:chain.002", "operation:v1:chain.003"],
            expansion.Steps.Select(step => step.Operation.Value));
        var diagnostic = Assert.Single(expansion.Diagnostics, item => item.Code == "SC-DIRECT-CALL-BUDGET");
        Assert.Contains("4", diagnostic.Detail, StringComparison.Ordinal);
        Assert.NotEmpty(diagnostic.Evidence);
        Assert.False(expansion.IsComplete);
    }

    [Fact]
    public void MethodBudgetCountsConfiguredRootAndPreservesIncompleteBoundaryCallSite()
    {
        var expansion = DirectExactTraversalFixture.BuildGraph("deep-chain", new DiagramBudget(3, 1024, 1024, 256, 45_000)).DirectCallExpansion;

        Assert.Equal(3, expansion.Steps.Length);
        Assert.Equal(["operation:v1:chain.000", "operation:v1:chain.001", "operation:v1:chain.002"],
            expansion.Steps.Select(step => step.Operation.Value));
        var boundary = Assert.Single(expansion.Steps.Where(step => !step.IsComplete));
        Assert.Equal("operation:v1:chain.002", boundary.Operation.Value);
        var diagnostic = Assert.Single(expansion.Diagnostics, item => item.Code == "SC-DIRECT-METHOD-BUDGET");
        Assert.Contains("3", diagnostic.Detail, StringComparison.Ordinal);
        Assert.NotEmpty(diagnostic.Evidence);
    }

    [Fact]
    public void SharedCalleeConsumesDistinctMethodOnceButEachOccurrenceConsumesCallBudget()
    {
        var expansion = DirectExactTraversalFixture.BuildGraph("shared-callee", new DiagramBudget(3, 3, 1024, 256, 45_000)).DirectCallExpansion;

        Assert.Equal(3, expansion.Steps.Length);
        Assert.Equal(["operation:v1:root.first", "operation:v1:shared.first", "operation:v1:root.second"],
            expansion.Steps.Select(step => step.Operation.Value));
        Assert.Contains(expansion.Diagnostics, item => item.Code == "SC-DIRECT-CALL-BUDGET");
        Assert.DoesNotContain(expansion.Diagnostics, item => item.Code == "SC-DIRECT-METHOD-BUDGET");
        Assert.Equal(2, expansion.Steps.Count(step => step.TargetMethod == DirectExactTraversalFixture.SharedCallee));
    }

    [Theory]
    [InlineData("direct-recursion")]
    [InlineData("mutual-recursion")]
    public void CyclesAreVisibleAtTheirCallSiteButNeverReentered(string partition)
    {
        var expansion = DirectExactTraversalFixture.BuildGraph(partition).DirectCallExpansion;

        string[] expectedCycleOperations = partition == "direct-recursion"
            ? ["operation:v1:self.call"]
            : ["operation:v1:mutual.back"];
        Assert.Equal(expectedCycleOperations,
            expansion.Steps.Where(step => step.IsCycleBoundary).Select(step => step.Operation.Value));
        Assert.Equal(expectedCycleOperations,
            expansion.Steps.Select(step => step.Operation.Value).Where(operation => expectedCycleOperations.Contains(operation)));
        Assert.All(expansion.Steps.Where(step => step.IsCycleBoundary), step => Assert.False(step.IsComplete));
        Assert.False(expansion.IsComplete);
        Assert.Contains(expansion.Diagnostics, diagnostic => diagnostic.Code == "SC-DIRECT-CYCLE");
    }

    [Fact]
    public void SharedCalleeUsesDistinctPathIdentitiesAndChronologicalDescendants()
    {
        var graph = DirectExactTraversalFixture.BuildGraph("shared-callee");
        var expansion = graph.DirectCallExpansion;
        var shared = expansion.Steps.Where(step => step.TargetMethod == DirectExactTraversalFixture.SharedCallee).ToArray();

        Assert.Equal(2, shared.Length);
        Assert.Equal(2, shared.Select(step => step.Id).Distinct().Count());
        Assert.Equal(["operation:v1:root.first", "operation:v1:shared.first", "operation:v1:root.second", "operation:v1:shared.first"],
            expansion.Steps.Select(step => step.Operation.Value));
        var plan = DocumentationPlanner.Plan(graph);
        Assert.Contains(plan.Diagram.Messages, message => message.Source == "fixture_shared"
            && message.Target == "fixture_leaf" && message.Label == "shared.first");
    }

    [Theory]
    [InlineData("cha")]
    [InlineData("incomplete")]
    [InlineData("ambiguous")]
    [InlineData("platform")]
    [InlineData("dynamic")]
    [InlineData("delegate")]
    [InlineData("constructor")]
    [InlineData("nested-function")]
    public void NonExactMaterialPartitionsAreNotTraversed(string partition)
    {
        var graph = DirectExactTraversalFixture.BuildGraph(partition);
        var expansion = graph.DirectCallExpansion;

        Assert.Empty(expansion.Steps.Where(step => step.Depth > 1));
        Assert.Empty(expansion.Diagnostics);
    }

    [Theory]
    [InlineData("body-unavailable", "SC-DIRECT-BODY-UNAVAILABLE")]
    [InlineData("unloaded-project", "SC-DIRECT-SOURCE-UNAVAILABLE")]
    [InlineData("metadata-target", "SC-DIRECT-SOURCE-UNAVAILABLE")]
    [InlineData("generated-target", "SC-DIRECT-SOURCE-UNAVAILABLE")]
    public void ExpansionBoundariesKeepParentVisibleAndWithholdChildren(string partition, string code)
    {
        var graph = DirectExactTraversalFixture.BuildGraph(partition);
        var expansion = graph.DirectCallExpansion;

        Assert.Contains(expansion.Steps, step => step.Depth == 1);
        Assert.DoesNotContain(expansion.Steps, step => step.Depth > 2);
        Assert.False(expansion.IsComplete);
        Assert.Contains(expansion.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Theory]
    [InlineData("sensitive-aws", "AKIA" + "1234567890ABCDEF")]
    [InlineData("sensitive-github", "ghp_test_credential_value")]
    [InlineData("sensitive-jwt", "eyJhbGciOiJIUzI1NiJ9")]
    [InlineData("sensitive-openai", "sk-test-credential-value-123")]
    [InlineData("sensitive-generic", "Abcdefghijklmnop1234")]
    public void SensitiveArgumentValuesNeverReachScenarioOrWordingProjection(string partition, string secret)
    {
        var graph = DirectExactTraversalFixture.BuildGraph(partition);
        var projection = DocumentationPlanner.Plan(graph).Diagram.DebugProjection?.ToString() ?? string.Empty;

        Assert.DoesNotContain(secret, projection, StringComparison.Ordinal);
        Assert.DoesNotContain("AKIA", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-", projection, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("foreign-behavior-profile")]
    [InlineData("foreign-behavior-fingerprint")]
    public void ForeignBehaviorSnapshotsWithholdConfiguredDirectExpansion(string partition)
    {
        var graph = DirectExactTraversalFixture.BuildGraph(partition);

        Assert.Empty(graph.Nodes.Where(node => node.Kind == ScenarioNodeKind.MethodCall));
        Assert.Empty(graph.DirectCallExpansion!.Steps);
        Assert.False(graph.DirectCallExpansion.IsComplete);
        var diagnostic = Assert.Single(graph.DirectCallExpansion.Diagnostics,
            item => item.Code == "SC-DIRECT-MISMATCH");
        Assert.NotEmpty(diagnostic.Evidence);
        Assert.Equal("SC-DIRECT-MISMATCH", diagnostic.Code);
    }

    [Theory]
    [InlineData("no-flow", "SC-DIRECT-NO-FLOW")]
    [InlineData("ambiguous-flow", "SC-DIRECT-AMBIGUOUS-FLOW")]
    public void MissingAndAmbiguousTargetFlowsHaveDistinctConservativeDiagnostics(string partition, string code)
    {
        var expansion = DirectExactTraversalFixture.BuildGraph(partition).DirectCallExpansion;

        Assert.Contains(expansion.Steps, step => step.Depth == 1);
        Assert.Contains(expansion.Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.DoesNotContain(expansion.Diagnostics, diagnostic => diagnostic.Code == "SC-DIRECT-MISMATCH");
    }

    /// <summary>
    /// Generic loaded cross-project traversal is allowed when both projects are loaded in the same
    /// compilation and the target has a MethodFlow. The foreign-project partition places Child
    /// in a different project than Root, and traversal expands into it without a cross-project stop.
    /// </summary>
    [Fact]
    public void CrossProjectTraversalExpandsWhenBothProjectsAreLoaded()
    {
        var graph = DirectExactTraversalFixture.BuildGraph("foreign-project");
        var expansion = graph.DirectCallExpansion;

        // The cross-project boundary is no longer emitted; traversal expands into the child.
        Assert.Contains(expansion.Steps, step => step.Depth == 1);
        Assert.DoesNotContain(expansion.Diagnostics,
            diagnostic => diagnostic.Code == "SC-DIRECT-CROSS-PROJECT");
        // The child body was traversed: Child's call to Grandchild is visible as a MethodCall node.
        Assert.Contains(graph.Nodes, node => node.Kind == ScenarioNodeKind.MethodCall);
    }

    [Fact]
    public void DescendantsInheritRootArmAndGuardedCalleeCallsFailClosed()
    {
        var graph = DirectExactTraversalFixture.BuildGraph("inherited-arm-and-guarded-child");
        var expansion = graph.DirectCallExpansion;

        var rootGuarded = Assert.Single(expansion.Steps, step => step.Operation.Value == "operation:v1:root.first");
        Assert.NotEmpty(rootGuarded.RootArmIds);
        Assert.All(expansion.Steps.Where(step => step.Operation.Value != "operation:v1:root.second"), step =>
            Assert.Equal(rootGuarded.RootArmIds, step.RootArmIds));
        Assert.DoesNotContain(expansion.Steps, step => step.Operation.Value == "child.guarded");
        Assert.DoesNotContain(graph.Diagnostics, diagnostic => diagnostic.Code == "SC011");
        Assert.DoesNotContain(
            DocumentationPlanner.Plan(ScenarioTestFactory.WithExactOwnerWording(graph)).Diagram.Diagnostics,
            diagnostic => diagnostic.Code == "DP002");
        foreach (var descendant in expansion.Steps.Where(step => step.Depth > 1))
        {
            Assert.Contains(graph.Topology.Memberships, membership => membership.ScenarioNode == descendant.ScenarioNodeId
                && rootGuarded.RootArmIds.Contains(membership.Arm));
        }
        Assert.Contains(expansion.Diagnostics, diagnostic => diagnostic.Code == "SC-DIRECT-GUARDED");
    }
}
