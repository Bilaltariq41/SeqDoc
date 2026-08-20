using System.Collections.Immutable;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Configuration;

namespace SeqDoc.Configuration;

public enum ConfigurationProvenance
{
    Default,
    ConfigurationFile,
    NamedProfile,
    CommandLine,
}

public sealed record ResolvedConfigurationValue<T>(T Value, ConfigurationProvenance Provenance);

public sealed record ResolvedDiagramBudget(
    ResolvedConfigurationValue<int> MaxExpandedMethods,
    ResolvedConfigurationValue<int> MaxExpandedCalls,
    ResolvedConfigurationValue<int> MaxMaterialMessages,
    ResolvedConfigurationValue<int> MaxParticipants,
    ResolvedConfigurationValue<int> MaxMermaidCharacters);

/// <summary>Contains command-line values that replace the corresponding file or default values.</summary>
public sealed record PassAConfigurationOverrides(
    string? Configuration = null,
    string? TargetFramework = null,
    string? RuntimeIdentifier = null,
    int? MaxParallelism = null,
    string? BinaryAnalysis = null,
    string? SourceLink = null,
    ImmutableSortedDictionary<string, string>? MsBuildProperties = null,
    ImmutableSortedDictionary<string, string>? KnownValues = null,
    ImmutableSortedSet<string>? ExcludeParticipants = null,
    ImmutableSortedSet<string>? ExcludeCalls = null);

/// <summary>
/// Selects an optional YAML file and named profile, then supplies the final command-line overlay.
/// A null file path resolves defaults without searching the working directory.
/// </summary>
public sealed record ConfigurationResolutionRequest(
    string? ConfigurationFilePath = null,
    string? Profile = null,
    PassAConfigurationOverrides? CommandLineOverrides = null);

/// <summary>Provides the Pass A analysis settings together with the source of every resolved value.</summary>
public sealed record ResolvedPassAConfiguration(
    ResolvedConfigurationValue<string> Configuration,
    ResolvedConfigurationValue<string?> TargetFramework,
    ResolvedConfigurationValue<string?> RuntimeIdentifier,
    ResolvedConfigurationValue<int> MaxParallelism,
    ResolvedConfigurationValue<string?> BinaryAnalysis,
    ResolvedConfigurationValue<string?> SourceLink,
    ResolvedConfigurationValue<ImmutableSortedSet<string>> Roots,
    ImmutableSortedDictionary<string, ResolvedConfigurationValue<string>> MsBuildProperties,
    ImmutableSortedDictionary<string, ResolvedConfigurationValue<string>> KnownValues,
    bool RootsSpecified = false,
    ResolvedConfigurationValue<ImmutableSortedSet<string>>? ExcludeParticipants = null,
    ResolvedConfigurationValue<ImmutableSortedSet<string>>? ExcludeCalls = null)
{
    public ResolvedDiagramBudget DiagramBudget { get; init; } = DefaultDiagramBudget;
    public ResolvedDiagramBudget ResolvedDiagramBudget => DiagramBudget;
    public ResolvedConfigurationValue<int> MaxExpandedMethods => ResolvedDiagramBudget.MaxExpandedMethods;
    public ResolvedConfigurationValue<int> MaxExpandedCalls => ResolvedDiagramBudget.MaxExpandedCalls;
    public ResolvedConfigurationValue<int> MaxMaterialMessages => ResolvedDiagramBudget.MaxMaterialMessages;
    public ResolvedConfigurationValue<int> MaxParticipants => ResolvedDiagramBudget.MaxParticipants;
    public ResolvedConfigurationValue<int> MaxMermaidCharacters => ResolvedDiagramBudget.MaxMermaidCharacters;

    private static ResolvedDiagramBudget DefaultDiagramBudget => new(
        Default(SeqDoc.Core.Configuration.DiagramBudget.Default.MaxExpandedMethods),
        Default(SeqDoc.Core.Configuration.DiagramBudget.Default.MaxExpandedCalls),
        Default(SeqDoc.Core.Configuration.DiagramBudget.Default.MaxMaterialMessages),
        Default(SeqDoc.Core.Configuration.DiagramBudget.Default.MaxParticipants),
        Default(SeqDoc.Core.Configuration.DiagramBudget.Default.MaxMermaidCharacters));

    private static ResolvedConfigurationValue<int> Default(int value) =>
        new(value, ConfigurationProvenance.Default);
}

public interface IConfigurationResolver
{
    Task<ApplicationResult<ResolvedPassAConfiguration>> ResolveAsync(
        ConfigurationResolutionRequest request,
        CancellationToken cancellationToken);
}
