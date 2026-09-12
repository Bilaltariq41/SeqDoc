using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using SeqDoc.Analysis.Roslyn.Diagnostics;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Identity;
using StableProjectId = SeqDoc.Core.Identity.ProjectId;

namespace SeqDoc.Analysis.Roslyn.Workspace;

internal static class CompilationWorkspaceLoader
{
    public static async Task<(LoadedCompilationProfile? Loaded, ImmutableArray<SeqDoc.Core.Diagnostics.AnalysisDiagnostic> Diagnostics)>
        LoadAsync(CompilationAnalysisRequest request, CancellationToken cancellationToken)
    {
        var properties = MsBuildGlobalProperties.CreateForWorkspace(request.Profile);
        var workspace = MSBuildWorkspace.Create(properties);
        var workspaceDiagnostics = new ConcurrentQueue<WorkspaceDiagnostic>();
        workspace.RegisterWorkspaceFailedHandler(args => workspaceDiagnostics.Enqueue(args.Diagnostic));

        try
        {
            Solution solution;
            Microsoft.CodeAnalysis.ProjectId? rootProjectId = null;
            var extension = Path.GetExtension(request.TargetPath);
            if (string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase))
            {
                var project = await workspace.OpenProjectAsync(
                    request.TargetPath,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                solution = project.Solution;
                rootProjectId = project.Id;
            }
            else
            {
                solution = await workspace.OpenSolutionAsync(
                    request.TargetPath,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            var loadedProjects = ImmutableArray.CreateBuilder<LoadedProject>();
            var compilerErrors = new List<(Diagnostic Diagnostic, StableProjectId Project)>();
            foreach (var project in SelectProjects(solution, rootProjectId)
                         .OrderBy(project => project.FilePath, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (project.FilePath is null)
                {
                    workspaceDiagnostics.Enqueue(new WorkspaceDiagnostic(
                        WorkspaceDiagnosticKind.Failure,
                        $"Project '{project.Name}' has no physical project path."));
                    continue;
                }

                var relativePath = ToRepositoryRelativePath(request.RepositoryRoot, project.FilePath);
                var stableId = StableIdentity.CreateProjectId(request.Profile.Id, relativePath);
                var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
                if (compilation is null)
                {
                    workspaceDiagnostics.Enqueue(new WorkspaceDiagnostic(
                        WorkspaceDiagnosticKind.Failure,
                        $"Project '{project.Name}' did not produce a C# compilation."));
                    continue;
                }

                compilerErrors.AddRange(compilation.GetDiagnostics(cancellationToken)
                    .Where(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                    .Select(diagnostic => (diagnostic, stableId)));
                loadedProjects.Add(new LoadedProject(
                    project,
                    compilation,
                    stableId,
                    relativePath,
                    GetEvaluatedTargetFramework(project, compilation, project.Id == rootProjectId)));
            }

            var warningPolicy = new MsBuildWarningPolicy(properties);
            var convertedWorkspaceDiagnostics = CompilerDiagnosticFactory.CreateWorkspace(
                workspaceDiagnostics,
                request.Profile.Id,
                warningPolicy.IsPromoted,
                request.RepositoryRoot);
            var convertedCompilerDiagnostics = CompilerDiagnosticFactory.CreateCompiler(
                compilerErrors,
                request.Profile.Id,
                request.RepositoryRoot);
            var diagnostics = convertedWorkspaceDiagnostics
                .AddRange(convertedCompilerDiagnostics)
                .OrderBy(diagnostic => diagnostic.Id.Value, StringComparer.Ordinal)
                .ToImmutableArray();

            if (WorkspaceDiagnosticClassifier.HasFailure(workspaceDiagnostics, warningPolicy.IsPromoted)
                || compilerErrors.Count > 0)
            {
                workspace.Dispose();
                return (null, diagnostics);
            }

            return (new LoadedCompilationProfile(workspace, loadedProjects.ToImmutable(), diagnostics), diagnostics);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    internal static IEnumerable<Project> SelectProjects(
        Solution solution,
        Microsoft.CodeAnalysis.ProjectId? rootProjectId)
    {
        if (rootProjectId is null)
        {
            return solution.Projects
                .Where(project => string.Equals(project.Language, LanguageNames.CSharp, StringComparison.Ordinal));
        }

        var selected = new HashSet<Microsoft.CodeAnalysis.ProjectId>();
        var pending = new Stack<Microsoft.CodeAnalysis.ProjectId>();
        pending.Push(rootProjectId);
        while (pending.TryPop(out var projectId))
        {
            if (!selected.Add(projectId) || solution.GetProject(projectId) is not { } project)
            {
                continue;
            }

            foreach (var reference in project.ProjectReferences)
            {
                pending.Push(reference.ProjectId);
            }
        }

        return selected
            .Select(solution.GetProject)
            .Where(project => project is not null
                && string.Equals(project.Language, LanguageNames.CSharp, StringComparison.Ordinal))
            .Select(project => project!);
    }

    private static string ToRepositoryRelativePath(string repositoryRoot, string path)
    {
        var relativePath = Path.GetRelativePath(repositoryRoot, path);
        if (Path.IsPathRooted(relativePath)
            || relativePath.Equals("..", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Loaded project '{path}' is outside the selected repository root.");
        }

        return RepositoryRelativePath.Normalize(relativePath);
    }

    private static string? GetEvaluatedTargetFramework(
        Project project,
        Compilation compilation,
        bool isRootProject)
    {
        var evaluated = project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(
            "build_property.TargetFramework",
            out var targetFramework)
            && !string.IsNullOrWhiteSpace(targetFramework)
            ? targetFramework
            : null;
        if (isRootProject || evaluated is null)
        {
            return evaluated;
        }

        var compilerFramework = compilation.Assembly.GetAttributes()
            .Where(attribute => attribute.AttributeClass?.ToDisplayString() ==
                "System.Runtime.Versioning.TargetFrameworkAttribute")
            .Select(attribute => attribute.ConstructorArguments.FirstOrDefault().Value as string)
            .Select(moniker => (Moniker: moniker, Framework: ToTargetFramework(moniker)))
            .FirstOrDefault(item => item.Framework is not null);
        if (compilerFramework.Framework is null)
        {
            return evaluated;
        }

        var platform = compilation.Assembly.GetAttributes()
            .Where(attribute => attribute.AttributeClass?.ToDisplayString() ==
                "System.Runtime.Versioning.TargetPlatformAttribute")
            .Select(attribute =>
            {
                var version = attribute.ConstructorArguments.ElementAtOrDefault(1).Value as string
                    ?? attribute.NamedArguments
                        .FirstOrDefault(argument => argument.Key.Equals("Version", StringComparison.OrdinalIgnoreCase))
                        .Value.Value as string;
                return (
                    Name: attribute.ConstructorArguments.ElementAtOrDefault(0).Value as string,
                    Version: version);
            })
            .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Name));
        var localPlatform = evaluated is not null && evaluated.StartsWith(
            compilerFramework.Framework,
            StringComparison.OrdinalIgnoreCase)
            ? GetPlatformSuffix(evaluated)
            : null;
        var compilerPlatform = string.IsNullOrWhiteSpace(platform.Name)
            ? localPlatform
            : $"-{platform.Name!.ToLowerInvariant()}{(string.IsNullOrWhiteSpace(platform.Version) ? string.Empty : platform.Version)}";
        return compilerFramework.Framework + compilerPlatform;
    }

    private static string? ToTargetFramework(string? moniker)
    {
        if (moniker is null)
        {
            return null;
        }

        var versionMarker = ",Version=v";
        var versionIndex = moniker.IndexOf(versionMarker, StringComparison.OrdinalIgnoreCase);
        if (versionIndex < 0)
        {
            return null;
        }

        var version = moniker[(versionIndex + versionMarker.Length)..];
        var family = moniker[..versionIndex];
        if (family.Equals(".NETCoreApp", StringComparison.OrdinalIgnoreCase))
        {
            return $"net{version}";
        }

        if (family.Equals(".NETStandard", StringComparison.OrdinalIgnoreCase))
        {
            return $"netstandard{version}";
        }

        if (family.Equals(".NETFramework", StringComparison.OrdinalIgnoreCase))
        {
            return $"net{version.Replace(".", string.Empty, StringComparison.Ordinal)}";
        }

        return null;
    }

    private static string? GetPlatformSuffix(string framework) =>
        framework.IndexOf('-') is var separator && separator >= 0
            ? framework[separator..].ToLowerInvariant()
            : null;

    internal static string? CanonicalTargetFramework(
        string moniker,
        string? platform,
        string? platformVersion,
        string? projectFramework) =>
        ToTargetFramework(moniker) is { } framework
            ? framework + (string.IsNullOrWhiteSpace(platform)
                ? projectFramework is not null && projectFramework.StartsWith(framework, StringComparison.OrdinalIgnoreCase)
                    ? GetPlatformSuffix(projectFramework)
                    : null
                : $"-{platform.ToLowerInvariant()}{platformVersion ?? string.Empty}")
            : projectFramework;
}
