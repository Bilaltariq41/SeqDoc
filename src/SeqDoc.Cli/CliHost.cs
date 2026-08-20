using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Analysis.Roslyn.Profiles;
using SeqDoc.Application.Analysis;
using SeqDoc.Application.Documentation;
using SeqDoc.Configuration;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Persistence.Sqlite;
using SeqDoc.Rendering.Markdown;

namespace SeqDoc.Cli;

public static class CliHost
{
    private static readonly JsonSerializerOptions BuildDiagnosticJsonOptions = new() { WriteIndented = true };

    private const string Help = """
        SeqDoc evidence-backed static analysis

        Commands:
          seqdoc analyze <solution-or-project> [options]
          seqdoc catalog <solution-or-project> [options]
          seqdoc inspect solution <solution-or-project> [options]

        Common options:
          --repository-root <path>  Logical repository root
          --config <path>           YAML configuration file
          --profile <name>          Named configuration profile
          --configuration <name>    Build configuration (default: Release)
          --framework <tfm>         Select one target framework
          --all-frameworks          Analyze/query every target framework separately
          --runtime <rid>           Runtime identifier
          --cache <path>             SQLite cache (default: <root>/.seqdoc/cache-v1.db)
          --output <path>            Generate evidence-backed documentation (analyze only)
          --entry <operation|id>     Generate exactly one flow by exact operation key or entry ID prefix
          --json                     Emit one versioned JSON document

        Catalog options:
          --kind <kind>              all, project, document, type, method, reference, invocation
          --query <text>             Case-insensitive free-text filter
          --id <prefix>              Exact case-sensitive unique ID prefix
        """;

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter standardOutput,
        TextWriter standardError,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        if (!CliArguments.TryParse(args, out var options, out string? parseError))
        {
            bool json = args.Contains("--json", StringComparer.Ordinal);
            var diagnostic = CreateDiagnostic(
                "SD4000",
                "The command line is invalid.",
                "command line",
                parseError!,
                "Run 'seqdoc --help' and correct the command.");
            if (json)
            {
                CliOutput.WriteJson(standardOutput, "unknown", ApplicationOutcome.InvalidInput, null, [diagnostic]);
            }
            else
            {
                CliOutput.WriteDiagnostics(standardError, [diagnostic]);
            }

            return 2;
        }

        if (options!.Command == CliCommand.Help)
        {
            standardOutput.WriteLine(Help);
            return 0;
        }

        string commandName = options.Command switch
        {
            CliCommand.Analyze => "analyze",
            CliCommand.Catalog => "catalog",
            CliCommand.InspectSolution => "inspect solution",
            _ => "help",
        };

