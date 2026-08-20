using System.Collections.Immutable;
using SeqDoc.Application.Analysis;
using SeqDoc.Configuration;
using SeqDoc.Core.Diagnostics;
using Xunit;

namespace SeqDoc.Configuration.Tests;

public sealed class YamlConfigurationResolverTests
{
    private static readonly string[] ExpectedProfilePropertyKeys = ["Alpha", "Zeta"];
    private static readonly string[] ExpectedKnownValueKeys = ["Features:NewPayments"];
    private static readonly string[] ExpectedOverlayPropertyKeys = ["Additional", "EnvironmentName", "Shared"];

    [Theory]
    [InlineData("ApiKey")]
    [InlineData("DatabasePassword")]
    [InlineData("connection_string")]
    public async Task SecretLikeProfilePropertiesAreRejectedBeforePersistence(string key)
    {
        string yaml = $"""
            schemaVersion: 1
            profiles:
              production:
                msbuildProperties:
                  {key}: value
            """;

        var result = await ResolveYamlAsync(yaml, profile: "production");

        AssertConfigurationFailure(result, "SD3011", $"msbuildProperties.{key}");
    }

    [Theory]
    [InlineData("binaryAnalysis", "full", "SD3012")]
    [InlineData("sourceLink", "online", "SD3013")]
    public async Task UnsupportedPassAModesAreRejected(string key, string value, string code)
    {
        string yaml = $"schemaVersion: 1\nanalysis:\n  {key}: {value}\n";

        var result = await ResolveYamlAsync(yaml);

        AssertConfigurationFailure(result, code, $"analysis.{key}");
    }

    [Fact]
    public async Task NullFileUsesDocumentedDefaultsWithoutSearchingForAFile()
    {
        var result = await ResolveAsync();

        Assert.Equal(ApplicationOutcome.Succeeded, result.Outcome);
        var value = Assert.IsType<ResolvedPassAConfiguration>(result.Value);
        Assert.Equal("Release", value.Configuration.Value);
        Assert.Equal(Environment.ProcessorCount, value.MaxParallelism.Value);
        Assert.Null(value.TargetFramework.Value);
        Assert.Null(value.RuntimeIdentifier.Value);
        Assert.Equal("metadata-only", value.BinaryAnalysis.Value);
        Assert.Equal("offline", value.SourceLink.Value);
        Assert.Equal(1024, value.MaxExpandedMethods.Value);
        Assert.Equal(4096, value.MaxExpandedCalls.Value);
        Assert.Equal(1024, value.MaxMaterialMessages.Value);
        Assert.Equal(256, value.MaxParticipants.Value);
        Assert.Equal(45_000, value.MaxMermaidCharacters.Value);
        Assert.All(
            new[]
            {
                value.Configuration.Provenance,
                value.MaxParallelism.Provenance,
                value.TargetFramework.Provenance,
                value.RuntimeIdentifier.Provenance,
                value.BinaryAnalysis.Provenance,
                value.SourceLink.Provenance,
                value.MaxExpandedMethods.Provenance,
                value.MaxExpandedCalls.Provenance,
                value.MaxMaterialMessages.Provenance,
                value.MaxParticipants.Provenance,
                value.MaxMermaidCharacters.Provenance,
            },
            provenance => Assert.Equal(ConfigurationProvenance.Default, provenance));
        Assert.Empty(value.MsBuildProperties);
        Assert.Empty(value.KnownValues);
    }

    [Fact]
    public async Task AnalysisFieldsComeFromYamlAndExplicitNullRetainsFileProvenance()
    {
        const string yaml = """
            schemaVersion: 1
            analysis:
              configuration: Debug
              targetFramework: net10.0
              runtimeIdentifier: null
              maxParallelism: 3
              binaryAnalysis: metadata-only
              sourceLink: offline
            """;

        var result = await ResolveYamlAsync(yaml);

        var value = Assert.IsType<ResolvedPassAConfiguration>(result.Value);
        Assert.Equal("Debug", value.Configuration.Value);
        Assert.Equal("net10.0", value.TargetFramework.Value);
        Assert.Null(value.RuntimeIdentifier.Value);
        Assert.Equal(3, value.MaxParallelism.Value);
        Assert.Equal("metadata-only", value.BinaryAnalysis.Value);
        Assert.Equal("offline", value.SourceLink.Value);
        Assert.All(
            new[]
            {
                value.Configuration.Provenance,
                value.TargetFramework.Provenance,
                value.RuntimeIdentifier.Provenance,
                value.MaxParallelism.Provenance,
                value.BinaryAnalysis.Provenance,
                value.SourceLink.Provenance,
            },
            provenance => Assert.Equal(ConfigurationProvenance.ConfigurationFile, provenance));
    }

