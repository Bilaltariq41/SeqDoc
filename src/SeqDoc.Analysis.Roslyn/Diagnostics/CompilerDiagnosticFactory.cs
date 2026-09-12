using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using SeqDoc.Analysis.Roslyn.Workspace;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using CoreDiagnosticSeverity = SeqDoc.Core.Diagnostics.DiagnosticSeverity;
using StableProjectId = SeqDoc.Core.Identity.ProjectId;

namespace SeqDoc.Analysis.Roslyn.Diagnostics;

internal static class CompilerDiagnosticFactory
{
    public static AnalysisDiagnostic CreateInput(
        string code,
        string summary,
        string cause,
        string nextAction,
        CompilationProfileId? profile = null)
    {
        return Create(
            code,
            CoreDiagnosticSeverity.Error,
            AnalysisStage.ProfileResolution,
            summary,
            new DiagnosticLocation("analysis target", profile),
            cause,
            "No compilation or Program Index was produced.",
            nextAction,
            profile,
            null,
            0);
    }

    public static AnalysisDiagnostic CreateProfileResolution(
        string code,
        CoreDiagnosticSeverity severity,
        string summary,
        string location,
        string technicalCause,
        string userImpact,
        string nextAction,
        string subjectId,
        string? internalDetail = null)
    {
        return Create(
            code,
            severity,
            AnalysisStage.ProfileResolution,
            summary,
            new DiagnosticLocation(location),
            technicalCause,
            userImpact,
            nextAction,
            null,
            subjectId,
            0,
            internalDetail);
    }

    public static ImmutableArray<AnalysisDiagnostic> CreateWorkspace(
        IEnumerable<WorkspaceDiagnostic> workspaceDiagnostics,
        CompilationProfileId profile,
        Func<string, string?, bool>? isWarningPromoted = null,
        string? repositoryRoot = null)
    {
        var ordered = workspaceDiagnostics
            .OrderBy(diagnostic => diagnostic.Kind)
            .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ToArray();

        return ordered.Select((diagnostic, ordinal) =>
        {
            var effectiveKind = WorkspaceDiagnosticClassifier.GetEffectiveKind(diagnostic, isWarningPromoted);
            return Create(
                "SD1101",
                effectiveKind == WorkspaceDiagnosticKind.Failure
                    ? CoreDiagnosticSeverity.Error
                    : CoreDiagnosticSeverity.Warning,
                AnalysisStage.WorkspaceLoad,
                effectiveKind == WorkspaceDiagnosticKind.Failure
                    ? "MSBuild could not load part of the selected project graph."
                    : "MSBuild reported a workspace warning.",
                new DiagnosticLocation("MSBuild workspace", profile),
                 ConfineDiagnosticText(diagnostic.Message, repositoryRoot),
                effectiveKind == WorkspaceDiagnosticKind.Failure
                    ? "The compiler gate failed and no Program Index was produced."
                    : "Analysis can continue, but the project may not match the intended build.",
                WorkspaceDiagnosticClassifier.IsNuGetAuditWarning(diagnostic)
                    ? "Review the advisory and update or explicitly suppress the affected package according to repository policy."
                    : WorkspaceDiagnosticClassifier.IsPackageTfmSupportWarning(diagnostic)
                        ? "Review the package's target-framework support and upgrade the project or package where practical."
                        : "Restore and build the selected target with the repository's pinned SDK, then retry.",
                profile,
                null,
                ordinal);
        }).ToImmutableArray();
    }

