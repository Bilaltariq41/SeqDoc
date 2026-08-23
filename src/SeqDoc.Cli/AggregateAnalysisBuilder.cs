using System.Collections.Immutable;
using SeqDoc.Analysis.Behavior;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Application.Analysis;
using SeqDoc.Application.Persistence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Configuration;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.FrameworkModels;
using SeqDoc.FrameworkModels.AspNetCore;
using SeqDoc.FrameworkModels.EntityFramework;
using SeqDoc.FrameworkModels.FusionCache;
using SeqDoc.FrameworkModels.MediatR;

namespace SeqDoc.Cli;

/// <summary>
/// Composition-root builder that produces the aggregate Program Index, behavior snapshot, and
/// memory-only companion facts for one profile without leaking adapter types. It runs the accepted
/// ASP.NET Core controller model and the translation-alpha Entity Framework query model through
/// <see cref="FrameworkModelHost"/>, joins the evidence-backed scenario graphs, and returns an
/// additive <see cref="AnalysisProfileCandidate"/>; callers persist only the candidate snapshot and
/// never the companions.
/// </summary>
internal sealed class AggregateAnalysisBuilder : IAnalysisBuilder
{
    private readonly RoslynProfileAnalysisExtractor extractor;
    private readonly BehaviorAnalyzer analyzer;
    private readonly FrameworkModelHost host;
    private ImmutableArray<MethodId> configuredRoots = [];
    private DiagramBudget diagramBudget = DiagramBudget.Default;

    public void ConfigureRoots(ImmutableSortedSet<string> roots)
    {
        configuredRoots = roots.Select(value => new MethodId(value)).ToImmutableArray();
    }

    public void ConfigureDiagramBudget(DiagramBudget budget) => diagramBudget = budget ?? DiagramBudget.Default;

    public AggregateAnalysisBuilder()
    {
        extractor = new RoslynProfileAnalysisExtractor();
        analyzer = new BehaviorAnalyzer();
        host = new FrameworkModelHost(
        [
            new AspNetCoreControllerModel(),
            new AspNetCoreMinimalApiModel(),
            new EntityFrameworkQueryModel(),
            new FusionCacheGetOrSetModel(),
            new MediatRDispatchModel(),
        ]);
    }

    public async Task<ApplicationResult<AnalysisProfileCandidate>> BuildAsync(
        CompilationAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        var extraction = await extractor.ExtractAsync(request, cancellationToken).ConfigureAwait(false);
        if (!extraction.IsSuccess)
        {
            return ApplicationResult.Failure<AnalysisProfileCandidate>(extraction.Outcome, extraction.Diagnostics);
        }

        var artifacts = extraction.Value!.Artifacts;
        if (configuredRoots.Length > 0)
        {
            var methods = extraction.Value.ProgramIndex.Methods;
            var invalid = configuredRoots.FirstOrDefault(root => !IsWellFormed(root) || methods.All(method => method.Id != root));
            if (!string.IsNullOrEmpty(invalid.Value))
            {
                var diagnostic = CreateRootDiagnostic(request.Profile.Id, invalid.Value);
                return ApplicationResult.Failure<AnalysisProfileCandidate>(ApplicationOutcome.InvalidInput, [diagnostic]);
            }
        }
        var analysis = await analyzer.AnalyzeAsync(
            new BehaviorAnalysisRequest(extraction.Value.ProgramIndex, artifacts.BehaviorInput),
            cancellationToken).ConfigureAwait(false);
        var diagnostics = extraction.Diagnostics.AddRange(analysis.Diagnostics);
        var existingDiagnosticIds = diagnostics.Select(diagnostic => diagnostic.Id).ToHashSet();
        diagnostics = diagnostics.AddRange(
            artifacts.PredicateSemanticFacts.Diagnostics
                .Where(diagnostic => existingDiagnosticIds.Add(diagnostic.Id)));
        if (!analysis.IsSuccess)
        {
            return ApplicationResult.Failure<AnalysisProfileCandidate>(analysis.Outcome, diagnostics);
        }

        var framework = await host.AnalyzeAsync(
            new FrameworkAnalysisRequest(
                new FrameworkDetectionContext(request.Profile, extraction.Value.ProgramIndex),
                new FrameworkAnalysisContext(
                    request.Profile,
                    extraction.Value.ProgramIndex,
                    artifacts.CallbackBoundaryFacts),
                artifacts.Operations,
                artifacts.Symbols),
            cancellationToken).ConfigureAwait(false);
        diagnostics = diagnostics.AddRange(framework.Diagnostics);

        var scenarioGraphs = ScenarioGraphBuilder.Build(new ScenarioAnalysisRequest(
            request.Profile,
            extraction.Value.ProgramIndex,
            analysis.Value!,
            framework,
            artifacts.SemanticFacts,
            artifacts.DependencyInjectionFacts,
            artifacts.StructuralResultFacts,
            artifacts.NonGetSemanticFacts,
            artifacts.ConditionalDependencyInjectionFacts,
            artifacts.ConfigurationSemanticFacts,
            artifacts.CallbackBoundaryFacts,
            artifacts.PredicateSemanticFacts,
            artifacts.MinimalApiHandlerFacts,
             ConfiguredRoots: configuredRoots,
             DiagramBudget: diagramBudget));

        return ApplicationResult.Success(
            new AnalysisProfileCandidate(
                new AnalysisProfileSnapshot(extraction.Value.ProgramIndex, analysis.Value),
                artifacts.SemanticFacts,
                framework,
                artifacts.DependencyInjectionFacts,
                artifacts.StructuralResultFacts,
                scenarioGraphs,
                artifacts.ConfigurationSemanticFacts,
                artifacts.ConditionalDependencyInjectionFacts,
                artifacts.CallbackBoundaryFacts,
                artifacts.PredicateSemanticFacts),
            diagnostics);
    }

    private static bool IsWellFormed(MethodId id)
        => id.Value.StartsWith("method:v1:", StringComparison.Ordinal)
            && id.Value.Length == "method:v1:".Length + 64
            && id.Value["method:v1:".Length..].All(Uri.IsHexDigit);

    private static AnalysisDiagnostic CreateRootDiagnostic(CompilationProfileId profile, string root)
    {
        const string code = "SD4011";
        var id = StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            code, AnalysisStage.CommandLine, profile, root, 0));
        return new AnalysisDiagnostic(id, code, DiagnosticSeverity.Error, AnalysisStage.CommandLine,
            "A configured method root is invalid for the selected compilation profile.",
            new DiagnosticLocation("selection.roots"),
            $"The exact MethodId '{root}' is malformed, unknown, or does not belong to this profile.",
            "No analysis candidate was activated.",
            "Select an exact MethodId from the catalog for the selected profile.", CertaintyLevel.Exact);
    }
}
