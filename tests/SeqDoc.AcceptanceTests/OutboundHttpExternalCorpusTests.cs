using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SeqDoc.Cli;
using SeqDoc.Rendering.Markdown;
using SeqDoc.Testing;
using Xunit;
using Xunit.Sdk;

namespace SeqDoc.AcceptanceTests;

/// <summary>
/// QHTTP-B / issue #53 acceptance: the merged issue #54 direct <c>HttpClient</c> GET/POST boundary
/// semantics must reach the production CLI's visible Markdown/Mermaid for the frozen external
/// FraudManagement lane (corpus revision <c>7aabfef9…</c>, <c>Release/net9.0</c>) driven by a
/// two-root selection YAML (<c>AddComplaint</c> POST, <c>Lookups</c> GET).
///
/// This is acceptance-only. It authorizes zero production or semantic change. Every assertion is on the
/// first observable consumer (generated Markdown/Mermaid and the CLI <c>--json</c> diagnostic stream),
/// never on an intermediate fact. Producer/unit admission logic is already covered by PR #59
/// (<c>OutboundHttpCliTests</c> et al.) and is not re-asserted here.
///
/// Harness: in-process production CLI via <see cref="CliHost.RunAsync"/> with the exact
/// <c>analyze &lt;sln&gt; --repository-root … --config &lt;tmp yaml&gt; --configuration Release
/// --framework net9.0 --cache … --output … --json</c> contract, run twice against fresh cache/output
/// directories for the determinism claim. The shared <c>Provided/FraudManagement</c> checkout is
/// asserted to sit exactly on the frozen revision <c>7aabfef9…</c> with a clean scoped working tree
/// and is analysed IN PLACE (the pattern the existing <c>ServiceClientExternalCorpusTests</c>
/// FraudManagement lane already uses); it is never mutated (frozen-file SHA-256 checked before and
/// after, every CLI cache/output/YAML/render under a fresh OS temp root deleted on success and on
/// failure). A clean/normalised <c>git worktree</c> is deliberately not used: the frozen Program
/// Index fingerprint is tied to this in-place checkout's historical LF/CRLF line-ending mix, so a
/// normalised checkout yields a different fingerprint — an owner-accepted boundary for this lane.
///
/// Skip is legitimate ONLY when the whole Provided corpus is not installed. Wrong revision, drifted
/// frozen hashes, a missing/duplicated GET or POST boundary, changed unrelated diagnostics,
/// nondeterministic bytes or diagnostic order, a leaked URI/credential value, a dangling link, a
/// budget breach, Mermaid parse/render failure, external-file mutation, or a leftover temp/repo delta
/// are all LOUD failures.
/// </summary>
[CollectionDefinition(OutboundHttpExternalCorpusSuite.Name, DisableParallelization = true)]
public sealed class OutboundHttpExternalCorpusSuite : ICollectionFixture<OutboundHttpExternalCorpusFixture>
{
    public const string Name = "OutboundHttpExternalCorpus";
}

[Collection(OutboundHttpExternalCorpusSuite.Name)]
public sealed class OutboundHttpExternalCorpusTests
{
    private readonly OutboundHttpExternalCorpusFixture _lane;
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public OutboundHttpExternalCorpusTests(
        OutboundHttpExternalCorpusFixture lane,
        Xunit.Abstractions.ITestOutputHelper output)
    {
        _lane = lane;
        _output = output;
    }

    private const string PostBehaviorPhrase =
        "The method calls HttpClient.PostAsync at an outbound HTTP POST request boundary.";
    private const string GetBehaviorPhrase =
        "The method calls HttpClient.GetAsync at an outbound HTTP GET request boundary.";
    private const string ExternalParticipantLabel = "HTTP boundary";
    private const string PostMermaidMessage = "HTTP POST request";
    private const string GetMermaidMessage = "HTTP GET request";
    private const string UnsupportedDiagnosticCode = "SEQHTTP001";
    private const string EvidenceSourcePath = "BLL/TCCIntegration/TCCService.cs";

    // Exact merged-A ordered CLI --json diagnostic-code baseline for this two-root lane
    // (issue #53 "Measured clean-baseline artifacts" + candidate-matrix Diagnostics row;
    // value captured in test-writer-notes). The two supported HTTP calls add no model
    // diagnostic, so this sequence must be byte-for-byte the pre-#54 baseline.
    private static readonly string[] ExpectedDiagnosticCodeBaseline =
        ["BE1001", "BE2010", "BE2010", "PRED001"];

    private const string ExpectedPostFlowFileName =
        "bll-tccintegration-tccservice-addcomplaint-bll-tccintegration-addcomplaintrequest-f1cc2038.md";
    private const string ExpectedGetFlowFileName =
        "bll-tccintegration-tccservice-lookups-94a25a61.md";

    // --- Claim 1: the POST root visibly presents exactly one conservative outbound HTTP POST boundary,
    // once, and never as a second generic direct-call / MethodCall presentation of the same call site.
    [Fact]
    public void PostRootPresentsExactlyOneConservativePostBoundary()
    {
        var run = _lane.RequireRun1();
        var flow = run.RequireFlow(ExpectedPostFlowFileName);
        string markdown = flow.Markdown;
        string mermaid = flow.Mermaid;

        Assert.Equal(1, Count(markdown, PostBehaviorPhrase));
        Assert.Equal(0, Count(markdown, GetBehaviorPhrase));
        Assert.Equal(0, Count(markdown, GetMermaidMessage));

        // The HTTP call site is presented once. The behaviour phrase is the only carrier of
        // "HttpClient.PostAsync"; a second occurrence would mean a duplicate generic MethodCall
        // presentation of the same operation (issue #53 objective 3/4).
        // Proxy for issue #53 objective 4 (no duplicate generic MethodCall for the same call site):
        // for this frozen lane the behaviour phrase is the only carrier of the literal "PostAsync",
        // and the "SC-DIRECT-BODY-UNAVAILABLE" count plus "no SEQHTTP001" corroborate the single
        // presentation. No brittle generic-phrase matching is added.
        Assert.Equal(1, Count(markdown, "PostAsync"));
        Assert.DoesNotContain("GetAsync", markdown, StringComparison.Ordinal);

        Assert.Equal(1, Count(mermaid, ExternalParticipantLabel));
        Assert.Equal(1, Count(mermaid, PostMermaidMessage));
        Assert.Equal(0, Count(mermaid, GetMermaidMessage));

        // Supported overload => no recognized-but-unsupported diagnostic. Asserted against the CLI
        // --json diagnostic-code stream (where SEQHTTP001 would actually surface), not the rendered
        // Markdown/Mermaid text, where diagnostic codes never appear.
        Assert.DoesNotContain(UnsupportedDiagnosticCode, run.DiagnosticCodes);

        // Unrelated conservative direct-call boundary count is unchanged by the HTTP model.
        Assert.Equal(2, Count(markdown, "SC-DIRECT-BODY-UNAVAILABLE"));
    }

