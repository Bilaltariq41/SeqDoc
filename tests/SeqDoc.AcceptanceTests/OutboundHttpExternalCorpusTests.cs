using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
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
/// never on an intermediate fact.
///
/// Corpus isolation (frozen contract amendment, issue #53 comment 5523887517): FraudManagement revision
/// <c>7aabfef9…</c> is materialised in an isolated detached <c>git worktree</c> under a short OS temp
/// path, with <c>core.autocrlf=false</c> and <c>core.eol=lf</c> set BEFORE checkout, so the analysed
/// tree is line-ending-normalised and reproducible. Only that normalised checkout is analysed; there is
/// no in-place code path and no fallback to the shared <c>Provided/FraudManagement</c> tree. The shared
/// supplied repository is never mutated (tracked + untracked <c>git status</c> and
/// <c>git worktree list</c> recorded before and after and asserted equal). The amended Program Index
/// fingerprint for the normalised checkout is
/// <c>df23b372a30754898787988bdf0c0537b2bdb19d14b3724c4314588281935117</c>.
///
/// There is NO skip path. Missing corpus, missing <c>FraudManagement</c>, revision/identity drift,
/// <c>git worktree</c> failure, <c>dotnet restore</c> failure, missing <c>node</c>/<c>npx</c>/Mermaid
/// CLI, a missing required artifact, non-deterministic output, a leaked sensitive value, a duplicated or
/// missing HTTP boundary, a budget breach, or a cleanup failure are all LOUD xUnit failures.
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

    // F5: bounded per-diagram Mermaid render wait, comfortably above observed real durations.
    private static readonly TimeSpan MermaidRenderTimeout = TimeSpan.FromMinutes(3);

    // Unchanged ordered CLI --json diagnostic-code baseline for this two-root lane. The two supported
    // HTTP calls add no model diagnostic, so this sequence must be byte-for-byte the pre-#54 baseline.
    private static readonly string[] ExpectedDiagnosticCodeBaseline =
        ["BE1001", "BE2010", "BE2010", "PRED001"];

    private const string ExpectedPostFlowFileName =
        "bll-tccintegration-tccservice-addcomplaint-bll-tccintegration-addcomplaintrequest-f1cc2038.md";
    private const string ExpectedGetFlowFileName =
        "bll-tccintegration-tccservice-lookups-94a25a61.md";

    // --- Claim 1: the POST root visibly presents exactly one conservative outbound HTTP POST boundary,
    // once, and never as a second generic HttpClient/PostAsync presentation of the same call site.
    [Fact]
    public void PostRootPresentsExactlyOneConservativePostBoundary()
    {
        var run = _lane.RequireRun1();
        var flow = run.RequireFlow(ExpectedPostFlowFileName);
        string markdown = flow.Markdown;
        string mermaid = flow.Mermaid;

        // Exactly one expected behaviour phrase; the opposite verb never appears.
        Assert.Equal(1, Count(markdown, PostBehaviorPhrase));
        Assert.Equal(0, Count(markdown, GetBehaviorPhrase));
        Assert.Equal(0, Count(markdown, GetMermaidMessage));

        // Direct observable (replaces the old literal-count proxy): the typed HTTP boundary is the ONLY
        // visible representation of this call site. No generic HttpClient participant and no generic
        // PostAsync/GetAsync Mermaid message anywhere in this operation's flow diagram.
        AssertNoGenericHttpClientPresentation(mermaid, flow.FileName);

        Assert.Equal(1, Count(mermaid, ExternalParticipantLabel));
        Assert.Equal(1, Count(mermaid, PostMermaidMessage));
        Assert.Equal(0, Count(mermaid, GetMermaidMessage));

        // Supported overload => no recognized-but-unsupported diagnostic (asserted on the CLI --json
        // diagnostic-code stream where SEQHTTP001 would actually surface).
        Assert.DoesNotContain(UnsupportedDiagnosticCode, run.DiagnosticCodes);

        // Unrelated conservative direct-call boundary count is unchanged by the HTTP model.
        Assert.Equal(2, Count(markdown, "SC-DIRECT-BODY-UNAVAILABLE"));
    }

    // --- Claim 2: the GET root visibly presents exactly one conservative outbound HTTP GET boundary,
    // once, and never as a second generic HttpClient/GetAsync presentation of the same call site.
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

        AssertNoGenericHttpClientPresentation(mermaid, flow.FileName);

        Assert.Equal(1, Count(mermaid, ExternalParticipantLabel));
        Assert.Equal(1, Count(mermaid, GetMermaidMessage));
        Assert.Equal(0, Count(mermaid, PostMermaidMessage));

        Assert.DoesNotContain(UnsupportedDiagnosticCode, run.DiagnosticCodes);

        Assert.Equal(1, Count(markdown, "SC-DIRECT-BODY-UNAVAILABLE"));
    }

    // --- G1 regression: the pure run-identity parser fails closed on every malformed-identity class
    // and returns the ordered tuple list for a well-formed input. Pure/synthetic - no fixture.
    [Fact]
    public void MalformedRunIdentityFailsClosed()
    {
        static IReadOnlyList<OutboundHttpRunIdentity> Parse(string json)
        {
            using var document = JsonDocument.Parse(json);
            return OutboundHttpRunIdentity.ParseRuns(document.RootElement);
        }

        // F9: a top-level object with no 'data', and a 'data' object with no 'runs', both fail closed
        // with the actionable "data.runs' is absent" message - RunCliAsync now calls ParseRuns
        // unconditionally (passing 'root' when 'data' is missing / not an object), so the lane raises
        // this named failure instead of a later bare Assert.NotEmpty.
        static IReadOnlyList<OutboundHttpRunIdentity> ParseData(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            bool hasData = root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object;
            return OutboundHttpRunIdentity.ParseRuns(hasData ? data : root);
        }

        Assert.Contains(
            "'data.runs' is absent",
            Assert.Throws<XunitException>(() => ParseData("{\"outcome\":\"succeeded\"}")).Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "'data.runs' is absent",
            Assert.Throws<XunitException>(() => ParseData("{\"data\":{\"toolchainVersion\":\"10.0.0\"}}")).Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "'data.runs' is absent",
            Assert.Throws<XunitException>(() => ParseData("{\"data\":\"not-an-object\"}")).Message,
            StringComparison.Ordinal);

        Assert.Contains("data.runs", Assert.Throws<XunitException>(() => Parse("{}")).Message, StringComparison.Ordinal);
        Assert.Contains("not a JSON array", Assert.Throws<XunitException>(() => Parse("{\"runs\":{}}")).Message, StringComparison.Ordinal);
        Assert.Contains("empty array", Assert.Throws<XunitException>(() => Parse("{\"runs\":[]}")).Message, StringComparison.Ordinal);
        Assert.Contains("not a run-identity object", Assert.Throws<XunitException>(() => Parse("{\"runs\":[1]}")).Message, StringComparison.Ordinal);
        Assert.Contains(
            "data.runs[0].profileId' is missing",
            Assert.Throws<XunitException>(() => Parse("{\"runs\":[{\"runId\":\"r\",\"indexFingerprint\":\"f\"}]}")).Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "data.runs[0].profileId' is Number, not a string",
            Assert.Throws<XunitException>(() => Parse("{\"runs\":[{\"profileId\":5,\"runId\":\"r\",\"indexFingerprint\":\"f\"}]}")).Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "data.runs[0].runId' is empty or whitespace",
            Assert.Throws<XunitException>(() => Parse("{\"runs\":[{\"profileId\":\"p\",\"runId\":\"  \",\"indexFingerprint\":\"f\"}]}")).Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "data.runs[0].indexFingerprint' is missing",
            Assert.Throws<XunitException>(() => Parse("{\"runs\":[{\"profileId\":\"p\",\"runId\":\"r\"}]}")).Message,
            StringComparison.Ordinal);

        var parsed = Parse(
            "{\"runs\":["
            + "{\"profileId\":\"p1\",\"runId\":\"r1\",\"indexFingerprint\":\"f1\"},"
            + "{\"profileId\":\"p2\",\"runId\":\"r2\",\"indexFingerprint\":\"f2\"}]}");
        Assert.Equal(
            new[]
            {
                new OutboundHttpRunIdentity("p1", "r1", "f1"),
                new OutboundHttpRunIdentity("p2", "r2", "f2"),
            },
            parsed);
    }

    // --- G2 regression: the pure artifact-hygiene scanner catches each leak class and is clean on
    // legitimate boundary text. Pure/synthetic - no fixture.
    [Fact]
    public void ArtifactHygieneScannerCatchesInjectedVolatileMarkers()
    {
        string[] forbidden = [@"C:\Users\ci\AppData\Local\Temp\sqhb-out-abcdef", "/var/tmp/sqhb-cache-123456"];

        Assert.Contains(
            OutboundHttpArtifactHygiene.FindVolatileMarkers(
                @"link: C:\Users\ci\AppData\Local\Temp\sqhb-out-abcdef\index.md", forbidden),
            m => m.Kind.Contains("path", StringComparison.Ordinal));
        Assert.Contains(
            OutboundHttpArtifactHygiene.FindVolatileMarkers(
                "link: C:/Users/ci/AppData/Local/Temp/sqhb-out-abcdef/index.md", forbidden),
            m => m.Kind.Contains("path", StringComparison.Ordinal));
        Assert.Contains(
            OutboundHttpArtifactHygiene.FindVolatileMarkers("generated 2026-09-04T12:30:00", forbidden),
            m => m.Kind.Contains("timestamp", StringComparison.Ordinal));
        Assert.Contains(
            OutboundHttpArtifactHygiene.FindVolatileMarkers("built 03:14:00 GMT on host", forbidden),
            m => m.Kind.Contains("clock", StringComparison.Ordinal));
        Assert.Contains(
            OutboundHttpArtifactHygiene.FindVolatileMarkers($"ran on {Environment.MachineName} today", forbidden),
            m => m.Kind == "machine-name");
        Assert.Empty(
            OutboundHttpArtifactHygiene.FindVolatileMarkers(
                "The method calls HttpClient.PostAsync at an outbound HTTP POST request boundary.", forbidden));
    }

    // --- G3 regression: the forbidden-corpus builder + scanner catches forbidden tokens and never
    // flags structural HTTP vocabulary or the accepted behavior phrase. Pure/synthetic - no fixture.
    [Fact]
    public void SensitiveCorpusCatchesForbiddenTokensButNotStructuralVocabulary()
    {
        var corpus = OutboundHttpSensitiveCorpus.Build(
        [
            new SensitiveConfigValue("TCCBaseAddress", "https://pre-internal-api.tcc-ltd.sa/"),
            new SensitiveConfigValue("TCCAPIKey", "Basic QUJDMTIzOnNlY3JldFZhbHVl"),
        ]);
        Assert.NotEmpty(corpus);
        Assert.Contains(corpus, e => e.Kind == "config-value" && e.Label.Contains("BaseAddress", StringComparison.Ordinal));
        Assert.Contains(corpus, e => e.Kind == "config-value" && e.Label.Contains("APIKey", StringComparison.Ordinal));

        var leaks = OutboundHttpSensitiveCorpus.FindLeaks(
            "payload has reporterNumber, path threeThirty/complaint/addComplaint, host pre-internal-api.tcc-ltd.sa, "
            + "reads HttpResponseMessage.IsSuccessStatusCode, calls JsonConvert.SerializeObject, "
            + "sees UpdateTypeList and TimeFrame and ReasonCodes, and a status list with a code",
            corpus);
        Assert.Contains(leaks, l => l.Kind == "payload-identifier");
        Assert.Contains(leaks, l => l.Kind == "request-path");
        Assert.Contains(leaks, l => l.Kind == "config-value");
        // Document-wide response/content BCL type names are their own kind and always a leak.
        Assert.Contains(leaks, l => l.Kind == "bcl-response-token");
        // The three step-name BCL tokens are a distinct kind (boundary-scoped at the call site).
        Assert.Contains(leaks, l => l.Kind == "bcl-token");
        Assert.Contains(corpus, e => e.Kind == "bcl-response-token" && e.Token == "StringContent");
        Assert.DoesNotContain(corpus, e => e.Kind == "bcl-token" && e.Token == "HttpResponseMessage");
        // F3: distinctive document-wide identifiers (UpdateTypeList/TimeFrame/ReasonCodes) and the
        // generic-English-word field names (list/status/code) are both caught by FindLeaks - they only
        // differ in WHERE the acceptance test scopes the scan, not in whether the pure scanner catches
        // them.
        Assert.Contains(corpus, e => e.Kind == "payload-identifier" && e.Token == "UpdateTypeList");
        Assert.Contains(leaks, l => l.Kind == "payload-identifier-generic");
        Assert.Contains(corpus, e => e.Kind == "payload-identifier-boundary" && e.Token == "code");
        // F3: the Accept header is now covered alongside Authorization.
        Assert.Contains(corpus, e => e.Kind == "header" && e.Token == "Accept");

        Assert.Empty(OutboundHttpSensitiveCorpus.FindLeaks(
            "The method calls HttpClient.PostAsync at an outbound HTTP POST request boundary. "
            + "An outbound HTTP GET request crosses the HTTP boundary.",
            corpus));
    }

    // The typed HTTP boundary is the only visible representation of the HttpClient call site: no generic
    // HttpClient participant, no PostAsync/GetAsync Mermaid message, no generic ".PostAsync("/".GetAsync("
    // call-syntax message in the flow diagram.
    private static void AssertNoGenericHttpClientPresentation(string mermaid, string flowFileName)
    {
        foreach (string line in mermaid.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("participant ", StringComparison.Ordinal))
            {
                Assert.False(
                    trimmed.Contains("HttpClient", StringComparison.Ordinal),
                    $"'{flowFileName}': generic HttpClient participant '{trimmed}' present alongside the typed HTTP boundary.");
            }

            bool isMessageLine = trimmed.Contains("->>", StringComparison.Ordinal)
                || trimmed.Contains("-->>", StringComparison.Ordinal);
            if (isMessageLine)
            {
                Assert.False(
                    trimmed.Contains("PostAsync", StringComparison.Ordinal)
                    || trimmed.Contains("GetAsync", StringComparison.Ordinal),
                    $"'{flowFileName}': generic HttpClient call Mermaid message '{trimmed}' present alongside the typed HTTP boundary.");
            }
        }
    }

    // --- Claim 3: the boundary presentation stays inside compiler evidence, is sensitive-value-safe, and
    // two clean runs are byte-identical including CLI diagnostics and the ordered run identity set.
    [Fact]
    public void OutputIsEvidenceBoundedValueSafeAndDeterministic()
    {
        var run1 = _lane.RequireRun1();
        var run2 = _lane.RequireRun2();

        // ---- Sensitive request literals from the normalised checkout's config must never appear in ANY
        // generated Markdown/Mermaid. On failure only the field label is reported, never the value.
        var sensitive = _lane.SensitiveConfigValues;
        Assert.NotEmpty(sensitive);
        Assert.Contains(sensitive, v => v.Label.Contains("BaseAddress", StringComparison.Ordinal));
        Assert.Contains(sensitive, v => v.Label.Contains("APIKey", StringComparison.Ordinal));
        foreach (var file in run1.Files.Where(IsRenderedDocument))
        {
            string text = Encoding.UTF8.GetString(file.Content);
            foreach (var (label, value) in sensitive)
            {
                if (value.Length != 0 && text.Contains(value, StringComparison.Ordinal))
                {
                    Assert.Fail($"sensitive config value for field '{label}' present in '{file.RelativePath}'.");
                }
            }
        }

        // ---- G3: explicit documented forbidden corpus (frozen TCC request/config/BCL facts + the
        // in-memory config values), scanned across EVERY generated artifact (all of run1.Files:
        // Markdown, Mermaid, technical-fallback sections, index.md, manifest, and .seqdoc/**).
        // Structural HTTP vocabulary is kept separate and asserted to remain.
        var forbiddenCorpus = OutboundHttpSensitiveCorpus.Build(_lane.SensitiveConfigValues);
        Assert.NotEmpty(forbiddenCorpus);
        Assert.Contains(forbiddenCorpus, e => e.Kind == "config-value" && e.Label.Contains("BaseAddress", StringComparison.Ordinal));
        Assert.Contains(forbiddenCorpus, e => e.Kind == "config-value" && e.Label.Contains("APIKey", StringComparison.Ordinal));

        // Request specifics and secrets (paths, header names/values, config keys, in-memory config
        // values, distinctive payload identifiers) plus the response/handler/content BCL type names
        // (ByteArrayContent, HttpResponseMessage, IsSuccessStatusCode, ReadAsStringAsync,
        // StringContent, HttpClientHandler, ServerCertificateCustomValidationCallback,
        // MediaTypeWithQualityHeaderValue - Kind "bcl-response-token") must be absent from EVERY
        // generated artifact. Only the three BCL type/method names that genuinely appear document-wide
        // as unrelated Method Flow step names (MediaTypeHeaderValue, SerializeObject, DeserializeObject
        // - Kind "bcl-token") are scoped to the boundary line + Mermaid message below, since removing
        // them document-wide would require a forbidden production Method Flow change.
        var docWideCorpus = forbiddenCorpus
            .Where(e => e.Kind != "bcl-token"
                && e.Kind != "payload-identifier-generic"
                && e.Kind != "payload-identifier-boundary")
            .ToArray();
        var boundaryScopedCorpus = forbiddenCorpus
            .Where(e => e.Kind is "bcl-token" or "payload-identifier-boundary")
            .ToArray();
        foreach (var file in run1.Files)
        {
            string text = Encoding.UTF8.GetString(file.Content);
            var leaks = OutboundHttpSensitiveCorpus.FindLeaks(text, docWideCorpus);
            Assert.True(
                leaks.Count == 0,
                $"'{file.RelativePath}' leaked forbidden token(s): "
                + string.Join(", ", leaks.Select(l => $"{l.Label}[{l.Kind}]").Distinct()));

            // CAUTION: HttpClient.PostAsync / HttpClient.GetAsync legitimately appear inside the
            // accepted Markdown behavior phrase. Assert their count never exceeds the number of
            // accepted behavior phrases in this file (no generic HttpClient call-syntax leak).
            if (file.RelativePath.EndsWith(".md", StringComparison.Ordinal))
            {
                Assert.Equal(Count(text, PostBehaviorPhrase), Count(text, "HttpClient.PostAsync"));
                Assert.Equal(Count(text, GetBehaviorPhrase), Count(text, "HttpClient.GetAsync"));
            }
        }

        // F3: generic-English-word DTO field names (see the corpus-build comment above) are a real
        // false-positive risk document-wide - observed matching "list"/"status" inside UNRELATED
        // auto-discovered-flow file names/links (e.g. index.md legitimately links to another analysed
        // method whose name happens to contain "list"/"status"), which is not a genuine leak. The only
        // files that can possibly carry a real TCC DTO field-name leak are the two configured POST/GET
        // flows themselves, so the check is scoped to exactly those two flows' Markdown + Mermaid.
        var genericWordCorpus = forbiddenCorpus.Where(e => e.Kind == "payload-identifier-generic").ToArray();
        foreach (var flow in new[]
                 {
                     run1.RequireFlow(ExpectedPostFlowFileName),
                     run1.RequireFlow(ExpectedGetFlowFileName),
                 })
        {
            var genericLeaks = OutboundHttpSensitiveCorpus.FindLeaks(flow.Markdown + "\n" + flow.Mermaid, genericWordCorpus);
            Assert.True(
                genericLeaks.Count == 0,
                $"'{flow.FileName}' leaked forbidden generic-word token(s): "
                + string.Join(", ", genericLeaks.Select(l => $"{l.Label}[{l.Kind}]").Distinct()));
        }

        // Structural vocabulary that must NOT be treated as a leak and must stay in the output.
        Assert.Equal(1, Count(run1.RequireFlow(ExpectedPostFlowFileName).Markdown, PostBehaviorPhrase));
        Assert.Equal(1, Count(run1.RequireFlow(ExpectedGetFlowFileName).Markdown, GetBehaviorPhrase));
        Assert.Equal(1, Count(run1.RequireFlow(ExpectedPostFlowFileName).Mermaid, ExternalParticipantLabel));
        Assert.Equal(1, Count(run1.RequireFlow(ExpectedGetFlowFileName).Mermaid, ExternalParticipantLabel));

        // ---- G2: direct checkout-path + timestamp hygiene over EVERY generated run-1 file. No
        // fixture-owned absolute path (OS or slash form) and no timestamp-shaped / volatile-runtime
        // marker may appear. Failure names the file and the marker KIND only, never a value.
        var ownedPathMarkers = _lane.OwnedPathMarkers;
        Assert.NotEmpty(ownedPathMarkers);
        foreach (var file in run1.Files)
        {
            string text = Encoding.UTF8.GetString(file.Content);
            var markers = OutboundHttpArtifactHygiene.FindVolatileMarkers(text, ownedPathMarkers);
            Assert.True(
                markers.Count == 0,
                $"'{file.RelativePath}' carries volatile/environment marker kind(s): "
                + string.Join(", ", markers.Select(m => m.Kind).Distinct()));
        }

        // ---- Gate 3/5: the outbound-HTTP boundary claim asserts ONLY the compiler-proven request
        // boundary - names the source evidence, carries an explicit non-strengthened certainty, and
        // withholds URI/host/header/body/credential/response/status/success/retry/resilience/
        // remote-completion wording.
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

            Assert.Contains($"evidence: {EvidenceSourcePath}", boundaryLine, StringComparison.Ordinal);
            Assert.Contains("certainty: Exact", boundaryLine, StringComparison.Ordinal);

            string scoped = boundaryLine + "\n" + mermaidMessageLine;
            foreach (string word in boundaryForbiddenWords)
            {
                Assert.DoesNotContain(word, scoped, StringComparison.OrdinalIgnoreCase);
            }

            // G3: no BCL request/response type token is the boundary vocabulary.
            foreach (var entry in boundaryScopedCorpus)
            {
                Assert.DoesNotContain(entry.Token, scoped, StringComparison.Ordinal);
            }
        }

        // Every generated flow *.md that carries either behaviour phrase (not only the two configured
        // roots - an automatic root can also reach an HttpClient call site) gets the same denylist,
        // scoped to that phrase's line plus its matching Mermaid message line.
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
        Assert.Equal(ExpectedDiagnosticCodeBaseline, run1.DiagnosticCodes);

        // ---- Complete ordered data.runs[] identity set: run1 == run2, and the amended
        // (profileId, indexFingerprint) pair is present exactly once. Never silently take run[0].
        AssertRunIdentitySet(run1, run2);

        // ---- CLI stderr: captured for both runs, compared deterministically, required empty (no
        // approved non-empty baseline string is pinned for this lane).
        Assert.Equal(run1.Stderr, run2.Stderr);
        Assert.True(
            string.IsNullOrWhiteSpace(run1.Stderr),
            $"CLI --json stderr was non-empty for the FraudManagement lane; captured value:\n{run1.Stderr}");

        // ---- Normalized digest over the ordered raw diagnostics[] JSON records.
        string run1DiagnosticsDigest = Sha256Hex(Encoding.UTF8.GetBytes(string.Concat(run1.DiagnosticRecords)));
        string run2DiagnosticsDigest = Sha256Hex(Encoding.UTF8.GetBytes(string.Concat(run2.DiagnosticRecords)));
        Assert.Equal(run1DiagnosticsDigest, run2DiagnosticsDigest);
        _output.WriteLine(
            $"[QHTTP-B matrix] diagnostics ordered-record digest run1={run1DiagnosticsDigest} run2={run2DiagnosticsDigest}");
    }

    // --- Claim 4: frozen external identity, normalised-checkout isolation + cleanup, version evidence,
    // artifact completeness/validity, and a real Mermaid CLI 11.16.0 render of every run-1 diagram.
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

        // ---- Shared supplied repository is byte-for-byte unchanged: tracked + untracked git status and
        // git worktree list recorded before and after the whole lane, asserted exactly equal.
        Assert.Equal(_lane.SharedRepoStatusBefore, _lane.SharedRepoStatusAfter);
        Assert.Equal(_lane.SharedWorktreeListBefore, _lane.SharedWorktreeListAfter);
        // F1: a linked worktree shares the SAME .git/config as the main repository unless
        // extensions.worktreeConfig=true is set, so `git status`/`git worktree list` equality alone does
        // NOT detect a shared-config-file mutation. Direct before/after proof over the shared
        // repository's local config (plain git local config for a checkout, not a secret).
        Assert.Equal(_lane.SharedConfigBefore, _lane.SharedConfigAfter);

        // ---- Profile / Program Index identity from data.runs[] (amended normalised-checkout values).
        AssertRunIdentitySet(run1, run2);

        // ---- G1: framework identity for this lane is pinned by the explicit --framework net9.0 CLI
        // arg plus the frozen constants; additionally assert the CLI reports net9.0 as available.
        Assert.Contains("net9.0", run1.AvailableTargetFrameworks);
        Assert.True(
            run1.AvailableTargetFrameworks.SequenceEqual(run2.AvailableTargetFrameworks),
            "data.availableTargetFrameworks differs between runs.");

        // ---- SDK / toolchain version from the CLI --json data (authoritative; not `dotnet --version`).
        Assert.False(
            string.IsNullOrWhiteSpace(run1.ToolchainVersion),
            "CLI --json data.toolchainVersion is absent/empty; QHTTP-B toolchain-version evidence is BLOCKED. "
            + "Do not fall back to `dotnet --version`.");
        Assert.Equal(run1.ToolchainVersion, run2.ToolchainVersion);
        _output.WriteLine($"[QHTTP-B matrix] CLI-reported .NET SDK/toolchain version = {run1.ToolchainVersion}");

        // ---- SeqDoc CLI assembly version (informational, else file version).
        string? seqdocCliVersion =
            typeof(CliHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(CliHost).Assembly.GetName().Version?.ToString();
        Assert.False(
            string.IsNullOrWhiteSpace(seqdocCliVersion),
            "Could not read a SeqDoc.Cli assembly version.");
        _output.WriteLine($"[QHTTP-B matrix] SeqDoc.Cli assembly version = {seqdocCliVersion}");

        // ---- Real Mermaid CLI version (must report 11.16.0).
        Assert.Equal("11.16.0", _lane.MermaidCliVersion);
        _output.WriteLine($"[QHTTP-B matrix] mermaid-cli --version = {_lane.MermaidCliVersion}");

        // ---- Fail loud if the CLI --json Mermaid budget path changed shape.
        Assert.True(
            _lane.MermaidBudgetResolvedFromJson,
            "CLI --json no longer exposes data.configuration.diagramBudget.maxMermaidCharacters.value; "
            + "the Mermaid budget assertions in this lane are no longer meaningful. Fix the JSON path.");

        // ---- Every named candidate-matrix artifact is present.
        run1.RequireFlow(ExpectedPostFlowFileName);
        run1.RequireFlow(ExpectedGetFlowFileName);
        Assert.Contains(run1.Files, f => f.RelativePath == "index.md");
        Assert.Contains(run1.Files, f => f.RelativePath == "seqdoc.manifest.json");
        Assert.True(
            run1.Files.Count(f => f.RelativePath.EndsWith(".mmd", StringComparison.Ordinal)) >= 2,
            "expected at least the POST and GET Mermaid diagrams.");

        // ---- Every Markdown link resolves.
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

        // ---- Index completeness.
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

        // ---- Manifest: every path relative; content-hash cross-check; listed set == generated
        // non-operational (rendered-document) set.
        var manifestEntries = new List<(string RelativePath, string Sha256)>();
        using (var manifestDocument = JsonDocument.Parse(
            run1.Files.Single(f => f.RelativePath == "seqdoc.manifest.json").Content))
        {
            foreach (var entry in manifestDocument.RootElement.GetProperty("files").EnumerateArray())
            {
                string path = entry.GetProperty("relativePath").GetString() ?? string.Empty;
                Assert.False(path.Length == 0, "manifest lists an empty path.");
                Assert.False(Path.IsPathRooted(path), $"manifest lists a rooted path '{path}'.");
                Assert.DoesNotContain(":", path, StringComparison.Ordinal);
                Assert.DoesNotContain("\\", path, StringComparison.Ordinal);
                Assert.False(path.StartsWith("//", StringComparison.Ordinal), $"manifest lists a UNC path '{path}'.");
                manifestEntries.Add((path, entry.GetProperty("sha256").GetString() ?? string.Empty));
            }
        }

        // issue #53 frozen matrix: exactly 35 listed files (17 flow .md + 17 .mmd + index.md). A silent automatic-root drop shrinks both the manifest and the generated set, so set-equality alone would not catch it.
        Assert.Equal(35, manifestEntries.Count);
        // frozen manifest byte length (normalised-checkout baseline recorded in test-writer-notes.md).
        Assert.Equal(6282, run1.Files.Single(f => f.RelativePath == "seqdoc.manifest.json").Content.Length);

        foreach (var (relativePath, listedHash) in manifestEntries)
        {
            var owned = run1.Files.SingleOrDefault(f => f.RelativePath == relativePath);
            Assert.True(owned is not null, $"manifest lists '{relativePath}' but no such file was generated.");
            Assert.Equal(listedHash, Sha256Hex(owned!.Content));
        }

        Assert.Equal(
            run1.Files.Select(f => f.RelativePath).Where(p => IsRenderedDocumentPath(p))
                .OrderBy(p => p, StringComparer.Ordinal),
            manifestEntries.Select(e => e.RelativePath).OrderBy(p => p, StringComparer.Ordinal));

        // ---- Per-artifact byte-length + SHA-256 for run 1 AND run 2, plus a complete-output digest
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
            $"[QHTTP-B matrix] mermaid budget={run1.MaxMermaidCharacters}; "
            + $"shared-repo tracked+untracked status before/after equal="
            + $"{_lane.SharedRepoStatusBefore == _lane.SharedRepoStatusAfter}; "
            + $"worktree-list before/after equal={_lane.SharedWorktreeListBefore == _lane.SharedWorktreeListAfter}; "
            + $"normalised-checkout HEAD={_lane.CheckoutHead}");

        foreach (var file in run1.Files.Where(f => f.RelativePath.EndsWith(".mmd", StringComparison.Ordinal)))
        {
            string mermaid = Encoding.UTF8.GetString(file.Content);
            Assert.True(
                mermaid.Length <= run1.MaxMermaidCharacters,
                $"'{file.RelativePath}' has {mermaid.Length} characters, over the configured budget {run1.MaxMermaidCharacters}.");
            Assert.Empty(MermaidValidator.Validate(mermaid));
        }

        // ---- Real Mermaid CLI 11.16.0 render of every run-1 diagram. Genuine unavailability is a LOUD
        // failure (the lane is BLOCKED), never a silent skip or pass.
        await RenderEveryDiagramWithMermaidCliAsync(run1, _lane.RegisterOwnedTempRoot);
    }

    // Complete ordered data.runs[] identity: run1 == run2 (all three fields), the amended
    // (profileId, indexFingerprint) pair present exactly once, exactly one entry, and a non-empty
    // runId on that entry (G1). Never silently takes run[0].
    private void AssertRunIdentitySet(OutboundHttpLaneRun run1, OutboundHttpLaneRun run2)
    {
        _output.WriteLine($"[QHTTP-B matrix] run1 identity set: {FormatIdentitySet(run1.IdentityPairs)}");
        _output.WriteLine($"[QHTTP-B matrix] run2 identity set: {FormatIdentitySet(run2.IdentityPairs)}");
        Assert.NotEmpty(run1.IdentitySet);
        Assert.True(
            run1.IdentitySet.SequenceEqual(run2.IdentitySet),
            $"data.runs[] identity set differs between runs: run1={FormatIdentitySet(run1.IdentityPairs)} run2={FormatIdentitySet(run2.IdentityPairs)}");
        var amendedPair = (
            OutboundHttpExternalCorpusFixture.ProfileId,
            OutboundHttpExternalCorpusFixture.ProgramIndexFingerprint);
        Assert.True(
            run1.IdentityPairs.Count(pair => pair == amendedPair) == 1,
            $"amended (profileId, indexFingerprint) pair not present exactly once; set={FormatIdentitySet(run1.IdentityPairs)}");
        // AGENTS gate 4 (isolation): the normalised two-root lane emits exactly one run identity; more than one entry means an unexpected extra root/profile reached the pipeline.
        Assert.Single(run1.IdentitySet);
        Assert.Equal(amendedPair, run1.IdentityPairs[0]);
        Assert.False(
            string.IsNullOrWhiteSpace(run1.IdentitySet[0].RunId),
            "data.runs[0].runId is absent/empty; the run identity is not fully captured.");
        Assert.Equal(run1.IdentitySet[0].RunId, run2.IdentitySet[0].RunId);
    }

    private static string FormatIdentitySet(ImmutableArray<(string ProfileId, string IndexFingerprint)> set) =>
        "[" + string.Join(" ; ", set.Select(p => $"({p.ProfileId}, {p.IndexFingerprint})")) + "]";

    private static bool IsRenderedDocument(OutboundHttpRenderedFile f) => IsRenderedDocumentPath(f.RelativePath);

    private static bool IsRenderedDocumentPath(string relativePath) =>
        relativePath.EndsWith(".md", StringComparison.Ordinal)
        || relativePath.EndsWith(".mmd", StringComparison.Ordinal);

    private static async Task RenderEveryDiagramWithMermaidCliAsync(
        OutboundHttpLaneRun run,
        Action<string> registerOwnedTempRoot)
    {
        string? npx = FindOnPath("npx");
        Assert.True(
            npx is not null && FindOnPath("node") is not null,
            "Mermaid CLI 11.16.0 is not runnable in this environment (node/npx missing). "
            + "Issue #53 requires this render; the QHTTP-B lane is BLOCKED, not skipped or passed.");

        string workDirectory = Path.Combine(Path.GetTempPath(), $"sqhb-mmd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDirectory);
        registerOwnedTempRoot(workDirectory);

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
            // IR-1 repair: start (never await) both stream reads BEFORE the bounded wait - see the
            // RunProcess repair note above for the deadlock this avoids.
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            // F5: bounded per-diagram wait. A hung mermaid-cli render must be killed and reported, never
            // hang the lane forever.
            using var renderTimeoutCts = new CancellationTokenSource(MermaidRenderTimeout);
            try
            {
                await process.WaitForExitAsync(renderTimeoutCts.Token);
            }
            catch (OperationCanceledException) when (renderTimeoutCts.IsCancellationRequested)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Process already exited between the timeout and Kill(); nothing further to do.
                }

                throw new XunitException(
                    $"mermaid-cli 11.16.0 did not render '{file.RelativePath}' within "
                    + $"{MermaidRenderTimeout.TotalMinutes} minute(s) and was killed. QHTTP-B is BLOCKED, not hung.");
            }

            string stdout = await stdoutTask;
            string stderr = await stderrTask;

            Assert.True(
                process.ExitCode == 0,
                $"mermaid-cli 11.16.0 failed to render '{file.RelativePath}' (exit {process.ExitCode}).\n{stdout}\n{stderr}");
            Assert.True(
                File.Exists(svgPath) && new FileInfo(svgPath).Length > 0,
                $"mermaid-cli 11.16.0 produced no SVG for '{file.RelativePath}'.");
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

