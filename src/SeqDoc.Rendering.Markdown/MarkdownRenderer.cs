using System.Text;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Core.Wording;

namespace SeqDoc.Rendering.Markdown;

/// <summary>
/// Serializes a wording document and its diagram plan into Markdown with an embedded, structurally
/// validated Mermaid sequence diagram. The renderer performs no semantic inference: it orders and
/// formats the plan's phrases, retains the evidence and certainty every phrase carries, and always
/// emits canonical newlines.
/// </summary>
public static class MarkdownRenderer
{
    public static string RenderDocument(WordingDocument wording, DiagramPlan diagram)
    {
        ArgumentNullException.ThrowIfNull(wording);
        ArgumentNullException.ThrowIfNull(diagram);

        var builder = new StringBuilder();
        builder.Append("# ").Append(wording.Title).Append('\n').Append('\n');
        builder
            .Append("SeqDoc generated this documentation from compiler evidence. ")
            .Append("Every statement retains supporting evidence and explicit certainty.")
            .Append('\n')
            .Append('\n');
        builder.Append("## Sequence diagram").Append('\n').Append('\n');
        builder.Append("```mermaid").Append('\n');
        builder.Append(MermaidRenderer.Render(diagram)).Append('\n');
        builder.Append("```").Append('\n');
        builder.Append("## Behavior").Append('\n').Append('\n');
        foreach (var phrase in wording.Phrases)
        {
            if (phrase.Kind == WordingPhraseKind.TechnicalFallback)
            {
                continue;
            }

            AppendPhrase(builder, phrase);
        }

        var fallbacks = wording.Phrases
            .Where(item => item.Kind == WordingPhraseKind.TechnicalFallback)
            .ToArray();
        if (fallbacks.Length > 0)
        {
            builder.Append('\n').Append("## Technical fallback").Append('\n').Append('\n');
            foreach (var phrase in fallbacks)
            {
                AppendPhrase(builder, phrase);
            }
        }

        if (diagram.Diagnostics.Length > 0)
        {
            builder.Append('\n').Append("## Diagram diagnostics").Append('\n').Append('\n');
            foreach (var diagnostic in diagram.Diagnostics.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
            {
                builder.Append("- ").Append(diagnostic.Summary)
                    .Append(" _(code: ").Append(diagnostic.Code)
                    .Append("; detail: ").Append(diagnostic.Detail).Append(")_").Append('\n');
            }
        }

        return builder.ToString();
    }

    public static string RenderIndex(
        string profileId,
        string programIndexFingerprint,
        IReadOnlyList<(string OperationKey, string FileName)> documents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(programIndexFingerprint);
        ArgumentNullException.ThrowIfNull(documents);

        var builder = new StringBuilder();
        builder.Append("# SeqDoc Documentation Index").Append('\n').Append('\n');
        builder
            .Append("SeqDoc generated this index from the active analysis. ")
            .Append("Document links resolve to evidence-backed flows in this directory.")
            .Append('\n')
            .Append('\n');
        builder.Append("## Profile").Append('\n').Append('\n');
        builder.Append("- Profile: ").Append(profileId).Append('\n');
        builder.Append("- Program Index fingerprint: ").Append(programIndexFingerprint).Append('\n').Append('\n');
        builder.Append("## Documents").Append('\n').Append('\n');
        foreach (var document in documents.OrderBy(item => item.OperationKey, StringComparer.Ordinal))
        {
            builder.Append("- [").Append(EscapeMarkdown(document.OperationKey)).Append("](")
                .Append(document.FileName).Append(')').Append('\n');
        }

        return builder.ToString();
    }

    private static void AppendPhrase(StringBuilder builder, WordingPhrase phrase)
    {
        string evidence = string.Join(
            ", ",
            phrase.Evidence.Select(item => item.Artifact).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        builder.Append("- ").Append(phrase.Text)
            .Append(" _(certainty: ").Append(phrase.Certainty)
            .Append("; evidence: ").Append(evidence).Append(")_").Append('\n');
    }

    private static string EscapeMarkdown(string value) => value.Replace("[", "\\[").Replace("]", "\\]");
}
