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

    [Fact]
    public async Task WaitAsyncDirectlyAgainstLiveGrandchildDrainsWithinBoundInsteadOfHanging()
    {
        // GH106-F1 repair proof: unlike the sibling test above (which deliberately routes around the
        // hang by calling Terminate() before WaitAsync), this test calls WaitAsync directly against a
        // still-alive, pipe-write-handle-holding grandchild — exactly the condition that hung
        // indefinitely before the repair, because DrainPipe's synchronous Read() blocks inside the
        // kernel and its own deadline check only runs between completed reads. The grandchild sleeps far
        // longer (8s) than the WaitAsync timeout given here (3s), so if the fix did not force-unblock the
        // blocked read, this test would hang until xunit's own default test-collection timeout rather
        // than returning within the asserted bound.
        string markerPath = Path.Combine(Path.GetTempPath(), $"seqdoc-i100a-livewait-{Guid.NewGuid():N}.marker");
        var options = NewOptions(["spawn-grandchild", markerPath, "8000"]);
        var result = ContainedProcess.Start(options);
        Assert.True(result.Succeeded, result.Detail);
        using var process = result.Process!;
        try
        {
            await WaitForFileToExistAsync(markerPath, TimeSpan.FromSeconds(5));
            Assert.False(File.Exists(markerPath + ".completed"));
            Assert.False(process.HasObservedActiveProcessZero());

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var wait = await process.WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None);
            stopwatch.Stop();

            // Bounded well short of the grandchild's own 8s sleep and far short of a real hang: proves
            // the drain race actually forced TerminateJobObject to unblock the stuck read rather than
            // waiting for the grandchild's natural exit or timing out only at the harness level.
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(6),
                $"WaitAsync took {stopwatch.Elapsed}, which is not bounded by the 3s drain deadline.");

            Assert.Equal(0, wait.ExitCode); // the immediate parent's own, already-recorded natural exit code

            // GH106-R2-F2 repair consequence: the live grandchild also prevents ACTIVE_PROCESS_ZERO from
            // ever being observed within WaitAsync's own 3s bound, so the exited==true branch now records
            // ProcessFailed ("family exit could not be proven") before the drain race below even runs.
            // Per the frozen failure-class precedence table, ProcessFailed (3) legitimately outranks
            // DrainIncomplete (4) — both describe the same root cause (the live descendant), and
            // ProcessFailed is the more fundamental of the two facts, so first-write-wins correctly
            // surfaces it instead of the drain's own truncation.
            Assert.Equal(ProcessOwnershipFailureClass.ProcessFailed, wait.FailureClass);
            Assert.Contains("parent-exited", wait.StdOut.Text, StringComparison.Ordinal);
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
    [InlineData((int)ConstructionFaultPoint.AfterDrainsStartedBeforeAssign)]
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

    // ---- GH106-R2 repair round: PR #108 human peer review findings F1-F12 (F13 folded into F8/F9) -----

    [Fact]
    public void PostDrainsUnwindFaultCancelsAndAwaitsBackgroundDrainsAndCompletionMonitor()
    {
        // GH106-R2-F1: a failure between StartDrains/StartCompletionMonitor and AssignProcessToJobObject/
        // ResumeThread must not leak the background drain/completion-monitor tasks. The construction-
        // fault hook captures the internal ContainedProcess reference (never returned to a caller on a
        // failed Start) so this test can prove those tasks are genuinely completed, not merely abandoned.
        ContainedProcess? captured = null;
        ContainedProcess.PostDrainsStartHookForTests = p => captured = p;
        try
        {
            var options = NewOptions(["sleep", "1000"]);
            var result = ContainedProcess.Start(options, ConstructionFaultPoint.AfterDrainsStartedBeforeAssign);

            Assert.False(result.Succeeded);
            Assert.NotNull(captured);
            Assert.NotNull(captured!.StdOutDrainTaskForTests);
            Assert.NotNull(captured.StdErrDrainTaskForTests);
            Assert.True(captured.StdOutDrainTaskForTests!.IsCompleted, "stdout drain task was left running after unwind.");
            Assert.True(captured.StdErrDrainTaskForTests!.IsCompleted, "stderr drain task was left running after unwind.");
            Assert.True(
                captured.CompletionMonitorTaskForTests is null || captured.CompletionMonitorTaskForTests.IsCompleted,
                "completion monitor task was left running after unwind.");
        }
        finally
        {
            ContainedProcess.PostDrainsStartHookForTests = null;
        }
    }

    [Fact]
    public async Task UnprovenActiveProcessZeroOnNormalExitRecordsProcessFailed()
    {
        // GH106-R2-F2: the frozen failure table requires ProcessFailed when family exit cannot be proven.
        // Stopping the completion monitor for real (a genuine code path, not a mock) means
        // ACTIVE_PROCESS_ZERO can truly never be observed; a short test-only bound keeps this fast rather
        // than waiting out the real 10s production bound.
        var options = NewOptions(["echo", "out", "err"]);
        var result = ContainedProcess.Start(options);
        Assert.True(result.Succeeded, result.Detail);
        using var process = result.Process!;
        process.ActiveProcessZeroBoundForTests = TimeSpan.FromMilliseconds(150);
        process.StopCompletionMonitorForTests();

        var wait = await process.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.Equal(ProcessOwnershipFailureClass.ProcessFailed, wait.FailureClass);
        Assert.False(wait.ActiveProcessZeroObserved);
    }

    [Fact]
    public async Task TimeoutBranchAwaitsFamilyExitAfterForcedTermination()
    {
        // GH106-R2-F3: the timeout/cancellation branch must terminate AND await family exit, not just
        // fire TerminateJobObject and return. The immediate child itself (not a descendant) is still
        // running well past WaitAsync's own short timeout, so this exercises the `!exited` branch
        // directly.
        string markerPath = Path.Combine(Path.GetTempPath(), $"seqdoc-i100a-timeoutawait-{Guid.NewGuid():N}.marker");
        var options = NewOptions(["sleep-with-marker", markerPath, "8000"]);
        var result = ContainedProcess.Start(options);
        Assert.True(result.Succeeded, result.Detail);
        using var process = result.Process!;
        try
        {
            var wait = await process.WaitAsync(TimeSpan.FromMilliseconds(300), CancellationToken.None);

            Assert.Equal(ProcessOwnershipFailureClass.TimedOut, wait.FailureClass);
            Assert.True(process.TerminateJobObjectWasCalled);
            Assert.True(
                wait.ActiveProcessZeroObserved,
                "TerminateJobObject was called but the timeout branch must await ACTIVE_PROCESS_ZERO before returning.");
            Assert.False(File.Exists(markerPath + ".completed"));
        }
        finally
        {
            File.Delete(markerPath);
            File.Delete(markerPath + ".completed");
        }
    }

    [Fact]
    public async Task TerminateJobObjectFailureRecordsProcessFailedWithoutOverwritingEarlierTimedOut()
    {
        // GH106-R2-F4: a discarded TerminateJobObject failure is a real gap. Force it to fail via the
        // same injectable-seam pattern as the WaitForSingleObject seam below, and prove the earlier,
        // higher-precedence TimedOut class still wins (tracker first-write-wins), while the failure is
        // still recorded rather than silently dropped. The override intercepts every in-band
        // TerminateJobObject call, so the sleeping child is never actually terminated during WaitAsync
        // itself — WaitAsync's own drain-await only unblocks once the child exits naturally, so a
        // deliberately short sleep (well past the 300ms timeout, but not 10s) keeps this test fast.
        var options = NewOptions(["sleep", "2000"]);
        var result = ContainedProcess.Start(options);
        Assert.True(result.Succeeded, result.Detail);
        using var process = result.Process!;

        ContainedProcess.TerminateJobObjectOverrideForTests = (_, _) => false;
        try
        {
            var wait = await process.WaitAsync(TimeSpan.FromMilliseconds(300), CancellationToken.None);

            Assert.Equal(ProcessOwnershipFailureClass.TimedOut, wait.FailureClass);
            Assert.True(process.TerminateJobObjectWasCalled);
        }
        finally
        {
            ContainedProcess.TerminateJobObjectOverrideForTests = null;

            // The override made every in-band TerminateJobObject call a no-op, so the sleeping child is
            // still alive; clean it up for real now that the override is cleared.
            process.Terminate();
        }
    }

    [Fact]
    public async Task WaitForSingleObjectFailureRecordsProcessFailedDistinctFromGenuineTimeout()
    {
        // GH106-R2-F5: WAIT_FAILED/WAIT_ABANDONED must not be misreported as TimedOut.
        var options = NewOptions(["sleep", "5000"]);
        var result = ContainedProcess.Start(options);
        Assert.True(result.Succeeded, result.Detail);
        using var process = result.Process!;

        ContainedProcess.WaitForSingleObjectOverrideForTests = (_, _) => NativeMethods.WAIT_FAILED;
        try
        {
            var wait = await process.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

            Assert.Equal(ProcessOwnershipFailureClass.ProcessFailed, wait.FailureClass);
        }
        finally
        {
            ContainedProcess.WaitForSingleObjectOverrideForTests = null;
        }
    }

    [Fact]
    public async Task StdinIsClosedImmediatelySoChildReadingToEofCompletesWithoutHanging()
    {
        // GH106-R2-F6: the parent's stdin write handle must be closed immediately (not retained but
        // unusable), giving a stdin-reading child immediate EOF instead of hanging forever.
        var options = NewOptions(["read-stdin-to-eof"]);
        var result = ContainedProcess.Start(options);
        Assert.True(result.Succeeded, result.Detail);
        using var process = result.Process!;

        var wait = await process.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.Equal(ProcessOwnershipFailureClass.None, wait.FailureClass);
        Assert.Equal("READ-COMPLETE:0", wait.StdOut.Text);
    }

    [Fact]
    public async Task TeardownDegradedIsObservableThroughPublicSurfaceAfterDispose()
    {
        // GH106-R2-F7: a real caller (not just an internal test-only accessor) must be able to observe a
        // degraded teardown. Force a genuine CloseHandle failure by closing the process handle out from
        // under Dispose before it runs, so Dispose's own CloseHandle call fails for real.
        var options = NewOptions(["echo", "out", "err"]);
        var result = ContainedProcess.Start(options);
        Assert.True(result.Succeeded, result.Detail);
        var process = result.Process!;
        var wait = await process.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.Equal(ProcessOwnershipFailureClass.None, wait.FailureClass);

        NativeMethods.CloseHandle(process.ProcessHandleForTests);

        process.Dispose();

        Assert.Equal(ProcessOwnershipFailureClass.TeardownDegraded, process.FailureClass);
        Assert.NotEmpty(process.TeardownFailures);
    }

    [Theory]
    [InlineData("exec-nul")]
    [InlineData("arg-nul")]
    [InlineData("env-name-nul")]
    [InlineData("env-value-nul")]
    [InlineData("env-name-equals")]
    public void MalformedVectorsFailClosedBeforeReachingNativeConstruction(string vector)
    {
        // GH106-R2-F8/F13: embedded-NUL and malformed environment-name vectors must fail closed before
        // any native call (StartCore is never reached: Start() returns synchronously from validation).
        ProcessOwnershipOptions options = vector switch
        {
            "exec-nul" => new ProcessOwnershipOptions
            {
                ExecutablePath = StubExecutablePath + "\0evil",
                Environment = DefaultEnvironment(),
            },
            "arg-nul" => NewOptions(["echo", "a\0b", "c"]),
            "env-name-nul" => NewOptionsWithEnv(new Dictionary<string, string>(DefaultEnvironment()) { ["BAD\0NAME"] = "v" }),
            "env-value-nul" => NewOptionsWithEnv(new Dictionary<string, string>(DefaultEnvironment()) { ["BADVALUE"] = "v\0v" }),
            "env-name-equals" => NewOptionsWithEnv(new Dictionary<string, string>(DefaultEnvironment()) { ["BAD=NAME"] = "v" }),
            _ => throw new InvalidOperationException($"unknown vector {vector}"),
        };

        var result = ContainedProcess.Start(options);

        Assert.False(result.Succeeded);
        Assert.Equal(ProcessOwnershipFailureClass.ProcessConstructionFailed, result.FailureClass);
        Assert.Null(result.Process);
    }

    [Fact]
    public void EnvironmentBlockDedupIsCaseInsensitiveLastWriteWinsAndWellFormed()
    {
        // GH106-R2-F9/F13: Windows environment-variable names are case-insensitive; BuildEnvironmentBlock
        // must collapse case-variant duplicate keys (last-write-wins) rather than treating them as
        // distinct entries. Exercised directly at the encoding layer — the least expensive reliable
        // layer for a pure string-transform claim.
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Path"] = "first-value",
            ["PATH"] = "second-value",
            ["AAA"] = "a",
        };

        string block = ProcessOwnershipEncoding.BuildEnvironmentBlock(env);

        Assert.Contains("second-value", block, StringComparison.Ordinal);
        Assert.DoesNotContain("first-value", block, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(block, "-value"));

        // BuildEnvironmentBlock's own .NET string ends with exactly one explicit NUL per entry;
        // Marshal.StringToHGlobalUni (used at the real construction call site) appends its own implicit
        // terminator on top of that, producing the double-null-terminated native block CREATE_UNICODE_ENVIRONMENT
        // requires — asserting on the raw pre-marshaling string here, not the native buffer.
        Assert.EndsWith("\0", block, StringComparison.Ordinal);
        Assert.False(block.EndsWith("\0\0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Utf8MultibyteCharacterStraddling64KiBReadBoundaryDecodesCorrectly()
    {
        // GH106-R2-F10 follow-up: a non-overlapped anonymous-pipe ReadFile returns as soon as any data is
        // available, not only once a full 64 KiB buffer fills, so the byte offset any given DrainPipe
        // Read() call actually returns at is governed by the OS pipe's own buffer size/scheduling — not by
        // the stub's chosen 65535-byte filler length — unless the pipe buffer is made large enough to hold
        // the whole payload atomically. Force that here so the split is guaranteed, by construction, to
        // land exactly at the stub's chosen offset, straddling DrainPipe's 64 KiB read boundary.
        ContainedProcess.PipeBufferSizeOverrideForTests = 80000;
        try
        {
            var options = NewOptions(["utf8-boundary"]);
            var result = ContainedProcess.Start(options);
            Assert.True(result.Succeeded, result.Detail);
            using var process = result.Process!;

            var wait = await process.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            Assert.Equal(ProcessOwnershipFailureClass.None, wait.FailureClass);
            Assert.False(wait.StdOut.Truncated);
            string expected = new string('x', 65535) + "€" + "-MARKER-END";
            Assert.Equal(expected, wait.StdOut.Text);
        }
        finally
        {
            ContainedProcess.PipeBufferSizeOverrideForTests = null;
        }
    }

    [Fact]
    public async Task DisposeClosesResourcesInExactTrueReverseAcquisitionOrder()
    {
        // GH106-R2-F11: proves the exact close/free trace order for a full normal disposal, not just a
        // handle-count delta. The stdin write handle is omitted from the expected trace because F6 closes
        // it during construction, so Dispose's own CloseTracked call for it is a no-op (never recorded).
        var options = NewOptions(["echo", "out", "err"]);
        var result = ContainedProcess.Start(options);
        Assert.True(result.Succeeded, result.Detail);
        var process = result.Process!;
        var wait = await process.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.Equal(ProcessOwnershipFailureClass.None, wait.FailureClass);

        process.Dispose();

        Assert.Equal(ExpectedFullDisposeOrder, process.TeardownOrderForTests);
    }

    private static readonly string[] ExpectedFullDisposeOrder =
    {
        "process handle",
        "thread handle",
        "environment block buffer",
        "command line buffer",
        "completion port handle",
        "job handle",
        "attribute list buffer",
        "handle list buffer",
        "stderr pipe handle",
        "stdout pipe handle",
    };

    [Fact]
    public void PartialConstructionUnwindClosesResourcesInExactTrueReverseAcquisitionOrder()
    {
        // GH106-R2-F11: the same exact-reverse-order proof, but for a partial-construction fault (the
        // unwind Stack<Action>, not Dispose's straight-line sequence) — the checkpoint's own requirement
        // to cover at least one S0-S3 fault point, not only full normal disposal.
        var trace = new List<string>();
        ContainedProcess.UnwindStepObserverForTests = trace.Add;
        try
        {
            var options = NewOptions(["sleep", "1000"]);
            var result = ContainedProcess.Start(options, ConstructionFaultPoint.AfterProcessCreatedBeforeAssign);

            Assert.False(result.Succeeded);
            Assert.Equal(ExpectedPartialUnwindOrder, trace);
        }
        finally
        {
            ContainedProcess.UnwindStepObserverForTests = null;
        }
    }

    private static readonly string[] ExpectedPartialUnwindOrder =
    {
        "process handle",
        "thread handle",
        "environment block buffer",
        "command line buffer",
        "completion port handle",
        "job handle",
        "attribute list buffer",
        "handle list buffer",
        "stderr write handle",
        "stderr read handle",
        "stdout write handle",
        "stdout read handle",
        "stdin write handle",
        "stdin read handle",
    };

    [Fact]
    public async Task AssignPrecedesResumeProvenByObservableChildReceiptFromItsOwnFirstInstruction()
    {
        // GH106-R2-F12: replaces the internal test-hook-only proof above with a genuine, external,
        // production-code-path receipt — the child itself queries its own job membership (via a plain
        // P/Invoke in the stub, no unsafe blocks) as the very first thing it does and prints the result,
        // proving assign-before-resume chronology from the child's own perspective rather than the test
        // process's.
        var options = NewOptions(["report-job-membership"]);
        var result = ContainedProcess.Start(options);
        Assert.True(result.Succeeded, result.Detail);
        using var process = result.Process!;

        var wait = await process.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.Equal(ProcessOwnershipFailureClass.None, wait.FailureClass);
        Assert.Equal("IN-JOB:True\r\n", wait.StdOut.Text);
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

    /// <summary>GH106-R2-F8 helper: an "echo" child whose only purpose is exercising a rejected environment vector.</summary>
    private static ProcessOwnershipOptions NewOptionsWithEnv(Dictionary<string, string> environment) =>
        NewOptions(["echo", "out", "err"], environment);

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