public sealed record SensitiveConfigValue(string Label, string Value);

/// <summary>
/// One <c>data.runs[]</c> identity, fully captured (G1): every entry carries a non-empty
/// <c>profileId</c>, <c>runId</c>, and <c>indexFingerprint</c> or parsing fails closed.
/// </summary>
public sealed record OutboundHttpRunIdentity(string ProfileId, string RunId, string IndexFingerprint)
{
    /// <summary>
    /// Pure fail-closed parser for the CLI <c>--json</c> <c>data.runs[]</c> identity array. Throws
    /// <see cref="XunitException"/> with an actionable message naming the exact defect when the array
    /// is absent, not an array, empty, carries a non-object entry, or any entry is missing / non-string
    /// / empty for <c>profileId</c>, <c>runId</c>, or <c>indexFingerprint</c>.
    /// </summary>
    internal static IReadOnlyList<OutboundHttpRunIdentity> ParseRuns(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("runs", out var runs))
        {
            throw new XunitException(
                "CLI --json 'data.runs' is absent; run identity cannot be verified fail-closed.");
        }

        if (runs.ValueKind != JsonValueKind.Array)
        {
            throw new XunitException(
                $"CLI --json 'data.runs' is {runs.ValueKind}, not a JSON array.");
        }

        var result = new List<OutboundHttpRunIdentity>();
        int index = 0;
        foreach (var entry in runs.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                throw new XunitException(
                    $"CLI --json 'data.runs[{index}]' is {entry.ValueKind}, not a run-identity object.");
            }

