using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using SeqDoc.Cli;
using SeqDoc.Rendering.Markdown;
using SeqDoc.Testing;
using Xunit;
using Xunit.Sdk;

namespace SeqDoc.AcceptanceTests;

/// <summary>
/// Issue #18 acceptance: the merged #16/#17 hosted-worker and scheduler/callback contracts must reach
/// visible Markdown and Mermaid through the real, config-driven (or, for CreditTransfer, automatic
/// no-config) production analysis pipeline for the three frozen external lanes - FraudManagement
/// (revision <c>7aabfef9…</c>), SMSGateway (revision <c>7ca79735…</c>) and CreditTransfer (revision
/// <c>e65a94b8…</c>, an isolated detached worktree since that historical commit predates the current
/// checkout HEAD).
///
/// Harness path: <b>in-process production CLI</b> via <see cref="CliHost.RunAsync"/>, the same
/// <c>analyze --project ... [--config &lt;yaml&gt;] --output ... --json</c> contract the shipped
/// <c>seqdoc</c> binary uses, established by <c>ServiceClientExternalCorpusTests</c> and
/// <c>OutboundHttpExternalCorpusTests</c>. If the Provided corpus is absent, the whole suite skips;
/// once it exists, a missing lane or infrastructure/build failure is a loud failure, never a silent
/// skip.
///
/// These claims assert structural/wording/determinism properties on the real produced artifacts, not
/// frozen byte-for-byte snapshots (the external corpus packages float; FraudManagement and SMSGateway
/// are pinned by exact git revision, and CreditTransfer additionally by its exact admitted root hash).
/// Every claim stays inside the I18 non-goals: no runtime/delivery/durability/timing claim is asserted
/// as present, and several claims assert such overclaiming vocabulary is exactly ABSENT.
/// </summary>
[CollectionDefinition(WorkerExternalCorpusSuite.Name, DisableParallelization = true)]
public sealed class WorkerExternalCorpusSuite : ICollectionFixture<WorkerExternalCorpusFixture>
{
    public const string Name = "WorkerExternalCorpus";
}

[Collection(WorkerExternalCorpusSuite.Name)]
public sealed class WorkerExternalCorpusTests
{
    private readonly WorkerExternalCorpusFixture _corpus;

    public WorkerExternalCorpusTests(WorkerExternalCorpusFixture corpus) => _corpus = corpus;

    // Frozen lane revisions (checkpoint.md "Frozen external lanes" table). Any drift is a loud failure,
    // never silently substituted.
    private const string FraudManagementRevision = "7aabfef98fa4d47781bd8a98b9061ddcafb88836";
    private const string SmsGatewayRevision = "7ca797356b1856eb815922ca977e9d85a569cb84";
    private const string CreditTransferRootHash =
        "method:v1:8a78a24d943ce76ce80d2b6108cadb8cadce7382c67de290a62baf3433a09970";

    // --- Claim 1: FraudManagement's Worker.ExecuteAsync root is admitted as the hosted-worker lifecycle
    // entry point with cancellation-parameter evidence (accepted #16 fact set for this real project), and
    // the timer/scheduler-registration boundary stays conservative: the real Quartz-based
    // BaseCronJob/IJob dispatch in this project (a runtime-string-switched JobBuilder.Create<T>() plus a
    // CronTrigger, not the modeled IHostedService+System.Threading.Timer shape) is never mis-admitted as
    // a registered scheduler callback anywhere in the generated output (risk #3).
    [Fact]
    public void FraudManagementWorkerRootIsAdmittedWithBoundedSchedulerRegistrationBoundary()
    {
        var lane = _corpus.RequireLane(WorkerLane.FraudManagement);
        Assert.Equal(FraudManagementRevision, lane.CheckoutHead);
        var run = lane.Run1;
        Assert.True(
            string.Equals(run.Outcome, "succeeded", StringComparison.OrdinalIgnoreCase),
            $"FraudManagement analysis outcome was '{run.Outcome}'; diagnostics: {run.DiagnosticSummary}");
        Assert.DoesNotContain("SD4011", run.DiagnosticCodes);

        var workerDocs = run.Files.Where(f => f.RelativePath.EndsWith(".md", StringComparison.Ordinal)
            && Encoding.UTF8.GetString(f.Content).StartsWith("# Hosted worker", StringComparison.Ordinal))
            .ToArray();
        var worker = Assert.Single(workerDocs);
        string markdown = Encoding.UTF8.GetString(worker.Content);
        Assert.Contains("# Hosted worker FraudManagementWindowsService.Worker", markdown, StringComparison.Ordinal);
        Assert.Contains("Hosted worker lifecycle entry point.", markdown, StringComparison.Ordinal);
        Assert.Contains(
            "The registered hosted-worker lifecycle includes ExecuteAsync with cancellation parameter evidence: stoppingToken.",
            markdown,
            StringComparison.Ordinal);

        // Risk #3: the real, unmodeled Quartz job-registration shape must not leak a false scheduler-
        // registration claim into ANY generated document, and no scheduler-callback wording is invented.
        foreach (var file in run.Files.Where(f => f.RelativePath.EndsWith(".md", StringComparison.Ordinal)))
        {
            string text = Encoding.UTF8.GetString(file.Content);
            Assert.DoesNotContain("registers a timer callback", text, StringComparison.Ordinal);
            foreach (string token in new[] { "Quartz", "IJob", "JobBuilder", "CronTrigger", "TimerSetup" })
            {
                Assert.DoesNotContain(token, text, StringComparison.Ordinal);
            }
        }
    }