    [Fact]
    public async Task SelectedProfileProducesSortedMapsWithEntryProvenance()
    {
        const string yaml = """
            schemaVersion: 1
            profiles:
              production:
                msbuildProperties:
                  Zeta: last
                  Alpha: first
                knownValues:
                  Features:NewPayments: "true"
              development:
                msbuildProperties:
                  EnvironmentName: Development
            """;

        var result = await ResolveYamlAsync(yaml, profile: "production");

        var value = Assert.IsType<ResolvedPassAConfiguration>(result.Value);
        Assert.Equal(ExpectedProfilePropertyKeys, value.MsBuildProperties.Keys);
        Assert.Equal("first", value.MsBuildProperties["Alpha"].Value);
        Assert.Equal(ConfigurationProvenance.NamedProfile, value.MsBuildProperties["Alpha"].Provenance);
        Assert.Equal(ExpectedKnownValueKeys, value.KnownValues.Keys);
        Assert.DoesNotContain("EnvironmentName", value.MsBuildProperties.Keys);
    }

    [Fact]
    public async Task CommandLineOverlayWinsAndPreservesUnaffectedProfileEntries()
    {
        const string yaml = """
            schemaVersion: 1
            analysis:
              configuration: Debug
              targetFramework: net9.0
              maxParallelism: 2
            profiles:
              production:
                msbuildProperties:
                  EnvironmentName: Production
                  Shared: file
                knownValues:
                  Feature: "false"
            """;
        var overrides = new PassAConfigurationOverrides(
            Configuration: "Release",
            TargetFramework: "net10.0",
            MaxParallelism: 6,
            MsBuildProperties: SortedMap(("Shared", "cli"), ("Additional", "value")),
            KnownValues: SortedMap(("Feature", "true")));

        var result = await ResolveYamlAsync(yaml, "production", overrides);

        var value = Assert.IsType<ResolvedPassAConfiguration>(result.Value);
        Assert.Equal("Release", value.Configuration.Value);
        Assert.Equal("net10.0", value.TargetFramework.Value);
        Assert.Equal(6, value.MaxParallelism.Value);
        Assert.Equal(ConfigurationProvenance.CommandLine, value.Configuration.Provenance);
        Assert.Equal(ConfigurationProvenance.CommandLine, value.TargetFramework.Provenance);
        Assert.Equal(ConfigurationProvenance.CommandLine, value.MaxParallelism.Provenance);
        Assert.Equal(ConfigurationProvenance.NamedProfile, value.MsBuildProperties["EnvironmentName"].Provenance);
        Assert.Equal("cli", value.MsBuildProperties["Shared"].Value);
        Assert.Equal(ConfigurationProvenance.CommandLine, value.MsBuildProperties["Shared"].Provenance);
        Assert.Equal(ExpectedOverlayPropertyKeys, value.MsBuildProperties.Keys);
        Assert.Equal("true", value.KnownValues["Feature"].Value);
    }

    [Theory]
    [InlineData("schemaVersion: 1\nunexpected: true", "$.unexpected")]
    [InlineData("schemaVersion: 1\nanalysis:\n  unexpected: true", "$.analysis.unexpected")]
    [InlineData("schemaVersion: 1\nprofiles:\n  p:\n    unexpected: {}", "$.profiles.p.unexpected")]
    [InlineData("schemaVersion: 1\ndocumentation:\n  unexpected: true", "$.documentation.unexpected")]
    [InlineData("schemaVersion: 1\nparticipants:\n  type:X:\n    unexpected: value", "$.participants.type:X.unexpected")]
    [InlineData("schemaVersion: 1\nruntimeBindings:\n  type:X:\n    implementation: type:Y\n    profiles: [p]\n    reason: selected\n    unexpected: value", "$.runtimeBindings.type:X.unexpected")]
    [InlineData("schemaVersion: 1\ndiagrams:\n  unexpected: 1", "$.diagrams.unexpected")]
    public async Task UnknownKeysAreRejectedAtEveryDocumentedLevel(string yaml, string location)
    {
        var result = await ResolveYamlAsync(yaml);

        AssertConfigurationFailure(result, "SD3003", location);
    }