    // --- Claim 2: the GET root visibly presents exactly one conservative outbound HTTP GET boundary,
    // once, and never as a second generic direct-call presentation of the same call site.
    [Fact]
    public void GetRootPresentsExactlyOneConservativeGetBoundary()
    {
        var run = _lane.RequireRun1();
        var flow = run.RequireFlow(ExpectedGetFlowFileName);
        string markdown = flow.Markdown;
        string mermaid = flow.Mermaid;

        Assert.Equal(1, Count(markdown, GetBehaviorPhrase));
        Assert.Equal(0, Count(markdown, PostBehaviorPhrase));
        Assert.Equal(0, Count(markdown, PostMermaidMessage));

        // Proxy for issue #53 objective 4 (no duplicate generic MethodCall for the same call site):
        // for this frozen lane the behaviour phrase is the only carrier of the literal "GetAsync",
        // and the "SC-DIRECT-BODY-UNAVAILABLE" count plus "no SEQHTTP001" corroborate the single
        // presentation. No brittle generic-phrase matching is added.
        Assert.Equal(1, Count(markdown, "GetAsync"));
        Assert.DoesNotContain("PostAsync", markdown, StringComparison.Ordinal);

        Assert.Equal(1, Count(mermaid, ExternalParticipantLabel));
        Assert.Equal(1, Count(mermaid, GetMermaidMessage));
        Assert.Equal(0, Count(mermaid, PostMermaidMessage));

        Assert.DoesNotContain(UnsupportedDiagnosticCode, run.DiagnosticCodes);

        Assert.Equal(1, Count(markdown, "SC-DIRECT-BODY-UNAVAILABLE"));
    }

    // --- Claim 3: the boundary presentation stays inside compiler evidence (Gate 3/5) and is
    // value-safe, and two clean runs are byte-identical including CLI diagnostics in emitted order.
    [Fact]
    public void OutputIsEvidenceBoundedValueSafeAndDeterministic()
    {
        var run1 = _lane.RequireRun1();
        var run2 = _lane.RequireRun2();

        // ---- Gate 5: the request URI/path values, the base-address / API-key config keys, the request
        // and response body reads, and the BCL response type / status interpretation are never a
        // legitimate conservative claim and must not appear in ANY generated Markdown/Mermaid.
        string[] globalLeakTokens =
        [
            "TCCBaseAddress", "TCCAPIKey", "_baseAddress", "_APIKey",
            "threeThirty/", "updateType/all", "complaint/addComplaint",
            "IsSuccessStatusCode", "HttpResponseMessage", "ReadAsStringAsync",
            // Credential header name (verbatim string arg in TCCService.cs
            // DefaultRequestHeaders.Add("Authorization", _APIKey)) and the request-body
            // content types - issue #53 objective 6.
            "Authorization", "StringContent", "ByteArrayContent",
        ];
        foreach (string token in globalLeakTokens)
        {
            Assert.DoesNotContain(token, run1.AllText, StringComparison.Ordinal);
        }

        // ---- Gate 3/5: the outbound-HTTP boundary claim itself (its behaviour sentence and its
        // Mermaid message) must assert ONLY the compiler-proven GET/POST request boundary - naming the
        // source evidence, carrying an explicit non-strengthened certainty, and withholding any
        // URI/host/header/body/credential/response/status/success/retry/resilience/remote-completion
        // wording. Unrelated generic caller-syntax on other lines (for example
        // "assigns: BaseAddress = System.Uri") is pre-existing conservative source observation and is
        // out of scope for this HTTP-family lane.
        string[] boundaryForbiddenWords =
        [
            "Authorization", "api key", "apikey", "api-key", "credential", "bearer", "token",
            "header", "uri", "url", "host", "endpoint", "request body", "response body", "payload",
            "response", "status", "success", "succeed", "failure", "fail", "200", "2xx",
            "retry", "retries", "resilience", "resilient", "circuit",
            "remote", "received", "delivered", "complete", "completion",
            "guaranteed", "definitely", "certainly", "always",
        ];
        foreach (var flow in new[]
                 {
                     run1.RequireFlow(ExpectedPostFlowFileName),
                     run1.RequireFlow(ExpectedGetFlowFileName),
                 })
        {
            string behaviorPhrase = flow.FileName == ExpectedPostFlowFileName ? PostBehaviorPhrase : GetBehaviorPhrase;
            string boundaryLine = flow.Markdown
                .Split('\n')
                .Single(line => line.Contains(behaviorPhrase, StringComparison.Ordinal));
            string mermaidMessageLine = flow.Mermaid
                .Split('\n')
                .Single(line => line.Contains("HTTP ", StringComparison.Ordinal) && line.Contains(" request", StringComparison.Ordinal));

            // The behaviour line names the source evidence and carries the exact observed certainty.
            // Pinned to the value recorded in test-writer-notes so a regression that WEAKENED or
            // STRENGTHENED it fails: the boundary-existence claim is provably Exact (the call occurs),
            // and nothing about it may become less or more certain without separate proof.
            Assert.Contains($"evidence: {EvidenceSourcePath}", boundaryLine, StringComparison.Ordinal);
            Assert.Contains("certainty: Exact", boundaryLine, StringComparison.Ordinal);

            string scoped = boundaryLine + "\n" + mermaidMessageLine;
            foreach (string word in boundaryForbiddenWords)
            {
                Assert.DoesNotContain(word, scoped, StringComparison.OrdinalIgnoreCase);
            }
        }

        // F11: the configured two roots are not the only flows that can reach an HttpClient GET/POST
        // call site - an automatic root that also calls one does too. Apply the same boundary-wording
        // denylist to EVERY generated flow *.md that carries either behaviour phrase, scoped to that
        // phrase's line plus its matching Mermaid message line.
        foreach (var file in run1.Files.Where(f => f.RelativePath.EndsWith(".md", StringComparison.Ordinal)))
        {
            string flowMarkdown = Encoding.UTF8.GetString(file.Content);
            string? phrase =
                flowMarkdown.Contains(PostBehaviorPhrase, StringComparison.Ordinal) ? PostBehaviorPhrase
                : flowMarkdown.Contains(GetBehaviorPhrase, StringComparison.Ordinal) ? GetBehaviorPhrase
                : null;
            if (phrase is null)
            {
                continue;
            }

            string mermaidMessage = phrase == PostBehaviorPhrase ? PostMermaidMessage : GetMermaidMessage;
            string scoped = flowMarkdown
                .Split('\n')
                .Single(line => line.Contains(phrase, StringComparison.Ordinal));

            var mermaidFile = run1.Files.SingleOrDefault(
                f => f.RelativePath == Path.ChangeExtension(file.RelativePath, ".mmd"));
            if (mermaidFile is not null)
            {
                string? messageLine = Encoding.UTF8.GetString(mermaidFile.Content)
                    .Split('\n')
                    .FirstOrDefault(line => line.Contains(mermaidMessage, StringComparison.Ordinal));
                if (messageLine is not null)
                {
                    scoped += "\n" + messageLine;
                }
            }

            foreach (string word in boundaryForbiddenWords)
            {
                Assert.DoesNotContain(word, scoped, StringComparison.OrdinalIgnoreCase);
            }
        }

        // ---- Determinism: identical file set, identical bytes, identical diagnostics in emitted order.
        Assert.Equal(
            run1.Files.Select(f => f.RelativePath).OrderBy(p => p, StringComparer.Ordinal),
            run2.Files.Select(f => f.RelativePath).OrderBy(p => p, StringComparer.Ordinal));
        foreach (var file in run1.Files)
        {
            var other = run2.Files.Single(f => f.RelativePath == file.RelativePath);
            Assert.True(
                file.Content.AsSpan().SequenceEqual(other.Content),
                $"'{file.RelativePath}' differs between two independent clean runs.");
        }
        Assert.True(
            run1.DiagnosticRecords.SequenceEqual(run2.DiagnosticRecords),
            "CLI --json diagnostic records differ between runs (compared in emitted order, never sorted).");
        Assert.True(
            run1.DiagnosticCodes.SequenceEqual(run2.DiagnosticCodes),
            $"diagnostic code sequence differs between runs: [{string.Join(", ", run1.DiagnosticCodes)}] vs [{string.Join(", ", run2.DiagnosticCodes)}].");
        Assert.NotEmpty(run1.DiagnosticCodes);
        Assert.DoesNotContain(UnsupportedDiagnosticCode, run1.DiagnosticCodes);

        // Exact code/count/order against the merged-A baseline. Drift here (an added or
        // reordered unrelated diagnostic) is a STOP condition, not a test to relax.
        Assert.Equal(ExpectedDiagnosticCodeBaseline, run1.DiagnosticCodes);

        // ---- G2: normalized digest over the ordered raw diagnostics[] JSON records.
        string run1DiagnosticsDigest = Sha256Hex(Encoding.UTF8.GetBytes(string.Concat(run1.DiagnosticRecords)));
        string run2DiagnosticsDigest = Sha256Hex(Encoding.UTF8.GetBytes(string.Concat(run2.DiagnosticRecords)));
        Assert.Equal(run1DiagnosticsDigest, run2DiagnosticsDigest);
        _output.WriteLine(
            $"[QHTTP-B matrix] diagnostics ordered-record digest run1={run1DiagnosticsDigest} run2={run2DiagnosticsDigest}");
    }

