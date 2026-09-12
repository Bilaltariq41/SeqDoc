using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using SeqDoc.Analysis.Roslyn.Diagnostics;
using SeqDoc.Analysis.Roslyn.Workspace;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using Xunit;
using CoreDiagnosticSeverity = SeqDoc.Core.Diagnostics.DiagnosticSeverity;
using CoreProjectId = SeqDoc.Core.Identity.ProjectId;

namespace SeqDoc.Analysis.Tests;

public sealed class CompilerDiagnosticFactoryTests
{
    private static readonly CompilationProfileId Profile = new("profile:v1:test");
    private static readonly string[] ExpectedCodes = ["CS1001", "CS1002"];
    private static readonly string[] ExpectedLocations = ["a.cs(1,1)", "z.cs(1,1)"];

    [Theory]
    [InlineData("/repo/src/App.cs", "src/App.cs")]
    [InlineData("/repo-child/src/App.cs", "<external-path>")]
    [InlineData("/repo/../secret.cs", "<external-path>")]
    [InlineData("https://example.test/repo/src/App.cs", "https://example.test/repo/src/App.cs")]
    [InlineData("Package Example.Core 1.2.3", "Package Example.Core 1.2.3")]
    [InlineData("\\\\server\\share\\repo\\src\\App.cs", "<external-path>")]
    [InlineData("Unable to read /outside/cache while validating option (1,1)", "Unable to read <external-path> while validating option (1,1)")]
    [InlineData("Unable to read /repo/cache while validating option (1,1)", "Unable to read cache while validating option (1,1)")]
    [InlineData("Build failed for project \"/repo/Workspace With Spaces/src/App.cs\"; preserve prose.", "Build failed for project \"Workspace With Spaces/src/App.cs\"; preserve prose.")]
    [InlineData("Build failed for project \"/outside/Workspace With Spaces/src/App.cs\"; preserve prose.", "Build failed for project \"<external-path>\"; preserve prose.")]
    public void WorkspaceConfinementPreservesRawClassificationAndNonPathText(string message, string expectedCause)
    {
        var raw = new WorkspaceDiagnostic(WorkspaceDiagnosticKind.Failure, message);

        var diagnostic = Assert.Single(CompilerDiagnosticFactory.CreateWorkspace(
            [raw], Profile, repositoryRoot: "/repo"));

        Assert.Equal(CoreDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(AnalysisStage.WorkspaceLoad, diagnostic.Stage);
        Assert.Equal(expectedCause, diagnostic.TechnicalCause);
        Assert.Equal(CertaintyLevel.Exact, diagnostic.Certainty);
    }

    [Theory]
    [InlineData("C:\\repo", "C:\\repo\\src\\App.cs", "src/App.cs")]
    [InlineData("C:\\repo", "C:\\repo/src\\App.cs", "src/App.cs")]
    [InlineData("\\\\server\\share\\repo", "\\\\server\\share\\repo\\src\\App.cs", "src/App.cs")]
    public void WindowsAndMixedSeparatorInternalPathsRemainRepositoryRelative(
        string repositoryRoot, string message, string expectedCause)
    {
        var diagnostic = Assert.Single(CompilerDiagnosticFactory.CreateWorkspace(
            [new WorkspaceDiagnostic(WorkspaceDiagnosticKind.Failure, message)],
            Profile,
            repositoryRoot: repositoryRoot));

        Assert.Equal(expectedCause, diagnostic.TechnicalCause);
    }

    [Fact]
    public void CompilerConfinementChangesOnlyPublicPathFields()
    {
        var diagnostics = new[]
        {
            (CreateCompilerDiagnostic("/repo/src/App.cs", "CS1002", "first"), new CoreProjectId("project:v1:test")),
            (CreateCompilerDiagnostic("/outside/src/App.cs", "CS1003", "second"), new CoreProjectId("project:v1:test")),
            (CreateCompilerDiagnostic("/repo/Workspace With Spaces/src/App.cs", "CS1004", "quoted \"compiler prose\""), new CoreProjectId("project:v1:test")),
            (CreateCompilerDiagnostic("/outside/Workspace With Spaces/src/App.cs", "CS1005", "external compiler prose"), new CoreProjectId("project:v1:test")),
        };

        var converted = CompilerDiagnosticFactory.CreateCompiler(
            diagnostics, Profile, repositoryRoot: "/repo");

        Assert.Equal(4, converted.Length);
        Assert.Equal("src/App.cs(1,1)", converted[0].Location.Description);
        Assert.Equal(ConfineCompilerText(diagnostics[0].Item1, "/repo/src/App.cs", "src/App.cs"), converted[0].TechnicalCause);
        Assert.Equal("<external-path>(1,1)", converted[1].Location.Description);
        Assert.Equal(ConfineCompilerText(diagnostics[1].Item1, "/outside/src/App.cs", "<external-path>"), converted[1].TechnicalCause);
        Assert.Equal("first", converted[0].Summary);
        Assert.Equal("Workspace With Spaces/src/App.cs(1,1)", converted[2].Location.Description);
        Assert.Equal(ConfineCompilerText(diagnostics[2].Item1, "/repo/Workspace With Spaces/src/App.cs", "Workspace With Spaces/src/App.cs"), converted[2].TechnicalCause);
        Assert.Equal("<external-path>(1,1)", converted[3].Location.Description);
        Assert.Equal(ConfineCompilerText(diagnostics[3].Item1, "/outside/Workspace With Spaces/src/App.cs", "<external-path>"), converted[3].TechnicalCause);
        Assert.Equal("quoted \"compiler prose\"", converted[2].Summary);
        Assert.All(converted, diagnostic =>
        {
            Assert.Equal(CoreDiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal(CertaintyLevel.Exact, diagnostic.Certainty);
        });
    }

    [Fact]
    public void CompilerOrderingIdentityAndDistinctDiagnosticsUseRawValues()
    {
        var first = CreateCompilerSet("/repo");
        var second = CreateCompilerSet("/relocated");

        var firstConverted = CompilerDiagnosticFactory.CreateCompiler(
            first, Profile, repositoryRoot: "/repo");
        var secondConverted = CompilerDiagnosticFactory.CreateCompiler(
            second, Profile, repositoryRoot: "/relocated");

        Assert.Equal(2, firstConverted.Length);
        Assert.NotEqual(firstConverted[0].Id, firstConverted[1].Id);
        Assert.Equal(firstConverted.Select(diagnostic => diagnostic.Id), secondConverted.Select(diagnostic => diagnostic.Id));
        Assert.Equal(firstConverted.Select(diagnostic => diagnostic.Code), secondConverted.Select(diagnostic => diagnostic.Code));
        Assert.Equal(ExpectedCodes, firstConverted.Select(diagnostic => diagnostic.Code));
        Assert.Equal(ExpectedLocations, firstConverted.Select(diagnostic => diagnostic.Location.Description));
    }

    private static Diagnostic CreateCompilerDiagnostic(string path, string id, string message)
    {
        var descriptor = new DiagnosticDescriptor(id, "title", message, "Compiler", Microsoft.CodeAnalysis.DiagnosticSeverity.Error, true);
        return Diagnostic.Create(
            descriptor,
            Location.Create(path, new TextSpan(0, 1), new LinePositionSpan(new(0, 0), new(0, 1))));
    }

    private static (Diagnostic Diagnostic, CoreProjectId Project)[] CreateCompilerSet(string root) =>
    [
        (CreateCompilerDiagnostic($"{root}/z.cs", "CS1002", "z message"), new CoreProjectId("project:v1:test")),
        (CreateCompilerDiagnostic($"{root}/a.cs", "CS1001", "a message"), new CoreProjectId("project:v1:test")),
    ];

    private static string ConfineCompilerText(Diagnostic diagnostic, string rawPath, string confinedPath) =>
        diagnostic.ToString().Replace(rawPath, confinedPath, StringComparison.Ordinal);
}