    public static ImmutableArray<AnalysisDiagnostic> CreateCompiler(
        IEnumerable<(Diagnostic Diagnostic, StableProjectId Project)> compilerDiagnostics,
        CompilationProfileId profile,
        string? repositoryRoot = null)
    {
        var ordered = compilerDiagnostics
            .OrderBy(item => item.Diagnostic.Id, StringComparer.Ordinal)
            .ThenBy(item => item.Diagnostic.Location.GetLineSpan().Path, StringComparer.Ordinal)
            .ThenBy(item => item.Diagnostic.Location.SourceSpan.Start)
            .ThenBy(item => item.Diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparer.Ordinal)
            .ThenBy(item => item.Project.Value, StringComparer.Ordinal)
            .ToArray();

        return ordered.Select((item, ordinal) =>
        {
            var lineSpan = item.Diagnostic.Location.GetLineSpan();
            var description = lineSpan.IsValid
                ? $"{ConfinePath(lineSpan.Path, repositoryRoot)}({lineSpan.StartLinePosition.Line + 1},{lineSpan.StartLinePosition.Character + 1})"
                : "compiler";

            return Create(
                item.Diagnostic.Id,
                CoreDiagnosticSeverity.Error,
                AnalysisStage.CompilationValidation,
                item.Diagnostic.GetMessage(CultureInfo.InvariantCulture),
                new DiagnosticLocation(description, profile, item.Project),
                 ConfineDiagnosticText(item.Diagnostic.ToString(), repositoryRoot),
                "The compiler gate failed and no Program Index was produced.",
                "Fix the compiler error using the selected configuration and target framework, then retry.",
                profile,
                item.Project.Value,
                ordinal);
        }).ToImmutableArray();
    }

    public static AnalysisDiagnostic CreateInfrastructure(
        string summary,
        Exception exception,
        CompilationProfileId? profile)
    {
        return Create(
            "SD1102",
            CoreDiagnosticSeverity.Error,
            AnalysisStage.WorkspaceLoad,
            summary,
            new DiagnosticLocation("MSBuild workspace", profile),
            exception.Message,
            "The compiler gate failed and no Program Index was produced.",
            "Confirm the pinned SDK is installed, restore the target, and retry in a fresh process.",
            profile,
            null,
            0,
            exception.ToString());
    }

    public static AnalysisDiagnostic CreateIndexFailure(
        Exception exception,
        CompilationProfileId profile)
    {
        return Create(
            "SD1301",
            CoreDiagnosticSeverity.Error,
            AnalysisStage.BaselineIndex,
            "The validated compilation could not be converted into a Program Index.",
            new DiagnosticLocation("baseline Program Index", profile),
            exception.Message,
            "No Program Index was produced.",
            "Report the failure with the internal diagnostic detail and analyzed project shape.",
            profile,
            null,
            0,
            exception.ToString());
    }