    // --- Claim 4: frozen external identity, corpus isolation, artifact completeness/validity, and a
    // real Mermaid CLI 11.16.0 render of every run-1 diagram.
    [Fact]
    public async Task FrozenIdentityIsolationAndArtifactValidityHold()
    {
        var run1 = _lane.RequireRun1();
        var run2 = _lane.RequireRun2();

        // ---- Frozen identity (any drift = STOP / blocker).
        Assert.Equal(OutboundHttpExternalCorpusFixture.CorpusRevision, _lane.CheckoutHead);
        Assert.Equal(OutboundHttpExternalCorpusFixture.SolutionSha256, _lane.FrozenBlobHashes.Solution);
        Assert.Equal(OutboundHttpExternalCorpusFixture.BllProjectSha256, _lane.FrozenBlobHashes.BllProject);
        Assert.Equal(OutboundHttpExternalCorpusFixture.SourceSha256, _lane.FrozenBlobHashes.Source);

        // ---- Source preservation: the in-place FraudManagement working files this lane analyses are
        // byte-for-byte unchanged by our two analysis runs (frozen-file SHA-256 before == after).
        Assert.Equal(_lane.HashesBefore, _lane.HashesAfter);
        Assert.Equal("", _lane.ExternalGitStatusBefore);
        Assert.Equal(_lane.ExternalGitStatusBefore, _lane.ExternalGitStatusAfter);

        // ---- Profile / Program Index identity. The production CLI --json surfaces these at
        // data.runs[].profileId and data.runs[].indexFingerprint (CamelCase policy); the fixture
        // reads that exact path. Both are hard assertions: a missing value means the QHTTP-B
        // frozen-identity proof for profile/TFM drift is BLOCKED, not degraded.
        Assert.True(
            _lane.ObservedProfileId is not null,
            "CLI --json no longer surfaces data.runs[].profileId; QHTTP-B frozen-identity proof for "
            + "profile/TFM drift is BLOCKED, not degraded.");
        Assert.True(
            _lane.ObservedIndexFingerprint is not null,
            "CLI --json no longer surfaces data.runs[].indexFingerprint; QHTTP-B frozen-identity proof "
            + "for Program Index drift is BLOCKED, not degraded.");
        _output.WriteLine(
            $"[QHTTP-B matrix] observed profileId={_lane.ObservedProfileId ?? "<null>"} "
            + $"(frozen {OutboundHttpExternalCorpusFixture.ProfileId})");
        _output.WriteLine(
            $"[QHTTP-B matrix] observed indexFingerprint={_lane.ObservedIndexFingerprint ?? "<null>"}; "
            + $"frozen in-place baseline={OutboundHttpExternalCorpusFixture.ProgramIndexFingerprint}; "
            + "clean/normalised worktree of 7aabfef9… yields "
            + "df23b372a30754898787988bdf0c0537b2bdb19d14b3724c4314588281935117");

        Assert.True(
            string.Equals(
                OutboundHttpExternalCorpusFixture.ProfileId,
                _lane.ObservedProfileId,
                StringComparison.Ordinal),
            $"observed profileId '{_lane.ObservedProfileId}' does not match the frozen "
            + $"'{OutboundHttpExternalCorpusFixture.ProfileId}'. A mismatch most likely means the local "
            + "Provided/FraudManagement checkout's working-tree line endings differ from the frozen baseline "
            + "(LF/CRLF). This is a corpus-normalisation / frozen-baseline decision for the issue #53 owner, "
            + "not a semantic regression; see docs/work/outbound-http/QHTTP-B/test-writer-notes.md "
            + "\"Known boundary for the PR\".");
        Assert.True(
            string.Equals(
                OutboundHttpExternalCorpusFixture.ProgramIndexFingerprint,
                _lane.ObservedIndexFingerprint,
                StringComparison.Ordinal),
            $"observed Program Index fingerprint '{_lane.ObservedIndexFingerprint}' does not match the frozen "
            + $"in-place baseline '{OutboundHttpExternalCorpusFixture.ProgramIndexFingerprint}'. A mismatch most "
            + "likely means the local Provided/FraudManagement checkout's working-tree line endings differ from "
            + "the frozen baseline (LF/CRLF), not a semantic Program Index regression; the value produced by a "
            + "clean/normalised worktree of 7aabfef9… is "
            + "df23b372a30754898787988bdf0c0537b2bdb19d14b3724c4314588281935117. This is a "
            + "corpus-normalisation / frozen-baseline decision for the issue #53 owner; see "
            + "docs/work/outbound-http/QHTTP-B/test-writer-notes.md \"Known boundary for the PR\".");

        // F7: run 2 captured the same identity path; a cross-run drift is a determinism failure.
        Assert.Equal(_lane.ObservedProfileId, _lane.ObservedProfileIdRun2);
        Assert.Equal(_lane.ObservedIndexFingerprint, _lane.ObservedIndexFingerprintRun2);
        _output.WriteLine(
            $"[QHTTP-B matrix] identity run1==run2: profileId={_lane.ObservedProfileId == _lane.ObservedProfileIdRun2} "
            + $"indexFingerprint={_lane.ObservedIndexFingerprint == _lane.ObservedIndexFingerprintRun2}");

        // F10: fail loud if the CLI --json budget path changed shape and ReadMermaidBudget silently
        // fell back to its hard-coded default instead of reading the real configured value.
        Assert.True(
            _lane.MermaidBudgetResolvedFromJson,
            "CLI --json no longer exposes data.configuration.diagramBudget.maxMermaidCharacters.value; "
            + "ReadMermaidBudget silently used its hard-coded 45000 fallback, so the Mermaid budget "
            + "assertions in this lane are no longer meaningful. Fix the JSON path.");

        // ---- Every named candidate-matrix artifact is present.
        run1.RequireFlow(ExpectedPostFlowFileName);
        run1.RequireFlow(ExpectedGetFlowFileName);
        Assert.Contains(run1.Files, f => f.RelativePath == "index.md");
        Assert.Contains(run1.Files, f => f.RelativePath == "seqdoc.manifest.json");
        Assert.True(
            run1.Files.Count(f => f.RelativePath.EndsWith(".mmd", StringComparison.Ordinal)) >= 2,
            "expected at least the POST and GET Mermaid diagrams.");

        // ---- Every Markdown link resolves; every Mermaid file is valid and within budget.
        var names = run1.Files
            .SelectMany(f => new[] { f.RelativePath, Path.GetFileName(f.RelativePath) })
            .ToHashSet(StringComparer.Ordinal);
        int linksChecked = 0;
        foreach (var file in run1.Files.Where(f => f.RelativePath.EndsWith(".md", StringComparison.Ordinal)))
        {
            string markdown = Encoding.UTF8.GetString(file.Content);
            foreach (Match match in Regex.Matches(markdown, @"\]\(([^)]+)\)"))
            {
                string target = match.Groups[1].Value.Trim();
                if (target.StartsWith('#')
                    || target.StartsWith("http://", StringComparison.Ordinal)
                    || target.StartsWith("https://", StringComparison.Ordinal)
                    || target.StartsWith("mailto:", StringComparison.Ordinal))
                {
                    continue;
                }

                string relative = target.Split('#', 2)[0];
                if (relative.StartsWith("./", StringComparison.Ordinal))
                {
                    relative = relative[2..];
                }
                if (relative.Length == 0)
                {
                    continue;
                }

                Assert.True(
                    names.Contains(relative),
                    $"'{file.RelativePath}' links to '{target}', which is not a generated file.");
                linksChecked++;
            }
        }
        _output.WriteLine($"[QHTTP-B matrix] Markdown link-check: pass, {linksChecked} links checked");

        // ---- Index completeness: every generated top-level flow document (all *.md except
        // index.md, no subdirectory) is a link target inside index.md (candidate-matrix Index row).
        string indexMarkdown = Encoding.UTF8.GetString(
            run1.Files.Single(f => f.RelativePath == "index.md").Content);
        foreach (var flow in run1.Files.Where(f =>
            f.RelativePath.EndsWith(".md", StringComparison.Ordinal)
            && f.RelativePath != "index.md"
            && !f.RelativePath.Contains('/')))
        {
            Assert.True(
                indexMarkdown.Contains($"]({flow.RelativePath})", StringComparison.Ordinal)
                || indexMarkdown.Contains($"](./{flow.RelativePath})", StringComparison.Ordinal),
                $"index.md does not link to generated flow '{flow.RelativePath}'.");
        }

        // ---- Manifest: exactly the merged-A baseline of 35 listed files, every path relative.
        using (var manifestDocument = JsonDocument.Parse(
            run1.Files.Single(f => f.RelativePath == "seqdoc.manifest.json").Content))
        {
            var listed = manifestDocument.RootElement.GetProperty("files").EnumerateArray().ToArray();
            Assert.Equal(35, listed.Length);
            foreach (var entry in listed)
            {
                string path = entry.GetProperty("relativePath").GetString() ?? string.Empty;
                Assert.False(path.Length == 0, "manifest lists an empty path.");
                Assert.False(Path.IsPathRooted(path), $"manifest lists a rooted path '{path}'.");
                Assert.DoesNotContain(":", path, StringComparison.Ordinal);
                Assert.DoesNotContain("\\", path, StringComparison.Ordinal);
                Assert.False(path.StartsWith("//", StringComparison.Ordinal), $"manifest lists a UNC path '{path}'.");
            }
        }

        // ---- G3: manifest content-hash cross-check + owned-path set == generated non-operational set.
        var manifestEntries = new List<(string RelativePath, string Sha256)>();
        using (var manifestCrossCheck = JsonDocument.Parse(
            run1.Files.Single(f => f.RelativePath == "seqdoc.manifest.json").Content))
        {
            foreach (var entry in manifestCrossCheck.RootElement.GetProperty("files").EnumerateArray())
            {
                manifestEntries.Add((
                    entry.GetProperty("relativePath").GetString() ?? string.Empty,
                    entry.GetProperty("sha256").GetString() ?? string.Empty));
            }
        }

        foreach (var (relativePath, listedHash) in manifestEntries)
        {
            var owned = run1.Files.SingleOrDefault(f => f.RelativePath == relativePath);
            Assert.True(owned is not null, $"manifest lists '{relativePath}' but no such file was generated.");
            Assert.Equal(listedHash, Sha256Hex(owned!.Content));
        }

        // F5: "operational" = anything that is not a rendered document. This robustly excludes
        // .seqdoc/**, seqdoc.manifest.json, AND seqdoc.stale (written at the output root by
        // OutputSetActivator on a failed run; absent on success today, but the exclusion must not
        // depend on that).
        var operationalPaths = run1.Files
            .Where(f => !f.RelativePath.EndsWith(".md", StringComparison.Ordinal)
                && !f.RelativePath.EndsWith(".mmd", StringComparison.Ordinal))
            .Select(f => f.RelativePath)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(
            run1.Files.Select(f => f.RelativePath)
                .Where(p => !operationalPaths.Contains(p))
                .OrderBy(p => p, StringComparer.Ordinal),
            manifestEntries.Select(e => e.RelativePath).OrderBy(p => p, StringComparer.Ordinal));

        // ---- G1: per-artifact byte-length + SHA-256 for run 1 AND run 2, plus a complete-output digest
        // over every file under each output root (including operational .seqdoc/**).
        string[] namedArtifacts =
        [
            ExpectedPostFlowFileName,
            Path.ChangeExtension(ExpectedPostFlowFileName, ".mmd"),
            ExpectedGetFlowFileName,
            Path.ChangeExtension(ExpectedGetFlowFileName, ".mmd"),
            "index.md",
            "seqdoc.manifest.json",
        ];
        foreach (string artifact in namedArtifacts)
        {
            var a1 = run1.Files.Single(f => f.RelativePath == artifact);
            var a2 = run2.Files.Single(f => f.RelativePath == artifact);
            string h1 = Sha256Hex(a1.Content);
            string h2 = Sha256Hex(a2.Content);
            Assert.Equal(a1.Content.Length, a2.Content.Length);
            Assert.Equal(h1, h2);
            _output.WriteLine(
                $"[QHTTP-B matrix] {artifact} | run1 len={a1.Content.Length} sha256={h1} | run2 len={a2.Content.Length} sha256={h2} | equal={h1 == h2}");
        }

        string completeDigest1 = CompleteOutputDigest(run1);
        string completeDigest2 = CompleteOutputDigest(run2);
        Assert.Equal(completeDigest1, completeDigest2);
        _output.WriteLine(
            $"[QHTTP-B matrix] complete-output digest run1={completeDigest1} run2={completeDigest2}");
        _output.WriteLine(
            $"[QHTTP-B matrix] dotnet --version={CaptureProcessOutput("dotnet", "--version")} "
            + $"node={CaptureProcessOutput("node", "--version")} "
            + "mermaid-cli=@mermaid-js/mermaid-cli@11.16.0");
        _output.WriteLine(
            $"[QHTTP-B matrix] mermaid budget={run1.MaxMermaidCharacters}; "
            + $"frozen-scope git status before='{_lane.ExternalGitStatusBefore}' after='{_lane.ExternalGitStatusAfter}' "
            + $"HEAD={_lane.CheckoutHead}");
        // F3 / issue #53 cleanup contract: record the COMPLETE external tracked status (whole corpus),
        // not only the analysis-scoped slice. Recorded for the matrix, not gated - a pre-existing obj/
        // left by the ServiceClient lane must not turn this lane BLOCKED.
        _output.WriteLine(
            "[QHTTP-B matrix] whole-corpus tracked git status (recorded, not gated) "
            + $"before='{_lane.ExternalGitStatusUnscopedBefore}' after='{_lane.ExternalGitStatusUnscopedAfter}'");

        foreach (var file in run1.Files.Where(f => f.RelativePath.EndsWith(".mmd", StringComparison.Ordinal)))
        {
            string mermaid = Encoding.UTF8.GetString(file.Content);
            Assert.True(
                mermaid.Length <= run1.MaxMermaidCharacters,
                $"'{file.RelativePath}' has {mermaid.Length} characters, over the configured budget {run1.MaxMermaidCharacters}.");
            Assert.Empty(MermaidValidator.Validate(mermaid));
            _output.WriteLine(
                $"[QHTTP-B matrix] mermaid budget {file.RelativePath}: {mermaid.Length}/{run1.MaxMermaidCharacters} ok");
        }

        // ---- Real Mermaid CLI 11.16.0 render of every run-1 diagram. Genuine unavailability is a LOUD
        // failure (the lane is BLOCKED), never a silent skip or pass.
        await RenderEveryDiagramWithMermaidCliAsync(run1);
    }

