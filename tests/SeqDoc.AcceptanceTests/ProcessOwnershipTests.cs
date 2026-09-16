using System.Globalization;
using RuntimeArchitecture = System.Runtime.InteropServices.Architecture;
using Xunit;

namespace SeqDoc.AcceptanceTests;

/// <summary>
/// GH-106 / I100-A acceptance for <c>ProcessOwnership.cs</c>. Every claim here targets the real Win32
/// producer (<see cref="ContainedProcess"/>/<see cref="NativeMethods"/>) driving the deterministic
/// <c>SeqDoc.AcceptanceTests.ProcessOwnershipStub</c> child process — the first observable consumer for
/// this primitive, per the checkpoint's evidence-chain requirement. Grouped one-per-risk per the
/// checkpoint's "Soft test budget" (~10-12 grouped claims); see the numbered group comment above each
/// test for the exact budget item it proves.
/// </summary>
public sealed class ProcessOwnershipTests
{
    private static readonly string StubExecutablePath = ResolveStubExecutablePath();

    // ---- Group 1: platform admission ----------------------------------------------------------------

    [Fact]
    public void PlatformAdmissionRealMachineAndSyntheticNegativesFailClosed()
    {
        // Real evaluation on the actual CI/dev machine — proves the positive admission path when this
        // machine genuinely is Windows x64 (the only supported lane); this repo has no Windows-x64
        // negative executable lane available, so a real negative machine is not exercised here.
        bool actual = ProcessOwnershipPlatform.IsSupported();
        Assert.Equal(
            OperatingSystem.IsWindows() && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == RuntimeArchitecture.X64,
            actual);

        // Synthetic negative claims via the injected-value seam (unit-level fake, never skipped):
        Assert.True(ProcessOwnershipPlatform.Evaluate(isWindows: true, RuntimeArchitecture.X64));
        Assert.False(ProcessOwnershipPlatform.Evaluate(isWindows: false, RuntimeArchitecture.X64));
        Assert.False(ProcessOwnershipPlatform.Evaluate(isWindows: true, RuntimeArchitecture.Arm64));
        Assert.False(ProcessOwnershipPlatform.Evaluate(isWindows: true, RuntimeArchitecture.X86));
        Assert.False(ProcessOwnershipPlatform.Evaluate(isWindows: false, RuntimeArchitecture.Arm64));
    }

