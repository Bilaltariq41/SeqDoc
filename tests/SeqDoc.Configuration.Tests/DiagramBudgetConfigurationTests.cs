using SeqDoc.Application.Analysis;
using SeqDoc.Configuration;
using SeqDoc.Core.Configuration;
using Xunit;

namespace SeqDoc.Configuration.Tests;

public sealed class DiagramBudgetConfigurationTests
{
    [Fact]
    public async Task ResolvedPassAConfigurationRetainsTwelveValuePositionalAbi()
    {
        var expectedRoots = System.Collections.Immutable.ImmutableSortedSet<string>.Empty;
        var value = new ResolvedPassAConfiguration(
            new("Release", ConfigurationProvenance.Default),
            new(null, ConfigurationProvenance.Default),
            new(null, ConfigurationProvenance.Default),
            new(1, ConfigurationProvenance.Default),
            new("metadata-only", ConfigurationProvenance.Default),
            new("offline", ConfigurationProvenance.Default),
            new(expectedRoots, ConfigurationProvenance.Default),
            System.Collections.Immutable.ImmutableSortedDictionary<string, ResolvedConfigurationValue<string>>.Empty,
            System.Collections.Immutable.ImmutableSortedDictionary<string, ResolvedConfigurationValue<string>>.Empty);

        var (configuration, targetFramework, runtimeIdentifier, maxParallelism, binaryAnalysis, sourceLink,
            roots, msbuildProperties, knownValues, rootsSpecified, excludeParticipants, excludeCalls) = value;

        Assert.Equal("Release", configuration.Value);
        Assert.Null(targetFramework.Value);
        Assert.Null(runtimeIdentifier.Value);
        Assert.Equal(1, maxParallelism.Value);
        Assert.Equal("metadata-only", binaryAnalysis.Value);
        Assert.Equal("offline", sourceLink.Value);
        Assert.Equal(expectedRoots, roots.Value);
        Assert.Empty(msbuildProperties);
        Assert.Empty(knownValues);
        Assert.False(rootsSpecified);
        Assert.Null(excludeParticipants);
        Assert.Null(excludeCalls);
        Assert.Equal(1024, value.DiagramBudget.MaxExpandedMethods.Value);
    }

    [Fact]
    public async Task AllBudgetFieldsResolveIndependentlyWithFileProvenance()
    {
        var result = await ResolveYamlAsync("""
            schemaVersion: 1
            diagrams:
              maxExpandedMethods: 11
              maxExpandedCalls: 22
              maxMaterialMessages: 33
              maxParticipants: 44
              maxMermaidCharacters: 55
            """);

        var value = Assert.IsType<ResolvedPassAConfiguration>(result.Value);
        Assert.Equal(11, value.MaxExpandedMethods.Value);
        Assert.Equal(22, value.MaxExpandedCalls.Value);
        Assert.Equal(33, value.MaxMaterialMessages.Value);
        Assert.Equal(44, value.MaxParticipants.Value);
        Assert.Equal(55, value.MaxMermaidCharacters.Value);
        Assert.All(
            new[] { value.MaxExpandedMethods, value.MaxExpandedCalls, value.MaxMaterialMessages,
                value.MaxParticipants, value.MaxMermaidCharacters },
            item => Assert.Equal(ConfigurationProvenance.ConfigurationFile, item.Provenance));
    }

    [Fact]
    public async Task OmittedBudgetFieldsUseFiniteDefaultsAndSchemaV1DiagramsRemainCompatible()
    {
        var result = await ResolveYamlAsync("""
            schemaVersion: 1
            diagrams:
              maxParticipants: 8
              maxMaterialMessages: 50
              maxFragmentDepth: 3
              processingColor: rgb(23, 37, 84)
            """);

        var value = Assert.IsType<ResolvedPassAConfiguration>(result.Value);
        Assert.Equal(DiagramBudget.Default.MaxExpandedMethods, value.MaxExpandedMethods.Value);
        Assert.Equal(DiagramBudget.Default.MaxExpandedCalls, value.MaxExpandedCalls.Value);
        Assert.Equal(8, value.MaxParticipants.Value);
        Assert.Equal(50, value.MaxMaterialMessages.Value);
        Assert.Equal(ConfigurationProvenance.Default, value.MaxExpandedMethods.Provenance);
        Assert.Equal(ConfigurationProvenance.ConfigurationFile, value.MaxParticipants.Provenance);
        Assert.Equal(ConfigurationProvenance.ConfigurationFile, value.MaxMaterialMessages.Provenance);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("not-an-integer")]
    [InlineData("9223372036854775807")]
    public async Task InvalidBudgetScalarsAreRejectedAtTheirYamlBoundary(string scalar)
    {
        var result = await ResolveYamlAsync($"schemaVersion: 1\ndiagrams:\n  maxExpandedCalls: {scalar}\n");

        AssertConfigurationFailure(result, "SD3003", "$.diagrams.maxExpandedCalls");
    }

    [Fact]
    public async Task UnknownDiagramBudgetAliasIsRejectedInsteadOfCreatingDuplicatePolicy()
    {
        var result = await ResolveYamlAsync("schemaVersion: 1\ndiagrams:\n  maxMessages: 10\n");

        AssertConfigurationFailure(result, "SD3003", "$.diagrams.maxMessages");
    }

    private static async Task<ApplicationResult<ResolvedPassAConfiguration>> ResolveYamlAsync(string yaml)
    {
        string path = Path.Combine(Path.GetTempPath(), $"seqdoc-diagram-budget-{Guid.NewGuid():N}.yml");
        await File.WriteAllTextAsync(path, yaml);
        try
        {
            return await new YamlConfigurationResolver().ResolveAsync(
                new ConfigurationResolutionRequest(path), CancellationToken.None);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void AssertConfigurationFailure(
        ApplicationResult<ResolvedPassAConfiguration> result, string code, string location)
    {
        Assert.Equal(ApplicationOutcome.InvalidInput, result.Outcome);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(code, diagnostic.Code);
        Assert.Equal(location, diagnostic.Location.Description);
    }
}
