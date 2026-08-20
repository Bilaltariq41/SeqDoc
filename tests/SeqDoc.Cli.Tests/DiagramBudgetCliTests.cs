using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace SeqDoc.Cli.Tests;

public sealed class DiagramBudgetCliTests
{
    [Fact]
    public async Task ConfigurationOutputContainsStableBudgetValuesAndProvenanceInJsonAndHumanForms()
    {
        string root = FindRepositoryRoot();
        string target = Path.Combine(root, "tests", "fixtures", "BehaviorDocumentation", "GetMeaning", "GetMeaning.csproj");
        using var cache = new TemporaryCache();
        string config = cache.WriteConfiguration("""
            schemaVersion: 1
            diagrams:
              maxExpandedMethods: 11
              maxExpandedCalls: 22
              maxMaterialMessages: 33
              maxParticipants: 44
              maxMermaidCharacters: 55
            """);

        var json = await RunAsync("analyze", target, "--repository-root", root, "--cache", cache.Path,
            "--config", config, "--json");
        Assert.Equal(0, json.ExitCode);
        using var document = JsonDocument.Parse(json.Output);
        JsonElement budget = document.RootElement.GetProperty("data").GetProperty("configuration").GetProperty("diagramBudget");
        Assert.Equal(11, budget.GetProperty("maxExpandedMethods").GetProperty("value").GetInt32());
        Assert.Equal(22, budget.GetProperty("maxExpandedCalls").GetProperty("value").GetInt32());
        Assert.Equal(33, budget.GetProperty("maxMaterialMessages").GetProperty("value").GetInt32());
        Assert.Equal(44, budget.GetProperty("maxParticipants").GetProperty("value").GetInt32());
        Assert.Equal(55, budget.GetProperty("maxMermaidCharacters").GetProperty("value").GetInt32());
        Assert.All(budget.EnumerateObject(), property =>
            Assert.Equal("configurationFile", property.Value.GetProperty("provenance").GetString()));

        var human = await RunAsync("analyze", target, "--repository-root", root, "--cache", cache.Path,
            "--config", config);
        Assert.Equal(0, human.ExitCode);
        Assert.Contains("Maximum expanded methods: 11 (ConfigurationFile)", human.Output, StringComparison.Ordinal);
        Assert.Contains("Maximum Mermaid characters: 55 (ConfigurationFile)", human.Output, StringComparison.Ordinal);
    }

    private static async Task<ProcessResult> RunAsync(params string[] arguments)
    {
        string root = FindRepositoryRoot();
        string assembly = Path.Combine(root, "src", "SeqDoc.Cli", "bin", "Release", "net10.0", "SeqDoc.Cli.dll");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add(assembly);
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }
        Assert.True(process.Start());
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await output, await error);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SeqDoc.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);

    private sealed class TemporaryCache : IDisposable
    {
        public TemporaryCache()
        {
            DirectoryPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"seqdoc-cli-budget-{Guid.NewGuid():N}");
            Path = System.IO.Path.Combine(DirectoryPath, "cache.db");
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }
        public string Path { get; }

        public string WriteConfiguration(string contents)
        {
            string path = System.IO.Path.Combine(DirectoryPath, "seqdoc.yml");
            File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
