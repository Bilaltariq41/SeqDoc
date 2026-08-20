using System.Collections.Immutable;
using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace SeqDoc.Configuration;

internal sealed record AnalysisSettings(
    string? Configuration,
    string? TargetFramework,
    string? RuntimeIdentifier,
    int? MaxParallelism,
    string? BinaryAnalysis,
    string? SourceLink,
    ImmutableSortedSet<string> SpecifiedFields);

internal sealed record DiagramSettings(
    int? MaxExpandedMethods,
    int? MaxExpandedCalls,
    int? MaxMaterialMessages,
    int? MaxParticipants,
    int? MaxMermaidCharacters);

internal sealed record NamedProfileSettings(
    ImmutableSortedDictionary<string, string> MsBuildProperties,
    ImmutableSortedDictionary<string, string> KnownValues);

internal sealed record YamlConfigurationDocument(
    AnalysisSettings Analysis,
    ImmutableSortedDictionary<string, NamedProfileSettings> Profiles,
    ImmutableSortedSet<string> Roots,
    bool RootsSpecified,
    ImmutableSortedSet<string> ExcludeParticipants,
    ImmutableSortedSet<string> ExcludeCalls,
    DiagramSettings Diagrams)
{
    private static readonly StringComparer KeyComparer = StringComparer.Ordinal;

    public static YamlConfigurationDocument Parse(string yaml)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(yaml);
        stream.Load(reader);
        if (stream.Documents.Count != 1)
        {
            throw new ConfigurationFormatException("$", "Configuration must contain exactly one YAML document.");
        }

        var root = RequireMapping(stream.Documents[0].RootNode, "$", allowEmptyScalar: true);
        var fields = ReadFields(root, "$", [
            "schemaVersion", "analysis", "profiles", "documentation", "selection", "participants",
            "terms", "conditions", "runtimeBindings", "diagrams",
        ]);

        if (!fields.TryGetValue("schemaVersion", out var schemaNode))
        {
            throw new ConfigurationFormatException("$.schemaVersion", "Existing configuration files require schemaVersion 1.");
        }

        int schemaVersion = RequireInteger(schemaNode, "$.schemaVersion", minimum: 1);
        if (schemaVersion != 1)
        {
            throw new ConfigurationFormatException("$.schemaVersion", $"Schema version {schemaVersion} is unsupported; expected 1.");
        }

        AnalysisSettings analysis = fields.TryGetValue("analysis", out var analysisNode)
            ? ParseAnalysis(analysisNode)
            : new(null, null, null, null, null, null, ImmutableSortedSet<string>.Empty);
        var profiles = fields.TryGetValue("profiles", out var profilesNode)
            ? ParseProfiles(profilesNode)
            : EmptyProfiles();

        var (roots, rootsSpecified, excludeParticipants, excludeCalls, diagrams) = ValidateLaterSections(fields);
        return new YamlConfigurationDocument(analysis, profiles, roots, rootsSpecified, excludeParticipants, excludeCalls, diagrams);
    }

    private static AnalysisSettings ParseAnalysis(YamlNode node)
    {
        var fields = ReadFields(RequireMapping(node, "$.analysis"), "$.analysis", [
            "configuration", "targetFramework", "runtimeIdentifier", "maxParallelism", "binaryAnalysis", "sourceLink",
        ]);
        return new AnalysisSettings(
            OptionalString(fields, "configuration", "$.analysis.configuration", allowNull: false),
            OptionalString(fields, "targetFramework", "$.analysis.targetFramework"),
            OptionalString(fields, "runtimeIdentifier", "$.analysis.runtimeIdentifier"),
            fields.TryGetValue("maxParallelism", out var parallelism)
                ? RequireInteger(parallelism, "$.analysis.maxParallelism", minimum: 1)
                : null,
            OptionalString(fields, "binaryAnalysis", "$.analysis.binaryAnalysis", allowNull: false),
            OptionalString(fields, "sourceLink", "$.analysis.sourceLink", allowNull: false),
            fields.Keys.ToImmutableSortedSet(KeyComparer));
    }

    private static ImmutableSortedDictionary<string, NamedProfileSettings> ParseProfiles(YamlNode node)
    {
        var mapping = RequireMapping(node, "$.profiles");
        var result = ImmutableSortedDictionary.CreateBuilder<string, NamedProfileSettings>(KeyComparer);
        foreach ((YamlNode keyNode, YamlNode valueNode) in mapping.Children)
        {
            string name = RequireNonEmptyKey(keyNode, "$.profiles");
            string path = $"$.profiles.{name}";
            if (result.ContainsKey(name))
            {
                throw new ConfigurationFormatException(path, "Duplicate profile names are not allowed.");
            }

            var fields = ReadFields(RequireMapping(valueNode, path), path, ["msbuildProperties", "knownValues"]);
            result.Add(name, new NamedProfileSettings(
                fields.TryGetValue("msbuildProperties", out var properties)
                    ? ParseStringMap(properties, $"{path}.msbuildProperties")
                    : EmptyStrings(),
                fields.TryGetValue("knownValues", out var knownValues)
                    ? ParseStringMap(knownValues, $"{path}.knownValues")
                    : EmptyStrings()));
        }

        return result.ToImmutable();
    }

    private static (ImmutableSortedSet<string> Roots, bool Specified, ImmutableSortedSet<string> ExcludeParticipants, ImmutableSortedSet<string> ExcludeCalls, DiagramSettings Diagrams) ValidateLaterSections(Dictionary<string, YamlNode> root)
    {
        var roots = ImmutableSortedSet.Create<string>(KeyComparer);
        var excludeParticipants = ImmutableSortedSet.Create<string>(KeyComparer);
        var excludeCalls = ImmutableSortedSet.Create<string>(KeyComparer);
        var diagramSettings = new DiagramSettings(null, null, null, null, null);
        bool rootsSpecified = false;
        if (root.TryGetValue("documentation", out var documentation))
        {
            var fields = ReadFields(RequireMapping(documentation, "$.documentation"), "$.documentation", [
                "outputDirectory", "repositoryProfile", "coverageMode", "includeComprehensiveAppendix",
                "excludeParticipants", "excludeCalls",
            ]);
            ValidateOptionalStrings(fields, "$.documentation", "outputDirectory", "repositoryProfile", "coverageMode");
            ValidateOptionalBoolean(fields, "includeComprehensiveAppendix", "$.documentation.includeComprehensiveAppendix");
            excludeParticipants = OptionalDocumentationSet(fields, "excludeParticipants", "$.documentation.excludeParticipants", participant: true);
            excludeCalls = OptionalDocumentationSet(fields, "excludeCalls", "$.documentation.excludeCalls", participant: false);
        }

        if (root.TryGetValue("selection", out var selection))
        {
            var fields = ReadFields(RequireMapping(selection, "$.selection"), "$.selection", ["include", "exclude", "critical", "roots"]);
            var include = OptionalStringSet(fields, "include", "$.selection.include");
            // Flow selection exclusions are intentionally validated for the selection contract but
            // are not presentation filters.
            var selectionExclude = OptionalStringSet(fields, "exclude", "$.selection.exclude");
            var critical = OptionalStringSet(fields, "critical", "$.selection.critical");
            rootsSpecified = fields.ContainsKey("roots");
            roots = OptionalRootStringSet(fields, "roots", "$.selection.roots");
            string? conflict = critical.Intersect(selectionExclude, KeyComparer).Order(KeyComparer).FirstOrDefault();
            if (conflict is not null)
            {
                throw new ConfigurationFormatException("$.selection", $"Flow '{conflict}' cannot be both critical and excluded.");
            }

            _ = include;
        }

        if (root.TryGetValue("participants", out var participants))
        {
            ValidateObjectMap(participants, "$.participants", ["displayName"], required: ["displayName"]);
        }

        if (root.TryGetValue("terms", out var terms))
        {
            _ = ParseStringMap(terms, "$.terms");
        }

        if (root.TryGetValue("conditions", out var conditions))
        {
            ValidateObjectMap(conditions, "$.conditions", ["positive", "negative"], required: ["positive", "negative"]);
        }

        if (root.TryGetValue("runtimeBindings", out var bindings))
        {
            ValidateRuntimeBindings(bindings);
        }

        if (root.TryGetValue("diagrams", out var diagramsNode))
        {
            var fields = ReadFields(RequireMapping(diagramsNode, "$.diagrams"), "$.diagrams", [
                "maxExpandedMethods", "maxExpandedCalls", "maxParticipants", "maxMaterialMessages", "maxMermaidCharacters",
                "maxFragmentDepth", "processingColor", "successColor",
                "recoveryColor", "warningColor", "terminalFailureColor",
            ]);
            foreach (string key in new[] { "maxExpandedMethods", "maxExpandedCalls", "maxParticipants", "maxMaterialMessages", "maxMermaidCharacters", "maxFragmentDepth" })
            {
                if (fields.TryGetValue(key, out var value))
                {
                    _ = RequireInteger(value, $"$.diagrams.{key}", minimum: 1);
                }
            }

            diagramSettings = new DiagramSettings(
                OptionalInteger(fields, "maxExpandedMethods", "$.diagrams.maxExpandedMethods"),
                OptionalInteger(fields, "maxExpandedCalls", "$.diagrams.maxExpandedCalls"),
                OptionalInteger(fields, "maxMaterialMessages", "$.diagrams.maxMaterialMessages"),
                OptionalInteger(fields, "maxParticipants", "$.diagrams.maxParticipants"),
                OptionalInteger(fields, "maxMermaidCharacters", "$.diagrams.maxMermaidCharacters"));

            ValidateOptionalStrings(fields, "$.diagrams", "processingColor", "successColor", "recoveryColor", "warningColor", "terminalFailureColor");
        }

        return (roots, rootsSpecified, excludeParticipants, excludeCalls, diagramSettings);
    }

    private static ImmutableSortedSet<string> OptionalDocumentationSet(
        Dictionary<string, YamlNode> fields, string key, string path, bool participant)
    {
        if (!fields.TryGetValue(key, out var node))
        {
            return ImmutableSortedSet.Create<string>(KeyComparer);
        }

        var values = RequireUniqueStringSet(node, path);
        foreach (string value in values)
        {
            if (participant)
            {
                if (value is "action" or "client" or "caller" or "dispatch" or "handler" or "service" or "data"
                    || !IsCanonicalTypePattern(value))
                {
                    throw new ConfigurationFormatException(path, $"excludeParticipants contains structural or malformed participant exclusion '{value}'.");
                }
            }
            else
            {
                int separator = value.LastIndexOf('.');
                bool wildcard = value.EndsWith(".*", StringComparison.Ordinal);
                string typePattern = value[..Math.Max(separator, 0)];
                string memberPattern = wildcard ? string.Empty : value[(separator + 1)..];
                if (separator <= 0 || separator == value.Length - 1
                    || !IsCanonicalTypePattern(typePattern)
                    || (!wildcard && !IsCanonicalMemberPattern(memberPattern))
                    || (wildcard && value.Count(character => character == '*') != 1))
                {
                    throw new ConfigurationFormatException(path, $"Malformed call exclusion '{value}'. Expected Type.Member or Type.*.");
                }
            }
        }

        return values;
    }

    private static bool IsCanonicalTypePattern(string value)
        => value.Length > 0
            && value.Split('.').All(segment => !string.IsNullOrWhiteSpace(segment)
                && !segment.Any(char.IsWhiteSpace)
                && !segment.Contains('*'));

    private static bool IsCanonicalMemberPattern(string value)
        => value.Length > 0
            && !value.Any(char.IsWhiteSpace)
            && !value.Contains('*');

    private static void ValidateObjectMap(
        YamlNode node,
        string path,
        IEnumerable<string> allowed,
        IEnumerable<string> required)
    {
        var mapping = RequireMapping(node, path);
        foreach ((YamlNode keyNode, YamlNode valueNode) in mapping.Children)
        {
            string key = RequireNonEmptyKey(keyNode, path);
            string itemPath = $"{path}.{key}";
            var fields = ReadFields(RequireMapping(valueNode, itemPath), itemPath, allowed);
            foreach (string field in required)
            {
                if (!fields.TryGetValue(field, out var fieldNode))
                {
                    throw new ConfigurationFormatException($"{itemPath}.{field}", $"Required key '{field}' is missing.");
                }

                _ = RequireString(fieldNode, $"{itemPath}.{field}", allowNull: false);
            }
        }
    }

    private static void ValidateRuntimeBindings(YamlNode node)
    {
        var mapping = RequireMapping(node, "$.runtimeBindings");
        foreach ((YamlNode keyNode, YamlNode valueNode) in mapping.Children)
        {
            string key = RequireNonEmptyKey(keyNode, "$.runtimeBindings");
            string path = $"$.runtimeBindings.{key}";
            var fields = ReadFields(RequireMapping(valueNode, path), path, ["implementation", "profiles", "reason"]);
            foreach (string required in new[] { "implementation", "profiles", "reason" })
            {
                if (!fields.ContainsKey(required))
                {
                    throw new ConfigurationFormatException($"{path}.{required}", $"Required key '{required}' is missing.");
                }
            }

            _ = RequireString(fields["implementation"], $"{path}.implementation", allowNull: false);
            _ = RequireString(fields["reason"], $"{path}.reason", allowNull: false);
            if (RequireUniqueStringSet(fields["profiles"], $"{path}.profiles").Count == 0)
            {
                throw new ConfigurationFormatException($"{path}.profiles", "At least one profile is required.");
            }
        }
    }

    private static ImmutableSortedDictionary<string, string> ParseStringMap(YamlNode node, string path)
    {
        var mapping = RequireMapping(node, path);
        var result = ImmutableSortedDictionary.CreateBuilder<string, string>(KeyComparer);
        foreach ((YamlNode keyNode, YamlNode valueNode) in mapping.Children)
        {
            string key = RequireNonEmptyKey(keyNode, path);
            if (result.ContainsKey(key))
            {
                throw new ConfigurationFormatException($"{path}.{key}", "Duplicate keys are not allowed.");
            }

            result.Add(key, RequireString(valueNode, $"{path}.{key}", allowNull: false));
        }

        return result.ToImmutable();
    }

    private static Dictionary<string, YamlNode> ReadFields(YamlMappingNode mapping, string path, IEnumerable<string> allowed)
    {
        var allowedSet = allowed.ToHashSet(KeyComparer);
        var result = new Dictionary<string, YamlNode>(KeyComparer);
        foreach ((YamlNode keyNode, YamlNode valueNode) in mapping.Children)
        {
            string key = RequireNonEmptyKey(keyNode, path);
            if (!allowedSet.Contains(key))
            {
                throw new ConfigurationFormatException($"{path}.{key}", $"Unknown key '{key}'.");
            }

            if (!result.TryAdd(key, valueNode))
            {
                throw new ConfigurationFormatException($"{path}.{key}", $"Duplicate key '{key}'.");
            }
        }

        return result;
    }

    private static YamlMappingNode RequireMapping(YamlNode node, string path, bool allowEmptyScalar = false)
    {
        if (node is YamlMappingNode mapping)
        {
            return mapping;
        }

        if (allowEmptyScalar && node is YamlScalarNode { Value: null })
        {
            return new YamlMappingNode();
        }

        throw new ConfigurationFormatException(path, "Expected an object.");
    }

    private static string RequireNonEmptyKey(YamlNode node, string path)
    {
        string value = RequireString(node, path, allowNull: false);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ConfigurationFormatException(path, "Keys cannot be empty.");
        }

        return value;
    }

    private static string RequireString(YamlNode node, string path, bool allowNull)
    {
        if (node is not YamlScalarNode scalar)
        {
            throw new ConfigurationFormatException(path, "Expected a string.");
        }

        if (IsNull(scalar))
        {
            if (allowNull)
            {
                return null!;
            }

            throw new ConfigurationFormatException(path, "A non-null string is required.");
        }

        if (string.IsNullOrWhiteSpace(scalar.Value))
        {
            throw new ConfigurationFormatException(path, "A non-empty string is required.");
        }

        return scalar.Value;
    }

    private static string? OptionalString(
        Dictionary<string, YamlNode> fields,
        string key,
        string path,
        bool allowNull = true) =>
        fields.TryGetValue(key, out var value) ? RequireString(value, path, allowNull) : null;

    private static int RequireInteger(YamlNode node, string path, int minimum)
    {
        if (node is not YamlScalarNode scalar
            || !int.TryParse(scalar.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            || value < minimum)
        {
            throw new ConfigurationFormatException(path, $"Expected an integer greater than or equal to {minimum}.");
        }

        return value;
    }

    private static int? OptionalInteger(Dictionary<string, YamlNode> fields, string key, string path) =>
        fields.TryGetValue(key, out var value) ? RequireInteger(value, path, minimum: 1) : null;

    private static void ValidateOptionalBoolean(Dictionary<string, YamlNode> fields, string key, string path)
    {
        if (fields.TryGetValue(key, out var node)
            && (node is not YamlScalarNode scalar || !bool.TryParse(scalar.Value, out _)))
        {
            throw new ConfigurationFormatException(path, "Expected true or false.");
        }
    }

    private static void ValidateOptionalStrings(Dictionary<string, YamlNode> fields, string path, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (fields.TryGetValue(key, out var node))
            {
                _ = RequireString(node, $"{path}.{key}", allowNull: false);
            }
        }
    }

    private static ImmutableSortedSet<string> OptionalStringSet(
        Dictionary<string, YamlNode> fields,
        string key,
        string path) =>
        fields.TryGetValue(key, out var node)
            ? RequireUniqueStringSet(node, path)
            : ImmutableSortedSet.Create<string>(KeyComparer);

    private static ImmutableSortedSet<string> OptionalRootStringSet(
        Dictionary<string, YamlNode> fields,
        string key,
        string path) =>
        fields.TryGetValue(key, out var node)
            ? RequireUniqueRootStringSet(node, path)
            : ImmutableSortedSet.Create<string>(KeyComparer);

    private static ImmutableSortedSet<string> RequireUniqueRootStringSet(YamlNode node, string path)
    {
        if (node is not YamlSequenceNode sequence)
        {
            throw new ConfigurationFormatException(path, "Expected a list of strings.");
        }

        var result = ImmutableSortedSet.CreateBuilder<string>(KeyComparer);
        foreach (YamlNode child in sequence.Children)
        {
            if (child is YamlScalarNode scalar
                && scalar.Style is ScalarStyle.Any or ScalarStyle.Plain
                && IsImplicitYamlTypedScalar(scalar.Value))
            {
                throw new ConfigurationFormatException(path, "Root values must be quoted YAML strings.");
            }

            string value = RequireString(child, path, allowNull: false);
            if (!result.Add(value))
            {
                throw new ConfigurationFormatException(path, $"Duplicate value '{value}' is not allowed.");
            }
        }

        return result.ToImmutable();
    }

    private static bool IsImplicitYamlTypedScalar(string? value) =>
        value is not null
        && (bool.TryParse(value, out _)
            || int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            || decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _));

    private static ImmutableSortedSet<string> RequireUniqueStringSet(YamlNode node, string path)
    {
        if (node is not YamlSequenceNode sequence)
        {
            throw new ConfigurationFormatException(path, "Expected a list of strings.");
        }

        var result = ImmutableSortedSet.CreateBuilder<string>(KeyComparer);
        foreach (YamlNode child in sequence.Children)
        {
            string value = RequireString(child, path, allowNull: false);
            if (!result.Add(value))
            {
                throw new ConfigurationFormatException(path, $"Duplicate value '{value}' is not allowed.");
            }
        }

        return result.ToImmutable();
    }

    private static bool IsNull(YamlScalarNode scalar) =>
        scalar.Value is null
        || string.Equals(scalar.Value, "null", StringComparison.OrdinalIgnoreCase)
        || scalar.Value == "~";

    private static ImmutableSortedDictionary<string, string> EmptyStrings() =>
        ImmutableSortedDictionary.Create<string, string>(KeyComparer);

    private static ImmutableSortedDictionary<string, NamedProfileSettings> EmptyProfiles() =>
        ImmutableSortedDictionary.Create<string, NamedProfileSettings>(KeyComparer);
}

internal sealed class ConfigurationFormatException(string path, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Path { get; } = path;
}