    private static async Task RenderEveryDiagramWithMermaidCliAsync(OutboundHttpLaneRun run)
    {
        string? npx = FindOnPath("npx");
        Assert.True(
            npx is not null && FindOnPath("node") is not null,
            "Mermaid CLI 11.16.0 is not runnable in this environment (node/npx missing). "
            + "Issue #53 requires this render; the QHTTP-B lane is BLOCKED, not skipped or passed.");

        string workDirectory = Path.Combine(Path.GetTempPath(), $"seqdoc-qhttpb-mmdcli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDirectory);
        try
        {
            foreach (var file in run.Files.Where(f => f.RelativePath.EndsWith(".mmd", StringComparison.Ordinal)))
            {
                string mmdPath = Path.Combine(workDirectory, Path.GetFileName(file.RelativePath));
                await File.WriteAllBytesAsync(mmdPath, file.Content);
                string svgPath = Path.ChangeExtension(mmdPath, ".svg");

                var startInfo = new ProcessStartInfo
                {
                    FileName = npx!,
                    WorkingDirectory = workDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (string argument in new[]
                         {
                             "--yes", "@mermaid-js/mermaid-cli@11.16.0",
                             "-i", mmdPath, "-o", svgPath,
                         })
                {
                    startInfo.ArgumentList.Add(argument);
                }

                using var process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Could not launch npx for mermaid-cli.");
                string stdout = await process.StandardOutput.ReadToEndAsync();
                string stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                Assert.True(
                    process.ExitCode == 0,
                    $"mermaid-cli 11.16.0 failed to render '{file.RelativePath}' (exit {process.ExitCode}).\n{stdout}\n{stderr}");
                Assert.True(
                    File.Exists(svgPath) && new FileInfo(svgPath).Length > 0,
                    $"mermaid-cli 11.16.0 produced no SVG for '{file.RelativePath}'.");
            }
        }
        finally
        {
            try
            {
                Directory.Delete(workDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string Sha256Hex(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static string CompleteOutputDigest(OutboundHttpLaneRun run)
    {
        var lines = run.Files
            .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
            .Select(f => $"{f.RelativePath}\t{Sha256Hex(f.Content)}");
        return Sha256Hex(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
    }

    private static string CaptureProcessOutput(string executable, string arguments)
    {
        try
        {
            string? full = FindOnPath(executable);
            if (full is null)
            {
                return "unavailable";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = full,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return "unavailable";
            }

            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return output.Length == 0 ? "unavailable" : output;
        }
        catch (Exception)
        {
            return "unavailable";
        }
    }

    private static int Count(string haystack, string needle)
    {
        int count = 0;
        int index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static string? FindOnPath(string executable)
    {
        string[] candidates = OperatingSystem.IsWindows()
            ? [executable + ".cmd", executable + ".exe", executable]
            : [executable];
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (string candidate in candidates)
            {
                string full = Path.Combine(directory.Trim(), candidate);
                if (File.Exists(full))
                {
                    return full;
                }
            }
        }

        return null;
    }
}

public sealed record OutboundHttpRenderedFile(string RelativePath, byte[] Content);

public sealed record OutboundHttpFlowArtifact(string FileName, string Markdown, string Mermaid);

public sealed record OutboundHttpLaneRun(
    string Outcome,
    ImmutableArray<string> DiagnosticCodes,
    ImmutableArray<string> DiagnosticRecords,
    string DiagnosticSummary,
    int MaxMermaidCharacters,
    ImmutableArray<OutboundHttpRenderedFile> Files)
{
    public string AllText => string.Join(
        "\n",
        Files
            .Where(f => f.RelativePath.EndsWith(".md", StringComparison.Ordinal)
                || f.RelativePath.EndsWith(".mmd", StringComparison.Ordinal))
            .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
            .Select(f => Encoding.UTF8.GetString(f.Content)));

    public OutboundHttpFlowArtifact RequireFlow(string markdownFileName)
    {
        var markdown = Files.SingleOrDefault(f => f.RelativePath == markdownFileName)
            ?? throw new XunitException(
                $"required flow document '{markdownFileName}' was not generated. "
                + $"Generated top-level Markdown: {string.Join(", ", Files.Where(f => f.RelativePath.EndsWith(".md", StringComparison.Ordinal)).Select(f => f.RelativePath))}. "
                + "A missing root/file is a QHTTP-B lane failure (possible frozen-identity drift).");

        string mermaidName = Path.ChangeExtension(markdownFileName, ".mmd");
        var mermaid = Files.SingleOrDefault(f => f.RelativePath == mermaidName)
            ?? throw new XunitException($"required Mermaid diagram '{mermaidName}' was not generated.");

        return new OutboundHttpFlowArtifact(
            markdownFileName,
            Encoding.UTF8.GetString(markdown.Content),
            Encoding.UTF8.GetString(mermaid.Content));
    }
}

public sealed record FrozenExternalHashes(string Solution, string BllProject, string Source);

/// <summary>
/// Asserts the shared <c>Provided/FraudManagement</c> checkout is exactly at the frozen revision
/// <c>7aabfef9…</c> with a clean scoped working tree, then analyses it IN PLACE (matching the existing
/// <c>ServiceClientExternalCorpusTests</c> FraudManagement lane). Writes the temporary two-root
/// selection YAML under an isolated OS temp root and invokes the production CLI twice (fresh cache +
/// output each time) for the determinism claim. Everything under the temp root is torn down on
/// <see cref="DisposeAsync"/>, including on failure; the external checkout is never mutated (frozen
/// files SHA-256-checked before and after). A normalised <c>git worktree</c> is intentionally not
/// used — the frozen Program Index fingerprint depends on this checkout's in-place LF/CRLF line
/// endings; a normalised tree yields a different fingerprint (owner-accepted boundary).
/// </summary>
public sealed class OutboundHttpExternalCorpusFixture : IAsyncLifetime
{
    public const string CorpusRevision = "7aabfef98fa4d47781bd8a98b9061ddcafb88836";
    public const string ProfileId = "profile:v1:f874be7e6b51bea2038f6cfac77ab510fc73e7208e9e47b4475e6a17896aaef1";
    public const string ProgramIndexFingerprint = "f9a36fd5662f01eead94779eb243f489d5bc1c6e1b7333d2f987b76e30d8146c";
    public const string SolutionSha256 = "67d6b9f15be05f86c06ea17fa92dd7474b8886b876d1d69cf11552741ffaaca1";
    public const string BllProjectSha256 = "c38a35ee7b3acf227fb9988ced35dceb2dec36165e8f8f01bb6b82ee6f658a06";
    public const string SourceSha256 = "eff261211900578a493d40900cd0de5418dbbd132bbc4f806f684b31e184dfce";

    private const string PostRootHash =
        "method:v1:c3310b12f1a331d7ee9871a964209e89da0a0dcb84b086e4b62cbbbdc2a66417";
    private const string GetRootHash =
        "method:v1:b7a44d4b1128669b35cda87326e73098991a24dbd0b975b9986c9050b8b45504";

    private readonly List<string> _tempDirectories = [];
    private string? _corpusRoot;

    public bool CorpusAbsent { get; private set; }

    public string? SkipReason { get; private set; }

    public OutboundHttpLaneRun? Run1 { get; private set; }

    public OutboundHttpLaneRun? Run2 { get; private set; }

    /// <summary>The exact <c>HEAD</c> of the pinned FraudManagement checkout the lane analysed.</summary>
    public string CheckoutHead { get; private set; } = string.Empty;

    public FrozenExternalHashes HashesBefore { get; private set; } = new("", "", "");

    public FrozenExternalHashes HashesAfter { get; private set; } = new("", "", "");

    /// <summary>
    /// SHA-256 of the canonical committed blob content of the three frozen files at
    /// <see cref="CorpusRevision"/>, read with <c>git show</c> so the value is independent of the
    /// checkout's <c>core.autocrlf</c> / smudge filters. This is the true frozen-file identity.
    /// </summary>
    public FrozenExternalHashes FrozenBlobHashes { get; private set; } = new("", "", "");

    public string ExternalGitStatusBefore { get; private set; } = string.Empty;

    public string ExternalGitStatusAfter { get; private set; } = string.Empty;

    /// <summary>
    /// Whole-corpus tracked-only <c>git status --porcelain --untracked-files=no</c> before and after
    /// the runs. Recorded for the issue #53 cleanup matrix; NOT gated (a pre-existing tracked delta
    /// from another FraudManagement lane must not turn this lane BLOCKED - the scoped status is the
    /// gate).
    /// </summary>
    public string ExternalGitStatusUnscopedBefore { get; private set; } = string.Empty;

    public string ExternalGitStatusUnscopedAfter { get; private set; } = string.Empty;

    public string? ObservedProfileId { get; private set; }

    public string? ObservedIndexFingerprint { get; private set; }

    public string? ObservedProfileIdRun2 { get; private set; }

    public string? ObservedIndexFingerprintRun2 { get; private set; }

    private bool _mermaidBudgetResolvedFromJson;

    /// <summary>
    /// True when <see cref="ReadMermaidBudget"/> actually found
    /// <c>data.configuration.diagramBudget.maxMermaidCharacters.value</c> in the CLI <c>--json</c>
    /// payload, rather than silently returning its hard-coded fallback.
    /// </summary>
    public bool MermaidBudgetResolvedFromJson => _mermaidBudgetResolvedFromJson;

    public OutboundHttpLaneRun RequireRun1() => Require(Run1);

    public OutboundHttpLaneRun RequireRun2() => Require(Run2);

    private OutboundHttpLaneRun Require(OutboundHttpLaneRun? run)
    {
        if (run is not null)
        {
            return run;
        }

        if (CorpusAbsent)
        {
            throw SkipException.ForSkip(
                "the Provided external test-project corpus is not installed.");
        }

        throw new XunitException(
            $"the FraudManagement outbound-HTTP acceptance lane did not produce a result: {SkipReason}");
    }

    public async Task InitializeAsync()
    {
        string providedRoot;
        try
        {
            providedRoot = ExternalCorpusResolver.Current.RequireGroup(ExternalCorpusGroup.Provided).Root;
        }
        catch (Exception exception) when (exception is SkipException or ExternalCorpusResolutionException)
        {
            CorpusAbsent = true;
            SkipReason = "the Provided external test-project corpus is not installed.";
            return;
        }

        _corpusRoot = Path.Combine(providedRoot, "FraudManagement");
        if (!File.Exists(Path.Combine(_corpusRoot, "FraudManagement.sln")))
        {
            throw new XunitException(
                $"the Provided corpus is installed but FraudManagement is missing at '{_corpusRoot}'. "
                + "That is a QHTTP-B lane failure, not a skip.");
        }

        // Revision pinning. The Provided FraudManagement checkout is the frozen lane: it must sit
        // exactly on CorpusRevision with a clean tree. A clean/normalised detached worktree is
        // deliberately not used: the frozen Program Index fingerprint was baselined against this
        // shared in-place checkout's inconsistent historical LF/CRLF working tree, and a normalised
        // worktree of 7aabfef9… yields a different fingerprint
        // (df23b372a30754898787988bdf0c0537b2bdb19d14b3724c4314588281935117) - an owner-accepted
        // boundary for issue #53. So the guard is an assertion, and any drift is a loud blocker,
        // never a skip.
        CheckoutHead = RunGit(_corpusRoot, "rev-parse", "HEAD").StdOut.Trim();
        if (!string.Equals(CheckoutHead, CorpusRevision, StringComparison.Ordinal))
        {
            throw new XunitException(
                $"FraudManagement checkout is at '{CheckoutHead}', not the frozen revision '{CorpusRevision}'. "
                + "QHTTP-B is BLOCKED until the corpus is pinned; this is not a skip.");
        }

        // Tracked-changes-only, scoped to the paths this lane actually analyses. An unrelated
        // obj/bin artifact left by the existing ServiceClientExternalCorpusTests FraudManagement
        // lane, or a stderr CRLF warning, must not turn a valid frozen checkout into BLOCKED.
        ExternalGitStatusBefore = FrozenScopeStatus(_corpusRoot);
        if (ExternalGitStatusBefore.Length != 0)
        {
            throw new XunitException(
                $"FraudManagement checkout has an uncommitted delta in the frozen analysis scope before the run:\n{ExternalGitStatusBefore}\n"
                + "QHTTP-B requires a clean frozen checkout; this is not a skip.");
        }

        // Whole-corpus tracked status, recorded only (issue #53: "Record the complete external source
        // status/delta when a Git checkout is available"). Not a gate.
        ExternalGitStatusUnscopedBefore = UnscopedTrackedStatus(_corpusRoot);

        // Canonical committed content identity of the three frozen files, read with `git show` so the
        // value is independent of the checkout's core.autocrlf / smudge filters.
        FrozenBlobHashes = new FrozenExternalHashes(
            GitBlobSha256(_corpusRoot, "FraudManagement.sln"),
            GitBlobSha256(_corpusRoot, "BLL/BLL.csproj"),
            GitBlobSha256(_corpusRoot, "BLL/TCCIntegration/TCCService.cs"));

        HashesBefore = HashFrozenFiles(_corpusRoot);

        string tmpRoot = NewTempDirectory("work");
        string configPath = Path.Combine(tmpRoot, "outbound-http-two-root.seqdoc.yaml");
        await File.WriteAllTextAsync(
            configPath,
            "schemaVersion: 1\n"
            + "selection:\n"
            + "  roots:\n"
            + $"    - {PostRootHash}\n"
            + $"    - {GetRootHash}\n");

        string solution = Path.Combine(_corpusRoot, "FraudManagement.sln");

        try
        {
            Run1 = await RunCliAsync(solution, _corpusRoot, configPath, captureIdentity: true);
            if (!IsSucceeded(Run1))
            {
                SkipReason = $"first CLI run outcome was '{Run1.Outcome}': {Run1.DiagnosticSummary}";
                Run1 = null;
                return;
            }

            Run2 = await RunCliAsync(solution, _corpusRoot, configPath, captureIdentity: false, isRun2: true);
            if (!IsSucceeded(Run2))
            {
                SkipReason = $"second determinism CLI run outcome was '{Run2.Outcome}'.";
                Run2 = null;
            }
        }
        finally
        {
            HashesAfter = HashFrozenFiles(_corpusRoot);
            ExternalGitStatusAfter = FrozenScopeStatus(_corpusRoot);
            ExternalGitStatusUnscopedAfter = UnscopedTrackedStatus(_corpusRoot);
        }
    }

    public Task DisposeAsync()
    {
        // The lane writes only under the isolated OS temp root (caches, output, YAML, Mermaid
        // renders); the external checkout is read-only. Remove the temp root, including on failure.
        foreach (string directory in _tempDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return Task.CompletedTask;
    }

    private static bool IsSucceeded(OutboundHttpLaneRun run) =>
        string.Equals(run.Outcome, "succeeded", StringComparison.OrdinalIgnoreCase);

    private async Task<OutboundHttpLaneRun> RunCliAsync(
        string solution,
        string laneRoot,
        string configPath,
        bool captureIdentity,
        bool isRun2 = false)
    {
        string outputDirectory = NewTempDirectory("out");
        string cacheDirectory = NewTempDirectory("cache");

        string[] args =
        [
            "analyze", solution,
            "--repository-root", laneRoot,
            "--config", configPath,
            "--configuration", "Release",
            "--framework", "net9.0",
            "--cache", Path.Combine(cacheDirectory, "cache-v1.db"),
            "--output", outputDirectory,
            "--json",
        ];

        var standardOutput = new StringWriter();
        var standardError = new StringWriter();
        await CliHost.RunAsync(args, standardOutput, standardError, CancellationToken.None);

        using var document = JsonDocument.Parse(standardOutput.ToString());
        var root = document.RootElement;

        string outcome = root.TryGetProperty("outcome", out var outcomeElement)
            ? outcomeElement.GetString() ?? "unknown"
            : "unknown";

        var codes = ImmutableArray.CreateBuilder<string>();
        var records = ImmutableArray.CreateBuilder<string>();
        var summary = new StringBuilder();
        if (root.TryGetProperty("diagnostics", out var diagnostics) && diagnostics.ValueKind == JsonValueKind.Array)
        {
            foreach (var diagnostic in diagnostics.EnumerateArray())
            {
                records.Add(diagnostic.GetRawText());
                string code = diagnostic.TryGetProperty("code", out var codeElement)
                    ? codeElement.GetString() ?? string.Empty
                    : string.Empty;
                codes.Add(code);
                summary.Append(code).Append(' ');
            }
        }

        // Capture the identity path on BOTH runs so the test can assert run1 == run2 (F7).
        var (observedProfileId, observedIndexFingerprint) = CaptureIdentity(root);
        if (isRun2)
        {
            ObservedProfileIdRun2 = observedProfileId;
            ObservedIndexFingerprintRun2 = observedIndexFingerprint;
        }
        else if (captureIdentity)
        {
            ObservedProfileId = observedProfileId;
            ObservedIndexFingerprint = observedIndexFingerprint;
        }

        return new OutboundHttpLaneRun(
            outcome,
            codes.ToImmutable(),
            records.ToImmutable(),
            summary.ToString().Trim(),
            ReadMermaidBudget(root),
            ReadGeneratedFiles(outputDirectory));
    }

    // The production CLI emits analyze identity at the exact path data.runs[].profileId and
    // data.runs[].indexFingerprint (CliHost.CreateAnalyzeData, serialized with a CamelCase naming
    // policy). Read that exact path - a shape mismatch must surface as a null observation the hard
    // assert in the test reports as BLOCKED, never a silent skip.
    private static (string? ProfileId, string? IndexFingerprint) CaptureIdentity(JsonElement root)
    {
        string? profileId = null;
        string? indexFingerprint = null;

        if (root.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("runs", out var runs)
            && runs.ValueKind == JsonValueKind.Array)
        {
            foreach (var run in runs.EnumerateArray())
            {
                if (run.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (run.TryGetProperty("profileId", out var pid) && pid.ValueKind == JsonValueKind.String)
                {
                    profileId ??= pid.GetString();
                }

                if (run.TryGetProperty("indexFingerprint", out var fp) && fp.ValueKind == JsonValueKind.String)
                {
                    indexFingerprint ??= fp.GetString();
                }
            }
        }

        return (profileId, indexFingerprint);
    }

    private int ReadMermaidBudget(JsonElement root)
    {
        if (root.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("configuration", out var configuration)
            && configuration.TryGetProperty("diagramBudget", out var diagramBudget)
            && diagramBudget.TryGetProperty("maxMermaidCharacters", out var maxMermaid)
            && maxMermaid.TryGetProperty("value", out var value)
            && value.TryGetInt32(out int budget))
        {
            _mermaidBudgetResolvedFromJson = true;
            return budget;
        }

        return 45000;
    }

    private static ImmutableArray<OutboundHttpRenderedFile> ReadGeneratedFiles(string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory))
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<OutboundHttpRenderedFile>();
        foreach (string path in Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(outputDirectory, path).Replace(Path.DirectorySeparatorChar, '/');
            builder.Add(new OutboundHttpRenderedFile(relative, File.ReadAllBytes(path)));
        }

        builder.Sort((left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));
        return builder.ToImmutable();
    }

    private static FrozenExternalHashes HashFrozenFiles(string root) => new(
        Sha256(Path.Combine(root, "FraudManagement.sln")),
        Sha256(Path.Combine(root, "BLL", "BLL.csproj")),
        Sha256(Path.Combine(root, "BLL", "TCCIntegration", "TCCService.cs")));

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private string NewTempDirectory(string label)
    {
        string path = Path.Combine(Path.GetTempPath(), $"seqdoc-qhttpb-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }

    private static string GitBlobSha256(string repositoryRoot, string relativePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("show");
        startInfo.ArgumentList.Add($"{CorpusRevision}:{relativePath}");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git.");
        using var buffer = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(buffer);
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new XunitException($"git show {CorpusRevision}:{relativePath} failed: {error}");
        }

        return Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
    }

    // Tracked changes only (`--untracked-files=no`), scoped to the solution and the BLL project the
    // frozen lane analyses. stderr (e.g. a CRLF warning) is deliberately ignored for the value.
    private static string FrozenScopeStatus(string corpusRoot)
    {
        var result = RunGit(
            corpusRoot,
            "status", "--porcelain", "--untracked-files=no", "--", "FraudManagement.sln", "BLL");
        if (result.ExitCode != 0)
        {
            throw new XunitException(
                $"git status on the FraudManagement checkout failed (exit {result.ExitCode}): {result.StdErr}");
        }

        return result.StdOut;
    }

    // Whole-corpus tracked changes (`--untracked-files=no`, no pathspec). Recorded for the issue #53
    // cleanup matrix only - never gated, so a pre-existing tracked delta from another FraudManagement
    // lane cannot turn this lane BLOCKED.
    private static string UnscopedTrackedStatus(string corpusRoot)
    {
        var result = RunGit(corpusRoot, "status", "--porcelain", "--untracked-files=no");
        if (result.ExitCode != 0)
        {
            throw new XunitException(
                $"git status (whole corpus) on the FraudManagement checkout failed (exit {result.ExitCode}): {result.StdErr}");
        }

        return result.StdOut;
    }

    // stdout and stderr are kept separate: only stdout carries `status --porcelain` / `rev-parse`
    // values, so a benign CRLF-conversion warning on stderr can never turn a clean frozen checkout
    // into a hard BLOCKED failure. stderr is surfaced only in exception text.
    private static (int ExitCode, string StdOut, string StdErr) RunGit(
        string workingDirectory,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git.");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout.Trim(), stderr.Trim());
    }
}