    private static AnalysisDiagnostic Create(
        string code,
        CoreDiagnosticSeverity severity,
        AnalysisStage stage,
        string summary,
        DiagnosticLocation location,
        string technicalCause,
        string userImpact,
        string nextAction,
        CompilationProfileId? profile,
        string? subjectId,
        int ordinal,
        string? internalDetail = null)
    {
        var id = StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            code,
            stage,
            profile,
            subjectId,
            ordinal));

        return new AnalysisDiagnostic(
            id,
            code,
            severity,
            stage,
            summary,
            location,
            technicalCause,
            userImpact,
            nextAction,
            CertaintyLevel.Exact,
            internalDetail: internalDetail);
    }

    private static string ConfineDiagnosticText(string text, string? repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            return text;
        }

        var builder = new System.Text.StringBuilder(text.Length);
        var position = 0;
        while (position < text.Length)
        {
            if (IsUrlStart(text, position))
            {
                var urlEnd = position;
                while (urlEnd < text.Length && !char.IsWhiteSpace(text[urlEnd]))
                {
                    urlEnd++;
                }

                builder.Append(text[position..urlEnd]);
                position = urlEnd;
                continue;
            }

            if (!TryReadAbsolutePath(text, position, out var end)
                || IsUrlPath(text, position))
            {
                builder.Append(text[position++]);
                continue;
            }

            builder.Append(ConfinePath(text[position..end], repositoryRoot));
            position = end;
        }

        return builder.ToString();
    }

    private static bool IsUrlStart(string text, int start)
    {
        var separator = text.IndexOf("://", start, StringComparison.Ordinal);
        if (separator <= start)
        {
            return false;
        }

        for (var position = start; position < separator; position++)
        {
            if (!char.IsLetter(text[position]))
            {
                return false;
            }
        }

        return true;
    }

    private static string ConfinePath(string path, string? repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot) || !IsAbsolutePath(path))
        {
            return path;
        }

        var root = NormalizePath(repositoryRoot);
        var candidate = NormalizePath(path);
        if (root is null || candidate is null)
        {
            return "<external-path>";
        }

        var comparison = IsWindowsPath(root) || IsWindowsPath(candidate)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!candidate.Equals(root, comparison)
            && !(root == "/" ? candidate.StartsWith('/') : candidate.StartsWith(root + "/", comparison)))
        {
            return "<external-path>";
        }

        var relative = candidate[root.Length..].TrimStart('/');
        return relative;
    }

    private static bool TryReadAbsolutePath(string text, int start, out int end)
    {
        end = start;
        if (!IsAbsolutePathStart(text, start) || (start > 0 && text[start - 1] == ':'))
        {
            return false;
        }

        var position = start;
        var quoted = start > 0 && (text[start - 1] == '"' || text[start - 1] == '\'');
        if (quoted)
        {
            var quote = text[start - 1];
            var close = text.IndexOf(quote, start);
            if (close > start)
            {
                end = close;
                return true;
            }
        }

        while (position < text.Length && !char.IsWhiteSpace(text[position])
            && !"\"'<>|".Contains(text[position]))
        {
            position++;
        }

        var locationDelimiter = FindCompilerLocationDelimiter(text, start);
        if (locationDelimiter > start && (position == text.Length || locationDelimiter > position))
        {
            position = locationDelimiter;
        }

        while (position > start && ".,;:!?]}".Contains(text[position - 1]))
        {
            position--;
        }

        var parenthesis = text.IndexOf('(', start, position - start);
        if (parenthesis > start)
        {
            position = parenthesis;
        }

        end = position;
        return end > start;
    }

    private static int FindCompilerLocationDelimiter(string text, int start)
    {
        for (var position = start; position < text.Length; position++)
        {
            if (text[position] != '(')
            {
                continue;
            }

            var close = text.IndexOf(')', position + 1);
            if (close <= position || close + 1 >= text.Length || text[close + 1] != ':')
            {
                continue;
            }

            var location = text[(position + 1)..close];
            var comma = location.IndexOf(',');
            if (comma > 0
                && int.TryParse(location[..comma], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                && int.TryParse(location[(comma + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                return position;
            }
        }

        return -1;
    }

    private static bool IsUrlPath(string text, int start)
    {
        var boundary = start - 1;
        while (boundary >= 0 && !char.IsWhiteSpace(text[boundary]))
        {
            boundary--;
        }

        return text[(boundary + 1)..Math.Min(start + 1, text.Length)].Contains("://", StringComparison.Ordinal);
    }

    private static bool IsAbsolutePath(string path) =>
        path.StartsWith('/') || path.StartsWith('\\')
        || (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':'
            && (path[2] == '/' || path[2] == '\\'));

    private static bool IsAbsolutePathStart(string text, int start) =>
        text[start] == '/' || text[start] == '\\'
        || (start + 2 < text.Length && char.IsLetter(text[start]) && text[start + 1] == ':'
            && (text[start + 2] == '/' || text[start + 2] == '\\'));

    private static string? NormalizePath(string path)
    {
        var value = path.Replace('\\', '/');
        var windows = value.Length >= 2 && value[1] == ':';
        var prefix = windows ? value[..2] : value.StartsWith("//", StringComparison.Ordinal) ? "//" : "/";
        var remainder = value[prefix.Length..];
        var segments = new List<string>();
        foreach (var segment in remainder.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }
            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    return null;
                }
                segments.RemoveAt(segments.Count - 1);
            }
            else
            {
                segments.Add(segment);
            }
        }

        var normalized = prefix + string.Join("/", segments);
        return normalized.Length > prefix.Length && normalized.EndsWith('/')
            ? normalized.TrimEnd('/')
            : normalized;
    }

    private static bool IsWindowsPath(string path) => path.Length >= 2 && path[1] == ':'
        || path.StartsWith("//", StringComparison.Ordinal);
}
