using System.Collections.Immutable;
using System.Text.Json;
using SeqDoc.Core.Configuration;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Rendering.Markdown;
using Xunit;

namespace SeqDoc.Rendering.Tests;

public sealed class DiagramPlanRenderingTests : IDisposable
{
    private static readonly string[] OwnedFileNames =
    [
        "index.md",
        "get-api-test-1234abcd.md",
        "get-api-test-1234abcd.mmd",
        "seqdoc.manifest.json",
    ];

    private static readonly string[] ExpectedManifestPaths =
    [
        "get-api-test-1234abcd.md",
        "get-api-test-1234abcd.mmd",
        "index.md",
    ];

    private static readonly JsonSerializerOptions JournalJsonOptions = new() { WriteIndented = true };

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"seqdoc-render-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void RenderedOutputAndManifestAreDeterministicCanonicalAndPathFree()
    {
        var entry = new DocumentSetEntry(
            "get-api-test-1234abcd",
            PlanTestFactory.CreateWordingDocument(),
            PlanTestFactory.CreateDiagramPlan());
        var built = DocumentationSetBuilder.Build("profile:v1:test", "fingerprint", [entry]);
        Assert.True(built.Succeeded, string.Join("; ", built.Errors));
        Assert.NotEmpty(built.Files);

        string first = Path.Combine(_directory, "first");
        string second = Path.Combine(_directory, "second");
        var firstReport = OutputSetActivator.Activate(first, built.Files);
        var secondReport = OutputSetActivator.Activate(second, built.Files);
        Assert.True(firstReport.Succeeded, firstReport.FailureMessage);
        Assert.True(secondReport.Succeeded, secondReport.FailureMessage);

        Assert.Equal(
            Directory.EnumerateFiles(first, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(first, path))
                .Order(StringComparer.Ordinal),
            Directory.EnumerateFiles(second, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(second, path))
                .Order(StringComparer.Ordinal));

        foreach (string relative in OwnedFileNames)
        {
            Assert.Equal(File.ReadAllBytes(Path.Combine(first, relative)), File.ReadAllBytes(Path.Combine(second, relative)));
            string content = File.ReadAllText(Path.Combine(first, relative));
            Assert.DoesNotContain("\r", content, StringComparison.Ordinal);
            Assert.DoesNotContain(first, content, StringComparison.Ordinal);
        }

        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(first, "seqdoc.manifest.json")));
        JsonElement files = manifest.RootElement.GetProperty("files");
        Assert.Equal(
            ExpectedManifestPaths,
            files.EnumerateArray().Select(item => item.GetProperty("relativePath").GetString()!).ToArray());
        Assert.All(files.EnumerateArray(), item => Assert.Equal(64, item.GetProperty("sha256").GetString()!.Length));

        string mermaid = File.ReadAllText(Path.Combine(first, "get-api-test-1234abcd.mmd"));
        Assert.Empty(MermaidValidator.Validate(mermaid));
    }

    [Fact]
    public void ConfiguredMermaidBudgetProducesValidDeterministicBoundedPrefixAndDiagnostic()
    {
        var original = PlanTestFactory.CreateDiagramPlan();
        var firstMessage = original.Messages[0];
        var oneMessageParticipants = original.Participants
            .Where(participant => participant.Key == firstMessage.Source || participant.Key == firstMessage.Target)
            .ToImmutableArray();
        var oneMessagePlan = new DiagramPlan(original.EntryPoint, original.Profile, original.OperationKey,
            oneMessageParticipants, [firstMessage], [], "legacy-one-message");
        int oneMessageLimit = MermaidRenderer.Render(oneMessagePlan).Length;
        var entry = new DocumentSetEntry("get-api-test-1234abcd", PlanTestFactory.CreateWordingDocument(), original);
        var budget = new DiagramBudget(1024, 4096, 1024, 256, oneMessageLimit);
        var first = DocumentationSetBuilder.Build("profile:v1:test", "fingerprint", [entry], budget);
        var second = DocumentationSetBuilder.Build("profile:v1:test", "fingerprint", [entry], budget);

        Assert.True(first.Succeeded, string.Join("; ", first.Errors));
        var firstMermaid = first.Files.Single(file => file.RelativePath.EndsWith(".mmd", StringComparison.Ordinal));
        var secondMermaid = second.Files.Single(file => file.RelativePath.EndsWith(".mmd", StringComparison.Ordinal));
        string firstText = System.Text.Encoding.UTF8.GetString(firstMermaid.Content);
        Assert.True(firstText.Length <= budget.MaxMermaidCharacters);
        Assert.Empty(MermaidValidator.Validate(firstText));
        Assert.Contains(firstMessage.Label, firstText, StringComparison.Ordinal);
        Assert.DoesNotContain(original.Messages[1].Label, firstText, StringComparison.Ordinal);
        Assert.Equal(firstMermaid.Content, secondMermaid.Content);
        Assert.Contains(first.Diagnostics, diagnostic => diagnostic.Code == "DP-MERMAID-TRUNCATED");
    }

    [Fact]
    public void DefaultBudgetPreservesLegacyBytesAndMinimumMermaidLimitFailsExplicitly()
    {
        var entry = new DocumentSetEntry("get-api-test-1234abcd", PlanTestFactory.CreateWordingDocument(), PlanTestFactory.CreateDiagramPlan());
        var legacy = DocumentationSetBuilder.Build("profile:v1:test", "fingerprint", [entry]);
        var explicitDefault = DocumentationSetBuilder.Build("profile:v1:test", "fingerprint", [entry], DiagramBudget.Default);
        Assert.Equal(legacy.Files.Select(file => (file.RelativePath, file.Content)), explicitDefault.Files.Select(file => (file.RelativePath, file.Content)));

        var belowMinimum = DocumentationSetBuilder.Build("profile:v1:test", "fingerprint", [entry], new DiagramBudget(1, 1, 1, 1, 14));
        Assert.False(belowMinimum.Succeeded);
        Assert.Empty(belowMinimum.Files);
        Assert.Contains(belowMinimum.Errors, error => error.Contains("at least 15", StringComparison.Ordinal));

        var exactMinimum = DocumentationSetBuilder.Build("profile:v1:test", "fingerprint", [entry], new DiagramBudget(1, 1, 1, 1, 15));
        Assert.True(exactMinimum.Succeeded, string.Join("; ", exactMinimum.Errors));
        var mermaid = exactMinimum.Files.Single(file => file.RelativePath.EndsWith(".mmd", StringComparison.Ordinal));
        Assert.Equal(15, System.Text.Encoding.UTF8.GetString(mermaid.Content).Length);
        Assert.Empty(MermaidValidator.Validate(System.Text.Encoding.UTF8.GetString(mermaid.Content)));
    }

    [Fact]
    public void DiagramDiagnosticsAreVisibleInGeneratedMarkdown()
    {
        var original = PlanTestFactory.CreateDiagramPlan();
        var diagnostic = new DiagramPlanDiagnostic(
            new SeqDoc.Core.Identity.DiagnosticId("diagnostic:v1:test:diagram"),
            "DP-BUDGET-TRUNCATED", "The diagram was truncated.", "messages=1");
        var diagram = new DiagramPlan(original.EntryPoint, original.Profile, original.OperationKey,
            original.Participants, original.Messages, original.Branches, original.DebugProjection,
            original.Sequence, [diagnostic]);
        var markdown = MarkdownRenderer.RenderDocument(PlanTestFactory.CreateWordingDocument(), diagram);
        Assert.Contains("## Diagram diagnostics", markdown, StringComparison.Ordinal);
        Assert.Contains("DP-BUDGET-TRUNCATED", markdown, StringComparison.Ordinal);
        Assert.Contains("messages=1", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void StaleCleanupDeletesOnlyPreviouslyOwnedFilesAndPreservesUnowned()
    {
        string root = Path.Combine(_directory, "output");
        var firstActivation = OutputSetActivator.Activate(
            root,
            [MakeFile("a.md", "a"), MakeFile("b.md", "b")]);
        Assert.True(firstActivation.Succeeded, firstActivation.FailureMessage);

        string unowned = Path.Combine(root, "u.md");
        File.WriteAllText(unowned, "unowned");

        var secondActivation = OutputSetActivator.Activate(
            root,
            [MakeFile("b.md", "b-new"), MakeFile("c.md", "c")]);
        Assert.True(secondActivation.Succeeded, secondActivation.FailureMessage);

        Assert.False(File.Exists(Path.Combine(root, "a.md")));
        Assert.Equal("b-new", File.ReadAllText(Path.Combine(root, "b.md")));
        Assert.Equal("c", File.ReadAllText(Path.Combine(root, "c.md")));
        Assert.Equal("unowned", File.ReadAllText(unowned));
        Assert.Contains(secondActivation.RemovedFiles, path => path == "a.md");

        string manifest = File.ReadAllText(Path.Combine(root, "seqdoc.manifest.json"));
        Assert.Contains("\"b.md\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"c.md\"", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("\"a.md\"", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("\"u.md\"", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void FailureBeforeActivationPreservesPriorOutputAndMarksStale()
    {
        string root = Path.Combine(_directory, "output");
        var first = OutputSetActivator.Activate(root, [MakeFile("a.md", "a")]);
        Assert.True(first.Succeeded, first.FailureMessage);

        var failed = OutputSetActivator.Activate(root, [MakeFile("../escape.md", "x")]);

        Assert.False(failed.Succeeded);
        Assert.Equal("a", File.ReadAllText(Path.Combine(root, "a.md")));
        Assert.True(File.Exists(Path.Combine(root, "seqdoc.stale")));
    }

    [Fact]
    public void InterruptedSwapRollsBackAndPreservesPriorOutput()
    {
        string root = Path.Combine(_directory, "output");
        var first = OutputSetActivator.Activate(root, [MakeFile("a.md", "a")]);
        Assert.True(first.Succeeded, first.FailureMessage);

        // Simulate a crash after the backup phase: a.md was moved to backup and b.md was partially
        // installed, with the journal still in the prepared state.
        string machinery = Path.Combine(root, ".seqdoc");
        Directory.CreateDirectory(Path.Combine(machinery, "backup"));
        File.Move(Path.Combine(root, "a.md"), Path.Combine(machinery, "backup", "a.md"));
        File.WriteAllText(Path.Combine(root, "b.md"), "partial");
        var journal = new
        {
            SchemaVersion = 1,
            State = "prepared",
            NewFiles = new[] { new { RelativePath = "b.md", Sha256 = "partial-hash" } },
            StaleFiles = new[] { "a.md" },
            BackupPaths = new[] { "a.md", "b.md" },
        };
        File.WriteAllText(
            Path.Combine(machinery, "journal.json"),
            JsonSerializer.Serialize(journal, JournalJsonOptions));

        OutputSetActivator.Recover(root);

        Assert.Equal("a", File.ReadAllText(Path.Combine(root, "a.md")));
        Assert.False(File.Exists(Path.Combine(root, "b.md")));
        Assert.True(File.Exists(Path.Combine(root, "seqdoc.stale")));
        string journalText = File.ReadAllText(Path.Combine(machinery, "journal.json"));
        using var document = JsonDocument.Parse(journalText);
        Assert.Equal("rolled-back", document.RootElement.GetProperty("State").GetString());

        // A subsequent activation starts cleanly from the recovered state.
        var second = OutputSetActivator.Activate(root, [MakeFile("a.md", "a"), MakeFile("c.md", "c")]);
        Assert.True(second.Succeeded, second.FailureMessage);
        Assert.True(File.Exists(Path.Combine(root, "c.md")));
        Assert.False(File.Exists(Path.Combine(root, "seqdoc.stale")));
    }

    [Theory]
    [InlineData("parent-relative")]
    [InlineData("invalid-hash")]
    [InlineData("duplicate-path")]
    [InlineData("reserved-name")]
    public void MalformedManifestMetadataNeverEscapesOutputRootAndOnlyMarksStale(string partition)
    {
        // Regression: manifest metadata is consumed only after complete validation. Parent-relative,
        // invalid-hash, duplicate, and reserved-machinery entries may never authorize moving or
        // deleting files; invalid metadata may only mark the output stale.
        string root = Path.Combine(_directory, "output");
        var first = OutputSetActivator.Activate(root, [MakeFile("a.md", "a")]);
        Assert.True(first.Succeeded, first.FailureMessage);

        string externalDirectory = Path.Combine(_directory, "outside");
        Directory.CreateDirectory(externalDirectory);
        string externalFile = Path.Combine(externalDirectory, "keep.txt");
        File.WriteAllText(externalFile, "keep");

        string maliciousManifest = partition switch
        {
            "parent-relative" => MalformedManifestJson(
                ("a.md", Sha256Of("a")),
                ("../outside/keep.txt", Sha256Of("keep"))),
            "invalid-hash" => MalformedManifestJson(("a.md", "not-a-64-char-hash")),
            "duplicate-path" => MalformedManifestJson(
                ("a.md", Sha256Of("a")),
                ("a.md", Sha256Of("a"))),
            "reserved-name" => MalformedManifestJson(("seqdoc.stale", Sha256Of("x"))),
            _ => throw new ArgumentOutOfRangeException(nameof(partition)),
        };
        File.WriteAllText(Path.Combine(root, "seqdoc.manifest.json"), maliciousManifest);

        var report = OutputSetActivator.Activate(root, [MakeFile("b.md", "b")]);

        Assert.False(report.Succeeded);
        Assert.False(File.Exists(Path.Combine(root, "b.md")));
        Assert.True(File.Exists(Path.Combine(root, "seqdoc.stale")));
        Assert.Equal("keep", File.ReadAllText(externalFile));
    }

    [Fact]
    public void InterruptionBeforeBackupMovesPreservesUntouchedPriorFiles()
    {
        // Regression: a crash immediately after the journal was written but before the backup phase
        // moved any prior file must not delete the untouched prior files. Rollback conservatively
        // preserves files that were never actually backed up.
        string root = Path.Combine(_directory, "output");
        var first = OutputSetActivator.Activate(root, [MakeFile("a.md", "a")]);
        Assert.True(first.Succeeded, first.FailureMessage);

        string machinery = Path.Combine(root, ".seqdoc");
        var journal = new
        {
            SchemaVersion = 1,
            State = "prepared",
            NewFiles = new[] { new { RelativePath = "b.md", Sha256 = "partial-hash" } },
            StaleFiles = new[] { "a.md" },
            BackupPaths = new[] { "a.md", "b.md" },
        };
        File.WriteAllText(
            Path.Combine(machinery, "journal.json"),
            JsonSerializer.Serialize(journal, JournalJsonOptions));

        OutputSetActivator.Recover(root);

        Assert.Equal("a", File.ReadAllText(Path.Combine(root, "a.md")));
        Assert.True(File.Exists(Path.Combine(root, "seqdoc.stale")));
        string journalText = File.ReadAllText(Path.Combine(machinery, "journal.json"));
        using var document = JsonDocument.Parse(journalText);
        Assert.Equal("rolled-back", document.RootElement.GetProperty("State").GetString());
    }

    [Fact]
    public void GeneratedPathCollisionWithUnownedFileFailsStaleAndPreservesFile()
    {
        // Regression: an existing file at a newly generated path that is not prior manifest-owned is
        // a collision. The activation must fail, preserve the unowned file, and mark the output stale
        // rather than backing up and replacing it.
        string root = Path.Combine(_directory, "output");
        var first = OutputSetActivator.Activate(root, [MakeFile("a.md", "a")]);
        Assert.True(first.Succeeded, first.FailureMessage);

        File.WriteAllText(Path.Combine(root, "b.md"), "user-owned");

        var report = OutputSetActivator.Activate(root, [MakeFile("b.md", "generated")]);

        Assert.False(report.Succeeded);
        Assert.Equal("user-owned", File.ReadAllText(Path.Combine(root, "b.md")));
        Assert.Equal("a", File.ReadAllText(Path.Combine(root, "a.md")));
        Assert.True(File.Exists(Path.Combine(root, "seqdoc.stale")));
    }

    private static RenderedOutputFile MakeFile(string path, string content) => new(path, System.Text.Encoding.UTF8.GetBytes(content));

    private static string MalformedManifestJson(params (string RelativePath, string Sha256)[] files)
        => JsonSerializer.Serialize(
            new
            {
                schemaVersion = 1,
                files = files
                    .Select(file => new { relativePath = file.RelativePath, sha256 = file.Sha256 })
                    .ToArray(),
            },
            ManifestJsonOptions);

    private static string Sha256Of(string text)
        => Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
}