    [Theory]
    [InlineData("analysis: {}", "Existing configuration files require schemaVersion 1")]
    [InlineData("schemaVersion: 2", "unsupported")]
    [InlineData("schemaVersion: one", "Expected an integer")]
    public async Task MissingUnsupportedOrInvalidSchemaIsRejected(string yaml, string causeFragment)
    {
        var result = await ResolveYamlAsync(yaml);

        var diagnostic = AssertConfigurationFailure(result, "SD3003");
        Assert.Contains(causeFragment, diagnostic.TechnicalCause, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MalformedYamlReturnsStructuredConfigurationDiagnostic()
    {
        var result = await ResolveYamlAsync("schemaVersion: 1\nanalysis: [");

        var diagnostic = AssertConfigurationFailure(result, "SD3002");
        Assert.Equal(AnalysisStage.Configuration, diagnostic.Stage);
        Assert.NotNull(diagnostic.InternalDetail);
        Assert.DoesNotContain(" at ", diagnostic.TechnicalCause, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DocumentedLaterPassSectionsAreValidatedButNotApplied()
    {
        const string yaml = """
            schemaVersion: 1
            documentation:
              outputDirectory: docs/seqdoc
              repositoryProfile: auto
              coverageMode: selected
              includeComprehensiveAppendix: false
            selection:
              include: [flow:a]
              exclude: [flow:b]
              critical: [flow:a]
            participants:
              type:Service:
                displayName: Reservation Service
            terms:
              pending: Awaiting review
            conditions:
              property:Reservation.IsPaid:
                positive: Already paid
                negative: Not paid
            runtimeBindings:
              type:IPaymentService:
                implementation: type:VisaPaymentService
                profiles: [production]
                reason: Production selects Visa
            diagrams:
              maxParticipants: 8
              maxMaterialMessages: 50
              maxFragmentDepth: 3
              processingColor: rgb(23, 37, 84)
              successColor: rgb(20, 83, 45)
              recoveryColor: rgb(17, 94, 89)
              warningColor: rgb(120, 53, 15)
              terminalFailureColor: rgb(127, 29, 29)
            """;

        var result = await ResolveYamlAsync(yaml);

        Assert.Equal(ApplicationOutcome.Succeeded, result.Outcome);
        Assert.Equal("Release", result.Value!.Configuration.Value);
        Assert.Empty(result.Value.MsBuildProperties);
    }

    [Fact]
    public async Task SelectionExcludeRemainsInertAndDocumentationExclusionsCarryProvenance()
    {
        const string yaml = """
            schemaVersion: 1
            selection:
              roots:
                - method:v1:Alpha.Run()
              exclude: ["flow:selection-only"]
            documentation:
              excludeParticipants: ["MyApp.Logger"]
              excludeCalls: ["MyApp.Logger.LogError"]
            """;

        var result = await ResolveYamlAsync(yaml);

        var value = Assert.IsType<ResolvedPassAConfiguration>(result.Value);
        Assert.Equal(["MyApp.Logger"], value.ExcludeParticipants!.Value);
        Assert.Equal(["MyApp.Logger.LogError"], value.ExcludeCalls!.Value);
        Assert.Equal(ConfigurationProvenance.ConfigurationFile, value.ExcludeParticipants.Provenance);
        Assert.Equal(ConfigurationProvenance.ConfigurationFile, value.ExcludeCalls.Provenance);
        Assert.True(value.RootsSpecified);
    }

    [Fact]
    public async Task PresentationIntegrityDocumentationExclusionsAreDedicatedAndDoNotChangeFlowSelection()
    {
        const string yaml = """
            schemaVersion: 1
            documentation:
              excludeParticipants: ["Payments.AuditWriter"]
              excludeCalls: ["Payments.TransferGateway.SendAsync"]
            selection:
              exclude: ["flow:keep-this-selection-contract"]
            """;

        var result = await ResolveYamlAsync(yaml);

        var value = Assert.IsType<ResolvedPassAConfiguration>(result.Value);
        Assert.Equal(["Payments.AuditWriter"], value.ExcludeParticipants!.Value);
        Assert.Equal(["Payments.TransferGateway.SendAsync"], value.ExcludeCalls!.Value);
    }

    [Fact]
    public async Task PresentationIntegrityRejectsStructuralRootParticipantExclusion()
    {
        const string yaml = """
            schemaVersion: 1
            selection:
              roots: ["method:v1:Payments.TransferEngine.SubmitAsync()"]
            documentation:
              excludeParticipants: ["action"]
            """;

        var result = await ResolveYamlAsync(yaml);

        var diagnostic = AssertConfigurationFailure(result, "SD3003");
        Assert.Contains("excludeParticipants", diagnostic.TechnicalCause, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("A..B")]
    [InlineData(".A.B")]
    [InlineData("A.B.")]
    [InlineData("A. B")]
    public async Task MalformedParticipantExclusionSegmentsAreRejected(string value)
    {
        var result = await ResolveYamlAsync($"schemaVersion: 1\ndocumentation:\n  excludeParticipants: [\"{value}\"]\n");
        AssertConfigurationFailure(result, "SD3003");
    }

    [Theory]
    [InlineData("A..B.Call")]
    [InlineData("A.B.*.Call")]
    [InlineData("A.B.*Call")]
    [InlineData("A.B.")]
    public async Task MalformedExactAndWildcardCallExclusionPatternsAreRejected(string value)
    {
        var result = await ResolveYamlAsync($"schemaVersion: 1\ndocumentation:\n  excludeCalls: [\"{value}\"]\n");
        AssertConfigurationFailure(result, "SD3003");
    }

    [Fact]
    public async Task CommandLineExclusionOverridesUseTheSameCanonicalPatternValidation()
    {
        var result = await ResolveAsync(new ConfigurationResolutionRequest(
            CommandLineOverrides: new PassAConfigurationOverrides(
                ExcludeParticipants: ImmutableSortedSet.Create(StringComparer.Ordinal, "A..B"))));
        AssertConfigurationFailure(result, "SD3014");
    }

    [Fact]
    public async Task SelectionRootsAreCanonicalSortedAndRetainConfigurationFileProvenance()
    {
        const string yaml = """
            schemaVersion: 1
            selection:
              roots:
                - method:v1:Zeta.Run()
                - method:v1:Alpha.Run()
              include: [flow:a]
              exclude: [flow:b]
              critical: [flow:a]
            """;

        var result = await ResolveYamlAsync(yaml);

        var value = Assert.IsType<ResolvedPassAConfiguration>(result.Value);
        Assert.Equal(
            ["method:v1:Alpha.Run()", "method:v1:Zeta.Run()"],
            value.Roots.Value);
        Assert.Equal(ConfigurationProvenance.ConfigurationFile, value.Roots.Provenance);
        Assert.True(value.RootsSpecified);
    }

    [Theory]
    [InlineData("method:v1:Alpha.Run()\n    - method:v1:Alpha.Run()", "$.selection.roots")]
    [InlineData("roots: method:v1:Alpha.Run()", "$.selection.roots")]
    [InlineData("- method:v1:Alpha.Run()\n    - 42", "$.selection.roots")]
    public async Task SelectionRootsRejectDuplicateAndNonStringValuesAtTheRootsPath(string roots, string path)
    {
        string yaml = $"schemaVersion: 1\nselection:\n  roots:\n    - {roots}\n";

        var result = await ResolveYamlAsync(yaml);

        AssertConfigurationFailure(result, "SD3003", path);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("true")]
    public async Task SelectionRootsRejectUnquotedYamlTypedScalarsButAcceptQuotedText(string scalar)
    {
        var rejected = await ResolveYamlAsync($"schemaVersion: 1\nselection:\n  roots: [{scalar}]\n");
        AssertConfigurationFailure(rejected, "SD3003", "$.selection.roots");

        var accepted = await ResolveYamlAsync($"schemaVersion: 1\nselection:\n  roots: [\"{scalar}\"]\n");
        Assert.Equal(ApplicationOutcome.Succeeded, accepted.Outcome);
        Assert.Equal([scalar], accepted.Value!.Roots.Value);
    }

    [Theory]
    [InlineData("schemaVersion: 1\nselection:\n  include: [flow:a, flow:a]", "Duplicate value")]
    [InlineData("schemaVersion: 1\nselection:\n  exclude: [flow:a]\n  critical: [flow:a]", "both critical and excluded")]
    [InlineData("schemaVersion: 1\nruntimeBindings:\n  type:X:\n    implementation: type:Y\n    profiles: []\n    reason: selected", "At least one profile")]
    [InlineData("schemaVersion: 1\nconditions:\n  property:X:\n    positive: yes", "Required key 'negative'")]
    public async Task DuplicateOrConflictingLaterSelectionIsRejected(string yaml, string causeFragment)
    {
        var result = await ResolveYamlAsync(yaml);

        var diagnostic = AssertConfigurationFailure(result, "SD3003");
        Assert.Contains(causeFragment, diagnostic.TechnicalCause, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DuplicateYamlKeyIsRejected()
    {
        const string yaml = """
            schemaVersion: 1
            analysis:
              configuration: Debug
              configuration: Release
            """;

        var result = await ResolveYamlAsync(yaml);

        AssertConfigurationFailure(result);
    }

    [Fact]
    public async Task MissingOrEmptyProfileSelectionIsRejectedWithoutPartialValues()
    {
        const string yaml = "schemaVersion: 1\nprofiles:\n  production: {}";

        var missing = await ResolveYamlAsync(yaml, "Production");
        var empty = await ResolveYamlAsync(yaml, " ");

        AssertConfigurationFailure(missing, "SD3006", "Production");
        AssertConfigurationFailure(empty, "SD3005", "profile");
        Assert.Null(missing.Value);
        Assert.Null(empty.Value);
    }

    [Fact]
    public async Task InvalidCliOverrideAndUnreadableFileReturnInvalidInputDiagnostics()
    {
        var invalid = await ResolveAsync(new ConfigurationResolutionRequest(
            CommandLineOverrides: new PassAConfigurationOverrides(MaxParallelism: 0)));
        string missingPath = Path.Combine(Path.GetTempPath(), $"seqdoc-missing-{Guid.NewGuid():N}.yml");
        var missing = await ResolveAsync(new ConfigurationResolutionRequest(missingPath));

        AssertConfigurationFailure(invalid, "SD3008", "analysis.maxParallelism");
        AssertConfigurationFailure(missing, "SD3004", "configuration file");
    }

    [Fact]
    public async Task CancellationIsReturnedAsTypedOutcome()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        string path = Path.GetTempFileName();
        try
        {
            var result = await new YamlConfigurationResolver().ResolveAsync(
                new ConfigurationResolutionRequest(path),
                cancellation.Token);

            Assert.Equal(ApplicationOutcome.Cancelled, result.Outcome);
            Assert.Empty(result.Diagnostics);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<ApplicationResult<ResolvedPassAConfiguration>> ResolveYamlAsync(
        string yaml,
        string? profile = null,
        PassAConfigurationOverrides? overrides = null)
    {
        string path = Path.Combine(Path.GetTempPath(), $"seqdoc-configuration-{Guid.NewGuid():N}.yml");
        await File.WriteAllTextAsync(path, yaml);
        try
        {
            return await ResolveAsync(new ConfigurationResolutionRequest(path, profile, overrides));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Task<ApplicationResult<ResolvedPassAConfiguration>> ResolveAsync(
        ConfigurationResolutionRequest? request = null) =>
        new YamlConfigurationResolver().ResolveAsync(request ?? new ConfigurationResolutionRequest(), CancellationToken.None);

    private static AnalysisDiagnostic AssertConfigurationFailure(
        ApplicationResult<ResolvedPassAConfiguration> result,
        string? code = null,
        string? location = null)
    {
        Assert.Equal(ApplicationOutcome.InvalidInput, result.Outcome);
        Assert.Null(result.Value);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(AnalysisStage.Configuration, diagnostic.Stage);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(code ?? diagnostic.Code, diagnostic.Code);
        Assert.Equal(location ?? diagnostic.Location.Description, diagnostic.Location.Description);
        Assert.Equal("No analysis configuration was produced.", diagnostic.UserImpact);
        return diagnostic;
    }

    private static ImmutableSortedDictionary<string, string> SortedMap(params (string Key, string Value)[] entries) =>
        entries.ToImmutableSortedDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
}