        try
        {
            var paths = ResolvePaths(options);
            var configurationResult = await new YamlConfigurationResolver().ResolveAsync(
                new ConfigurationResolutionRequest(
                    paths.ConfigurationFile,
                    options.Profile,
                    new PassAConfigurationOverrides(
                        Configuration: options.Configuration,
                        TargetFramework: options.TargetFramework,
                        RuntimeIdentifier: options.RuntimeIdentifier)),
                cancellationToken).ConfigureAwait(false);
            if (!configurationResult.IsSuccess)
            {
                return WriteResult(
                    standardOutput,
                    standardError,
                    options.Json,
                    commandName,
                    configurationResult.Outcome,
                    null,
                    configurationResult.Diagnostics);
            }

            var configuration = configurationResult.Value!;
            if (options.AllFrameworks && configuration.RootsSpecified)
            {
                var diagnostic = CreateDiagnostic(
                    "SD4012",
                    "Configured method roots cannot be used with --all-frameworks.",
                    "selection.roots",
                    "Root ownership is scoped to one selected compilation profile in this checkpoint.",
                    "Select one framework or omit selection.roots.");
                return WriteResult(standardOutput, standardError, options.Json, commandName,
                    ApplicationOutcome.InvalidInput, null, [diagnostic]);
            }
            var request = new CompilationProfileResolutionRequest(
                paths.RepositoryRoot,
                paths.TargetPath,
                configuration.Configuration.Value,
                configuration.TargetFramework.Value,
                options.AllFrameworks,
                configuration.RuntimeIdentifier.Value,
                Flatten(configuration.MsBuildProperties),
                BuildAnalysisProperties(configuration),
                configuration.MaxParallelism.Value);

            if (options.Command == CliCommand.Analyze)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(paths.CachePath)!);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    var diagnostic = CreateDiagnostic(
                        "SD4006",
                        "The cache directory could not be prepared.",
                        DisplayPath(paths.RepositoryRoot, paths.CachePath),
                        exception.Message,
                        "Choose a writable cache path and run SeqDoc again.",
                        exception);
                    return WriteResult(
                        standardOutput,
                        standardError,
                        options.Json,
                        commandName,
                        ApplicationOutcome.PersistenceFailure,
                        null,
                        [diagnostic]);
                }
            }

            var aggregateBuilder = new AggregateAnalysisBuilder();
            aggregateBuilder.ConfigureRoots(configuration.Roots.Value);
            var workflow = new PassAWorkflow(
                new MsBuildCompilationProfileResolver(),
                new RoslynProgramIndexBuilder(),
                new SqliteProgramIndexStore(paths.CachePath),
                new SqliteAnalysisStore(paths.CachePath),
                aggregateBuilder);

            return options.Command switch
            {
                CliCommand.Analyze => await RunAnalyzeAsync(
                    workflow, request, configuration, paths, options.OutputPath, options.Entry, options.Json, standardOutput, standardError, cancellationToken).ConfigureAwait(false),
                CliCommand.Catalog => await RunCatalogAsync(
                    workflow, request, configuration, options, paths, standardOutput, standardError, cancellationToken).ConfigureAwait(false),
                CliCommand.InspectSolution => await RunInspectAsync(
                    workflow, request, configuration, paths, options.Json, standardOutput, standardError, cancellationToken).ConfigureAwait(false),
                _ => 2,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return WriteResult(
                standardOutput,
                standardError,
                options.Json,
                commandName,
                ApplicationOutcome.Cancelled,
                null,
                []);
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or ArgumentException
                                          or InvalidOperationException)
        {
            var diagnostic = CreateDiagnostic(
                "SD4004",
                "The command could not be completed.",
                options.TargetPath ?? "command line",
                exception.Message,
                "Correct the path or command options, then run SeqDoc again.",
                exception);
            return WriteResult(
                standardOutput,
                standardError,
                options.Json,
                commandName,
                ApplicationOutcome.InvalidInput,
                null,
                [diagnostic]);
        }
    }

    private static async Task<int> RunAnalyzeAsync(
        PassAWorkflow workflow,
        CompilationProfileResolutionRequest request,
        ResolvedPassAConfiguration configuration,
        ResolvedPaths paths,
        string? outputPath,
        string? entry,
        bool json,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var result = await workflow.AnalyzeAsync(request, cancellationToken).ConfigureAwait(false);

        // Documentation generation is a separate post-analysis phase. It runs only after the
        // accepted snapshot activates successfully, and a generation failure never rolls back the
        // active analysis; it preserves prior documentation and marks it stale.
        DocumentationSummary? documentation = null;
        if (result.IsSuccess && outputPath is not null)
        {
            var generation = GenerateDocumentation(
                result.Value!, paths.RepositoryRoot, outputPath, entry,
                configuration.ExcludeParticipants?.Value ?? ImmutableSortedSet.Create<string>(StringComparer.Ordinal),
                configuration.ExcludeCalls?.Value ?? ImmutableSortedSet.Create<string>(StringComparer.Ordinal));
            if (generation.InvalidEntry)
            {
                // An unknown or ambiguous focused entry is user input error, never a documentation
                // generation failure; prior documentation is preserved untouched.
                object? invalidData = result.Value is null ? null : CreateAnalyzeData(result.Value, paths, configuration, null);
                return WriteResult(
                    output,
                    error,
                    json,
                    "analyze",
                    ApplicationOutcome.InvalidInput,
                    invalidData,
                    result.Diagnostics.Add(generation.Diagnostic!),
                    humanSuccessWritten: true);
            }

            if (generation.InvalidInput)
            {
                object? invalidData = result.Value is null ? null : CreateAnalyzeData(result.Value, paths, configuration, null);
                return WriteResult(
                    output, error, json, "analyze", ApplicationOutcome.InvalidInput, invalidData,
                    result.Diagnostics.Add(generation.Diagnostic!), humanSuccessWritten: true);
            }

            if (!generation.Succeeded)
            {
                // The active analysis activated successfully; a distinct documentation-generation
                // outcome reports the failure without rolling back analysis semantics. Previous
                // documentation is preserved and marked stale.
                object? failureData = result.Value is null ? null : CreateAnalyzeData(result.Value, paths, configuration, null);
                return WriteResult(
                    output,
                    error,
                    json,
                    "analyze",
                    ApplicationOutcome.DocumentationGenerationFailure,
                    failureData,
                    result.Diagnostics.Add(generation.Diagnostic!),
                    humanSuccessWritten: true);
            }

            documentation = generation.Summary;
        }

        object? data = result.Value is null ? null : CreateAnalyzeData(result.Value, paths, configuration, documentation);
        if (!json && result.IsSuccess)
        {
            WriteAnalyzeHuman(result.Value!, paths, configuration, output);
            if (documentation is not null)
            {
                output.WriteLine($"Documentation: {DisplayPath(paths.RepositoryRoot, documentation.OutputPath)}");
                foreach (string file in documentation.Files)
                {
                    output.WriteLine($"  {file}");
                }
            }
        }

        DiagnosticArtifact? artifact = null;
        if (result.Outcome == ApplicationOutcome.BuildFailure)
        {
            try
            {
                artifact = await WriteBuildDiagnosticsAsync(
                    paths.CachePath,
                    paths.RepositoryRoot,
                    result.Diagnostics,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // The primary build outcome remains authoritative if the secondary report cannot be written.
            }
        }

        return WriteResult(
            output,
            error,
            json,
            "analyze",
            result.Outcome,
            data,
            result.Diagnostics,
            humanSuccessWritten: true,
            artifact);
    }

    private static object CreateAnalyzeData(
        PassAAnalysisSummary summary,
        ResolvedPaths paths,
        ResolvedPassAConfiguration configuration,
        DocumentationSummary? documentation) => new
        {
            target = DisplayPath(paths.RepositoryRoot, paths.TargetPath),
            cachePath = DisplayPath(paths.RepositoryRoot, paths.CachePath),
            configuration = CreateConfigurationData(configuration),
            summary.ToolchainVersion,
            summary.AvailableTargetFrameworks,
            runs = summary.Runs.Select(run => new
            {
                profileId = run.ProfileId.Value,
                runId = run.RunId.Value,
                run.IndexFingerprint,
            }),
            summary.Counts,
            documentation,
        };

    private static void WriteAnalyzeHuman(
        PassAAnalysisSummary summary,
        ResolvedPaths paths,
        ResolvedPassAConfiguration configuration,
        TextWriter output)
    {
        output.WriteLine($"Target: {DisplayPath(paths.RepositoryRoot, paths.TargetPath)}");
        output.WriteLine($"Activated {summary.Runs.Length} Program Index profile(s).");
        output.WriteLine($"Toolchain: .NET SDK {summary.ToolchainVersion}");
        output.WriteLine($"Available frameworks: {string.Join(", ", summary.AvailableTargetFrameworks)}");
        output.WriteLine($"Cache: {DisplayPath(paths.RepositoryRoot, paths.CachePath)}");
        WriteConfiguration(output, configuration);
        foreach (var run in summary.Runs)
        {
            var count = summary.Counts.Single(item => item.ProfileId == run.ProfileId);
            output.WriteLine($"Profile: {run.ProfileId.Value}; run: {run.RunId.Value}; fingerprint: {run.IndexFingerprint}");
            output.WriteLine($"Counts: {count.Projects} project(s), {count.Documents} document(s), {count.Types} type(s), {count.Methods} method(s), {count.References} reference(s), {count.Invocations} invocation(s), {count.Diagnostics} diagnostic(s).");
        }
    }

    /// <summary>
    /// Generates documentation for every admitted flow of the analyzed profile (no Get-only filter)
    /// in operation-key then entry-id order, or exactly one flow when a focused entry is selected.
    /// The focused entry must be an exact operation key or an entry-ID prefix resolving to exactly one
    /// flow; unknown or ambiguous selections fail as InvalidInput without touching prior output.
    /// </summary>
    private static GenerationResult GenerateDocumentation(
        PassAAnalysisSummary summary, string repositoryRoot, string outputPath, string? entry,
        ImmutableSortedSet<string> excludeParticipants, ImmutableSortedSet<string> excludeCalls)
    {
        string absoluteOutput = Path.GetFullPath(outputPath, repositoryRoot);
        var graphs = summary.CompanionInspections
            .SelectMany(inspection => inspection.ScenarioGraphs.Graphs)
            .OrderBy(graph => graph.OperationKey, StringComparer.Ordinal)
            .ThenBy(graph => graph.EntryPoint.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        if (graphs.Length == 0)
        {
            OutputSetActivator.MarkStale(absoluteOutput);
            return GenerationResult.Failure(CreateGenerationDiagnostic(
                "SD4008",
                "No admitted flows were found for the analyzed profile; previous documentation is marked stale.",
                outputPath));
        }

        ScenarioGraphsForSelection(graphs, entry, out var selected, out var selectionDiagnostic);
        if (selectionDiagnostic is not null)
        {
            // An unknown or ambiguous focused entry is an input error; it never touches prior output
            // and never marks previously generated documentation stale.
            return GenerationResult.InvalidEntrySelection(selectionDiagnostic);
        }

        var entries = new List<DocumentSetEntry>();
        foreach (var graph in selected)
        {
            DocumentationPlan plan;
            try
            {
                plan = DocumentationPlanner.Plan(graph, excludeParticipants, excludeCalls);
            }
            catch (ArgumentException exception) when (exception.ParamName == "excludeParticipants")
            {
                return GenerationResult.InvalidDocumentationInput(CreateGenerationDiagnostic(
                    "SD4011",
                    "The documentation exclusion is invalid.",
                    outputPath,
                    exception.Message));
            }
            string fileName = DocumentationFileNaming.EntryKey(graph.EntryPoint, graph.OperationKey);
            entries.Add(new DocumentSetEntry(fileName, plan.Wording, plan.Diagram));
        }

        var contributing = summary.CompanionInspections
            .Where(inspection => inspection.ScenarioGraphs.Graphs.Any(graph => selected.Any(item => item.EntryPoint == graph.EntryPoint)))
            .OrderBy(inspection => inspection.ProfileId.Value, StringComparer.Ordinal)
            .FirstOrDefault();
        string profileId = contributing?.ProfileId.Value ?? string.Empty;
        string fingerprint = contributing?.ScenarioGraphs.ProgramIndexFingerprint ?? string.Empty;
        var built = DocumentationSetBuilder.Build(profileId, fingerprint, entries);
        if (!built.Succeeded)
        {
            OutputSetActivator.MarkStale(absoluteOutput);
            return GenerationResult.Failure(CreateGenerationDiagnostic(
                "SD4007",
                "Documentation generation failed; previous documentation is marked stale.",
                outputPath));
        }

        var activation = OutputSetActivator.Activate(absoluteOutput, built.Files);
        if (!activation.Succeeded)
        {
            return GenerationResult.Failure(CreateGenerationDiagnostic(
                "SD4007",
                "Documentation output activation failed; previous documentation is preserved and marked stale.",
                outputPath));
        }

        return GenerationResult.Success(new DocumentationSummary(
            DisplayPath(repositoryRoot, absoluteOutput),
            activation.WrittenFiles));
    }

    /// <summary>
    /// Selects the flows to document. Without a focused entry every admitted flow is selected. With a
    /// focused entry, exactly one flow must match an exact operation key or the exact readable entry
    /// key; duplicate matches are ambiguous, and entry-ID prefixes are never accepted (a unique prefix
    /// is still rejected as not exact). Zero and multiple matches produce the deterministic SD40XX
    /// InvalidInput diagnostic and select nothing.
    /// </summary>
    private static void ScenarioGraphsForSelection(
        ImmutableArray<ScenarioGraph> graphs,
        string? entry,
        out ImmutableArray<ScenarioGraph> selected,
        out AnalysisDiagnostic? diagnostic)
    {
        selected = graphs;
        diagnostic = null;
        if (entry is null)
        {
            return;
        }

        var exactKey = graphs
            .Where(graph => string.Equals(graph.OperationKey, entry, StringComparison.Ordinal))
            .ToArray();
        if (exactKey.Length == 1)
        {
            selected = exactKey.ToImmutableArray();
            return;
        }

        if (exactKey.Length > 1)
        {
            diagnostic = CreateGenerationDiagnostic(
                "SD4010",
                "The focused entry selection is ambiguous.",
                entry,
                $"The exact operation key matched {exactKey.Length} admitted flows.");
            selected = [];
            return;
        }

        // The stable entry key is the same readable identity the output set exposes as file names
        // (operation-key slug plus the entry-id suffix), so a focused entry can name one flow exactly.
        var exactEntryKey = graphs
            .Where(graph => string.Equals(
                DocumentationFileNaming.EntryKey(graph.EntryPoint, graph.OperationKey),
                entry,
                StringComparison.Ordinal))
            .ToArray();
        if (exactEntryKey.Length == 1)
        {
            selected = exactEntryKey.ToImmutableArray();
            return;
        }

        if (exactEntryKey.Length > 1)
        {
            diagnostic = CreateGenerationDiagnostic(
                "SD4010",
                "The focused entry selection is ambiguous.",
                entry,
                $"The exact entry key matched {exactEntryKey.Length} admitted flows.");
            selected = [];
            return;
        }

        // Entry-ID prefixes are never exact selections. A prefix matching several entry IDs is
        // reported as ambiguous; a prefix matching zero or one entry is unknown because the value was
        // neither an exact operation key nor an exact entry key.
        int prefixMatches = graphs.Count(graph => graph.EntryPoint.Value.StartsWith(entry, StringComparison.Ordinal));
        if (prefixMatches > 1)
        {
            diagnostic = CreateGenerationDiagnostic(
                "SD4010",
                "The focused entry selection is ambiguous.",
                entry,
                $"The entry-ID prefix matched {prefixMatches} admitted flows.");
        }
        else
        {
            diagnostic = CreateGenerationDiagnostic(
                "SD4009",
                "No admitted flow matches the focused entry selection.",
                entry,
                "The value was neither an exact operation key nor an exact entry key; entry-ID prefixes are not accepted.");
        }

        selected = [];
    }

    private static AnalysisDiagnostic CreateGenerationDiagnostic(string code, string summaryText, string outputPath, string? cause = null)
    {
        var id = StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            code,
            AnalysisStage.CommandLine,
            null,
            outputPath,
            0));
        return new AnalysisDiagnostic(
            id,
            code,
            DiagnosticSeverity.Error,
            AnalysisStage.CommandLine,
            summaryText,
            new DiagnosticLocation(outputPath),
            cause ?? "The analysis activated successfully, but the requested documentation could not be generated or activated.",
            "Previous documentation is preserved and marked stale; it may not match the active analysis.",
            "Correct the target, entry, or output path and run 'seqdoc analyze <target> --output <path>' again.",
            CertaintyLevel.Exact);
    }

    private sealed record DocumentationSummary(string OutputPath, ImmutableArray<string> Files);

    private sealed record GenerationResult(bool Succeeded, bool InvalidEntry, bool InvalidInput, DocumentationSummary? Summary, AnalysisDiagnostic? Diagnostic)
    {
        public static GenerationResult Success(DocumentationSummary summary) => new(true, false, false, summary, null);

        public static GenerationResult Failure(AnalysisDiagnostic diagnostic) => new(false, false, false, null, diagnostic);

        public static GenerationResult InvalidEntrySelection(AnalysisDiagnostic diagnostic) => new(false, true, false, null, diagnostic);

        public static GenerationResult InvalidDocumentationInput(AnalysisDiagnostic diagnostic) => new(false, false, true, null, diagnostic);
    }

    private static async Task<int> RunCatalogAsync(
        PassAWorkflow workflow,
        CompilationProfileResolutionRequest request,
        ResolvedPassAConfiguration configuration,
        CliArguments options,
        ResolvedPaths paths,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var result = await workflow.CatalogAsync(
            new CatalogQuery(request, options.CatalogKind, options.Query, options.IdPrefix),
            cancellationToken).ConfigureAwait(false);
        object? data = result.Value is null ? null : new
        {
            cachePath = DisplayPath(paths.RepositoryRoot, paths.CachePath),
            configuration = CreateConfigurationData(configuration),
            result.Value.Items,
        };
        if (!options.Json && result.IsSuccess)
        {
            foreach (var item in result.Value!.Items)
            {
                output.WriteLine($"{item.Kind}\t{item.Id}\t{item.Name}\t{item.Context}\t{item.Detail}\t{item.ProfileId.Value}");
            }

            output.WriteLine($"Cache: {DisplayPath(paths.RepositoryRoot, paths.CachePath)}");
            WriteConfiguration(output, configuration);
            output.WriteLine($"{result.Value.Items.Length} catalog item(s).");
        }

        return WriteResult(output, error, options.Json, "catalog", result.Outcome, data, result.Diagnostics, humanSuccessWritten: true);
    }

    private static async Task<int> RunInspectAsync(
        PassAWorkflow workflow,
        CompilationProfileResolutionRequest request,
        ResolvedPassAConfiguration configuration,
        ResolvedPaths paths,
        bool json,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var result = await workflow.InspectAsync(request, cancellationToken).ConfigureAwait(false);
        var inspectionDiagnostics = result.Value?.Profiles
            .SelectMany(profile => profile.Diagnostics)
            .ToImmutableArray() ?? [];
        var inspectionProjection = CliOutput.CreateProjection(inspectionDiagnostics, null, null);
        var displayedDiagnosticIds = inspectionProjection.Displayed
            .Select(diagnostic => diagnostic.Id)
            .ToHashSet();
        object? data = result.Value is null ? null : new
        {
            cachePath = DisplayPath(paths.RepositoryRoot, paths.CachePath),
            configuration = CreateConfigurationData(configuration),
            inspection = new
            {
                result.Value.TargetPath,
                result.Value.ToolchainVersion,
                result.Value.AvailableTargetFrameworks,
                profiles = result.Value.Profiles.Select(profile => new
                {
                    profile.ProfileId,
                    profile.RunId,
                    profile.TargetFramework,
                    profile.Configuration,
                    profile.SchemaVersion,
                    profile.ProducerVersion,
                    profile.InputManifestHash,
                    profile.IndexFingerprint,
                    profile.Counts,
                    behavior = profile.Behavior is null
                        ? null
                        : new
                        {
                            profile.Behavior.Available,
                            profile.Behavior.BehaviorFingerprint,
                            profile.Behavior.MethodFlows,
                            profile.Behavior.CallSites,
                            profile.Behavior.CallEdges,
                        },
                    profile.Projects,
                    diagnostics = profile.Diagnostics
                        .Where(diagnostic => displayedDiagnosticIds.Contains(diagnostic.Id))
                        .Select(CliOutput.ToCliDiagnostic)
                        .ToImmutableArray(),
                }),
                diagnosticOutput = inspectionProjection.Output,
            },
        };
        if (!json && result.IsSuccess)
        {
            output.WriteLine($"Target: {DisplayPath(paths.RepositoryRoot, paths.TargetPath)}");
            output.WriteLine($"Toolchain: .NET SDK {result.Value!.ToolchainVersion}");
            output.WriteLine($"Available frameworks: {string.Join(", ", result.Value.AvailableTargetFrameworks)}");
            output.WriteLine($"Cache: {DisplayPath(paths.RepositoryRoot, paths.CachePath)}");
            WriteConfiguration(output, configuration);
            foreach (var profile in result.Value.Profiles)
            {
                output.WriteLine($"Profile: {profile.ProfileId.Value}");
                output.WriteLine($"Run: {profile.RunId.Value}");
                output.WriteLine($"Framework: {profile.TargetFramework}; configuration: {profile.Configuration}; schema: {profile.SchemaVersion}; producer: {profile.ProducerVersion}; manifest: {profile.InputManifestHash}");
                output.WriteLine($"Fingerprint: {profile.IndexFingerprint}");
                if (profile.Behavior is { } behavior)
                {
                    output.WriteLine(behavior.Available
                        ? $"Behavior: available; flows: {behavior.MethodFlows}; call sites: {behavior.CallSites}; call edges: {behavior.CallEdges}"
                        : "Behavior: not available (reanalyze this profile)");
                }
                output.WriteLine($"Projects: {profile.Counts.Projects}; documents: {profile.Counts.Documents}; types: {profile.Counts.Types}; methods: {profile.Counts.Methods}; references: {profile.Counts.References}; invocations: {profile.Counts.Invocations}; diagnostics: {profile.Counts.Diagnostics}");
                foreach (var project in profile.Projects)
                {
                    output.WriteLine($"Project: {project.Id.Value}; {project.Name}; {project.Path}; {project.Status}");
                }

            }

            CliOutput.WriteDiagnostics(error, inspectionProjection, reportUnavailableArtifact: false);
            if (inspectionProjection.Output.OmittedCount > 0)
            {
                error.WriteLine($"Complete diagnostics remain in active cache: {DisplayPath(paths.RepositoryRoot, paths.CachePath)}");
            }
        }

        return WriteResult(output, error, json, "inspect solution", result.Outcome, data, result.Diagnostics, humanSuccessWritten: true);
    }

    private static int WriteResult(
        TextWriter output,
        TextWriter error,
        bool json,
        string command,
        ApplicationOutcome outcome,
        object? data,
        ImmutableArray<AnalysisDiagnostic> diagnostics,
        bool humanSuccessWritten = false,
        DiagnosticArtifact? artifact = null)
    {
        if (json)
        {
            CliOutput.WriteJson(output, command, outcome, data, diagnostics, artifact?.Path, artifact?.Sha256);
        }
        else
        {
            CliOutput.WriteDiagnostics(
                error,
                diagnostics.Where(diagnostic =>
                    outcome != ApplicationOutcome.Succeeded || diagnostic.Severity != DiagnosticSeverity.Info),
                artifact?.Path,
                artifact?.Sha256);
            if (outcome == ApplicationOutcome.Cancelled)
            {
                error.WriteLine("SeqDoc was cancelled; the previous active cache remains unchanged.");
            }
            else if (outcome == ApplicationOutcome.Succeeded && !humanSuccessWritten)
            {
                output.WriteLine("SeqDoc completed successfully.");
            }
        }

        return CliOutput.ExitCode(outcome);
    }

    private static ResolvedPaths ResolvePaths(CliArguments options)
    {
        string targetPath = Path.GetFullPath(options.TargetPath!);
        string targetDirectory = Directory.Exists(targetPath)
            ? targetPath
            : Path.GetDirectoryName(targetPath) ?? Environment.CurrentDirectory;
        string repositoryRoot = options.RepositoryRoot is null
            ? FindRepositoryRoot(targetDirectory)
            : Path.GetFullPath(options.RepositoryRoot);
        string cachePath = options.CachePath is null
            ? Path.Combine(repositoryRoot, ".seqdoc", "cache-v1.db")
            : Path.GetFullPath(options.CachePath, repositoryRoot);
        string? configurationFile = options.ConfigurationFile is null
            ? null
            : Path.GetFullPath(options.ConfigurationFile, repositoryRoot);
        return new ResolvedPaths(repositoryRoot, targetPath, cachePath, configurationFile);
    }

    private static string FindRepositoryRoot(string start)
    {
        var current = new DirectoryInfo(Path.GetFullPath(start));
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Path.GetFullPath(start);
    }

    private static ImmutableSortedDictionary<string, string> Flatten(
        ImmutableSortedDictionary<string, ResolvedConfigurationValue<string>> values) =>
        values.ToImmutableSortedDictionary(pair => pair.Key, pair => pair.Value.Value, values.KeyComparer);

    private static ImmutableSortedDictionary<string, string> BuildAnalysisProperties(
        ResolvedPassAConfiguration configuration)
    {
        var values = Flatten(configuration.KnownValues).ToBuilder();
        if (configuration.BinaryAnalysis.Value is not null)
        {
            values["seqdoc.binaryAnalysis"] = configuration.BinaryAnalysis.Value;
        }

        if (configuration.SourceLink.Value is not null)
        {
            values["seqdoc.sourceLink"] = configuration.SourceLink.Value;
        }

        return values.ToImmutable();
    }

    private static object CreateConfigurationData(ResolvedPassAConfiguration configuration) => new
    {
        configuration = configuration.Configuration,
        targetFramework = configuration.TargetFramework,
        runtimeIdentifier = configuration.RuntimeIdentifier,
        maxParallelism = configuration.MaxParallelism,
        binaryAnalysis = configuration.BinaryAnalysis,
        sourceLink = configuration.SourceLink,
        roots = configuration.Roots,
        msbuildProperties = configuration.MsBuildProperties,
        knownValues = configuration.KnownValues,
        diagramBudget = new
        {
            maxExpandedMethods = configuration.MaxExpandedMethods,
            maxExpandedCalls = configuration.MaxExpandedCalls,
            maxMaterialMessages = configuration.MaxMaterialMessages,
            maxParticipants = configuration.MaxParticipants,
            maxMermaidCharacters = configuration.MaxMermaidCharacters,
        },
    };

    private static void WriteConfiguration(TextWriter output, ResolvedPassAConfiguration configuration)
    {
        output.WriteLine($"Configuration: {configuration.Configuration.Value} ({configuration.Configuration.Provenance})");
        output.WriteLine($"Target framework: {configuration.TargetFramework.Value ?? "<automatic>"} ({configuration.TargetFramework.Provenance})");
        output.WriteLine($"Runtime identifier: {configuration.RuntimeIdentifier.Value ?? "<none>"} ({configuration.RuntimeIdentifier.Provenance})");
        output.WriteLine($"Maximum parallelism: {configuration.MaxParallelism.Value} ({configuration.MaxParallelism.Provenance})");
        output.WriteLine($"Binary analysis: {configuration.BinaryAnalysis.Value ?? "<default>"} ({configuration.BinaryAnalysis.Provenance})");
        output.WriteLine($"Source Link: {configuration.SourceLink.Value ?? "<default>"} ({configuration.SourceLink.Provenance})");
        output.WriteLine($"Maximum expanded methods: {configuration.MaxExpandedMethods.Value} ({configuration.MaxExpandedMethods.Provenance})");
        output.WriteLine($"Maximum expanded calls: {configuration.MaxExpandedCalls.Value} ({configuration.MaxExpandedCalls.Provenance})");
        output.WriteLine($"Maximum material messages: {configuration.MaxMaterialMessages.Value} ({configuration.MaxMaterialMessages.Provenance})");
        output.WriteLine($"Maximum participants: {configuration.MaxParticipants.Value} ({configuration.MaxParticipants.Provenance})");
        output.WriteLine($"Maximum Mermaid characters: {configuration.MaxMermaidCharacters.Value} ({configuration.MaxMermaidCharacters.Provenance})");
        output.WriteLine($"Configured roots: {string.Join(", ", configuration.Roots.Value)} ({configuration.Roots.Provenance})");
        foreach ((string key, ResolvedConfigurationValue<string> value) in configuration.MsBuildProperties)
        {
            output.WriteLine($"MSBuild property: {key}={value.Value} ({value.Provenance})");
        }

        foreach ((string key, ResolvedConfigurationValue<string> value) in configuration.KnownValues)
        {
            output.WriteLine($"Known value: {key}={value.Value} ({value.Provenance})");
        }
    }

    private static string DisplayPath(string repositoryRoot, string path)
    {
        string relative = Path.GetRelativePath(repositoryRoot, path);
        return relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            ? Path.GetFullPath(path)
            : RepositoryRelativePath.Normalize(relative);
    }

    private static async Task<DiagnosticArtifact> WriteBuildDiagnosticsAsync(
        string cachePath,
        string repositoryRoot,
        ImmutableArray<AnalysisDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        string path = Path.Combine(Path.GetDirectoryName(cachePath)!, "build-diagnostics.json");
        var data = new
        {
            schemaVersion = 1,
            diagnosticCount = diagnostics.Length,
            diagnostics = CliOutput.OrderDiagnostics(diagnostics).Select(CliOutput.ToCliDiagnostic).ToImmutableArray(),
        };
        byte[] content = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data, BuildDiagnosticJsonOptions));
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }

        return new DiagnosticArtifact(
            DisplayPath(repositoryRoot, path),
            Convert.ToHexStringLower(SHA256.HashData(content)));
    }

    private sealed record DiagnosticArtifact(string Path, string Sha256);

    private static AnalysisDiagnostic CreateDiagnostic(
        string code,
        string summary,
        string location,
        string cause,
        string nextAction,
        Exception? exception = null)
    {
        var id = StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            code,
            AnalysisStage.CommandLine,
            null,
            location,
            0));
        return new AnalysisDiagnostic(
            id,
            code,
            DiagnosticSeverity.Error,
            AnalysisStage.CommandLine,
            summary,
            new DiagnosticLocation(location),
            cause,
            "No analysis result was produced and the active cache was not changed.",
            nextAction,
            CertaintyLevel.Exact,
            internalDetail: exception?.GetType().FullName);
    }

    private sealed record ResolvedPaths(
        string RepositoryRoot,
        string TargetPath,
        string CachePath,
        string? ConfigurationFile);
}