            result.Add(new OutboundHttpRunIdentity(
                RequireString(entry, "profileId", index),
                RequireString(entry, "runId", index),
                RequireString(entry, "indexFingerprint", index)));
            index++;
        }

        if (result.Count == 0)
        {
            throw new XunitException(
                "CLI --json 'data.runs' is an empty array; no run identity was emitted.");
        }

        return result;
    }

    private static string RequireString(JsonElement entry, string property, int index)
    {
        if (!entry.TryGetProperty(property, out var value))
        {
            throw new XunitException($"CLI --json 'data.runs[{index}].{property}' is missing.");
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new XunitException(
                $"CLI --json 'data.runs[{index}].{property}' is {value.ValueKind}, not a string.");
        }

        string text = value.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new XunitException(
                $"CLI --json 'data.runs[{index}].{property}' is empty or whitespace.");
        }

        return text;
    }
}

public sealed record OutboundHttpLaneRun(
    string Outcome,
    ImmutableArray<string> DiagnosticCodes,
    ImmutableArray<string> DiagnosticRecords,
    string DiagnosticSummary,
    string Stderr,
    string? ToolchainVersion,
    ImmutableArray<string> AvailableTargetFrameworks,
    ImmutableArray<OutboundHttpRunIdentity> IdentitySet,
    int MaxMermaidCharacters,
    ImmutableArray<OutboundHttpRenderedFile> Files)
{
    // Amended frozen pair stays (ProfileId, ProgramIndexFingerprint); runId is carried separately.
    public ImmutableArray<(string ProfileId, string IndexFingerprint)> IdentityPairs =>
        [.. IdentitySet.Select(i => (i.ProfileId, i.IndexFingerprint))];

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
/// Materialises FraudManagement revision <c>7aabfef9…</c> in an isolated detached <c>git worktree</c>
/// under a short OS temp path, normalised with <c>core.autocrlf=false</c> / <c>core.eol=lf</c> BEFORE
/// checkout, restores it, and invokes the production CLI twice (fresh cache + output each) against ONLY
/// that normalised checkout. There is no in-place code path. The shared supplied repository is never
/// mutated: tracked + untracked <c>git status</c> and <c>git worktree list</c> are recorded before and
/// after and asserted equal. One cleanup owner (<see cref="CleanupAsync"/>) removes the worktree and
/// every owned temp root (cache, output, YAML, Mermaid renders) and verifies each is gone; a cleanup
/// failure throws. There is no skip path - every failure mode is a loud xUnit failure.
/// </summary>
public sealed class OutboundHttpExternalCorpusFixture : IAsyncLifetime
{
    public const string CorpusRevision = "7aabfef98fa4d47781bd8a98b9061ddcafb88836";
    public const string ProfileId = "profile:v1:f874be7e6b51bea2038f6cfac77ab510fc73e7208e9e47b4475e6a17896aaef1";

    // Amended Program Index fingerprint for the line-ending-normalised detached worktree
    // (issue #53 comment 5523887517). The historical in-place f9a36fd5… value is superseded.
    public const string ProgramIndexFingerprint =
        "df23b372a30754898787988bdf0c0537b2bdb19d14b3724c4314588281935117";

    public const string SolutionSha256 = "67d6b9f15be05f86c06ea17fa92dd7474b8886b876d1d69cf11552741ffaaca1";
    public const string BllProjectSha256 = "c38a35ee7b3acf227fb9988ced35dceb2dec36165e8f8f01bb6b82ee6f658a06";
    public const string SourceSha256 = "eff261211900578a493d40900cd0de5418dbbd132bbc4f806f684b31e184dfce";

    private const string PostRootHash =
        "method:v1:c3310b12f1a331d7ee9871a964209e89da0a0dcb84b086e4b62cbbbdc2a66417";
    private const string GetRootHash =
        "method:v1:b7a44d4b1128669b35cda87326e73098991a24dbd0b975b9986c9050b8b45504";

    // F5: bounded waits so the lane is cancellable/killable instead of hanging forever. Each timeout
    // comfortably exceeds observed real durations (the full lane today is a few minutes cold) without
    // making the lane flaky.
    private static readonly TimeSpan GitProcessTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan DotnetRestoreTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CliAnalyzeTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MermaidVersionCheckTimeout = TimeSpan.FromMinutes(3);

    private readonly List<string> _ownedTempRoots = [];
    private readonly object _ownedTempRootsLock = new();
    private string? _corpusGitToplevel;
    private string? _worktreePath;
    private bool _worktreeCleanedUp;
    private bool _sharedStateSnapshotTaken;

    public OutboundHttpLaneRun? Run1 { get; private set; }

    public OutboundHttpLaneRun? Run2 { get; private set; }

    public string CheckoutHead { get; private set; } = string.Empty;

    public FrozenExternalHashes FrozenBlobHashes { get; private set; } = new("", "", "");

    public string SharedRepoStatusBefore { get; private set; } = string.Empty;

    public string SharedRepoStatusAfter { get; private set; } = string.Empty;

    public string SharedWorktreeListBefore { get; private set; } = string.Empty;

    public string SharedWorktreeListAfter { get; private set; } = string.Empty;

    public string SharedConfigBefore { get; private set; } = string.Empty;

    public string SharedConfigAfter { get; private set; } = string.Empty;

    public ImmutableArray<SensitiveConfigValue> SensitiveConfigValues { get; private set; } = [];

    public string? MermaidCliVersion { get; private set; }

    private bool _mermaidBudgetResolvedFromJson;

    public bool MermaidBudgetResolvedFromJson => _mermaidBudgetResolvedFromJson;

    public OutboundHttpLaneRun RequireRun1() => Run1
        ?? throw new XunitException("the FraudManagement outbound-HTTP acceptance lane produced no run 1 result.");

    public OutboundHttpLaneRun RequireRun2() => Run2
        ?? throw new XunitException("the FraudManagement outbound-HTTP acceptance lane produced no run 2 result.");

    public void RegisterOwnedTempRoot(string path)
    {
        lock (_ownedTempRootsLock)
        {
            _ownedTempRoots.Add(path);
        }
    }

    /// <summary>
    /// G2: every fixture-owned absolute path root (worktree parent + both CLI out roots + both CLI
    /// cache roots + the YAML cfg root + the Mermaid-render root) plus the isolated worktree path.
    /// None of these may appear - in OS or forward-slash form - in any generated artifact.
    /// </summary>
    public IReadOnlyList<string> OwnedPathMarkers
    {
        get
        {
            lock (_ownedTempRootsLock)
            {
                var markers = new List<string>(_ownedTempRoots);
                if (_worktreePath is not null)
                {
                    markers.Add(_worktreePath);
                }

                return markers
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            string providedRoot = ExternalCorpusResolver.Current.RequireGroup(ExternalCorpusGroup.Provided).Root;
            string corpusDir = Path.Combine(providedRoot, "FraudManagement");
            if (!File.Exists(Path.Combine(corpusDir, "FraudManagement.sln")))
            {
                throw new XunitException(
                    $"the Provided corpus is installed but FraudManagement is missing at '{corpusDir}'. "
                    + "That is a QHTTP-B lane failure, not a skip.");
            }

            var toplevelResult = RunProcess("git", corpusDir, "rev-parse", "--show-toplevel");
            if (toplevelResult.ExitCode != 0)
            {
                throw new XunitException($"could not resolve the FraudManagement git toplevel: {toplevelResult.StdErr}");
            }

            _corpusGitToplevel = toplevelResult.StdOut.Trim();

            // Identity: the revision must exist in the shared repo.
            var revCheck = RunProcess("git", _corpusGitToplevel, "cat-file", "-e", $"{CorpusRevision}^{{commit}}");
            if (revCheck.ExitCode != 0)
            {
                throw new XunitException(
                    $"revision '{CorpusRevision}' is not present in the FraudManagement corpus repository. "
                    + "QHTTP-B is BLOCKED until the corpus carries the frozen revision; this is not a skip.");
            }

            SharedWorktreeListBefore = RunProcessOrThrow("git", _corpusGitToplevel, "worktree", "list", "--porcelain");
            SharedRepoStatusBefore = RunProcessOrThrow("git", _corpusGitToplevel, "status", "--porcelain");
            // F1: direct proof the shared repository's LOCAL git config file is never mutated. A linked
            // worktree shares the same .git/config as the main repository unless
            // extensions.worktreeConfig=true is set, so a persistent `git config` write against the
            // worktree path would silently land here. Captured before any worktree operation.
            SharedConfigBefore = RunProcessOrThrow("git", _corpusGitToplevel, "config", "--list", "--local");

            // Isolated detached worktree, line-ending-normalised. core.longpaths/autocrlf/eol are applied
            // as command-scoped `-c` overrides on the checkout invocation only (the sole step that reads
            // them) so no config file - shared or otherwise - is ever written to disk.
            string worktreeParent = NewOwnedTempRoot("wt");
            _worktreePath = Path.Combine(worktreeParent, "fm");
            RunProcessOrThrow("git", _corpusGitToplevel, "worktree", "add", "--no-checkout", "--detach", _worktreePath, CorpusRevision);
            RunProcessOrThrow("git", _worktreePath, "sparse-checkout", "set", "--no-cone",
                "/*", "!obj/", "!bin/", "!packages/", "!.vs/", "!PackageTmp/");
            RunProcessOrThrow(
                "git", _worktreePath,
                "-c", "core.longpaths=true", "-c", "core.autocrlf=false", "-c", "core.eol=lf",
                "checkout");

            CheckoutHead = RunProcessOrThrow("git", _worktreePath, "rev-parse", "HEAD").Trim();
            if (!string.Equals(CheckoutHead, CorpusRevision, StringComparison.Ordinal))
            {
                throw new XunitException(
                    $"normalised worktree HEAD is '{CheckoutHead}', not the frozen revision '{CorpusRevision}'.");
            }

            FrozenBlobHashes = new FrozenExternalHashes(
                GitBlobSha256(_worktreePath, "FraudManagement.sln"),
                GitBlobSha256(_worktreePath, "BLL/BLL.csproj"),
                GitBlobSha256(_worktreePath, "BLL/TCCIntegration/TCCService.cs"));

            SensitiveConfigValues = ReadSensitiveConfigValues(_worktreePath);

            var restore = RunProcess(
                "dotnet", _worktreePath, DotnetRestoreTimeout, "restore",
                Path.Combine(_worktreePath, "FraudManagement.sln"));
            if (restore.ExitCode != 0)
            {
                throw new XunitException(
                    $"`dotnet restore` on the normalised FraudManagement worktree failed (exit {restore.ExitCode}). "
                    + $"QHTTP-B is BLOCKED; this is not a skip.\n{restore.StdOut}\n{restore.StdErr}");
            }

            MermaidCliVersion = ReadMermaidCliVersion();

            string configRoot = NewOwnedTempRoot("cfg");
            string configPath = Path.Combine(configRoot, "outbound-http-two-root.seqdoc.yaml");
            await File.WriteAllTextAsync(
                configPath,
                "schemaVersion: 1\n"
                + "selection:\n"
                + "  roots:\n"
                + $"    - {PostRootHash}\n"
                + $"    - {GetRootHash}\n");

            string solution = Path.Combine(_worktreePath, "FraudManagement.sln");

            Run1 = await RunCliAsync(solution, _worktreePath, configPath);
            AssertSucceeded(Run1, "first");
            Run2 = await RunCliAsync(solution, _worktreePath, configPath);
            AssertSucceeded(Run2, "second determinism");
        }
        finally
        {
            await CleanupAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await CleanupAsync();
    }

    // The single cleanup owner. Idempotent and re-runnable: called from InitializeAsync's finally (which
    // also snapshots the shared-repo state the tests assert against) and again from DisposeAsync (which
    // catches any temp root a test registered after init, e.g. the Mermaid-render dir). The worktree
    // removal and the shared-state snapshot happen once; the owned-temp-root sweep + verification run
    // every time.
    private async Task CleanupAsync()
    {
        var failures = new List<string>();

        // The production CLI opens the SQLite cache with connection pooling on, so the pool keeps the
        // cache-v1.db file handle open past the run. Release every pooled handle (and run finalizers)
        // before deleting the owned cache roots - otherwise deletion loses a genuine race, not a real
        // isolation failure.
        try
        {
            Type.GetType("Microsoft.Data.Sqlite.SqliteConnection, Microsoft.Data.Sqlite")?
                .GetMethod("ClearAllPools", BindingFlags.Public | BindingFlags.Static)?
                .Invoke(null, null);
        }
        catch (Exception exception) when (exception is TargetInvocationException or MissingMethodException)
        {
            // Best effort; the bounded deletion retry below still guards the real assertion.
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();

        // 1. Remove the detached worktree from the shared repo (bounded retry for OneDrive locks).
        if (!_worktreeCleanedUp && _worktreePath is not null && _corpusGitToplevel is not null)
        {
            bool removed = false;
            for (int attempt = 1; attempt <= 2 && !removed; attempt++)
            {
                // IR-2 repair: a slow OneDrive-lock condition can legitimately exceed the default
                // RunProcess timeout, and that now throws (fixed by the IR-1 repair) instead of hanging.
                // Treat a timeout here as a failed attempt only - swallow it and fall through to the
                // existing removed-check/retry logic, so CleanupAsync still always reaches the temp-root
                // sweep and the shared-state "after" snapshot below.
                try
                {
                    RunProcess("git", _corpusGitToplevel, "worktree", "remove", "--force", _worktreePath);
                    RunProcess("git", _corpusGitToplevel, "worktree", "prune");
                }
                catch (XunitException)
                {
                    // Treated as a failed attempt; the removed-check below decides whether to retry.
                }

                try
                {
                    removed = !Directory.Exists(_worktreePath) && !IsRegisteredWorktree(_corpusGitToplevel, _worktreePath);
                }
                catch (XunitException)
                {
                    // IsRegisteredWorktree's own RunProcess call also uses the default bounded timeout;
                    // treat that timeout the same way - a failed attempt, not an aborted CleanupAsync.
                    removed = false;
                }

                if (!removed && attempt == 1)
                {
                    await Task.Delay(750);
                }
            }

            // F2: only mark cleanup complete once removal is CONFIRMED. If both attempts fail, leave the
            // flag false so a later CleanupAsync call (from DisposeAsync) re-enters this block and
            // retries, while THIS call still fails loudly below.
            if (removed)
            {
                _worktreeCleanedUp = true;
            }
            else
            {
                failures.Add($"detached worktree '{_worktreePath}' is still present or registered after cleanup.");
            }
        }

        // 2. Delete every owned temp root (bounded retry).
        string[] roots;
        lock (_ownedTempRootsLock)
        {
            roots = _ownedTempRoots.ToArray();
        }

        foreach (string root in roots)
        {
            bool gone = false;
            for (int attempt = 1; attempt <= 4 && !gone; attempt++)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }

                    gone = !Directory.Exists(root);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    if (attempt < 4)
                    {
                        await Task.Delay(500 * attempt);
                    }
                }
            }

            if (!gone)
            {
                failures.Add($"owned temp root '{root}' could not be deleted.");
            }
        }

        // 3. Record the shared repository state after cleanup for the isolation assertions (once).
        if (!_sharedStateSnapshotTaken && _corpusGitToplevel is not null)
        {
            _sharedStateSnapshotTaken = true;
            SharedWorktreeListAfter = RunProcessOrThrow("git", _corpusGitToplevel, "worktree", "list", "--porcelain");
            SharedRepoStatusAfter = RunProcessOrThrow("git", _corpusGitToplevel, "status", "--porcelain");
            SharedConfigAfter = RunProcessOrThrow("git", _corpusGitToplevel, "config", "--list", "--local");
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "QHTTP-B fixture cleanup did not complete: " + string.Join(" | ", failures));
        }
    }

    private static void AssertSucceeded(OutboundHttpLaneRun run, string label)
    {
        if (!string.Equals(run.Outcome, "succeeded", StringComparison.OrdinalIgnoreCase))
        {
            throw new XunitException(
                $"{label} CLI run outcome was '{run.Outcome}' (expected 'succeeded'). "
                + $"Diagnostics: {run.DiagnosticSummary}. QHTTP-B is BLOCKED; this is not a skip.");
        }
    }

    private async Task<OutboundHttpLaneRun> RunCliAsync(string solution, string laneRoot, string configPath)
    {
        string outputDirectory = NewOwnedTempRoot("out");
        string cacheDirectory = NewOwnedTempRoot("cache");

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
        // F5: bounded wait - the CLI analyze run must be cancellable/killable instead of running forever.
        using var cliTimeoutCts = new CancellationTokenSource(CliAnalyzeTimeout);
        try
        {
            await CliHost.RunAsync(args, standardOutput, standardError, cliTimeoutCts.Token);
        }
        catch (OperationCanceledException) when (cliTimeoutCts.IsCancellationRequested)
        {
            throw new XunitException(
                $"CLI `analyze` did not complete within {CliAnalyzeTimeout.TotalMinutes} minute(s). "
                + "QHTTP-B is BLOCKED, not hung.");
        }

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

        string? toolchainVersion = null;
        var availableTfms = ImmutableArray.CreateBuilder<string>();
        bool hasData = root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object;
        if (hasData)
        {
            if (data.TryGetProperty("toolchainVersion", out var tv) && tv.ValueKind == JsonValueKind.String)
            {
                toolchainVersion = tv.GetString();
            }

            if (data.TryGetProperty("availableTargetFrameworks", out var tfms)
                && tfms.ValueKind == JsonValueKind.Array)
            {
                foreach (var tfm in tfms.EnumerateArray())
                {
                    if (tfm.ValueKind == JsonValueKind.String)
                    {
                        availableTfms.Add(tfm.GetString() ?? string.Empty);
                    }
                }
            }
        }

        // G1 / F9: fail closed on malformed run identity ALWAYS - a missing/non-object 'data' must
        // raise the named actionable XunitException here, never fall through to a bare Assert.NotEmpty.
        ImmutableArray<OutboundHttpRunIdentity> identitySet =
            [.. OutboundHttpRunIdentity.ParseRuns(hasData ? data : root)];

        return new OutboundHttpLaneRun(
            outcome,
            codes.ToImmutable(),
            records.ToImmutable(),
            summary.ToString().Trim(),
            standardError.ToString(),
            toolchainVersion,
            availableTfms.ToImmutable(),
            identitySet,
            ReadMermaidBudget(root),
            ReadGeneratedFiles(outputDirectory));
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

    // Reads the actual non-empty TCC base-address / API-key request literals from the normalised
    // checkout's config files, in memory only. Values are never persisted or emitted.
    private static ImmutableArray<SensitiveConfigValue> ReadSensitiveConfigValues(string worktreeRoot)
    {
        var found = new List<SensitiveConfigValue>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var xmlPattern = new Regex(
            "<add\\s+key=\"(?<key>TCC[A-Za-z]*)\"\\s+value=\"(?<value>[^\"]*)\"",
            RegexOptions.IgnoreCase);
        var jsonPattern = new Regex(
            "\"(?<key>TCC[A-Za-z]*)\"\\s*:\\s*\"(?<value>[^\"]*)\"",
            RegexOptions.IgnoreCase);

        foreach (string path in Directory.EnumerateFiles(worktreeRoot, "*", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(path);
            bool isConfig = name.EndsWith(".config", StringComparison.OrdinalIgnoreCase);
            bool isAppSettings = name.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            bool isWebConfig = name.Equals("web.config", StringComparison.OrdinalIgnoreCase);
            if (!isConfig && !isAppSettings && !isWebConfig)
            {
                continue;
            }

            string content;
            try
            {
                content = File.ReadAllText(path);
            }
            catch (IOException)
            {
                // F4: this is a REQUIRED lane (issue #53 - no skip path). Silently skipping an unreadable
                // matching config file would let a real secret in that file escape the forbidden corpus
                // while the lane still passed. Fail loud, naming only the RELATIVE path - never contents.
                throw new XunitException(
                    $"could not read sensitive-config candidate '{Path.GetRelativePath(worktreeRoot, path)}' "
                    + "for the forbidden-value corpus; QHTTP-B is BLOCKED, not best-effort.");
            }

            foreach (var pattern in new[] { xmlPattern, jsonPattern })
            {
                foreach (Match match in pattern.Matches(content))
                {
                    string key = match.Groups["key"].Value;
                    string value = match.Groups["value"].Value;
                    bool sensitiveKey =
                        key.Contains("BaseAddress", StringComparison.OrdinalIgnoreCase)
                        || key.Contains("APIKey", StringComparison.OrdinalIgnoreCase)
                        || key.Contains("Address", StringComparison.OrdinalIgnoreCase)
                        || key.Contains("Url", StringComparison.OrdinalIgnoreCase)
                        || key.Contains("Uri", StringComparison.OrdinalIgnoreCase)
                        || key.Contains("Key", StringComparison.OrdinalIgnoreCase)
                        || key.Contains("Secret", StringComparison.OrdinalIgnoreCase)
                        || key.Contains("Password", StringComparison.OrdinalIgnoreCase);
                    if (!sensitiveKey || value.Length == 0)
                    {
                        continue;
                    }

                    if (seen.Add($"{key}\u0000{value}"))
                    {
                        found.Add(new SensitiveConfigValue(key, value));
                    }

                    // Also treat the host component of any absolute URI value as sensitive.
                    if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
                        && !string.IsNullOrEmpty(uri.Host)
                        && seen.Add($"{key}.host\u0000{uri.Host}"))
                    {
                        found.Add(new SensitiveConfigValue($"{key} (host)", uri.Host));
                    }
                }
            }
        }

        return found.ToImmutableArray();
    }

    private string NewOwnedTempRoot(string label)
    {
        string path = Path.Combine(Path.GetTempPath(), $"sqhb-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        RegisterOwnedTempRoot(path);
        return path;
    }

    private static string GitBlobSha256(string worktreeRoot, string relativePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = worktreeRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("show");
        startInfo.ArgumentList.Add($"{CorpusRevision}:{relativePath}");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git.");
        using var buffer = new MemoryStream();
        // IR-1 repair: start (never block on) both the stdout copy and the stderr read BEFORE the bounded
        // wait - see the RunProcess repair note above for the deadlock this avoids.
        Task copyTask = process.StandardOutput.BaseStream.CopyToAsync(buffer);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        // F5: bounded wait - a hung `git show` must be killed and reported, never hang the lane forever.
        if (!process.WaitForExit((int)GitProcessTimeout.TotalMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Process already exited between the timeout check and Kill(); nothing further to do.
            }

            throw new XunitException(
                $"`git show {CorpusRevision}:{relativePath}` did not exit within "
                + $"{GitProcessTimeout.TotalSeconds}s and was killed. QHTTP-B is BLOCKED, not hung.");
        }

        copyTask.GetAwaiter().GetResult();
        string error = stderrTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new XunitException($"git show {CorpusRevision}:{relativePath} failed: {error}");
        }

        return Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
    }

    private static string ReadMermaidCliVersion()
    {
        string? npx = FindOnPathStatic("npx");
        if (npx is null || FindOnPathStatic("node") is null)
        {
            throw new XunitException(
                "node/npx are not runnable in this environment; the Mermaid CLI 11.16.0 version cannot be "
                + "confirmed. Issue #53 requires this render; the QHTTP-B lane is BLOCKED, not skipped.");
        }

        var result = RunProcess(
            npx, Path.GetTempPath(), MermaidVersionCheckTimeout,
            "--yes", "@mermaid-js/mermaid-cli@11.16.0", "--version");
        if (result.ExitCode != 0)
        {
            throw new XunitException(
                $"`npx --yes @mermaid-js/mermaid-cli@11.16.0 --version` failed (exit {result.ExitCode}).\n{result.StdErr}");
        }

        // mmdc prints just the version number (e.g. "11.16.0").
        return result.StdOut.Trim();
    }

    private static string? FindOnPathStatic(string executable)
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

    private static bool IsRegisteredWorktree(string toplevel, string worktreePath)
    {
        string expected = Path.GetFullPath(worktreePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var result = RunProcess("git", toplevel, "worktree", "list", "--porcelain");
        foreach (string line in result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith("worktree ", StringComparison.Ordinal))
            {
                continue;
            }

            string actual = Path.GetFullPath(line["worktree ".Length..].Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string RunProcessOrThrow(string executable, string workingDirectory, params string[] arguments)
    {
        var result = RunProcess(executable, workingDirectory, arguments);
        if (result.ExitCode != 0)
        {
            throw new XunitException(
                $"`{executable} {string.Join(' ', arguments)}` failed (exit {result.ExitCode}) in '{workingDirectory}'.\n"
                + $"{result.StdOut}\n{result.StdErr}");
        }

        return result.StdOut;
    }

    private static (int ExitCode, string StdOut, string StdErr) RunProcess(
        string executable,
        string workingDirectory,
        params string[] arguments) => RunProcess(executable, workingDirectory, GitProcessTimeout, arguments);

    // F5: bounded wait instead of an unbounded `process.WaitForExit()`. A timed-out process is killed
    // (entire tree) and reported as an actionable failure naming the executable/arguments/timeout - never
    // a secret value.
    private static (int ExitCode, string StdOut, string StdErr) RunProcess(
        string executable,
        string workingDirectory,
        TimeSpan timeout,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
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
            ?? throw new InvalidOperationException($"Could not start '{executable}'.");
        // IR-1 repair: start (never block on) both stream reads BEFORE the bounded wait, so a child that
        // fills one pipe's OS buffer while we would otherwise be synchronously blocked reading the OTHER
        // stream first cannot deadlock the process before the timeout check is ever reached.
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        bool exited = process.WaitForExit((int)timeout.TotalMilliseconds);
        if (!exited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Process already exited between the timeout check and Kill(); nothing further to do.
            }

            throw new XunitException(
                $"`{executable} {string.Join(' ', arguments)}` in '{workingDirectory}' did not exit within "
                + $"{timeout.TotalSeconds}s and was killed. QHTTP-B is BLOCKED, not hung.");
        }

        string stdout = stdoutTask.GetAwaiter().GetResult().Trim();
        string stderr = stderrTask.GetAwaiter().GetResult().Trim();
        return (process.ExitCode, stdout, stderr);
    }
}

/// <summary>
/// G2: pure scanner for checkout-path and timestamp/volatile-runtime hygiene. Returns every offending
/// match as <c>(Marker, Kind)</c> where <c>Marker</c> is safe to log for path/timestamp classes and is
/// the KIND label (never the value) for environment identity classes.
/// </summary>
internal static class OutboundHttpArtifactHygiene
{
    // ISO-8601 date or date-time. Bare 4-digit years and dotted version tokens (net9.0, 9.0.11) are
    // deliberately NOT matched - the literal "-NN-NN" shape is required.
    private static readonly Regex IsoTimestamp = new(
        @"\b\d{4}-\d{2}-\d{2}([T ]\d{2}:\d{2}(:\d{2})?)?\b", RegexOptions.Compiled);

    // Clock string with a GMT/UTC zone suffix.
    private static readonly Regex ZonedClock = new(
        @"\b\d{1,2}:\d{2}(:\d{2})?\s*(GMT|UTC)\b", RegexOptions.Compiled);

    internal static IReadOnlyList<(string Marker, string Kind)> FindVolatileMarkers(
        string text, IEnumerable<string> forbiddenPaths)
    {
        var hits = new List<(string, string)>();

        foreach (string path in forbiddenPaths)
        {
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            if (text.Contains(path, StringComparison.OrdinalIgnoreCase))
            {
                hits.Add((path, "fixture-owned-path"));
            }

            string slash = path.Replace('\\', '/');
            if (!string.Equals(slash, path, StringComparison.Ordinal)
                && text.Contains(slash, StringComparison.OrdinalIgnoreCase))
            {
                hits.Add((slash, "fixture-owned-path-slash"));
            }
        }

        foreach (Match match in IsoTimestamp.Matches(text))
        {
            hits.Add((match.Value, "iso-8601-timestamp"));
        }

        foreach (Match match in ZonedClock.Matches(text))
        {
            hits.Add((match.Value, "zoned-clock"));
        }

        foreach (var (token, kind) in new[]
                 {
                     (Environment.MachineName, "machine-name"),
                     (Environment.UserName, "user-name"),
                     (Environment.UserDomainName, "user-domain"),
                 })
        {
            if (!string.IsNullOrWhiteSpace(token)
                && token.Length >= 4
                && text.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                hits.Add((kind, kind));
            }
        }

        return hits;
    }
}

/// <summary>G3: one frozen forbidden-corpus entry. <c>Token</c> is scanned literally.</summary>
internal sealed record OutboundHttpCorpusEntry(string Label, string Kind, string Token);

/// <summary>
/// G3: builds the explicit documented forbidden corpus (frozen TCC request-path / header / config-key /
/// BCL / payload-identifier facts plus the in-memory configuration values) and scans text for it.
/// Structural HTTP vocabulary is deliberately excluded (see <see cref="StructuralVocabulary"/>).
/// </summary>
internal static class OutboundHttpSensitiveCorpus
{
    // Legitimate structural vocabulary - never a leak.
    internal static readonly string[] StructuralVocabulary =
    [
        "HTTP boundary", "HTTP POST request", "HTTP GET request",
        "HTTP", "POST", "GET", "request", "boundary", "outbound",
        "The method calls HttpClient.PostAsync at an outbound HTTP POST request boundary.",
        "The method calls HttpClient.GetAsync at an outbound HTTP GET request boundary.",
    ];

    internal static IReadOnlyList<OutboundHttpCorpusEntry> Build(
        IEnumerable<SensitiveConfigValue> configValues)
    {
        var entries = new List<OutboundHttpCorpusEntry>();

        void Add(string label, string kind, string token)
        {
            if (!string.IsNullOrWhiteSpace(token))
            {
                entries.Add(new OutboundHttpCorpusEntry(label, kind, token));
            }
        }

        foreach (string path in new[]
                 {
                     "threeThirty/complaint/addComplaint",
                     "threeThirty/lookup/updateType/all",
                     "threeThirty/notification/update",
                     "threeThirty/callBypass/add",
                     "threeThirty",
                 })
        {
            Add($"request-path:{path}", "request-path", path);
        }

        Add("header-name:Authorization", "header", "Authorization");
        Add("header-name:Accept", "header", "Accept");
        Add("media-type:application/json", "media-type", "application/json");

        foreach (string key in new[] { "TCCBaseAddress", "TCCAPIKey", "_baseAddress", "_APIKey" })
        {
            Add($"config-key:{key}", "config-key", key);
        }

        // Boundary-scoped: these three BCL type/method names genuinely appear document-wide as
        // Method Flow step names of JsonConvert.SerializeObject(request) / new MediaTypeHeaderValue(...)
        // / JsonConvert.DeserializeObject<...> in BLL/TCCIntegration/TCCService.cs. They are not
        // removable without a forbidden production Method Flow change, so the acceptance claim is only
        // that they never become the outbound-HTTP BOUNDARY vocabulary (boundary line + Mermaid message).
        foreach (string token in new[] { "MediaTypeHeaderValue", "SerializeObject", "DeserializeObject" })
        {
            Add($"bcl-token:{token}", "bcl-token", token);
        }

        // Document-wide: response/handler/content BCL type names that must never appear in ANY generated
        // artifact (checkpoint Risk 4 / non-goal: no status/success/response claims).
        foreach (string token in new[]
                 {
                     "ByteArrayContent", "HttpResponseMessage", "IsSuccessStatusCode", "ReadAsStringAsync",
                     "StringContent", "HttpClientHandler", "ServerCertificateCustomValidationCallback",
                     "MediaTypeWithQualityHeaderValue",
                 })
        {
            Add($"bcl-response-token:{token}", "bcl-response-token", token);
        }

        foreach (string token in new[]
                 {
                     "reporterNumber", "reportedIdentity", "typeOfComplaint", "operatorTcn",
                     "serviceRating", "serviceFeedback", "tccTcn", "updateType",
                 })
        {
            Add($"payload-id:{token}", "payload-identifier", token);
        }

        // F3: distinctive DTO/model identifiers from the frozen source, unlikely to collide with
        // legitimate Method Flow / Markdown prose - checked document-wide with a plain literal scan
        // (Ordinal, exact casing). See test-writer-notes.md for the per-token false-positive check.
        foreach (string token in new[]
                 {
                     "contentType", "UpdateTypeList", "TimeFrame", "ReasonCodes", "Tcn", "tcn",
                 })
        {
            Add($"payload-id:{token}", "payload-identifier", token);
        }

        // F3: generic-English-word DTO/model field names from the frozen source. A document-wide plain
        // scan over these is a real false-positive risk against unrelated legitimate content (observed:
        // "list"/"status" matched inside unrelated auto-discovered-flow file paths recorded in
        // .seqdoc/journal.json - see test-writer-notes.md). Kept as their own kind so the scan below can
        // scope them to rendered documents only (.md/.mmd), the only place a genuine DTO field-name leak
        // could actually manifest; the manifest/journal never carry arbitrary source identifiers.
        foreach (string token in new[] { "Code", "description", "Description", "reason", "list", "status" })
        {
            Add($"payload-id:{token}", "payload-identifier-generic", token);
        }

        // F3: "message" / "code" / "Value" genuinely appear document-wide in the accepted POST flow
        // itself - they are the compiler-observed AddComplaintRequest/-Response/UpdateType field NAMES
        // narrated as ordinary Method Flow assignment steps (e.g. "assigns: message = ...") in
        // BLL/TCCIntegration/TCCService.cs, not a runtime value leak. Same non-removability precedent as
        // "bcl-token": removing them document-wide would need a forbidden production Method Flow change.
        // Scoped (like bcl-token) to never become the outbound-HTTP BOUNDARY vocabulary.
        foreach (string token in new[] { "message", "code", "Value" })
        {
            Add($"payload-id:{token}", "payload-identifier-boundary", token);
        }

        foreach (var value in configValues)
        {
            // Only distinctive (>= 8 char) config values enter the generic scan; the existing exact
            // sensitive-config scan covers the rest. Field LABEL only ever reaches a failure message.
            if (value.Value.Length >= 8)
            {
                Add($"config-value:{value.Label}", "config-value", value.Value);
            }

            if (value.Value.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                string payload = value.Value["Basic ".Length..].Trim();
                if (payload.Length >= 8)
                {
                    Add($"config-value:{value.Label} (payload)", "config-value", payload);
                }
            }

            if (Uri.TryCreate(value.Value, UriKind.Absolute, out var uri)
                && !string.IsNullOrEmpty(uri.Host)
                && uri.Host.Length >= 8)
            {
                Add($"config-value:{value.Label} (host)", "config-value", uri.Host);
            }
        }

        return entries;
    }

    internal static IReadOnlyList<(string Label, string Kind)> FindLeaks(
        string text, IEnumerable<OutboundHttpCorpusEntry> corpus)
    {
        var hits = new List<(string, string)>();
        foreach (var entry in corpus)
        {
            if (text.Contains(entry.Token, StringComparison.Ordinal))
            {
                hits.Add((entry.Label, entry.Kind));
            }
        }

        return hits;
    }
}
