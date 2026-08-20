using System.Collections.Immutable;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Configuration;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using YamlDotNet.Core;

namespace SeqDoc.Configuration;

public sealed class YamlConfigurationResolver : IConfigurationResolver
{
    private static readonly string[] SensitiveKeyFragments =
        ["PASSWORD", "PASSPHRASE", "PWD", "SECRET", "TOKEN", "APIKEY", "ACCESSKEY", "PRIVATEKEY", "CONNECTIONSTRING", "CREDENTIAL", "AUTHORIZATION"];

    public async Task<ApplicationResult<ResolvedPassAConfiguration>> ResolveAsync(
        ConfigurationResolutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            YamlConfigurationDocument? document = null;
            if (request.ConfigurationFilePath is not null)
            {
                if (string.IsNullOrWhiteSpace(request.ConfigurationFilePath))
                {
                    return Failure("SD3001", "The configuration path is empty.", "configuration file", "An explicit configuration path must identify a file.", "Provide a non-empty configuration file path.");
                }

                string yaml = await File.ReadAllTextAsync(request.ConfigurationFilePath, cancellationToken).ConfigureAwait(false);
                document = YamlConfigurationDocument.Parse(yaml);
            }

            return Resolve(document, request);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ApplicationResult.Failure<ResolvedPassAConfiguration>(ApplicationOutcome.Cancelled, []);
        }
        catch (ConfigurationFormatException exception)
        {
            return Failure("SD3003", "The configuration file is invalid.", exception.Path, exception.Message, "Correct the configuration and run SeqDoc again.", exception.InnerException);
        }
        catch (YamlException exception)
        {
            return Failure("SD3002", "The configuration file is malformed YAML.", "configuration file", "The YAML parser could not read the document.", "Correct the YAML syntax and run SeqDoc again.", exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure("SD3004", "The configuration file could not be read.", "configuration file", "The selected path was missing, inaccessible, or could not be read.", "Verify the path and file permissions, then run SeqDoc again.", exception);
        }
    }

    private static ApplicationResult<ResolvedPassAConfiguration> Resolve(
        YamlConfigurationDocument? document,
        ConfigurationResolutionRequest request)
    {
        var configuration = new ResolvedConfigurationValue<string>("Release", ConfigurationProvenance.Default);
        var targetFramework = new ResolvedConfigurationValue<string?>(null, ConfigurationProvenance.Default);
        var runtimeIdentifier = new ResolvedConfigurationValue<string?>(null, ConfigurationProvenance.Default);
        var maxParallelism = new ResolvedConfigurationValue<int>(Environment.ProcessorCount, ConfigurationProvenance.Default);
        var binaryAnalysis = new ResolvedConfigurationValue<string?>("metadata-only", ConfigurationProvenance.Default);
        var sourceLink = new ResolvedConfigurationValue<string?>("offline", ConfigurationProvenance.Default);
        var roots = new ResolvedConfigurationValue<ImmutableSortedSet<string>>(ImmutableSortedSet.Create<string>(StringComparer.Ordinal), ConfigurationProvenance.Default);
        var msbuildProperties = EmptyResolvedMap();
        var knownValues = EmptyResolvedMap();
        var diagramBudget = DefaultDiagramBudget();
        bool rootsSpecified = false;

        if (document is not null)
        {
            configuration = document.Analysis.SpecifiedFields.Contains("configuration")
                ? new ResolvedConfigurationValue<string>(document.Analysis.Configuration!, ConfigurationProvenance.ConfigurationFile)
                : configuration;
            targetFramework = document.Analysis.SpecifiedFields.Contains("targetFramework")
                ? new ResolvedConfigurationValue<string?>(document.Analysis.TargetFramework, ConfigurationProvenance.ConfigurationFile)
                : targetFramework;
            runtimeIdentifier = document.Analysis.SpecifiedFields.Contains("runtimeIdentifier")
                ? new ResolvedConfigurationValue<string?>(document.Analysis.RuntimeIdentifier, ConfigurationProvenance.ConfigurationFile)
                : runtimeIdentifier;
            maxParallelism = document.Analysis.SpecifiedFields.Contains("maxParallelism")
                ? new ResolvedConfigurationValue<int>(document.Analysis.MaxParallelism!.Value, ConfigurationProvenance.ConfigurationFile)
                : maxParallelism;
            binaryAnalysis = document.Analysis.SpecifiedFields.Contains("binaryAnalysis")
                ? new ResolvedConfigurationValue<string?>(document.Analysis.BinaryAnalysis, ConfigurationProvenance.ConfigurationFile)
                : binaryAnalysis;
            sourceLink = document.Analysis.SpecifiedFields.Contains("sourceLink")
                ? new ResolvedConfigurationValue<string?>(document.Analysis.SourceLink, ConfigurationProvenance.ConfigurationFile)
                : sourceLink;
            rootsSpecified = document.RootsSpecified;
            roots = new ResolvedConfigurationValue<ImmutableSortedSet<string>>(document.Roots,
                document.RootsSpecified ? ConfigurationProvenance.ConfigurationFile : ConfigurationProvenance.Default);
            diagramBudget = ResolveDiagramBudget(document.Diagrams);
        }

        if (request.Profile is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Profile))
            {
                return Failure("SD3005", "The selected profile name is empty.", "profile", "Profile selection requires a non-empty name.", "Provide a named profile or omit profile selection.");
            }