    // ---- Group 2: executable/argument/environment vectors -------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("has \"embedded\" quotes")]
    [InlineData(@"trailing\backslash\")]
    [InlineData(@"c:\path\with\backslashes\")]
    [InlineData("plain")]
    public async Task ArgumentQuotingRoundTripsThroughRealChildArgv(string tricky)
    {
        var options = NewOptions(["echo-args", tricky, "sentinel-after"]);
        var result = ContainedProcess.Start(options);
        Assert.True(result.Succeeded, result.Detail);
        using var process = result.Process!;

        var wait = await process.WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
        Assert.Equal(ProcessOwnershipFailureClass.None, wait.FailureClass);
        Assert.Equal($"0:{tricky}\r\n1:sentinel-after\r\n", wait.StdOut.Text);
    }

    [Fact]
    public async Task EnvironmentIsDeterministicExplicitSetNotAmbientInheritance()
    {
        var env = DefaultEnvironment();
        env["SEQDOC_I100A_ONLY_IN_CHILD"] = "expected-value";
        var options = NewOptions(["print-env", "SEQDOC_I100A_ONLY_IN_CHILD"], env);

        var result = ContainedProcess.Start(options);
        Assert.True(result.Succeeded, result.Detail);
        using var process = result.Process!;
        var wait = await process.WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
        Assert.Equal("expected-value", wait.StdOut.Text);

        // An ambient variable set only in THIS test process (never added to the explicit child
        // environment) must not silently leak through to the child.
        System.Environment.SetEnvironmentVariable("SEQDOC_I100A_AMBIENT_ONLY", "must-not-leak");
        try
        {
            var options2 = NewOptions(["print-env", "SEQDOC_I100A_AMBIENT_ONLY"]);
            var result2 = ContainedProcess.Start(options2);
            Assert.True(result2.Succeeded, result2.Detail);
            using var process2 = result2.Process!;
            var wait2 = await process2.WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
            Assert.Equal("<unset>", wait2.StdOut.Text);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("SEQDOC_I100A_AMBIENT_ONLY", null);
        }
    }

    [Theory]
    [InlineData("relative\\path\\stub.exe")]
    [InlineData(@"C:\this\path\does\not\exist\stub.exe")]
    public void NonRootedOrMissingExecutablePathFailsClosedWithoutPathSearch(string badPath)
    {
        var options = new ProcessOwnershipOptions { ExecutablePath = badPath, Environment = DefaultEnvironment() };
        var result = ContainedProcess.Start(options);
        Assert.False(result.Succeeded);
        Assert.Equal(ProcessOwnershipFailureClass.ProcessConstructionFailed, result.FailureClass);
        Assert.Null(result.Process);
    }

    // ---- Group 3: exact 3-handle inheritance and rejection of an unrelated handle -------------------

    [Fact]
    public async Task OnlyTheThreeStdHandlesAreInheritedAndAnUnrelatedHandleIsNot()
    {
        // Canary: an OS-inheritable handle that exists in THIS process but is never placed in the
        // primitive's PROC_THREAD_ATTRIBUTE_HANDLE_LIST.
        string canaryPath = Path.Combine(Path.GetTempPath(), $"seqdoc-i100a-canary-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(canaryPath, "canary");
        using var canary = new FileStream(canaryPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        nint canaryHandle = canary.SafeFileHandle.DangerousGetHandle();
        Assert.True(NativeMethods.SetHandleInformation(canaryHandle, NativeMethods.HANDLE_FLAG_INHERIT, NativeMethods.HANDLE_FLAG_INHERIT));

        var options = NewOptions(["sleep", "2000"]);
        var result = ContainedProcess.Start(options);
        Assert.True(result.Succeeded, result.Detail);
        using var process = result.Process!;
        try
        {
            nint currentProcess = NativeMethods.GetCurrentProcess();

            // Positive control: each of the 3 std pipe child-side handles IS present in the child.
            AssertHandleValuePresentInChild(process.ProcessHandleForTests, currentProcess, process.StdOutChildHandleValueForTests, expectedPresent: true);
            AssertHandleValuePresentInChild(process.ProcessHandleForTests, currentProcess, process.StdErrChildHandleValueForTests, expectedPresent: true);
            AssertHandleValuePresentInChild(process.ProcessHandleForTests, currentProcess, process.StdInChildHandleValueForTests, expectedPresent: true);

            // Negative: the canary, despite being OS-inheritable, was excluded from the attribute list
            // and must NOT be present in the child under bInheritHandles = TRUE.
            AssertHandleValuePresentInChild(process.ProcessHandleForTests, currentProcess, canaryHandle, expectedPresent: false);
        }
        finally
        {
            process.Terminate();
            await process.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            File.Delete(canaryPath);
        }
    }

    private static void AssertHandleValuePresentInChild(nint childProcessHandle, nint currentProcess, nint handleValue, bool expectedPresent)
    {
        bool duplicated = NativeMethods.DuplicateHandle(
            childProcessHandle, handleValue, currentProcess, out nint dup, 0, false, 0x00000002 /* DUPLICATE_SAME_ACCESS */);
        if (duplicated)
        {
            NativeMethods.CloseHandle(dup);
        }

        Assert.Equal(expectedPresent, duplicated);
    }

    // ---- Group 4: assign-before-resume chronology ----------------------------------------------------

    [Fact]
    public async Task AssignPrecedesResumeSoJobMembershipIsEstablishedWhileStillSuspended()
    {
        bool? observedWhileSuspended = null;
        ContainedProcess.AssignBeforeResumeHookForTests = (job, proc) =>
        {
            NativeMethods.IsProcessInJob(proc, job, out bool result);
            observedWhileSuspended = result;
        };

        try
        {
            var options = NewOptions(["sleep", "50"]);
            var result = ContainedProcess.Start(options);
            Assert.True(result.Succeeded, result.Detail);
            using var process = result.Process!;

            Assert.True(
                observedWhileSuspended == true,
                "The job must already contain the process before ResumeThread is called.");

            var wait = await process.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            Assert.Equal(ProcessOwnershipFailureClass.None, wait.FailureClass);
        }
        finally
        {
            ContainedProcess.AssignBeforeResumeHookForTests = null;
        }
    }

    // ---- Group 5: parent-exit-survival with contained grandchild -------------------------------------

    [Fact]
    public async Task ParentExitsNormallyAndContainedGrandchildSurvivesThenIsExplicitlyTerminated()
    {
        // The grandchild inherits the still-open, still-OS-inheritable stdout/stderr pipe write handles
        // from the immediate parent when the parent spawns it (the parent is an ordinary, unrestricted
        // Process.Start call — this primitive's PROC_THREAD_ATTRIBUTE_HANDLE_LIST restriction applies
        // only to processes THIS primitive itself creates, not to further descendants). That means the
        // pipes will not reach EOF until the grandchild itself closes them, so this test deliberately
        // proves "still running" via the marker file and the query phase BEFORE calling WaitAsync (which
        // drains those pipes), then only drains/verifies output AFTER Terminate() has force-closed every
        // process (and therefore every inherited handle) in the job.
        string markerPath = Path.Combine(Path.GetTempPath(), $"seqdoc-i100a-grandchild-{Guid.NewGuid():N}.marker");
        var options = NewOptions(["spawn-grandchild", markerPath, "8000"]);
        var result = ContainedProcess.Start(options);
        Assert.True(result.Succeeded, result.Detail);
        using var process = result.Process!;
        try
        {
            await WaitForFileToExistAsync(markerPath, TimeSpan.FromSeconds(5));
            Assert.False(File.Exists(markerPath + ".completed"));
            Assert.False(process.HasObservedActiveProcessZero());

            // Explicitly terminate the whole family; the grandchild must never reach natural completion.
            Assert.True(process.Terminate());

            var wait = await process.WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
            Assert.Equal(0, wait.ExitCode); // the immediate parent's own, already-recorded natural exit code
            Assert.Contains("parent-exited", wait.StdOut.Text, StringComparison.Ordinal);
            Assert.True(wait.ActiveProcessZeroObserved);
            Assert.False(File.Exists(markerPath + ".completed"));
        }
        finally
        {
            File.Delete(markerPath);
            File.Delete(markerPath + ".completed");
        }
    }

    // ---- Group 6: complete concurrent stdout/stderr drain under load ---------------------------------

    [Fact]
    public async Task ConcurrentStdOutAndStdErrDrainToEofUnderLoadWithoutDeadlock()
    {
        const int Lines = 25_000;
        string lineCount = Lines.ToString(CultureInfo.InvariantCulture);
        var options = NewOptions(["bulk", lineCount, lineCount], drainTimeout: TimeSpan.FromSeconds(60));
        var result = ContainedProcess.Start(options);
        Assert.True(result.Succeeded, result.Detail);
        using var process = result.Process!;

        var wait = await process.WaitAsync(TimeSpan.FromSeconds(60), CancellationToken.None);

        Assert.Equal(ProcessOwnershipFailureClass.None, wait.FailureClass);
        Assert.False(wait.StdOut.Truncated);
        Assert.False(wait.StdErr.Truncated);
        Assert.Contains($"OUT{Lines - 1:D6}", wait.StdOut.Text, StringComparison.Ordinal);
        Assert.Contains($"ERR{Lines - 1:D6}", wait.StdErr.Text, StringComparison.Ordinal);
        Assert.Equal(Lines, CountOccurrences(wait.StdOut.Text, "OUT"));
        Assert.Equal(Lines, CountOccurrences(wait.StdErr.Text, "ERR"));
    }

    // ---- Group 7: bounded forced termination with explicit truncation marking ------------------------

    [Fact]
    public async Task DrainBoundExceededRecordsDrainIncompleteWithTruncatedPrefixNeverClaimedLossless()
    {
        var options = NewOptions(["slow-bulk", "50", "500"], drainTimeout: TimeSpan.FromMilliseconds(300));
        var result = ContainedProcess.Start(options);
        Assert.True(result.Succeeded, result.Detail);
        using var process = result.Process!;

        var wait = await process.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.Equal(ProcessOwnershipFailureClass.DrainIncomplete, wait.FailureClass);
        Assert.True(wait.StdOut.Truncated);
        Assert.DoesNotContain("SLOW-COMPLETE", wait.StdOut.Text, StringComparison.Ordinal);
    }

    // ---- Group 8: timeout vs. cancellation vs. body-failure precedence -------------------------------

    [Fact]
    public async Task WaitTimeoutTerminatesAndRecordsTimedOut()
    {
        var options = NewOptions(["sleep", "10000"]);
        var result = ContainedProcess.Start(options);
        Assert.True(result.Succeeded, result.Detail);
        using var process = result.Process!;

        var wait = await process.WaitAsync(TimeSpan.FromMilliseconds(300), CancellationToken.None);

        Assert.Equal(ProcessOwnershipFailureClass.TimedOut, wait.FailureClass);
        Assert.True(wait.TimedOut);
        Assert.False(wait.Cancelled);
        Assert.True(process.TerminateJobObjectWasCalled);
    }

    [Fact]
    public async Task CallerCancellationTerminatesAndRecordsTimedOutClassWithCancelledFlag()
    {
        var options = NewOptions(["sleep", "10000"]);
        var result = ContainedProcess.Start(options);
        Assert.True(result.Succeeded, result.Detail);
        using var process = result.Process!;

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var wait = await process.WaitAsync(TimeSpan.FromSeconds(30), cts.Token);

        Assert.Equal(ProcessOwnershipFailureClass.TimedOut, wait.FailureClass);
        Assert.False(wait.TimedOut);
        Assert.True(wait.Cancelled);
    }

    [Fact]
    public async Task NonZeroExitRecordsProcessFailed()
    {
        var options = NewOptions(["exitcode", "7"]);
        var result = ContainedProcess.Start(options);
        Assert.True(result.Succeeded, result.Detail);
        using var process = result.Process!;

        var wait = await process.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.Equal(ProcessOwnershipFailureClass.ProcessFailed, wait.FailureClass);
        Assert.Equal(7, wait.ExitCode);
    }

    [Theory]
    [InlineData(ProcessOwnershipFailureClass.ProcessConstructionFailed, ProcessOwnershipFailureClass.TimedOut)]
    [InlineData(ProcessOwnershipFailureClass.TimedOut, ProcessOwnershipFailureClass.ProcessFailed)]
    [InlineData(ProcessOwnershipFailureClass.ProcessFailed, ProcessOwnershipFailureClass.DrainIncomplete)]
    [InlineData(ProcessOwnershipFailureClass.DrainIncomplete, ProcessOwnershipFailureClass.TeardownDegraded)]
    public void FailureClassTrackerFirstRecordedFailureIsNeverOverwritten(
        ProcessOwnershipFailureClass first, ProcessOwnershipFailureClass second)
    {
        var tracker = new FailureClassTracker();
        tracker.Record(first, "first");
        tracker.Record(second, "second");

        Assert.Equal(first, tracker.Class);
        Assert.Equal("first", tracker.Detail);
    }

    // ---- Group 9: S0-S3 partial-construction unwind ---------------------------------------------------

    // xunit test methods must be public, but ConstructionFaultPoint is deliberately internal (a
    // test-only seam, never production surface) — InlineData carries the enum's underlying int instead
    // and the method casts it back, so no internal type ever appears in a public member signature.
    [Theory]
    [InlineData((int)ConstructionFaultPoint.AfterPipesCreated)]
    [InlineData((int)ConstructionFaultPoint.AfterAttributeListBuilt)]
    [InlineData((int)ConstructionFaultPoint.AfterJobCreated)]
    [InlineData((int)ConstructionFaultPoint.AfterProcessCreatedBeforeAssign)]
    public void EachConstructionFaultPointUnwindsOnlyWhatWasAcquired(int faultPointValue)
    {
        var faultPoint = (ConstructionFaultPoint)faultPointValue;
        using var baselineProcess = System.Diagnostics.Process.GetCurrentProcess();
        baselineProcess.Refresh();
        long before = baselineProcess.HandleCount;

        var options = NewOptions(["sleep", "1000"]);
        var result = ContainedProcess.Start(options, faultPoint);

        Assert.False(result.Succeeded);
        Assert.Equal(ProcessOwnershipFailureClass.ProcessConstructionFailed, result.FailureClass);
        Assert.Null(result.Process);

        baselineProcess.Refresh();
        long after = baselineProcess.HandleCount;

        // A generous bound (not an exact equality) — the CLR/xunit host itself opens and closes
        // incidental handles concurrently; the point is proving no large, monotonically growing leak per
        // fault point, not chasing an exact process-wide handle count.
        Assert.True(after - before < 25, $"Handle count grew by {after - before} after a fault at {faultPoint}.");
    }

    // ---- Group 10: active-zero-gated normal disposal --------------------------------------------------

    [Fact]
    public async Task CleanExitObservesActiveProcessZeroAndDisposesWithoutExplicitTerminate()
    {
        var options = NewOptions(["echo", "out", "err"]);
        var result = ContainedProcess.Start(options);
        Assert.True(result.Succeeded, result.Detail);
        var process = result.Process!;

        var wait = await process.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.Equal(ProcessOwnershipFailureClass.None, wait.FailureClass);
        Assert.True(wait.ActiveProcessZeroObserved);
        Assert.False(process.TerminateJobObjectWasCalled);

        process.Dispose();
        Assert.False(process.TerminateJobObjectWasCalled);
        Assert.Empty(process.TeardownFailuresForTests);
    }

    // ---- Group 11: unrelated-process isolation, deterministic receipts, repeated-run cleanup ---------

    [Fact]
    public async Task UnrelatedProcessIsolationWithDeterministicReceiptsAndRepeatedRunsCleanUpIdempotently()
    {
        using var unrelated = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = StubExecutablePath,
            ArgumentList = { "sleep", "5000" },
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Could not start the unrelated control process.");

        try
        {
            for (int iteration = 0; iteration < 3; iteration++)
            {
                var options = NewOptions(["echo", $"run-{iteration}-out", $"run-{iteration}-err"]);
                var result = ContainedProcess.Start(options);
                Assert.True(result.Succeeded, result.Detail);
                using var process = result.Process!;

                var wait = await process.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
                Assert.Equal(ProcessOwnershipFailureClass.None, wait.FailureClass);
                Assert.Equal($"run-{iteration}-out", wait.StdOut.Text);
                Assert.Equal($"run-{iteration}-err", wait.StdErr.Text);

                process.Dispose();
                Assert.Empty(process.TeardownFailuresForTests);
            }

            // The unrelated control process, never touched by any ContainedProcess/Job in this test,
            // must still be alive — containment/termination never reaches outside the owned job.
            Assert.False(unrelated.HasExited);
        }
        finally
        {
            if (!unrelated.HasExited)
            {
                unrelated.Kill(entireProcessTree: true);
            }
        }
    }

    // ---- shared helpers --------------------------------------------------------------------------------

    private static ProcessOwnershipOptions NewOptions(
        IReadOnlyList<string> arguments,
        Dictionary<string, string>? environment = null,
        TimeSpan? drainTimeout = null) =>
        new()
        {
            ExecutablePath = StubExecutablePath,
            Arguments = arguments,
            Environment = environment ?? DefaultEnvironment(),
            DrainTimeout = drainTimeout ?? TimeSpan.FromSeconds(30),
        };

    /// <summary>
    /// The minimal explicit, deterministic child environment: only <c>SystemRoot</c> (required for the
    /// .NET apphost/CLR to resolve system DLLs), copied deliberately rather than inherited wholesale.
    /// </summary>
    private static Dictionary<string, string> DefaultEnvironment()
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        string? systemRoot = System.Environment.GetEnvironmentVariable("SystemRoot");
        if (!string.IsNullOrEmpty(systemRoot))
        {
            env["SystemRoot"] = systemRoot;
        }

        return env;
    }

    private static string ResolveStubExecutablePath()
    {
        string baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string[] segments = baseDir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        int binIndex = Array.LastIndexOf(segments, "bin");
        if (binIndex < 0 || binIndex + 2 >= segments.Length)
        {
            throw new InvalidOperationException($"Could not parse a bin/<Config>/<Tfm> layout from '{baseDir}'.");
        }

        string config = segments[binIndex + 1];
        string tfm = segments[binIndex + 2];

        var testsRoot = new DirectoryInfo(baseDir);
        while (testsRoot is not null && testsRoot.Name != "tests")
        {
            testsRoot = testsRoot.Parent;
        }

        if (testsRoot is null)
        {
            throw new InvalidOperationException($"Could not locate a 'tests' ancestor above '{baseDir}'.");
        }

        string stubPath = Path.Combine(
            testsRoot.FullName,
            "SeqDoc.AcceptanceTests.ProcessOwnershipStub",
            "bin",
            config,
            tfm,
            "SeqDoc.AcceptanceTests.ProcessOwnershipStub.exe");

        if (!File.Exists(stubPath))
        {
            throw new InvalidOperationException(
                $"Stub executable not found at '{stubPath}'. Build "
                + "tests/SeqDoc.AcceptanceTests.ProcessOwnershipStub before running ProcessOwnershipTests.");
        }

        return stubPath;
    }

    private static Task WaitForFileToExistAsync(string path, TimeSpan timeout) =>
        WaitForConditionOrThrowAsync(() => File.Exists(path), timeout);

    private static async Task WaitForConditionOrThrowAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (deadline.Elapsed > timeout)
            {
                throw new TimeoutException($"Condition was not met within {timeout}.");
            }

            await Task.Delay(25);
        }
    }

    private static int CountOccurrences(string text, string marker)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += marker.Length;
        }

        return count;
    }
}
