using System.Collections.Immutable;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Configuration;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Core.ScenarioGraph;
using Xunit;

namespace SeqDoc.Scenarios.Tests;

/// <summary>
/// A compiler-shaped, Roslyn-neutral fixture for the bounded traversal checkpoints. The graph is deliberately produced by
/// ScenarioGraphBuilder; this fixture only supplies the joined Program Index, Method Flow, and
/// Call Graph facts that the product normally receives from the earlier pipeline stages.
/// </summary>
internal static class DirectExactTraversalFixture
{
    internal static readonly MethodId SharedCallee = Method("Shared");
    internal static readonly ScenarioArmId RootTrueArm = new("scenario-arm:v1:fixture.root:true");
    internal static readonly string[] ExpectedSharedDescendantOrder = ["root.first", "shared.first", "root.second", "shared.second"];

    internal static ScenarioGraph BuildGraph(string partition, DiagramBudget? budget = null)
    {
        var request = CreateRequest(partition, budget);
        return Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);
    }

    internal static object Plan(ScenarioGraph graph) =>
        DocumentationPlanner.Plan(graph).Diagram.DebugProjection;

    internal static string[] ExpectedCycleSites(string partition) =>
        partition == "direct-recursion" ? ["self.call"] : [];

    private static ScenarioAnalysisRequest CreateRequest(string partition, DiagramBudget? budget = null)
    {
        var root = Method("Root");
        var child = Method("Child");
        var grandchild = Method("Grandchild");
        var shared = SharedCallee;
        var leaf = Method("Leaf");
        var foreignProject = new ProjectId("project:v1:foreign");
        var project = new ProjectId("project:v1:fixture");
        var methods = new List<MethodSpec>
        {
            new(root, "Root", project), new(child, "Child", partition == "foreign-project" ? foreignProject : project), new(grandchild, "Grandchild", project),
            new(shared, "Shared", project), new(leaf, "Leaf", project),
        };

        var calls = new Dictionary<MethodId, List<CallSpec>>
        {
            [root] = [new("root.first", child), new("root.second", shared)],
            [child] = [new("child.first", grandchild)],
            [grandchild] = [],
            [shared] = [],
            [leaf] = [],
        };

        if (partition is "deep-chain" or "deep-chain-reversed")
        {
            const int chainLength = 1024;
            var chain = Enumerable.Range(0, chainLength)
                .Select(itemIndex => Method($"Chain{itemIndex:D3}"))
                .ToArray();
            methods.AddRange(chain.Select((method, itemIndex) => new MethodSpec(method, $"Chain{itemIndex:D3}", project)));
            calls[root] = [new CallSpec("chain.000", chain[0])];
            for (var chainIndex = 0; chainIndex < chain.Length - 1; chainIndex++)
            {
                calls[chain[chainIndex]] = [new CallSpec($"chain.{chainIndex + 1:D3}", chain[chainIndex + 1])];
            }
            calls[chain[^1]] = [];
        }

        switch (partition)
        {
            case "depth-three":
                calls[grandchild] = [new("grandchild.first", leaf)];
                break;
            case "direct-recursion":
                calls[root] = [new("self.call", root)];
                break;
            case "mutual-recursion":
                calls[child] = [new("mutual.back", root)];
                break;
            case "shared-callee":
                calls[root] = [new("root.first", shared), new("root.second", shared)];
                calls[shared] = [new("shared.first", leaf)];
                break;
            case "duplicate-agreeing":
            case "duplicate-disagreeing":
                calls[root] = [calls[root][0]];
                break;
        }

        var boundary = partition switch
        {
            "body-unavailable" => child,
            _ => (MethodId?)null,
        };
        if (boundary is { } boundaryMethod)
        {
            var methodIndex = methods.FindIndex(item => item.Id == boundaryMethod);
            methods[methodIndex] = methods[methodIndex] with
            {
                BodyFingerprint = null,
            };
        }

        var rejected = new HashSet<string>(StringComparer.Ordinal);
        if (partition is "cha" or "incomplete" or "ambiguous" or "platform" or "dynamic" or "delegate" or "constructor" or "nested-function")
        {
            rejected.Add("root.first");
            rejected.Add("operation:v1:root.first");
            calls[root] = [calls[root][0]];
        }

        var profile = ScenarioTestFactory.Profile;
        var evidence = SourceEvidence("ct6-fixture");
        var index = CreateIndex(profile, project, foreignProject, methods, partition);
        var flows = methods.Select(spec => CreateFlow(spec, calls[spec.Id], rejected, partition, evidence)).ToImmutableArray();
        if (partition == "no-flow")
        {
            flows = flows.Where(flow => flow.Method != child).ToImmutableArray();
        }
        else if (partition == "ambiguous-flow")
        {
            flows = flows.Add(flows.Single(flow => flow.Method == child) with { FlowFingerprint = "ambiguous-flow-copy" });
        }
        var sites = flows.SelectMany(flow => flow.Nodes.OfType<InvocationFlowNode>().Select(invocation =>
        {
            var target = invocation.Target!.Value;
            var resolution = new CallTargetResolution(
                rejected.Contains(invocation.Operation.Value) ? CallResolutionKind.Cha : CallResolutionKind.DirectExact,
                [target], "source", IsComplete: partition != "incomplete",
                [], [evidence], CertaintyLevel.Exact);
            return new CallSite(new($"call-site:v1:{invocation.Method.Value}:{invocation.Operation.Value}"),
                invocation.Method, invocation.Operation, CallKind.Instance, target, resolution, [evidence], CertaintyLevel.Exact);
        })).GroupBy(site => (site.ContainingMethod, site.InvocationOperation))
            .Select(group => group.OrderBy(site => site.Id.Value, StringComparer.Ordinal).First())
            .ToImmutableArray();
        var behaviorProfile = partition == "foreign-behavior-profile" ? ScenarioTestFactory.ForeignProfile : profile;
        var behaviorFingerprint = partition == "foreign-behavior-fingerprint"
            ? "foreign-behavior-program-index"
            : index.IndexFingerprint;
        var behavior = new BehaviorSnapshot(1, "ct6-test", behaviorProfile, behaviorFingerprint, flows,
            new CallGraph(sites.Select(site => new CallGraphEdge(site.ContainingMethod, site.Id, site.Resolution.Candidates[0])).ToImmutableArray(), sites),
            new RtaFoundation([], true), [], [], "ct6-behavior");

        var baseRequest = ScenarioTestFactory.CreateGetRequest();
        var result = baseRequest with
        {
            Profile = profile,
            ProgramIndex = index,
            Behavior = behavior,
            FrameworkFacts = new FrameworkAnalysisResult(true, [], [], [], [], [], []),
            ConfiguredRoots = [root],
            DiagramBudget = budget,
        };
        if (partition.EndsWith("-reversed", StringComparison.Ordinal))
        {
            result = result with
            {
                Behavior = result.Behavior with
                {
                    MethodFlows = result.Behavior.MethodFlows.Reverse()
                        .Select(flow => flow with
                        {
                            Nodes = flow.Nodes.Reverse().ToImmutableArray(),
                            Edges = flow.Edges.Reverse().ToImmutableArray(),
                            ControlDependences = flow.ControlDependences.Reverse().ToImmutableArray(),
                        }).ToImmutableArray(),
                    CallGraph = result.Behavior.CallGraph with
                    {
                        CallSites = result.Behavior.CallGraph.CallSites.Reverse().ToImmutableArray(),
                        Edges = result.Behavior.CallGraph.Edges.Reverse().ToImmutableArray(),
                    },
                },
                ConfiguredRoots = result.ConfiguredRoots.Reverse().ToImmutableArray(),
            };
        }
        return result;
    }

    private static MethodFlowSnapshot CreateFlow(MethodSpec method, List<CallSpec> calls,
        HashSet<string> rejected, string partition, EvidenceRef evidence)
    {
        var entry = new EntryFlowNode(new($"flow-node:v1:{method.Id.Value}:entry"), method.Id, [evidence], CertaintyLevel.Exact);
        var exit = new ExitFlowNode(new($"flow-node:v1:{method.Id.Value}:exit"), method.Id, [evidence], CertaintyLevel.Exact);
        var nodes = new List<FlowNode> { entry, exit };
        var edges = new List<FlowEdge>();
        var dependences = new List<ControlDependence>();
        DecisionFlowNode? decision = null;
        if (method.Id == Method("Root") && partition == "inherited-arm-and-guarded-child")
        {
            decision = new DecisionFlowNode(new($"flow-node:v1:{method.Id.Value}:decision"), method.Id,
                new("operation:v1:fixture.guard"), [evidence], CertaintyLevel.Exact);
            nodes.Add(decision);
            edges.Add(Edge(method.Id, entry, decision, FlowEdgeKind.Normal, evidence));
        }

        foreach (var (call, ordinal) in calls.Select((call, ordinal) => (call, ordinal)))
        {
            var invocation = new InvocationFlowNode(new($"flow-node:v1:{method.Id.Value}:{call.Operation}"), method.Id,
                new($"operation:v1:{call.Operation}"), call.Target, false, false, false, false,
                rejected.Contains(call.Operation) && partition == "dynamic", [evidence], CertaintyLevel.Exact,
                $"Fixture.{call.Target.Value.Split('.').Last()}", call.Operation,
                partition == "nested-function" && rejected.Contains(call.Operation), true,
                partition is not ("unloaded-project" or "metadata-target"), ordinal, 0, "Fixture", partition == "platform" && rejected.Contains(call.Operation));
            if (method.Id == Method("Root") && call.Operation == "root.first" && partition.StartsWith("sensitive-", StringComparison.Ordinal))
            {
                invocation = invocation with
                {
                    ConstantArguments = [new CompilerProvenArgument(0, "System.String", SensitiveValue(partition))]
                };
            }
            if (partition == "delegate" && rejected.Contains(call.Operation))
            {
                invocation = invocation with { IsDelegateOrEventInvoke = true };
            }
            if (partition == "constructor" && rejected.Contains(call.Operation))
            {
                invocation = invocation with { IsConstructor = true };
            }
            nodes.Add(invocation);
            if (decision is not null && ordinal == 0)
            {
                dependences.Add(new ControlDependence(decision.Id, invocation.Id, true, [evidence], CertaintyLevel.Exact));
                edges.Add(Edge(method.Id, decision, invocation, FlowEdgeKind.True, evidence));
                edges.Add(Edge(method.Id, invocation, exit, FlowEdgeKind.Normal, evidence));
            }
            else if (decision is not null)
            {
                edges.Add(Edge(method.Id, decision, invocation, FlowEdgeKind.False, evidence));
            }
        }
        if (method.Id == Method("Root") && partition is "duplicate-agreeing" or "duplicate-disagreeing")
        {
            var original = nodes.OfType<InvocationFlowNode>().First();
            nodes.Add(original with
            {
                Id = new FlowNodeId($"flow-node:v1:{method.Id.Value}:duplicate"),
                Target = partition == "duplicate-disagreeing" ? Method("Grandchild") : original.Target,
            });
            nodes.Add(new AwaitFlowNode(new($"flow-node:v1:{method.Id.Value}:await"), method.Id,
                original.Operation, [evidence], CertaintyLevel.Exact));
        }
        edges.Add(Edge(method.Id, nodes[^1], exit, FlowEdgeKind.Normal, evidence));
        if (method.Id == Method("Child") && partition == "inherited-arm-and-guarded-child" && nodes.OfType<InvocationFlowNode>().SingleOrDefault() is { } guarded)
        {
            dependences.Add(new ControlDependence(new($"flow-node:v1:{method.Id.Value}:local-decision"), guarded.Id, true, [evidence], CertaintyLevel.Exact));
        }
        var flowFingerprint = method.BodyFingerprint ?? "flow-body";
        return new MethodFlowSnapshot(method.Id, flowFingerprint, nodes.ToImmutableArray(), edges.ToImmutableArray(), [], [],
            new LocalValueGraph([], []), dependences.ToImmutableArray(), null, [], $"flow:{method.BodyFingerprint}");
    }

    private static ProgramIndexSnapshot CreateIndex(CompilationProfile profile, ProjectId project, ProjectId foreign,
        List<MethodSpec> methods, string partition)
    {
        var projects = ImmutableArray.Create(
            new ProgramProject(project, "Fixture", "Fixture.csproj", profile.Id, profile.TargetFramework, ProjectKind.Library, "project", [], [SourceEvidence("project")]),
            new ProgramProject(foreign, "Foreign", "Foreign.csproj", profile.Id, profile.TargetFramework, ProjectKind.Library, "foreign", [], [SourceEvidence("foreign")]));
        var types = methods.Select(spec => new ProgramType(new($"symbol:v1:{spec.Id.Value}:type"), spec.Project,
            new SymbolId("symbol:v1:namespace"), $"Fixture.{spec.Name}", ProgramTypeKind.Class, null, [], "type", [SourceEvidence("type")]));
        var programMethods = methods.Select(spec => new ProgramMethod(spec.Id, new($"symbol:v1:{spec.Id.Value}"),
            new($"symbol:v1:{spec.Id.Value}:type"), spec.Name, $"Fixture.{spec.Name}()", [], "System.Void", "signature",
             spec.BodyFingerprint, [SourceEvidence("method", partition == "generated-target" && spec.Id == Method("Child"))])).ToImmutableArray();
        return new ProgramIndexSnapshot(1, "ct6-test", profile, projects, [], [], types.ToImmutableArray(), [], programMethods,
            [], [], [], [], [], "ct6-input", "ct6-index");
    }

    private static FlowEdge Edge(MethodId method, FlowNode source, FlowNode target, FlowEdgeKind kind, EvidenceRef evidence) =>
        new(new($"flow-edge:v1:{method.Value}:{source.Id.Value}:{target.Id.Value}:{kind}"), method, source.Id, target.Id, kind, null, [evidence], CertaintyLevel.Exact);

    private static MethodId Method(string name) => new($"method:v1:Fixture.{name}");
    private static string SensitiveValue(string partition) => partition switch
    {
        "sensitive-aws" => "AKIA" + "1234567890ABCDEF",
        "sensitive-github" => "ghp_test_credential_value",
        "sensitive-jwt" => "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3OCJ9.signaturevalue",
        "sensitive-openai" => "sk-test-credential-value-123",
        _ => "Abcdefghijklmnop1234",
    };
    private static EvidenceRef SourceEvidence(string value, bool generated = false) =>
        new(new($"evidence:v1:{value}"), generated ? EvidenceKind.GeneratedSource : EvidenceKind.Source,
            "ct6-fixture", null, value, null, CertaintyLevel.Exact);

    private sealed record MethodSpec(MethodId Id, string Name, ProjectId Project, string? BodyFingerprint = "body");
    private sealed record CallSpec(string Operation, MethodId Target);
}