            if (document is null || !document.Profiles.TryGetValue(request.Profile, out var profile))
            {
                return Failure("SD3006", "The selected configuration profile does not exist.", request.Profile, "No exact, case-sensitive profile name matched the selection.", "Select a profile declared in the configuration file.");
            }

            msbuildProperties = ResolveMap(profile.MsBuildProperties, ConfigurationProvenance.NamedProfile);
            knownValues = ResolveMap(profile.KnownValues, ConfigurationProvenance.NamedProfile);
        }

        PassAConfigurationOverrides? commandLine = request.CommandLineOverrides;
        if (commandLine is not null)
        {
            configuration = Overlay(commandLine.Configuration, configuration, ConfigurationProvenance.CommandLine);
            targetFramework = Overlay(commandLine.TargetFramework, targetFramework, ConfigurationProvenance.CommandLine);
            runtimeIdentifier = Overlay(commandLine.RuntimeIdentifier, runtimeIdentifier, ConfigurationProvenance.CommandLine);
            maxParallelism = OverlayInteger(commandLine.MaxParallelism, maxParallelism, ConfigurationProvenance.CommandLine);
            binaryAnalysis = Overlay(commandLine.BinaryAnalysis, binaryAnalysis, ConfigurationProvenance.CommandLine);
            sourceLink = Overlay(commandLine.SourceLink, sourceLink, ConfigurationProvenance.CommandLine);
            msbuildProperties = OverlayMap(msbuildProperties, commandLine.MsBuildProperties, ConfigurationProvenance.CommandLine);
            knownValues = OverlayMap(knownValues, commandLine.KnownValues, ConfigurationProvenance.CommandLine);
        }

        AnalysisDiagnostic? invalidMap = ValidateMap(msbuildProperties, "msbuildProperties")
            ?? ValidateMap(knownValues, "knownValues");
        if (invalidMap is not null)
        {
            return ApplicationResult.Failure<ResolvedPassAConfiguration>(ApplicationOutcome.InvalidInput, [invalidMap]);
        }

        AnalysisDiagnostic? invalid = ValidateResolved(configuration, targetFramework, runtimeIdentifier, maxParallelism, binaryAnalysis, sourceLink);
        var excludeParticipants = new ResolvedConfigurationValue<ImmutableSortedSet<string>>(
            document?.ExcludeParticipants ?? ImmutableSortedSet.Create<string>(StringComparer.Ordinal),
            document is null ? ConfigurationProvenance.Default : ConfigurationProvenance.ConfigurationFile);
        var excludeCalls = new ResolvedConfigurationValue<ImmutableSortedSet<string>>(
            document?.ExcludeCalls ?? ImmutableSortedSet.Create<string>(StringComparer.Ordinal),
            document is null ? ConfigurationProvenance.Default : ConfigurationProvenance.ConfigurationFile);
        if (commandLine?.ExcludeParticipants is not null)
        {
            excludeParticipants = new(commandLine.ExcludeParticipants, ConfigurationProvenance.CommandLine);
        }
        if (commandLine?.ExcludeCalls is not null)
        {
            excludeCalls = new(commandLine.ExcludeCalls, ConfigurationProvenance.CommandLine);
        }
        AnalysisDiagnostic? invalidExclusion = ValidateExclusions(excludeParticipants.Value, excludeCalls.Value);
        if (invalidExclusion is not null)
        {
            return ApplicationResult.Failure<ResolvedPassAConfiguration>(ApplicationOutcome.InvalidInput, [invalidExclusion]);
        }
        return invalid is null
            ? ApplicationResult.Success(new ResolvedPassAConfiguration(
                configuration,
                targetFramework,
                runtimeIdentifier,
                maxParallelism,
                binaryAnalysis,
                sourceLink,
                roots,
                msbuildProperties,
                knownValues,
                rootsSpecified,
                excludeParticipants,
                excludeCalls)
            { DiagramBudget = diagramBudget })
            : ApplicationResult.Failure<ResolvedPassAConfiguration>(ApplicationOutcome.InvalidInput, [invalid]);
    }

    private static AnalysisDiagnostic? ValidateResolved(
        ResolvedConfigurationValue<string> configuration,
        ResolvedConfigurationValue<string?> targetFramework,
        ResolvedConfigurationValue<string?> runtimeIdentifier,
        ResolvedConfigurationValue<int> maxParallelism,
        ResolvedConfigurationValue<string?> binaryAnalysis,
        ResolvedConfigurationValue<string?> sourceLink)
    {
        if (string.IsNullOrWhiteSpace(configuration.Value))
        {
            return CreateDiagnostic("SD3007", "The resolved build configuration is empty.", "analysis.configuration", "A configuration value must contain text.", "Provide a build configuration such as Release.");
        }

        if (maxParallelism.Value < 1)
        {
            return CreateDiagnostic("SD3008", "The resolved maximum parallelism is invalid.", "analysis.maxParallelism", "Maximum parallelism must be at least 1.", "Provide a positive maximum parallelism value.");
        }

        if (binaryAnalysis.Value is not null
            && !string.Equals(binaryAnalysis.Value, "metadata-only", StringComparison.Ordinal))
        {
            return CreateDiagnostic("SD3012", "The binary-analysis mode is unsupported in Pass A.", "analysis.binaryAnalysis", $"Mode '{binaryAnalysis.Value}' is not available.", "Use 'metadata-only' or omit the setting.");
        }

        if (sourceLink.Value is not null
            && !string.Equals(sourceLink.Value, "offline", StringComparison.Ordinal))
        {
            return CreateDiagnostic("SD3013", "The Source Link mode is unsupported in Pass A.", "analysis.sourceLink", $"Mode '{sourceLink.Value}' is not available.", "Use 'offline' or omit the setting.");
        }

        foreach ((string path, string? value) in new[]
                 {
                     ("analysis.targetFramework", targetFramework.Value),
                     ("analysis.runtimeIdentifier", runtimeIdentifier.Value),
                     ("analysis.binaryAnalysis", binaryAnalysis.Value),
                     ("analysis.sourceLink", sourceLink.Value),
                 })
        {
            if (value is not null && string.IsNullOrWhiteSpace(value))
            {
                return CreateDiagnostic("SD3009", "A command-line configuration value is empty.", path, "An explicitly supplied value must contain text.", "Provide a non-empty value or omit the override.");
            }
        }

        return null;
    }

    private static AnalysisDiagnostic? ValidateExclusions(
        ImmutableSortedSet<string> participants,
        ImmutableSortedSet<string> calls)
    {
        foreach (string value in participants)
        {
            if (!IsCanonicalType(value))
            {
                return CreateDiagnostic("SD3014", "A participant exclusion pattern is malformed.",
                    "documentation.excludeParticipants", $"'{value}' is not a non-empty dot-separated type identity.",
                    "Use a canonical containing type without wildcard characters.");
            }
        }

        foreach (string value in calls)
        {
            int separator = value.LastIndexOf('.');
            bool wildcard = value.EndsWith(".*", StringComparison.Ordinal);
            string type = separator > 0 ? value[..separator] : string.Empty;
            string member = separator >= 0 && !wildcard ? value[(separator + 1)..] : string.Empty;
            if (!IsCanonicalType(type) || (!wildcard && !IsCanonicalMember(member)) ||
                (wildcard && value.Count(character => character == '*') != 1))
            {
                return CreateDiagnostic("SD3015", "A call exclusion pattern is malformed.",
                    "documentation.excludeCalls", $"'{value}' is not an exact Type.Member or Type.* pattern.",
                    "Use non-empty dot-separated canonical segments and only a trailing Type.* wildcard.");
            }
        }

        return null;
    }

    private static bool IsCanonicalType(string value)
        => value.Length > 0 && value.Split('.').All(segment => !string.IsNullOrWhiteSpace(segment)
            && !segment.Any(char.IsWhiteSpace)
            && !segment.Contains('*'));

    private static bool IsCanonicalMember(string value)
        => value.Length > 0 && !value.Any(char.IsWhiteSpace) && !value.Contains('*');

    private static AnalysisDiagnostic? ValidateMap(
        ImmutableSortedDictionary<string, ResolvedConfigurationValue<string>> values,
        string path)
    {
        foreach ((string key, ResolvedConfigurationValue<string> value) in values)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value.Value))
            {
                return CreateDiagnostic(
                    "SD3010",
                    "A command-line configuration map entry is empty.",
                    path,
                    "Map keys and values must contain text.",
                    "Remove the empty entry or provide a non-empty key and value.");
            }

            if (IsSensitiveKey(key))
            {
                return CreateDiagnostic(
                    "SD3011",
                    "A configuration property appears to contain secret material.",
                    $"{path}.{key}",
                    "Secret-bearing property names are not permitted in persisted analysis profiles.",
                    "Remove the secret and use a non-sensitive build selector instead.");
            }
        }

        return null;
    }

    private static bool IsSensitiveKey(string key)
    {
        string normalized = key.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
        return SensitiveKeyFragments.Any(fragment => normalized.Contains(fragment, StringComparison.Ordinal));
    }

    private static ResolvedConfigurationValue<T> Overlay<T>(
        T? overlay,
        ResolvedConfigurationValue<T> current,
        ConfigurationProvenance provenance) =>
        overlay is null ? current : new ResolvedConfigurationValue<T>(overlay, provenance);

    private static ResolvedConfigurationValue<int> OverlayInteger(
        int? overlay,
        ResolvedConfigurationValue<int> current,
        ConfigurationProvenance provenance) =>
        overlay is null ? current : new ResolvedConfigurationValue<int>(overlay.Value, provenance);

    private static ResolvedDiagramBudget ResolveDiagramBudget(DiagramSettings settings) => new(
        ResolveBudgetValue(settings.MaxExpandedMethods, SeqDoc.Core.Configuration.DiagramBudget.Default.MaxExpandedMethods),
        ResolveBudgetValue(settings.MaxExpandedCalls, SeqDoc.Core.Configuration.DiagramBudget.Default.MaxExpandedCalls),
        ResolveBudgetValue(settings.MaxMaterialMessages, SeqDoc.Core.Configuration.DiagramBudget.Default.MaxMaterialMessages),
        ResolveBudgetValue(settings.MaxParticipants, SeqDoc.Core.Configuration.DiagramBudget.Default.MaxParticipants),
        ResolveBudgetValue(settings.MaxMermaidCharacters, SeqDoc.Core.Configuration.DiagramBudget.Default.MaxMermaidCharacters));

    private static ResolvedDiagramBudget DefaultDiagramBudget() => ResolveDiagramBudget(
        new DiagramSettings(null, null, null, null, null));

    private static ResolvedConfigurationValue<int> ResolveBudgetValue(int? value, int defaultValue) =>
        new(value ?? defaultValue, value.HasValue ? ConfigurationProvenance.ConfigurationFile : ConfigurationProvenance.Default);

    private static ImmutableSortedDictionary<string, ResolvedConfigurationValue<string>> OverlayMap(
        ImmutableSortedDictionary<string, ResolvedConfigurationValue<string>> current,
        ImmutableSortedDictionary<string, string>? overlay,
        ConfigurationProvenance provenance)
    {
        if (overlay is null)
        {
            return current;
        }

        var builder = current.ToBuilder();
        foreach ((string key, string value) in overlay)
        {
            builder[key] = new ResolvedConfigurationValue<string>(value, provenance);
        }

        return builder.ToImmutable();
    }

    private static ImmutableSortedDictionary<string, ResolvedConfigurationValue<string>> ResolveMap(
        ImmutableSortedDictionary<string, string> values,
        ConfigurationProvenance provenance) =>
        values.ToImmutableSortedDictionary(
            pair => pair.Key,
            pair => new ResolvedConfigurationValue<string>(pair.Value, provenance),
            StringComparer.Ordinal);

    private static ImmutableSortedDictionary<string, ResolvedConfigurationValue<string>> EmptyResolvedMap() =>
        ImmutableSortedDictionary.Create<string, ResolvedConfigurationValue<string>>(StringComparer.Ordinal);

    private static ApplicationResult<ResolvedPassAConfiguration> Failure(
        string code,
        string summary,
        string location,
        string cause,
        string nextAction,
        Exception? exception = null) =>
        ApplicationResult.Failure<ResolvedPassAConfiguration>(
            ApplicationOutcome.InvalidInput,
            [CreateDiagnostic(code, summary, location, cause, nextAction, exception)]);

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
            AnalysisStage.Configuration,
            null,
            location,
            0));
        return new AnalysisDiagnostic(
            id,
            code,
            DiagnosticSeverity.Error,
            AnalysisStage.Configuration,
            summary,
            new DiagnosticLocation(location),
            cause,
            "No analysis configuration was produced.",
            nextAction,
            CertaintyLevel.Exact,
            internalDetail: exception?.GetType().FullName);
    }
}