    // --- Claim 2: SMSGateway's SMSGatewayWorker root is admitted with the same accepted lifecycle-entry
    // and cancellation-parameter facts, and the #17 callback/non-durable-recovery boundary holds across
    // the WHOLE lane's generated output: no ACK/NACK, acknowledgment, delivery, durability, or
    // exactly-once vocabulary is invented anywhere (subscription is not execution proof and ACK/NACK is
    // not delivery proof).
    [Fact]
    public void SmsGatewayWorkerLifecycleAndCallbackRecoveryBoundaryHolds()
    {
        var lane = _corpus.RequireLane(WorkerLane.SmsGateway);
        Assert.Equal(SmsGatewayRevision, lane.CheckoutHead);
        var run = lane.Run1;
        Assert.True(
            string.Equals(run.Outcome, "succeeded", StringComparison.OrdinalIgnoreCase),
            $"SMSGateway analysis outcome was '{run.Outcome}'; diagnostics: {run.DiagnosticSummary}");

        var workerDocs = run.Files.Where(f => f.RelativePath.EndsWith(".md", StringComparison.Ordinal)
            && Encoding.UTF8.GetString(f.Content).StartsWith("# Hosted worker", StringComparison.Ordinal))
            .ToArray();
        var worker = Assert.Single(workerDocs);
        string workerMarkdown = Encoding.UTF8.GetString(worker.Content);
        Assert.Contains(
            "# Hosted worker LP.SMSGateway.WindowsHost.SMSGatewayWorker",
            workerMarkdown,
            StringComparison.Ordinal);
        Assert.Contains(
            "The registered hosted-worker lifecycle includes ExecuteAsync with cancellation parameter evidence: stoppingToken.",
            workerMarkdown,
            StringComparison.Ordinal);

        // #17 boundary: across every generated document in this lane, no delivery/durability/exactly-
        // once overclaim appears. Short tokens (ACK/NACK) are matched as whole words so the ubiquitous
        // "Technical fallback" section text (which legitimately contains "...allb-ACK-hyphen"-shaped
        // substrings like "fallback"/"rollback") is never a false positive.
        string[] wholeWordTokens = ["ACK", "NACK"];
        string[] substringTokens =
        [
            "acknowledg", "delivered", "delivery guarantee", "delivery success", "durable", "durability",
            "exactly-once", "exactly once", "guaranteed delivery", "message delivered", "was received",
        ];
        foreach (var file in run.Files.Where(f => f.RelativePath.EndsWith(".md", StringComparison.Ordinal)))
        {
            string text = Encoding.UTF8.GetString(file.Content);
            foreach (string token in wholeWordTokens)
            {
                Assert.False(
                    Regex.IsMatch(text, $@"\b{token}\b"),
                    $"'{file.RelativePath}' carries forbidden delivery-proof token '{token}'.");
            }

            foreach (string token in substringTokens)
            {
                Assert.DoesNotContain(token, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // --- Claim 3: CreditTransfer's no-config automatic hosted-worker admission reproduces the exact
    // frozen root hash and stays bounded to registration/lifecycle-entry/cancellation-parameter evidence
    // only - even though the real Worker.ExecuteAsync body contains a while-loop, try/catch and
    // Task.Delay retry-like shape, the pipeline does not (and this claim asserts it does not) extend the
    // hosted-worker lifecycle claim into a polling/retry/timing/durable-recovery claim for this lane.
    [Fact]
    public void CreditTransferNoConfigAutomaticHostedWorkerAdmissionIsBounded()
    {
        var lane = _corpus.RequireLane(WorkerLane.CreditTransfer);
        Assert.Equal(WorkerExternalCorpusFixture.CreditTransferRevision, lane.CheckoutHead);
        var run = lane.Run1;
        Assert.True(
            string.Equals(run.Outcome, "succeeded", StringComparison.OrdinalIgnoreCase),
            $"CreditTransfer analysis outcome was '{run.Outcome}'; diagnostics: {run.DiagnosticSummary}");

        Assert.Contains(CreditTransferRootHash, run.RawJsonFragments.Single());

        var workerDocs = run.Files.Where(f => f.RelativePath.EndsWith(".md", StringComparison.Ordinal)
            && Encoding.UTF8.GetString(f.Content).StartsWith("# Hosted worker", StringComparison.Ordinal))
            .ToArray();
        var worker = Assert.Single(workerDocs);
        string markdown = Encoding.UTF8.GetString(worker.Content);
        Assert.Contains("# Hosted worker CreditTransferWorker.Worker", markdown, StringComparison.Ordinal);
        Assert.Contains("Hosted worker lifecycle entry point.", markdown, StringComparison.Ordinal);
        Assert.Contains(
            "The registered hosted-worker lifecycle includes ExecuteAsync with cancellation parameter evidence: stoppingToken.",
            markdown,
            StringComparison.Ordinal);

        // Bounded non-goal: no polling/retry/timing/durable-recovery wording anywhere in the lane.
        string[] forbiddenTokens =
        [
            "awaited repeating loop", "catch-to-loop", "cancellation check", "retry", "retries",
            "polling", "poll ", "every hour", "WaitTimeInHours", "eventually succeeds",
            "completed successfully", "durable", "delivery", "exactly-once", "throw boundary",
            "return boundary",
        ];
        foreach (var file in run.Files.Where(f => f.RelativePath.EndsWith(".md", StringComparison.Ordinal)))
        {
            string text = Encoding.UTF8.GetString(file.Content);
            foreach (string token in forbiddenTokens)
            {
                Assert.DoesNotContain(token, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // --- Claim 4: every analyzed lane is byte-deterministic across two fully independent clean runs
    // (independent SQLite cache + output directory each time).
    [Fact]
    public void EveryLaneProducesByteIdenticalOutputAcrossTwoIndependentRuns()
    {
        foreach (var laneKind in Enum.GetValues<WorkerLane>())
        {
            var lane = _corpus.RequireLane(laneKind);
            var first = lane.Run1.Files;
            var second = lane.Run2.Files;

            Assert.Equal(
                first.Select(f => f.RelativePath).OrderBy(p => p, StringComparer.Ordinal),
                second.Select(f => f.RelativePath).OrderBy(p => p, StringComparer.Ordinal));
            Assert.True(
                lane.Run1.DiagnosticRecords.SequenceEqual(lane.Run2.DiagnosticRecords),
                $"{laneKind}: diagnostic records differ between two clean runs.");

            foreach (var file in first)
            {
                var other = second.Single(candidate => candidate.RelativePath == file.RelativePath);
                Assert.True(
                    file.Content.AsSpan().SequenceEqual(other.Content),
                    $"{laneKind}: '{file.RelativePath}' differs between two independent runs.");
            }
        }
    }

    // --- Claim 5 (required negative): a real construct that looks like a worker but is not a
    // BackgroundService/hosted-worker root must not be admitted as one. FraudManagement's Quartz IJob
    // classes (dynamically dispatched by BaseCronJob, never a modeled hosted-worker/timer shape) and
    // SMSGateway's manual `new Thread(...)` dispatch-loop constructs (SmscCluster.NodeDispatchLoop,
    // JobScheduler.SchedulingThreadMethod - legacy manual-thread workers, not BackgroundService) are
    // real, currently-present code in the analyzed lanes; this asserts exactly one "Hosted worker"
    // document exists per lane (the true BackgroundService root only) and that these legacy/foreign
    // method names never appear as a hosted-worker document title.
    [Fact]
    public void LegacyAndForeignWorkerConstructsAreNotAdmittedAsHostedWorkerRoots()
    {
        // The negative candidates are confirmed present as real source in the analyzed lanes (not an
        // invented fixture) so the boundary this claim proves is meaningful.
        Assert.Contains("new Thread(", _corpus.SmscClusterSource);
        Assert.Contains("NodeDispatchLoop", _corpus.SmscClusterSource);
        Assert.Contains("new Thread(", _corpus.JobSchedulerSource);
        Assert.Contains("SchedulingThreadMethod", _corpus.JobSchedulerSource);

        var fraudManagement = _corpus.RequireLane(WorkerLane.FraudManagement).Run1;
        var smsGateway = _corpus.RequireLane(WorkerLane.SmsGateway).Run1;

        foreach (var run in new[] { fraudManagement, smsGateway })
        {
            var hostedWorkerDocs = run.Files
                .Where(f => f.RelativePath.EndsWith(".md", StringComparison.Ordinal)
                    && Encoding.UTF8.GetString(f.Content).StartsWith("# Hosted worker", StringComparison.Ordinal))
                .ToArray();
            Assert.Single(hostedWorkerDocs);

            // No document's title line (the first "# ..." heading) names a legacy/foreign worker
            // construct as a hosted worker.
            foreach (var file in run.Files.Where(f => f.RelativePath.EndsWith(".md", StringComparison.Ordinal)))
            {
                string text = Encoding.UTF8.GetString(file.Content);
                string titleLine = text.Split('\n').FirstOrDefault(line => line.StartsWith("# ", StringComparison.Ordinal)) ?? string.Empty;
                if (!titleLine.StartsWith("# Hosted worker", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (string legacyToken in new[]
                         {
                             "NodeDispatchLoop", "SchedulingThreadMethod", "AutoMarkRequestsCompleted",
                             "ProcessReportedRequest", "PrepareReportingRequest",
                         })
                {
                    Assert.DoesNotContain(legacyToken, titleLine, StringComparison.Ordinal);
                }
            }
        }
    }

    // --- Claim 6: every generated artifact across all three lanes is within the configured Mermaid
    // budget, every Mermaid diagram validates structurally, every relative Markdown link resolves, and
    // no credential-shaped value leaks into any generated document.
    [Fact]
    public void EveryLaneStaysWithinBudgetHasNoDanglingLinksAndIsCredentialSafe()
    {
        string[] credentialTokens =
        [
            "password", "connectionstring", "connection string", "secret", "apikey", "api key", "pwd=",
        ];

        foreach (var laneKind in Enum.GetValues<WorkerLane>())
        {
            var run = _corpus.RequireLane(laneKind).Run1;
            var names = run.Files
                .SelectMany(f => new[] { f.RelativePath, Path.GetFileName(f.RelativePath) })
                .ToHashSet(StringComparer.Ordinal);

            foreach (var file in run.Files.Where(f => f.RelativePath.EndsWith(".mmd", StringComparison.Ordinal)))
            {
                string mermaid = Encoding.UTF8.GetString(file.Content);
                Assert.True(
                    mermaid.Length <= run.MaxMermaidCharacters,
                    $"{laneKind}: '{file.RelativePath}' has {mermaid.Length} characters, over the configured budget {run.MaxMermaidCharacters}.");
                Assert.Empty(MermaidValidator.Validate(mermaid));
            }

            foreach (var file in run.Files.Where(f => f.RelativePath.EndsWith(".md", StringComparison.Ordinal)))
            {
                string markdown = Encoding.UTF8.GetString(file.Content);
                foreach (string token in credentialTokens)
                {
                    Assert.DoesNotContain(token, markdown, StringComparison.OrdinalIgnoreCase);
                }

                foreach (Match match in Regex.Matches(markdown, @"\]\(([^)]+)\)"))
                {
                    string target = match.Groups[1].Value.Trim();
                    if (target.StartsWith("http://", StringComparison.Ordinal)
                        || target.StartsWith("https://", StringComparison.Ordinal)
                        || target.StartsWith("mailto:", StringComparison.Ordinal)
                        || target.StartsWith('#'))
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
                        $"{laneKind}: '{file.RelativePath}' links to '{target}', which is not a generated file.");
                }
            }
        }
    }
}

public enum WorkerLane
{
    FraudManagement,
    SmsGateway,
    CreditTransfer,
}

/// <summary>One completed production-CLI analysis run for one external lane.</summary>
public sealed record WorkerLaneRun(
    string Outcome,
    ImmutableArray<string> DiagnosticCodes,
    ImmutableArray<string> DiagnosticRecords,
    ImmutableArray<string> RawJsonFragments,
    string DiagnosticSummary,
    int MaxMermaidCharacters,
    ImmutableArray<WorkerRenderedFile> Files);

public sealed record WorkerRenderedFile(string RelativePath, byte[] Content);

public sealed record WorkerLaneResult(WorkerLaneRun Run1, WorkerLaneRun Run2, string CheckoutHead);

/// <summary>
/// Runs each Issue #18 external lane twice through the production config-driven (or, for CreditTransfer,
/// automatic no-config) CLI pipeline, so the expensive MSBuild workspace load happens at most twice per
/// lane. CreditTransfer is materialised in an isolated detached <c>git worktree</c> pinned to the frozen
/// historical revision (which predates the shared checkout's current HEAD), mirroring the isolation
/// approach <c>ServiceClientExternalCorpusFixture</c> uses for its SMS lane. FraudManagement and
/// SMSGateway are already checked out at their frozen revisions in the shared Provided corpus, so no
/// worktree is required for them (verified by an exact <c>git rev-parse HEAD</c> equality assertion in
/// each claim, rather than silently trusting the checkout).
/// </summary>
public sealed class WorkerExternalCorpusFixture : IAsyncLifetime
{
    private readonly Dictionary<WorkerLane, WorkerLaneResult> _results = [];
    private readonly Dictionary<WorkerLane, string> _skips = [];
    private readonly List<string> _tempDirectories = [];
    private string? _creditTransferWorktree;
    private string? _creditTransferSourceRoot;

    public bool CorpusAbsent { get; private set; }

    public string SmscClusterSource { get; private set; } = string.Empty;
    public string JobSchedulerSource { get; private set; } = string.Empty;

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
            foreach (WorkerLane lane in Enum.GetValues<WorkerLane>())
            {
                _skips[lane] = "the Provided external test-project corpus is not installed.";
            }

            return;
        }

        string repositoryRoot = ExternalCorpusResolver.DiscoverRepositoryRoot(AppContext.BaseDirectory);
        string examples = Path.Combine(repositoryRoot, "docs", "examples");

        string fraudManagementRoot = Path.Combine(providedRoot, "FraudManagement");
        string smsGatewayRoot = Path.Combine(providedRoot, "SMSGateway-om");
        _creditTransferSourceRoot = Path.Combine(providedRoot, "CreditTransfer-om");

        TryReadNegativeSources(smsGatewayRoot);

        await LoadAsync(
            WorkerLane.FraudManagement,
            fraudManagementRoot,
            "FraudManagement.sln",
            Path.Combine(examples, "fraud-management.yaml"));
        await LoadAsync(
            WorkerLane.SmsGateway,
            smsGatewayRoot,
            Path.Combine("Source", "LP.SMSGateway.WindowsHost", "LP.SMSGateway.WindowsHost.csproj"),
            Path.Combine(examples, "sms-gateway.yaml"));

        _creditTransferWorktree = CreateCreditTransferWorktree(_creditTransferSourceRoot);
        if (_creditTransferWorktree is not null)
        {
            await LoadAsync(
                WorkerLane.CreditTransfer,
                _creditTransferWorktree,
                Path.Combine("CreditTransferWorker", "CreditTransferWorker.csproj"),
                configPath: null);
        }
    }

    public Task DisposeAsync()
    {
        Exception? cleanupFailure = null;
        if (_creditTransferWorktree is not null && _creditTransferSourceRoot is not null)
        {
            try
            {
                RemoveIsolatedWorktree(_creditTransferSourceRoot, _creditTransferWorktree);
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
            }
        }

        // The production CLI opens the SQLite cache with connection pooling on, so the pool keeps
        // cache-v1.db file handles open past the run. Release every pooled handle (and run finalizers)
        // before deleting the owned temp directories - otherwise deletion loses a genuine race.
        SqliteConnection.ClearAllPools();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        var tempDirectoryFailures = new List<string>();
        foreach (string directory in _tempDirectories)
        {
            bool gone = false;
            const int attempts = 4;
            const int retryDelayMilliseconds = 500;

            for (int attempt = 1; attempt <= attempts && !gone; attempt++)
            {
                try
                {
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(directory, recursive: true);
                    }

                    gone = !Directory.Exists(directory);
                }
                catch (IOException)
                {
                    if (attempt < attempts)
                    {
                        Thread.Sleep(retryDelayMilliseconds * attempt);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    if (attempt < attempts)
                    {
                        Thread.Sleep(retryDelayMilliseconds * attempt);
                    }
                }
            }

            if (!gone)
            {
                tempDirectoryFailures.Add(directory);
            }
        }

        if (cleanupFailure is not null || tempDirectoryFailures.Count > 0)
        {
            var message = new StringBuilder("CreditTransfer cleanup did not complete safely.");
            if (cleanupFailure is not null)
            {
                message.Append(" Worktree cleanup failed.");
            }

            if (tempDirectoryFailures.Count > 0)
            {
                message.Append(" Failed to delete temp directories: ");
                message.Append(string.Join(", ", tempDirectoryFailures));
            }

            if (cleanupFailure is not null)
            {
                throw new InvalidOperationException(message.ToString(), cleanupFailure);
            }

            throw new InvalidOperationException(message.ToString());
        }

        return Task.CompletedTask;
    }

    public WorkerLaneResult RequireLane(WorkerLane lane)
    {
        if (CorpusAbsent)
        {
            throw SkipException.ForSkip("the Provided external test-project corpus is not installed.");
        }

        if (_results.TryGetValue(lane, out var result))
        {
            return result;
        }

        Assert.Fail(_skips.TryGetValue(lane, out var reason)
            ? $"{lane}: required lane was not analyzed. Exact reason: {reason}"
            : $"{lane}: required lane was not analyzed.");
        throw new InvalidOperationException("unreachable");
    }

    private void TryReadNegativeSources(string smsGatewayRoot)
    {
        string clusterPath = Path.Combine(smsGatewayRoot, "Source", "LP.Messaging.SMS", "SmscCluster.cs");
        string schedulerPath = Path.Combine(smsGatewayRoot, "Source", "LP.SMSGateway.Common", "JobScheduler.cs");
        if (File.Exists(clusterPath))
        {
            SmscClusterSource = File.ReadAllText(clusterPath);
        }

        if (File.Exists(schedulerPath))
        {
            JobSchedulerSource = File.ReadAllText(schedulerPath);
        }
    }

    private async Task LoadAsync(
        WorkerLane lane,
        string laneRoot,
        string relativeTarget,
        string? configPath)
    {
        string target = Path.Combine(laneRoot, relativeTarget);
        if (!File.Exists(target))
        {
            _skips[lane] = $"lane target '{relativeTarget}' is not present under '{laneRoot}'.";
            return;
        }

        if (configPath is not null && !File.Exists(configPath))
        {
            _skips[lane] = $"lane config '{Path.GetFileName(configPath)}' is not present.";
            return;
        }

        try
        {
            var first = await RunAsync(lane, laneRoot, target, configPath);
            if (IsInfrastructureFailure(first))
            {
                _skips[lane] = $"analysis could not run in this environment (outcome '{first.Outcome}'): {first.DiagnosticSummary}";
                return;
            }

            var second = await RunAsync(lane, laneRoot, target, configPath);
            if (IsInfrastructureFailure(second))
            {
                _skips[lane] = $"second determinism run could not complete (outcome '{second.Outcome}').";
                return;
            }

            string checkoutHead = RunGit(laneRoot, "rev-parse", "HEAD").Output.Trim();
            _results[lane] = new WorkerLaneResult(first, second, checkoutHead);
        }
        catch (Exception exception) when (exception is not SkipException)
        {
            string detail = exception.ToString();
            _skips[lane] = "lane analysis threw before producing a result: "
                + detail[..Math.Min(detail.Length, 1500)];
        }
    }

    private static bool IsInfrastructureFailure(WorkerLaneRun run)
    {
        if (string.Equals(run.Outcome, "succeeded", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (string infrastructureOutcome in new[] { "BuildFailure", "PersistenceFailure", "Cancelled" })
        {
            if (string.Equals(run.Outcome, infrastructureOutcome, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return run.DiagnosticCodes.Any(code => code is "SD4000" or "SD4004" or "SD4006");
    }

    private async Task<WorkerLaneRun> RunAsync(
        WorkerLane lane,
        string laneRoot,
        string target,
        string? configPath)
    {
        string outputDirectory = NewTempDirectory($"{lane}-out");
        string cacheDirectory = NewTempDirectory($"{lane}-cache");

        var args = new List<string>
        {
            "analyze", target,
            "--repository-root", laneRoot,
            "--configuration", "Release",
            "--framework", "net9.0",
            "--cache", Path.Combine(cacheDirectory, "cache-v1.db"),
            "--output", outputDirectory,
            "--json",
        };
        if (configPath is not null)
        {
            args.Add("--config");
            args.Add(configPath);
        }

        var standardOutput = new StringWriter();
        var standardError = new StringWriter();
        await CliHost.RunAsync(args.ToArray(), standardOutput, standardError, CancellationToken.None);

        string json = standardOutput.ToString();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        string outcome = root.GetProperty("outcome").GetString() ?? "unknown";

        var codes = ImmutableArray<string>.Empty;
        var records = ImmutableArray<string>.Empty;
        var summary = new StringBuilder();
        if (root.TryGetProperty("diagnostics", out var diagnostics) && diagnostics.ValueKind == JsonValueKind.Array)
        {
            var builder = ImmutableArray.CreateBuilder<string>();
            var recordBuilder = ImmutableArray.CreateBuilder<string>();
            foreach (var diagnostic in diagnostics.EnumerateArray())
            {
                recordBuilder.Add(diagnostic.GetRawText());
                string code = diagnostic.GetProperty("code").GetString() ?? string.Empty;
                builder.Add(code);
                summary.Append(code)
                    .Append(": ")
                    .Append(diagnostic.TryGetProperty("technicalCause", out var cause) ? cause.GetString() : null)
                    .Append(' ');
            }

            codes = builder.ToImmutable();
            records = recordBuilder.ToImmutable();
        }

        int budget = ReadMermaidBudget(root);
        var files = ReadGeneratedFiles(outputDirectory);
        var rawFragments = ImmutableArray.Create(json);

        return new WorkerLaneRun(outcome, codes, records, rawFragments, summary.ToString().Trim(), budget, files);
    }

    private static int ReadMermaidBudget(JsonElement root)
    {
        if (root.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("configuration", out var configuration)
            && configuration.TryGetProperty("diagramBudget", out var diagramBudget)
            && diagramBudget.TryGetProperty("maxMermaidCharacters", out var maxMermaid)
            && maxMermaid.TryGetProperty("value", out var value)
            && value.TryGetInt32(out int budget))
        {
            return budget;
        }

        return 45000;
    }

    private static ImmutableArray<WorkerRenderedFile> ReadGeneratedFiles(string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory))
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<WorkerRenderedFile>();
        foreach (string path in Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(outputDirectory, path).Replace(Path.DirectorySeparatorChar, '/');
            builder.Add(new WorkerRenderedFile(relative, File.ReadAllBytes(path)));
        }

        builder.Sort((left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));
        return builder.ToImmutable();
    }

    private string NewTempDirectory(string label)
    {
        string path = Path.Combine(Path.GetTempPath(), $"seqdoc-i18-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }

    /// <summary>
    /// Materialises the frozen historical CreditTransfer revision in an isolated detached worktree (that
    /// revision predates the shared checkout's current HEAD) and restores it so MSBuild design-time
    /// evaluation has the NuGet assets it needs. Returns null (recorded as a skip on both lanes that need
    /// it) rather than throwing, if git/dotnet infrastructure itself is unavailable.
    /// </summary>
    private string? CreateCreditTransferWorktree(string sourceRoot)
    {
        if (!Directory.Exists(sourceRoot))
        {
            _skips[WorkerLane.CreditTransfer] = $"'{sourceRoot}' is not present.";
            return null;
        }

        var cat = RunGit(sourceRoot, "cat-file", "-e", CreditTransferRevision);
        if (cat.ExitCode != 0)
        {
            _skips[WorkerLane.CreditTransfer] =
                $"the exact frozen CreditTransfer revision '{CreditTransferRevision}' is absent from the supplied sibling checkout's local history.";
            return null;
        }

        string path = Path.Combine(Path.GetTempPath(), $"seqdoc-i18-ct-worktree-{Guid.NewGuid():N}");
        var add = RunGit(sourceRoot, "worktree", "add", "--detach", path, CreditTransferRevision);
        if (add.ExitCode != 0)
        {
            _skips[WorkerLane.CreditTransfer] =
                $"could not create the isolated CreditTransfer worktree. git error: {add.Output}";
            return null;
        }

        _tempDirectories.Add(path);

        string workerCsproj = Path.Combine(path, "CreditTransferWorker", "CreditTransferWorker.csproj");
        if (!File.Exists(workerCsproj))
        {
            _skips[WorkerLane.CreditTransfer] =
                $"'CreditTransferWorker/CreditTransferWorker.csproj' is not present at the frozen revision.";
            return path;
        }

        var restore = RunDotnet(Path.Combine(path, "CreditTransferWorker"), "restore", "CreditTransferWorker.csproj");
        if (restore.ExitCode != 0)
        {
            _skips[WorkerLane.CreditTransfer] = $"'dotnet restore' failed for the isolated worktree: {restore.Output}";
            return path;
        }

        return path;
    }

    internal const string CreditTransferRevision = "e65a94b873e712a17bc337a2b687ab4bd3dacece";

    private static void RemoveIsolatedWorktree(string sourceRoot, string worktreePath)
    {
        const int attempts = 8;
        const int retryDelayMilliseconds = 500;
        string expectedPath = Path.GetFullPath(worktreePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var observations = new List<string>();

        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            var remove = RunGit(sourceRoot, "worktree", "remove", "--force", worktreePath);
            var prune = RunGit(sourceRoot, "worktree", "prune", "--expire", "now");
            bool registered = IsRegisteredWorktree(sourceRoot, expectedPath);
            bool directoryExists = Directory.Exists(worktreePath);
            observations.Add($"attempt {attempt}: remove={remove.ExitCode} ({remove.Output}); prune={prune.ExitCode} ({prune.Output}); registered={registered}; directory={directoryExists}");

            if (remove.ExitCode == 0 && prune.ExitCode == 0 && !registered && !directoryExists)
            {
                return;
            }

            if (directoryExists && !registered)
            {
                try
                {
                    Directory.Delete(worktreePath, recursive: true);
                }
                catch (IOException exception)
                {
                    observations.Add($"directory delete: {exception.Message}");
                }
                catch (UnauthorizedAccessException exception)
                {
                    observations.Add($"directory delete: {exception.Message}");
                }
            }

            if (attempt < attempts)
            {
                Thread.Sleep(retryDelayMilliseconds);
            }
        }

        bool remainsRegistered = IsRegisteredWorktree(sourceRoot, expectedPath);
        bool remainsOnDisk = Directory.Exists(worktreePath);
        throw new InvalidOperationException(
            $"CreditTransfer isolated worktree cleanup failed after {attempts} bounded attempts. "
            + $"registered={remainsRegistered}; directory={remainsOnDisk}; "
            + string.Join(" | ", observations));
    }

    private static bool IsRegisteredWorktree(string sourceRoot, string expectedPath)
    {
        var result = RunGit(sourceRoot, "worktree", "list", "--porcelain");
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Could not verify CreditTransfer isolated worktree registration (git exit {result.ExitCode}): {result.Output}");
        }

        foreach (string line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith("worktree ", StringComparison.Ordinal))
            {
                continue;
            }

            string actualPath = Path.GetFullPath(line["worktree ".Length..].Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(actualPath, expectedPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static (int ExitCode, string Output) RunGit(string workingDirectory, params string[] arguments) =>
        RunProcess("git", workingDirectory, arguments);

    private static (int ExitCode, string Output) RunDotnet(string workingDirectory, params string[] arguments) =>
        RunProcess("dotnet", workingDirectory, arguments);

    private static (int ExitCode, string Output) RunProcess(string fileName, string workingDirectory, string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output.Trim());
    }
}
