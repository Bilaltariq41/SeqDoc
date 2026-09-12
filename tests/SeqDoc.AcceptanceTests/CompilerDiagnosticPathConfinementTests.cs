using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Application.Analysis;
using SeqDoc.Cli;
using SeqDoc.Core.Identity;
using Xunit;
using Xunit.Sdk;

namespace SeqDoc.AcceptanceTests;

public sealed class CompilerDiagnosticPathConfinementTests
{
    private const string Revision = "02b82a5115ef6e2d138c70670f28b959fb646f6e";

    [Fact]
    public async Task RelocatedWorkspaceCheckoutsConfineWarningsAcrossCliAndActiveSnapshot()
    {
        string source = ResolveCorpusRepository();
        string beforeStatus = Git(source, "status", "--short").Output;
        string beforeWorktrees = Git(source, "worktree", "list", "--porcelain").Output;
        string beforeConfig = Git(source, "config", "--local", "--list").Output;
        MetadataSnapshot beforeMetadata = CaptureMetadata(source);
        string first = NewTemp("seqdoc-i13-dp-workspace-a");
        string second = NewTemp("seqdoc-i13-dp-workspace-b");
        Exception? cleanupFailure = null;

        try
        {
            Assert.Equal(0, Git(source, "cat-file", "-e", Revision + "^{commit}").ExitCode);
            AddWorktree(source, first);
            AddWorktree(source, second);
            Restore(first, "CreditTransferWeb/CreditTransfer.csproj");
            Restore(second, "CreditTransferWeb/CreditTransfer.csproj");

            var a = await ObserveCliAsync(first, "CreditTransferWeb/CreditTransfer.csproj", "net9.0", expectSuccess: true);
            var b = await ObserveCliAsync(second, "CreditTransferWeb/CreditTransfer.csproj", "net9.0", expectSuccess: true);

            JsonElement[] aWarnings = WorkspaceWarnings(a.Diagnostics);
            JsonElement[] bWarnings = WorkspaceWarnings(b.Diagnostics);
            Assert.Equal(7, aWarnings.Length);
            Assert.Equal(7, bWarnings.Length);
            Assert.All(aWarnings, diagnostic =>
            {
                Assert.DoesNotContain(first, diagnostic.GetRawText(), StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(second, diagnostic.GetRawText(), StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("CreditTransferServices\\CreditTransferServices.csproj", diagnostic.GetProperty("location").GetString() ?? "", StringComparison.Ordinal);
            });
            Assert.All(aWarnings, diagnostic =>
            {
                string location = diagnostic.GetProperty("location").GetString() ?? "";
                string cause = diagnostic.GetProperty("technicalCause").GetString() ?? "";
                Assert.Contains("CreditTransferServices/CreditTransferServices.csproj", location + cause, StringComparison.Ordinal);
                Assert.NotEmpty(cause);
            });
            Assert.Contains(aWarnings, d => (d.GetProperty("technicalCause").GetString() ?? "").Contains("http", StringComparison.OrdinalIgnoreCase));

            Assert.Equal(Encoding.UTF8.GetBytes(a.DiagnosticsJson), Encoding.UTF8.GetBytes(b.DiagnosticsJson));
            Assert.Equal(a.ConsoleDiagnostics, b.ConsoleDiagnostics);
            Assert.DoesNotContain(first, a.ConsoleDiagnostics, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(second, a.ConsoleDiagnostics, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(first, b.ConsoleDiagnostics, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(second, b.ConsoleDiagnostics, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(IdsAndOrder(aWarnings), IdsAndOrder(bWarnings));
            Assert.Equal(a.PersistedDiagnosticsJson, b.PersistedDiagnosticsJson);
            Assert.Equal(Canonical(aWarnings), a.PersistedDiagnosticsJson);
            Assert.Equal(a.ProfileId, b.ProfileId);
            Assert.Equal(a.RunId, b.RunId);
            Assert.Equal(a.IndexFingerprint, b.IndexFingerprint);
            Assert.Equal(a.PackageSet, b.PackageSet);
            Assert.Contains("CoreWCF.Primitives/1.5.2", a.PackageSet, StringComparison.Ordinal);
            Assert.Equal(0, a.ExitCode);
            Assert.False(a.HasBuildArtifact);
            Assert.DoesNotContain(first, Canonical(aWarnings), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(second, Canonical(aWarnings), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            var cleanupFailures = new List<Exception>();
            foreach (string path in new[] { first, second })
            {
                try
                {
                    RemoveWorktree(source, path);
                }
                catch (Exception exception)
                {
                    cleanupFailures.Add(exception);
                }
            }
            try
            {
                Assert.Equal(beforeStatus, Git(source, "status", "--short").Output);
                Assert.Equal(beforeWorktrees, Git(source, "worktree", "list", "--porcelain").Output);
                Assert.Equal(beforeConfig, Git(source, "config", "--local", "--list").Output);
                Assert.Equal(beforeMetadata, CaptureMetadata(source));
                Assert.False(Directory.Exists(first));
                Assert.False(Directory.Exists(second));
            }
            catch (Exception exception) { cleanupFailures.Add(exception); }
            cleanupFailure = cleanupFailures.Count == 0 ? null : new AggregateException(cleanupFailures);
        }

        if (cleanupFailure is not null)
        {
            throw new XunitException("fixture worktree cleanup did not complete safely.", cleanupFailure);
        }
    }

    [Fact]
    public async Task RelocatedCompilerFixturesPreserveRawAndPublicFailureDiagnostics()
    {
        string first = NewTemp("seqdoc-i13-dp-compiler-a");
        string second = NewTemp("seqdoc-i13-dp-compiler-b");
        Exception? cleanupFailure = null;
        try
        {
            CreateBrokenFixture(first);
            CreateBrokenFixture(second);
            var a = await ObserveCompilerAsync(first);
            var b = await ObserveCompilerAsync(second);

            Assert.NotEmpty(a.ExtractedCompilerDiagnostics);
            Assert.Contains(a.ExtractedCompilerDiagnostics, diagnostic => diagnostic.Contains("MissingResult", StringComparison.Ordinal));
            Assert.NotEmpty(b.ExtractedCompilerDiagnostics);
            Assert.Equal(Encoding.UTF8.GetBytes(a.DiagnosticsJson), Encoding.UTF8.GetBytes(b.DiagnosticsJson));
            Assert.Equal(a.IdsAndOrder, b.IdsAndOrder);
            Assert.Equal(a.ArtifactBytesSha256, b.ArtifactBytesSha256);
            Assert.All(a.Diagnostics, diagnostic => Assert.Contains(Canonical([diagnostic]), DiagnosticRecords(a.ArtifactDiagnosticsJson)));
            Assert.All(b.Diagnostics, diagnostic => Assert.Contains(Canonical([diagnostic]), DiagnosticRecords(b.ArtifactDiagnosticsJson)));
            Assert.Equal(a.ConsoleDiagnostics, b.ConsoleDiagnostics);
            Assert.DoesNotContain(first, a.ConsoleDiagnostics, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(second, a.ConsoleDiagnostics, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(first, b.ConsoleDiagnostics, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(second, b.ConsoleDiagnostics, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(3, a.ExitCode);
            Assert.Equal(3, b.ExitCode);
            Assert.True(a.HasBuildArtifact);
            Assert.True(b.HasBuildArtifact);
            Assert.False(a.HasPersistedValidState);
            Assert.False(b.HasPersistedValidState);
            Assert.False(File.Exists(Path.Combine(first, ".seqdoc", "cache-v1.db")));
            Assert.All(a.Diagnostics, diagnostic =>
            {
                string raw = diagnostic.GetRawText();
                Assert.DoesNotContain(first, raw, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(second, raw, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(first, diagnostic.GetProperty("summary").GetString() ?? "", StringComparison.OrdinalIgnoreCase);
            });
            Assert.All(b.Diagnostics, diagnostic =>
            {
                Assert.DoesNotContain(second, diagnostic.GetRawText(), StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(second, diagnostic.GetProperty("summary").GetString() ?? "", StringComparison.OrdinalIgnoreCase);
            });
            Assert.Contains(a.Diagnostics, diagnostic => diagnostic.GetRawText().Contains("Broken.cs", StringComparison.Ordinal));
        }
        finally
        {
            var cleanupFailures = new List<Exception>();
            foreach (string path in new[] { first, second })
            {
                try { Delete(path); }
                catch (Exception exception) { cleanupFailures.Add(exception); }
            }
            if (cleanupFailures.Count > 0)
            {
                cleanupFailure = new AggregateException(cleanupFailures);
            }
            Assert.False(Directory.Exists(first));
            Assert.False(Directory.Exists(second));
        }
        if (cleanupFailure is not null)
        {
            throw new XunitException("fixture compiler-root cleanup did not complete safely.", cleanupFailure);
        }
    }

    private static async Task<CliObservation> ObserveCliAsync(string root, string relativeProject, string framework, bool expectSuccess)
    {
        string target = Path.Combine(root, relativeProject.Replace('/', Path.DirectorySeparatorChar));
        string cacheRoot = NewTemp("seqdoc-i13-dp-cache");
        Directory.CreateDirectory(cacheRoot);
        string cache = Path.Combine(cacheRoot, "cache-v1.db");
        try
        {
            var jsonOutput = new StringWriter();
            var jsonError = new StringWriter();
            int exitCode = await CliHost.RunAsync(["analyze", target, "--repository-root", root, "--configuration", "Release", "--framework", framework, "--cache", cache, "--json"], jsonOutput, jsonError);
            using var document = JsonDocument.Parse(jsonOutput.ToString());
            JsonElement rootJson = document.RootElement.Clone();
            Assert.True(exitCode == (expectSuccess ? 0 : 3), jsonOutput.ToString() + jsonError);
            var diagnostics = rootJson.GetProperty("diagnostics").EnumerateArray().Select(x => x.Clone()).ToArray();
            string artifactFile = Path.Combine(Path.GetDirectoryName(cache)!, "build-diagnostics.json");
            string artifactJson = File.Exists(artifactFile)
                ? Canonical(JsonDocument.Parse(File.ReadAllText(artifactFile)).RootElement.GetProperty("diagnostics").EnumerateArray().Select(x => x.Clone()))
                : "";
            string artifactHash = File.Exists(artifactFile) ? HashFile(artifactFile) : "";

            var consoleOutput = new StringWriter();
            var consoleError = new StringWriter();
            int consoleExitCode = await CliHost.RunAsync(["analyze", target, "--repository-root", root, "--configuration", "Release", "--framework", framework, "--cache", cache], consoleOutput, consoleError);
            Assert.Equal(exitCode, consoleExitCode);
            JsonElement[] consumerDiagnostics = diagnostics.Any(d => d.GetProperty("code").GetString() == "SD1101" && d.GetProperty("stage").GetString() == "WorkspaceLoad")
                ? diagnostics.Where(d => d.GetProperty("code").GetString() == "SD1101" && d.GetProperty("stage").GetString() == "WorkspaceLoad").ToArray()
                : diagnostics;
            string consoleText = consoleOutput.ToString();
            string consoleDiagnostics = ExtractDiagnosticBlocks(consumerDiagnostics, consoleText + consoleError);
            WorkspaceSnapshot persisted = expectSuccess
                ? await InspectDiagnosticsAsync(root, target, cache, framework)
                : WorkspaceSnapshot.Empty;
            bool hasPersistedValidState = !expectSuccess && File.Exists(cache)
                && await HasValidPersistedStateAsync(root, target, cache, framework);
            return new CliObservation(exitCode, diagnostics, rootJson.GetProperty("diagnostics").GetRawText(), artifactJson, artifactHash, persisted.DiagnosticsJson, consoleDiagnostics, IdsAndOrder(diagnostics), File.Exists(artifactFile), hasPersistedValidState, persisted.ProfileId, persisted.RunId, persisted.IndexFingerprint, expectSuccess ? PackageSet(root) : "");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Delete(cacheRoot);
        }
    }

    private static async Task<CompilerObservation> ObserveCompilerAsync(string root)
    {
        string relative = "DiagnosticFixture.csproj";
        string target = Path.Combine(root, relative);
        var extraction = await new RoslynProfileAnalysisExtractor().ExtractAsync(new CompilationAnalysisRequest(root, target, CompilationProfile.Create(relative, "Release", "net9.0")), CancellationToken.None);
        Assert.False(extraction.IsSuccess);
        var raw = extraction.Diagnostics.Where(d => d.Stage.ToString() == "CompilationValidation").Select(d => $"{d.Id.Value}|{d.Code}|{d.Location.Description}|{d.Summary}|{d.TechnicalCause}").ToArray();
        Assert.NotEmpty(raw);
        Assert.Contains(raw, diagnostic => diagnostic.Contains("MissingResult", StringComparison.Ordinal));
        var cli = await ObserveCliAsync(root, relative, "net9.0", expectSuccess: false);
        return new CompilerObservation(raw, cli.Diagnostics, cli.DiagnosticsJson, cli.ArtifactDiagnosticsJson, cli.ArtifactBytesSha256, cli.ConsoleDiagnostics, cli.IdsAndOrder, cli.ExitCode, cli.HasBuildArtifact, cli.HasPersistedValidState);
    }

    private static async Task<WorkspaceSnapshot> InspectDiagnosticsAsync(string root, string target, string cache, string framework)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int code = await CliHost.RunAsync(["inspect", "solution", target, "--repository-root", root, "--configuration", "Release", "--framework", framework, "--cache", cache, "--json"], output, error);
        Assert.Equal(0, code);
        using var document = JsonDocument.Parse(output.ToString());
        JsonElement profile = document.RootElement.GetProperty("data").GetProperty("inspection").GetProperty("profiles")[0];
        return new WorkspaceSnapshot(
            Canonical(WorkspaceWarnings(profile.GetProperty("diagnostics").EnumerateArray())),
            ScalarString(profile.GetProperty("profileId")),
            ScalarString(profile.GetProperty("runId")),
            profile.GetProperty("indexFingerprint").GetString()!,
            "");
    }

    private static string ScalarString(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? value.GetString()!
        : value.GetProperty("value").GetString()!;

    private static async Task<bool> HasValidPersistedStateAsync(string root, string target, string cache, string framework)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int code = await CliHost.RunAsync(["inspect", "solution", target, "--repository-root", root, "--configuration", "Release", "--framework", framework, "--cache", cache, "--json"], output, error);
        if (code != 0) { return false; }
        using var document = JsonDocument.Parse(output.ToString());
        return document.RootElement.GetProperty("data").GetProperty("inspection").GetProperty("profiles").GetArrayLength() > 0;
    }

    private static string PackageSet(string root)
    {
        var packages = new List<string>();
        foreach (string project in new[] { "CreditTransferWeb", "CreditTransferServices", "CreditTransferEngine" })
        {
            string assets = Path.Combine(root, project, "obj", "project.assets.json");
            Assert.True(File.Exists(assets), assets);
            using var document = JsonDocument.Parse(File.ReadAllText(assets));
            foreach (string package in document.RootElement.GetProperty("libraries").EnumerateObject().Select(property => property.Name))
            {
                packages.Add(project + "/" + package);
            }
        }
        return string.Join("|", packages.OrderBy(package => package, StringComparer.Ordinal));
    }

    private static string ResolveCorpusRepository()
    {
        string root = Environment.GetEnvironmentVariable("SEQDOC_TEST_PROJECTS_ROOT") is { Length: > 0 } value
            ? value
            : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "SeqDoc-TestProjects"));
        string repository = Path.Combine(root, "Provided", "CreditTransfer-om");
        Assert.True(Directory.Exists(repository), repository);
        return repository;
    }

    private static JsonElement[] WorkspaceWarnings(IEnumerable<JsonElement> diagnostics) => diagnostics
        .Where(diagnostic => diagnostic.GetProperty("code").GetString() == "SD1101"
            && diagnostic.GetProperty("stage").GetString() == "WorkspaceLoad")
        .ToArray();

    private static void CreateBrokenFixture(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(Path.Combine(root, "DiagnosticFixture.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(root, "src", "Broken.cs"), "namespace DiagnosticFixture; public sealed class Broken { public MissingResult Execute(UnknownRequest request) => request.CreateResult(); }");
    }

    private static void AddWorktree(string source, string path)
    {
        Directory.CreateDirectory(path);
        Delete(path);
        var result = Git(source, "worktree", "add", "--detach", path, Revision);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(Revision, Git(path, "rev-parse", "HEAD").Output.Trim());
    }

    private static void Restore(string root, string relativeProject)
    {
        ProcessResult result = RunProcess("dotnet", root, "restore", Path.Combine(root, relativeProject.Replace('/', Path.DirectorySeparatorChar)), "--nologo", "--verbosity", "quiet");
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
    }

    private static void RemoveWorktree(string source, string path)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        for (int attempt = 0; attempt < 20; attempt++)
        {
            ProcessResult removal = Git(source, "worktree", "remove", "--force", path);
            bool registered = Git(source, "worktree", "list", "--porcelain").Output.Contains(path, StringComparison.OrdinalIgnoreCase);
            if (!registered && Directory.Exists(path))
            {
                try
                {
                    foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }
                    foreach (string directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(directory, FileAttributes.Normal);
                    }
                    File.SetAttributes(path, FileAttributes.Normal);
                    Directory.Delete(path, recursive: true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            registered = Git(source, "worktree", "list", "--porcelain").Output.Contains(path, StringComparison.OrdinalIgnoreCase);
            if (!Directory.Exists(path) && !registered)
            {
                return;
            }
            if (removal.ExitCode != 0 && registered)
            {
                Thread.Sleep(500);
                continue;
            }
            Thread.Sleep(500);
        }
        throw new XunitException($"fixture worktree cleanup incomplete: {path}");
    }

    private static ProcessResult Git(string directory, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start git.");
        if (!process.WaitForExit(30000))
        {
            process.Kill(true);
            throw new TimeoutException("git timed out");
        }
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        return new ProcessResult(process.ExitCode, output, error);
    }

    private static ProcessResult RunProcess(string fileName, string directory, params string[] arguments)
    {
        var info = new ProcessStartInfo(fileName) { WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120000))
        {
            process.Kill(true);
            throw new TimeoutException($"{fileName} timed out");
        }

        return new ProcessResult(process.ExitCode, outputTask.GetAwaiter().GetResult(), errorTask.GetAwaiter().GetResult());
    }

    private static string NewTemp(string name) => Path.Combine(Path.GetTempPath(), $"{name}-{Guid.NewGuid():N}");

    private static MetadataSnapshot CaptureMetadata(string source)
    {
        string common = Git(source, "rev-parse", "--git-common-dir").Output.Trim();
        string worktrees = Path.GetFullPath(Path.Combine(source, common, "worktrees"));
        if (!Directory.Exists(worktrees))
        {
            return new MetadataSnapshot(false, []);
        }

        var files = Directory.EnumerateFiles(worktrees, "*", SearchOption.AllDirectories)
            .Select(file => new MetadataFile(Path.GetRelativePath(worktrees, file), HashFile(file)))
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .ToArray();
        return new MetadataSnapshot(true, files);
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void Delete(string path)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        for (int attempt = 0; attempt < 120 && Directory.Exists(path); attempt++)
        {
            try { Directory.Delete(path, true); }
            catch (IOException) when (attempt < 119) { Thread.Sleep(500); }
            catch (UnauthorizedAccessException) when (attempt < 119) { Thread.Sleep(500); }
        }
        if (Directory.Exists(path)) { throw new XunitException($"fixture-owned directory cleanup incomplete: {path}"); }
    }
    private static string Canonical(IEnumerable<JsonElement> values, params string[] roots) => JsonSerializer.Serialize(values.Select(value => NormalizeDiagnostic(value, roots)).ToArray());
    private static object? NormalizeDiagnostic(JsonElement value, string[] roots) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().ToDictionary(property => property.Name.ToLowerInvariant(), property => NormalizeDiagnostic(property.Value, roots)),
        JsonValueKind.Array => value.EnumerateArray().Select(item => NormalizeDiagnostic(item, roots)).ToArray(),
        JsonValueKind.String => roots.Aggregate(value.GetString()!, (text, root) => text.Replace(root, "<fixture-root>", StringComparison.OrdinalIgnoreCase)),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
    private static string IdsAndOrder(IEnumerable<JsonElement> values) => string.Join("|", values.Select(x => $"{x.GetProperty("id").GetString()}:{x.GetProperty("code").GetString()}"));
    private static string[] DiagnosticRecords(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray().Select(value => Canonical([value])).ToArray();
    }
    private static string ExtractDiagnosticBlocks(IEnumerable<JsonElement> values, string console)
    {
        var blocks = new List<string>();
        var search = 0;
        foreach (JsonElement value in values)
        {
            string[] lines =
            [
                $"{value.GetProperty("code").GetString()}: {value.GetProperty("summary").GetString()}",
                $"Location: {value.GetProperty("location").GetString()}",
                $"Cause: {value.GetProperty("technicalCause").GetString()}",
                $"Impact: {value.GetProperty("userImpact").GetString()}",
                $"Next action: {value.GetProperty("nextAction").GetString()}",
            ];
            int start = FindConsoleLine(console, lines[0], search);
            Assert.True(start >= 0, $"Console diagnostic line was not emitted: {lines[0]}");
            int end = start;
            foreach (string line in lines)
            {
                int lineStart = FindConsoleLine(console, line, end);
                Assert.True(lineStart >= end, $"Console diagnostic line was not emitted: {line}");
                end = EndOfLine(console, lineStart);
            }

            blocks.Add(console[start..end]);
            search = end;
        }

        return string.Concat(blocks);
    }

    private static int FindConsoleLine(string text, string expected, int start)
    {
        for (int position = text.IndexOf(expected, start, StringComparison.Ordinal);
             position >= 0;
             position = text.IndexOf(expected, position + expected.Length, StringComparison.Ordinal))
        {
            int lineStart = position == 0 ? 0 : text.LastIndexOf('\n', position - 1) + 1;
            int lineEnd = text.IndexOf('\n', position);
            if (lineEnd < 0) { lineEnd = text.Length; }
            if (position >= lineStart && lineEnd - position >= expected.Length)
            {
                return lineStart;
            }
        }

        return -1;
    }

    private static int EndOfLine(string text, int lineStart)
    {
        int newline = text.IndexOf('\n', lineStart);
        return newline < 0 ? text.Length : newline + 1;
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
    private sealed record WorkspaceSnapshot(string DiagnosticsJson, string ProfileId, string RunId, string IndexFingerprint, string PackageSet)
    {
        public static WorkspaceSnapshot Empty { get; } = new("", "", "", "", "");
    }
    private sealed record CliObservation(int ExitCode, JsonElement[] Diagnostics, string DiagnosticsJson, string ArtifactDiagnosticsJson, string ArtifactBytesSha256, string PersistedDiagnosticsJson, string ConsoleDiagnostics, string IdsAndOrder, bool HasBuildArtifact, bool HasPersistedValidState, string ProfileId, string RunId, string IndexFingerprint, string PackageSet);
    private sealed record CompilerObservation(string[] ExtractedCompilerDiagnostics, JsonElement[] Diagnostics, string DiagnosticsJson, string ArtifactDiagnosticsJson, string ArtifactBytesSha256, string ConsoleDiagnostics, string IdsAndOrder, int ExitCode, bool HasBuildArtifact, bool HasPersistedValidState);
    private sealed record MetadataSnapshot(bool DirectoryExists, MetadataFile[] Files);
    private sealed record MetadataFile(string Path, string Hash);
}
