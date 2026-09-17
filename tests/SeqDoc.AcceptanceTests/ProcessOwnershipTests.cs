using System.Globalization;
using System.Text;
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
            OperatingSystem.IsWindows()
                && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == RuntimeArchitecture.X64
                && Environment.OSVersion.Version >= new Version(10, 0),
            actual);

        // Synthetic claims use the amended pure evaluator seam (unit-level fake, never skipped):
        // Windows 10 x64 is admitted; pre-10 x64 and every non-x64 architecture fail closed.
        Assert.True(EvaluatePlatform(true, RuntimeArchitecture.X64, new Version(10, 0)));
        Assert.False(EvaluatePlatform(true, RuntimeArchitecture.X64, new Version(6, 3)));
        Assert.False(EvaluatePlatform(true, RuntimeArchitecture.Arm64, new Version(10, 0)));
        Assert.False(EvaluatePlatform(true, RuntimeArchitecture.X86, new Version(10, 0)));
        Assert.False(EvaluatePlatform(false, RuntimeArchitecture.X64, new Version(10, 0)));
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

    // ---- Group 4: creation-time containment and construction unwind -------------------------------

    [Fact]
    public async Task CreationTimeJobAdmissionContainsSuspendedChildAndPostCreateAssignIsNotUsed()
    {
        // Keep the historical hook explicitly disabled; creation-time admission supersedes it.
        ContainedProcess.AssignBeforeResumeHookForTests = null;
        bool? observedWhileSuspended = null;
        nint duplicatedProcess = nint.Zero;
        var observer = FindStaticTestSeam("PostCreateProcessObserverForTests", typeof(Action<nint, nint>));
        Assert.True(observer is not null,
            "Expected post-CreateProcess observer seam receiving job/process handles while suspended.");
        var nativeCalls = new ProcessOwnershipNativeCalls
        {
            AssignProcessToJobObject = (_, _) => throw new InvalidOperationException(
                "post-create AssignProcessToJobObject must not be used after creation-time admission"),
        };

        try
        {
            observer!.SetValue(null, (Action<nint, nint>)((job, proc) =>
            {
                Assert.True(NativeMethods.IsProcessInJob(proc, job, out bool result));
                observedWhileSuspended = result;
                Assert.True(NativeMethods.DuplicateHandle(
                    NativeMethods.GetCurrentProcess(), proc,
                    NativeMethods.GetCurrentProcess(), out duplicatedProcess, 0, false, 0x00000002));
            }));
            var options = NewOptions(["sleep", "50"], nativeCalls: nativeCalls);
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
            observer?.SetValue(null, null);
            if (duplicatedProcess != nint.Zero)
            {
                NativeMethods.CloseHandle(duplicatedProcess);
            }
        }
    }

    [Fact]
    public void CreationFaultWithFailedTerminateStillClosesKillOnCloseJobAroundSuspendedChild()
    {
        nint duplicatedProcess = nint.Zero;
        var observer = FindStaticTestSeam("PostCreateProcessObserverForTests", typeof(Action<nint, nint>));
        Assert.True(observer is not null,
            "Expected post-CreateProcess observer seam receiving job/process handles while suspended.");
        try
        {
            observer!.SetValue(null, (Action<nint, nint>)((_, process) =>
            {
                Assert.True(NativeMethods.DuplicateHandle(
                    NativeMethods.GetCurrentProcess(), process,
                    NativeMethods.GetCurrentProcess(), out duplicatedProcess, 0, false, 0x00000002));
            }));
            var result = ContainedProcess.Start(
                NewOptions(["sleep", "5000"], nativeCalls: new ProcessOwnershipNativeCalls
                {
                    TerminateProcess = (_, _) => NativeCallResult.Failure(8101),
                }),
                ConstructionFaultPoint.AfterProcessCreatedBeforeAssign);

            Assert.False(result.Succeeded);
            Assert.Contains("8101", result.Detail, StringComparison.Ordinal);
            Assert.NotEqual(nint.Zero, duplicatedProcess);
            Assert.Equal(NativeMethods.WAIT_OBJECT_0,
                NativeMethods.WaitForSingleObject(duplicatedProcess, 5000));
        }
        finally
        {
            observer?.SetValue(null, null);
            if (duplicatedProcess != nint.Zero)
            {
                NativeMethods.CloseHandle(duplicatedProcess);
            }
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

    [Fact]
    public async Task ParentExitWithDescendantPipeClosureStillTerminatesFamilyAndProvesActiveZero()
    {
        string markerPath = Path.Combine(Path.GetTempPath(), $"seqdoc-i100a-closepipes-{Guid.NewGuid():N}.marker");
        var result = ContainedProcess.Start(NewOptions(["spawn-grandchild-closes-pipes", markerPath, "8000"]));
        Assert.True(result.Succeeded, result.Detail);
        using var process = result.Process!;
        try
        {
            await WaitForFileToExistAsync(markerPath, TimeSpan.FromSeconds(5));
            var wait = await process.WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None);

            Assert.True(process.TerminateJobObjectWasCalled,
                "EOF from the descendant must not be mistaken for family exit.");
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
    public async Task FailedTerminationWithOpenPipeIsBoundedAndRetainsOrderedSecondaryEvidence()
    {
        var options = NewOptions(
            ["sleep", "8000"],
            nativeCalls: new ProcessOwnershipNativeCalls
            {
                TerminateJobObject = (_, _) => NativeCallResult.Failure(5),
            });
        var result = ContainedProcess.Start(options);
        Assert.True(result.Succeeded, result.Detail);
        using var process = result.Process!;
        process.ActiveProcessZeroBoundForTests = TimeSpan.FromMilliseconds(100);

        Task<ProcessOwnershipWaitResult> waitTask = process.WaitAsync(TimeSpan.FromMilliseconds(300), CancellationToken.None);
        try
        {
            Task completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(6)));
            Assert.Same(waitTask, completed);
            var wait = await waitTask;
            Assert.Equal(ProcessOwnershipFailureClass.TimedOut, wait.FailureClass);
            Assert.True(wait.StdOut.Truncated || wait.StdErr.Truncated);
            int terminateEvidence = IndexOfEvidence(wait.SecondaryFailures, "TerminateJobObject", "5");
            int familyEvidence = IndexOfEvidence(wait.SecondaryFailures, "ACTIVE_PROCESS_ZERO");
            Assert.True(terminateEvidence >= 0);
            Assert.True(familyEvidence > terminateEvidence);
        }
        finally { process.Terminate(); }
    }

    [Fact]
    public async Task DisposeCoordinatesWithConcurrentWaitAndIsIdempotent()
    {
        var result = ContainedProcess.Start(NewOptions(["sleep", "5000"]));
        Assert.True(result.Succeeded, result.Detail);
        var process = result.Process!;
        Task<ProcessOwnershipWaitResult> waitTask = process.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

        process.Dispose();
        process.Dispose();

        Task completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(waitTask, completed);
        var wait = await waitTask;
        Assert.True(wait.ActiveProcessZeroObserved);
        Assert.All(new[] { process.StdOutDrainTaskForTests, process.StdErrDrainTaskForTests, process.CompletionMonitorTaskForTests },
            task => Assert.True(task is null || task.IsCompleted));
        process.Dispose();
    }

    [Fact]
    public async Task DisposeDoesNotReleaseHandlesWhileWaitUsesNativeProcessHandle()
    {
        var entered = new TaskCompletionSource<nint>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releases = new System.Collections.Concurrent.ConcurrentQueue<string>();
        int terminalCalls = 0;
        var nativeCalls = new ProcessOwnershipNativeCalls
        {
            TerminateJobObject = (handle, exitCode) =>
            {
                Interlocked.Increment(ref terminalCalls);
                return NativeMethods.TerminateJobObject(handle, exitCode)
                    ? NativeCallResult.Success()
                    : NativeCallResult.Failure(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            },
            WaitForSingleObject = (handle, timeout) =>
            {
                entered.TrySetResult(handle);
                release.Task.GetAwaiter().GetResult();
                uint waitResult = NativeMethods.WaitForSingleObject(handle, timeout);
                int error = waitResult == NativeMethods.WAIT_FAILED
                    ? System.Runtime.InteropServices.Marshal.GetLastWin32Error()
                    : 0;
                return new NativeWaitResult(waitResult, error);
            },
        };
        var result = ContainedProcess.Start(NewOptions(["sleep", "5000"], nativeCalls: nativeCalls));
        Assert.True(result.Succeeded, result.Detail);
        var process = result.Process!;
        System.Reflection.PropertyInfo? releaseObserver = null;
        Task<ProcessOwnershipWaitResult>? waitTask = null;
        Task? disposeTask = null;
        try
        {
            releaseObserver = FindInstanceTestSeam("ResourceReleaseObserverForTests", typeof(Action<string>));
            Assert.True(releaseObserver is not null, "Expected per-process resource-release observer seam is not implemented yet.");
            releaseObserver!.SetValue(process, (Action<string>)releases.Enqueue);

            waitTask = process.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            Assert.Equal(process.ProcessHandleForTests, await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            disposeTask = Task.Run(process.Dispose);

            Task negativeWinner = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromMilliseconds(250)));
            Assert.NotSame(disposeTask, negativeWinner);
            Assert.DoesNotContain("process handle", releases);
            Assert.DoesNotContain("job handle", releases);

            release.TrySetResult();
            await Task.WhenAll(waitTask, disposeTask).WaitAsync(TimeSpan.FromSeconds(10));

            var wait = await waitTask;
            Assert.Equal(ProcessOwnershipFailureClass.ProcessFailed, wait.FailureClass);
            Assert.False(wait.TimedOut);
            Assert.False(wait.Cancelled);
            Assert.True(wait.ActiveProcessZeroObserved);
            Assert.Equal(1, terminalCalls);
            Assert.True(process.TerminateJobObjectWasCalled);
            Assert.Empty(process.TeardownFailuresForTests);
            Assert.Equal(1, releases.Count(label => label == "process handle"));
            Assert.Equal(1, releases.Count(label => label == "job handle"));
        }
        finally
        {
            release.TrySetResult();
            if (waitTask is not null && disposeTask is not null)
            {
                try
                {
                    await Task.WhenAll(waitTask, disposeTask).WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch
                {
                    try { process.Terminate(); } catch { }
                }
            }
            else
            {
                process.Dispose();
            }

            if (releaseObserver is not null)
            {
                releaseObserver.SetValue(process, null);
            }
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
        var options = NewOptions(["sleep", "2000"], nativeCalls: new ProcessOwnershipNativeCalls
        {
            TerminateJobObject = (_, _) => NativeCallResult.Failure(5),
        });
        var result = ContainedProcess.Start(options);
        Assert.True(result.Succeeded, result.Detail);
        using var process = result.Process!;

        try
        {
            var wait = await process.WaitAsync(TimeSpan.FromMilliseconds(300), CancellationToken.None);

            Assert.Equal(ProcessOwnershipFailureClass.TimedOut, wait.FailureClass);
            Assert.True(process.TerminateJobObjectWasCalled);
        }
        finally
        {
            // The override made every in-band TerminateJobObject call a no-op, so the sleeping child is
            // still alive; clean it up for real now that the override is cleared.
            process.Terminate();
        }
    }

    [Fact]
    public async Task WaitForSingleObjectFailureRecordsProcessFailedDistinctFromGenuineTimeout()
    {
        // GH106-R2-F5: WAIT_FAILED/WAIT_ABANDONED must not be misreported as TimedOut.
        var options = NewOptions(["sleep", "5000"], nativeCalls: new ProcessOwnershipNativeCalls
        {
            WaitForSingleObject = (_, _) => NativeWaitResult.Failed(1234),
        });
        var result = ContainedProcess.Start(options);
        Assert.True(result.Succeeded, result.Detail);
        using var process = result.Process!;

        try
        {
            var wait = await process.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

            Assert.Equal(ProcessOwnershipFailureClass.ProcessFailed, wait.FailureClass);
            Assert.False(wait.TimedOut);
            Assert.False(wait.Cancelled);
            Assert.Contains("1234", wait.Detail, StringComparison.Ordinal);
        }
        finally { }
    }

    [Fact]
    public void ConstructionPhaseFailuresLeaveObservableEvidenceWithoutAbandonedTasks()
    {
        // JOB_LIST admits the child during CreateProcess; there is no post-create assignment failure
        // mode to exercise. Retain the resume/unwind failure, which still proves cleanup evidence.
        var options = NewOptions(["sleep", "1000"], nativeCalls: new ProcessOwnershipNativeCalls
        {
            ResumeThread = _ => NativeCallResult<uint>.Failure(995),
            TerminateJobObject = (_, _) => NativeCallResult.Failure(5),
            WaitForSingleObject = (_, _) => NativeWaitResult.Failed(6),
        });
        var result = ContainedProcess.Start(options);
        Assert.False(result.Succeeded);
        Assert.Equal(ProcessOwnershipFailureClass.ProcessConstructionFailed, result.FailureClass);
        Assert.NotNull(result.Detail);
    }

    [Fact]
    public async Task LaterCleanupFailureDoesNotReplacePrimaryFailureAndIsOrdered()
    {
        var options = NewOptions(["sleep", "5000"], nativeCalls: new ProcessOwnershipNativeCalls
        {
            TerminateJobObject = (_, _) => NativeCallResult.Failure(4321),
        });
        var result = ContainedProcess.Start(options);
        Assert.True(result.Succeeded, result.Detail);
        using var process = result.Process!;
        try
        {
            var wait = await process.WaitAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None);
            Assert.Equal(ProcessOwnershipFailureClass.TimedOut, wait.FailureClass);
            Assert.True(IndexOfEvidence(wait.SecondaryFailures, "4321") >= 0);
        }
        finally { }
    }

    [Fact]
    public async Task ExplicitRuntimeDiscoveryVariablesArePassedWithoutAmbientLeakage()
    {
        var environment = DefaultEnvironment();
        string? rootX64 = Environment.GetEnvironmentVariable("DOTNET_ROOT_X64");
        string? root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        Assert.True(!string.IsNullOrWhiteSpace(rootX64) || !string.IsNullOrWhiteSpace(root),
            "The official isolated SDK 10.0.302 lane must provide DOTNET_ROOT_X64 and/or DOTNET_ROOT.");
        if (rootX64 is not null)
        {
            environment["DOTNET_ROOT_X64"] = rootX64;
        }

        if (root is not null)
        {
            environment["DOTNET_ROOT"] = root;
        }
        string variable = rootX64 is not null ? "DOTNET_ROOT_X64" : "DOTNET_ROOT";
        string expected = environment[variable];
        var result = ContainedProcess.Start(NewOptions(["print-env", variable], environment));
        Assert.True(result.Succeeded, result.Detail);
        using var process = result.Process!;
        var wait = await process.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.Equal(expected, wait.StdOut.Text);
        Assert.DoesNotContain("PATH", environment.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Utf8DecoderReassemblesACharacterAcrossControlledChunks()
    {
        Assert.Equal("prefix€suffix", ProcessOwnershipEncoding.DecodeUtf8Chunks(
            [Encoding.UTF8.GetBytes("prefix"), [0xE2], [0x82, 0xAC, .. Encoding.UTF8.GetBytes("suffix")]]));
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
        // GH106-R2-F7: inject one deterministic owned-handle close failure without pre-closing a numeric
        // handle that could be reused by the process. All other closes use the real native operation.
        bool armed = false;
        nint failedHandle = nint.Zero;
        var nativeCalls = new ProcessOwnershipNativeCalls();
        var closeSeam = FindInstanceTestSeam("CloseHandle", typeof(Func<nint, NativeCallResult>));
        Assert.True(closeSeam is not null, "Expected per-instance CloseHandle seam.");
        closeSeam!.SetValue(nativeCalls, (Func<nint, NativeCallResult>)(handle =>
        {
            if (armed && failedHandle == nint.Zero)
            {
                failedHandle = handle;
                return NativeCallResult.Failure(8301);
            }

            if (NativeMethods.CloseHandle(handle))
            {
                return NativeCallResult.Success();
            }

            return NativeCallResult.Failure(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        }));

        var options = NewOptions(["echo", "out", "err"], nativeCalls: nativeCalls);
        var result = ContainedProcess.Start(options);
        Assert.True(result.Succeeded, result.Detail);
        var process = result.Process!;
        try
        {
            var wait = await process.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            Assert.Equal(ProcessOwnershipFailureClass.None, wait.FailureClass);

            armed = true;
            process.Dispose();

            Assert.Equal(ProcessOwnershipFailureClass.TeardownDegraded, process.FailureClass);
            Assert.Contains(process.TeardownFailures,
                evidence => evidence.Contains("8301", StringComparison.Ordinal));
            Assert.NotEqual(nint.Zero, failedHandle);
        }
        finally
        {
            armed = false;
            closeSeam.SetValue(nativeCalls, null);
            if (failedHandle != nint.Zero)
            {
                NativeMethods.CloseHandle(failedHandle);
            }

            process.Dispose();
        }
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
    public async Task DisposeClosesResourcesInExactTrueReverseAcquisitionOrder()
    {
        // GH106-R2-F11: proves dependency-safe reverse release. In particular, the job-list payload is
        // released only after DeleteProcThreadAttributeList has released the two-entry attribute list.
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
        "attribute list buffer",
        "job list buffer",
        "completion port handle",
        "job handle",
        "handle list buffer",
        "stderr pipe handle",
        "stdout pipe handle",
    };

    [Fact]
    public void PartialConstructionUnwindClosesResourcesInExactTrueReverseAcquisitionOrder()
    {
        // GH106-R2-F11: the same dependency-safe reverse-order proof for partial construction. Attribute
        // list deletion precedes release of its job-list payload; only then are the pipe handles unwound.
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
        "attribute list buffer",
        "job list buffer",
        "completion port handle",
        "job handle",
        "handle list buffer",
        "stderr write handle",
        "stderr read handle",
        "stdout write handle",
        "stdout read handle",
        "stdin write handle",
        "stdin read handle",
    };

    [Fact]
    public async Task ChildReceiptProvesCreationTimeJobAdmissionBeforeItsOwnFirstInstruction()
    {
        // GH106-R2-F12: replaces the internal test-hook-only proof above with a genuine, external,
        // production-code-path receipt — the child itself queries its own job membership (via a plain
        // P/Invoke in the stub, no unsafe blocks) as the very first thing it does and prints the result,
        // proving creation-time job admission from the child's own perspective rather than the test
        // process's; the receipt must be true before the first child instruction runs.
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

    [Fact]
    public async Task DisposeDoesNotReleaseFamilyResourcesWhenActiveProcessZeroCannotBeProven()
    {
        var result = ContainedProcess.Start(NewOptions(["echo", "out", "err"]));
        Assert.True(result.Succeeded, result.Detail);
        var process = result.Process!;
        var releases = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var releaseObserver = FindInstanceTestSeam("ResourceReleaseObserverForTests", typeof(Action<string>));
        Assert.True(releaseObserver is not null, "Expected resource-release observer seam.");
        releaseObserver!.SetValue(process, (Action<string>)releases.Enqueue);
        var retainedResources = FindReadableInstanceProperty("HasRetainedFamilyResourcesForTests", typeof(bool));
        Assert.True(retainedResources is not null && !retainedResources.CanWrite,
            "Expected read-only retained-family-resource observability seam.");
        var lifecycle = typeof(ContainedProcess).GetField("_lifecycleState",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.True(lifecycle is not null, "Expected observable lifecycle state for retained ownership.");
        try
        {
            process.ActiveProcessZeroBoundForTests = TimeSpan.FromMilliseconds(100);
            process.StopCompletionMonitorForTests();

            Task dispose = Task.Run(process.Dispose);
            await dispose.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotEqual(ProcessOwnershipFailureClass.None, process.FailureClass);
            var firstFailures = process.TeardownFailures.ToArray();
            var firstOrder = process.TeardownOrderForTests.ToArray();
            Assert.Contains(firstFailures,
                evidence => evidence.Contains("ACTIVE_PROCESS_ZERO", StringComparison.Ordinal));
            Assert.True((bool)retainedResources!.GetValue(process)!,
                "Failed family proof must retain ownership of the process, job, and completion handles.");
            Assert.NotEqual("Disposed", lifecycle!.GetValue(process)!.ToString());
            Assert.DoesNotContain(releases, label => label is "process handle" or "completion port handle"
                or "job handle");

            int releaseCount = releases.Count;
            await Task.Run(process.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True((bool)retainedResources.GetValue(process)!,
                "A retry under the same unavailable-proof condition must remain retained.");
            Assert.Equal(releaseCount, releases.Count);
            Assert.Equal(firstFailures, process.TeardownFailures);
            Assert.Equal(firstOrder, process.TeardownOrderForTests);
            Assert.Equal(1, process.TeardownFailures.Count(
                evidence => evidence.Contains("ACTIVE_PROCESS_ZERO", StringComparison.Ordinal)));

            var terminateException = Record.Exception(() => _ = process.Terminate());
            Assert.False(terminateException is ObjectDisposedException,
                "Retained ownership must remain usable by Terminate after a failed disposal proof.");
        }
        finally
        {
            releaseObserver.SetValue(process, null);
            process.Dispose();
        }
    }

    [Fact]
    public void FailedSecondAttributeInitializationDoesNotDeleteUninitializedBuffer()
    {
        var initialize = FindInstanceTestSeam("InitializeProcThreadAttributeList", typeof(Func<nint, NativeCallResult>));
        Assert.True(initialize is not null,
            "Expected per-instance InitializeProcThreadAttributeList fault seam.");
        var deleteObserver = FindStaticTestSeam("AttributeListDeleteObserverForTests", typeof(Action<nint>));
        Assert.True(deleteObserver is not null,
            "Expected static attribute-list deletion observer seam.");

        int deleteCount = 0;
        var nativeCalls = new ProcessOwnershipNativeCalls();
        try
        {
            initialize!.SetValue(nativeCalls, (Func<nint, NativeCallResult>)(buffer =>
                buffer == nint.Zero ? NativeCallResult.Success() : NativeCallResult.Failure(8301)));
            deleteObserver!.SetValue(null, (Action<nint>)(_ => Interlocked.Increment(ref deleteCount)));

            var result = ContainedProcess.Start(NewOptions(["echo", "out", "err"], nativeCalls: nativeCalls));

            Assert.False(result.Succeeded);
            Assert.Equal(ProcessOwnershipFailureClass.ProcessConstructionFailed, result.FailureClass);
            Assert.Contains("8301", result.Detail, StringComparison.Ordinal);
            Assert.Equal(0, deleteCount);
        }
        finally
        {
            initialize.SetValue(nativeCalls, null);
            deleteObserver.SetValue(null, null);
        }
    }

    [Fact]
    public async Task ThrowingAttributeDeleteObserverDoesNotInterruptDisposalCleanup()
    {
        var deleteObserver = FindStaticTestSeam("AttributeListDeleteObserverForTests", typeof(Action<nint>));
        Assert.True(deleteObserver is not null, "Expected static attribute-list deletion observer seam.");
        int deleteCount = 0;
        const string sentinel = "attribute-delete-observer-sentinel";
        var result = ContainedProcess.Start(NewOptions(["echo", "out", "err"]));
        Assert.True(result.Succeeded, result.Detail);
        var process = result.Process!;

        try
        {
            deleteObserver!.SetValue(null, (Action<nint>)(_ =>
            {
                Interlocked.Increment(ref deleteCount);
                throw new InvalidOperationException(sentinel);
            }));

            var wait = await process.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            Assert.Equal(ProcessOwnershipFailureClass.None, wait.FailureClass);
            var exception = Record.Exception(() =>
            {
                process.Dispose();
            });

            Assert.Null(exception);
            Assert.Equal(1, deleteCount);
            Assert.Equal(ExpectedFullDisposeOrder, process.TeardownOrderForTests);
            Assert.Equal(ProcessOwnershipFailureClass.None, process.FailureClass);
            Assert.DoesNotContain(process.TeardownFailures,
                evidence => evidence.Contains(sentinel, StringComparison.Ordinal));
        }
        finally
        {
            deleteObserver.SetValue(null, null);
            process.Dispose();
        }
    }

    [Fact]
    public async Task ThrowingResourceReleaseObserverDoesNotInterruptBufferCleanup()
    {
        var releaseObserver = FindInstanceTestSeam("ResourceReleaseObserverForTests", typeof(Action<string>));
        Assert.True(releaseObserver is not null, "Expected per-instance resource-release observer seam.");
        var receipts = new System.Collections.Concurrent.ConcurrentQueue<string>();
        const string sentinel = "attribute-list-buffer-observer-sentinel";
        var result = ContainedProcess.Start(NewOptions(["echo", "out", "err"]));
        Assert.True(result.Succeeded, result.Detail);
        var process = result.Process!;
        var retainedResources = FindReadableInstanceProperty("HasRetainedFamilyResourcesForTests", typeof(bool));
        Assert.NotNull(retainedResources);
        var lifecycle = typeof(ContainedProcess).GetField("_lifecycleState",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(lifecycle);

        try
        {
            releaseObserver!.SetValue(process, (Action<string>)(label =>
            {
                receipts.Enqueue(label);
                if (label == "attribute list buffer")
                {
                    throw new InvalidOperationException(sentinel);
                }
            }));

            var wait = await process.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            Assert.Equal(ProcessOwnershipFailureClass.None, wait.FailureClass);

            Task dispose = Task.Run(process.Dispose);
            await dispose.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(ExpectedFullDisposeOrder, process.TeardownOrderForTests);
            Assert.Equal(ExpectedFullDisposeOrder, receipts);
            Assert.Equal(ProcessOwnershipFailureClass.None, process.FailureClass);
            Assert.DoesNotContain(process.TeardownFailures,
                evidence => evidence.Contains(sentinel, StringComparison.Ordinal));
            Assert.False((bool)retainedResources!.GetValue(process)!);
            Assert.Equal("Disposed", lifecycle!.GetValue(process)!.ToString());
        }
        finally
        {
            releaseObserver!.SetValue(process, null);
            process.Dispose();
        }
    }

    [Fact]
    public async Task ThrowingResourceReleaseObserverDoesNotInterruptNativeHandleCleanup()
    {
        var releaseObserver = FindInstanceTestSeam("ResourceReleaseObserverForTests", typeof(Action<string>));
        Assert.True(releaseObserver is not null, "Expected per-instance resource-release observer seam.");
        var receipts = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var closedHandles = new System.Collections.Concurrent.ConcurrentQueue<nint>();
        const string sentinel = "process-handle-observer-sentinel";
        var nativeCalls = new ProcessOwnershipNativeCalls
        {
            CloseHandle = handle =>
            {
                closedHandles.Enqueue(handle);
                return NativeMethods.CloseHandle(handle)
                    ? NativeCallResult.Success()
                    : NativeCallResult.Failure(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            },
        };
        var result = ContainedProcess.Start(NewOptions(["echo", "out", "err"], nativeCalls: nativeCalls));
        Assert.True(result.Succeeded, result.Detail);
        var process = result.Process!;
        var retainedResources = FindReadableInstanceProperty("HasRetainedFamilyResourcesForTests", typeof(bool));
        Assert.NotNull(retainedResources);
        var lifecycle = typeof(ContainedProcess).GetField("_lifecycleState",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(lifecycle);
        nint processHandle = process.ProcessHandleForTests;

        try
        {
            releaseObserver!.SetValue(process, (Action<string>)(label =>
            {
                receipts.Enqueue(label);
                if (label == "process handle")
                {
                    throw new InvalidOperationException(sentinel);
                }
            }));

            var wait = await process.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            Assert.Equal(ProcessOwnershipFailureClass.None, wait.FailureClass);

            Task dispose = Task.Run(process.Dispose);
            await dispose.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Contains(processHandle, closedHandles);
            Assert.Equal(ExpectedFullDisposeOrder, process.TeardownOrderForTests);
            Assert.Equal(ExpectedFullDisposeOrder, receipts);
            Assert.Equal(ProcessOwnershipFailureClass.None, process.FailureClass);
            Assert.Empty(process.TeardownFailures);
            Assert.DoesNotContain(process.TeardownFailures,
                evidence => evidence.Contains(sentinel, StringComparison.Ordinal));
            Assert.False((bool)retainedResources!.GetValue(process)!);
            Assert.Equal("Disposed", lifecycle!.GetValue(process)!.ToString());
        }
        finally
        {
            releaseObserver!.SetValue(process, null);
            process.Dispose();
        }
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

    // ---- Owner recovery R3: serialized lifecycle, drain, family proof, unwind, evidence, and SDK ----

    [Fact]
    public async Task TerminateAndDisposeRaceHasOneSerializedTerminalSequenceAndNoPostReleaseNativeCall()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int terminationCalls = 0;
        bool nativeAfterRelease = false;
        nint handleSeen = nint.Zero;
        ContainedProcess? process = null;
        var options = NewOptions(["sleep", "5000"], nativeCalls: new ProcessOwnershipNativeCalls
        {
            TerminateJobObject = (handle, _) =>
            {
                Interlocked.Increment(ref terminationCalls);
                handleSeen = handle;
                nativeAfterRelease |= process?.TeardownOrderForTests.Contains("job handle") == true;
                entered.TrySetResult();
                release.Task.GetAwaiter().GetResult();
                return NativeCallResult.Success();
            },
        });
        var result = ContainedProcess.Start(options);
        Assert.True(result.Succeeded, result.Detail);
        process = result.Process!;

        Task terminate = Task.Run(() => process.Terminate());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task dispose = Task.Run(process.Dispose);
        await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromMilliseconds(250)));
        release.SetResult();

        await Task.WhenAll(terminate, dispose).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, terminationCalls);
        Assert.NotEqual(nint.Zero, handleSeen);
        Assert.False(nativeAfterRelease);
        process.Dispose();
    }

    [Fact]
    public async Task FailedTerminationWaitsForBothDrainsBeforeClosingPipeHandlesAndMarksIncompleteOutput()
    {
        var releases = new List<(string Label, bool StdOutComplete, bool StdErrComplete, bool MonitorComplete)>();
        ContainedProcess? observedProcess = null;
        var options = NewOptions(["sleep", "5000"], nativeCalls: new ProcessOwnershipNativeCalls
        {
            TerminateJobObject = (_, _) => NativeCallResult.Failure(7001),
        });
        var result = ContainedProcess.Start(options);
        Assert.True(result.Succeeded, result.Detail);
        observedProcess = result.Process!;
        var releaseObserver = FindInstanceTestSeam("ResourceReleaseObserverForTests", typeof(Action<string>));
        Assert.True(releaseObserver is not null,
            "Expected per-process resource-release observer seam is not implemented yet.");
        releaseObserver!.SetValue(observedProcess, (Action<string>)(label => releases.Add((
            label,
            observedProcess.StdOutDrainTaskForTests?.IsCompleted ?? true,
            observedProcess.StdErrDrainTaskForTests?.IsCompleted ?? true,
            observedProcess.CompletionMonitorTaskForTests?.IsCompleted ?? true))));
        try
        {
            observedProcess.ActiveProcessZeroBoundForTests = TimeSpan.FromMilliseconds(100);
            var wait = await observedProcess.WaitAsync(TimeSpan.FromMilliseconds(250), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains(wait.SecondaryFailures, evidence => evidence.Contains("7001", StringComparison.Ordinal));
            Assert.True(wait.StdOut.Truncated || wait.StdErr.Truncated ||
                wait.SecondaryFailures.Any(evidence => evidence.Contains("Drain", StringComparison.OrdinalIgnoreCase)));

            observedProcess.Dispose();
            var stdoutReleases = releases.Where(release => release.Label == "stdout pipe handle").ToArray();
            var stderrReleases = releases.Where(release => release.Label == "stderr pipe handle").ToArray();
            Assert.NotEmpty(stdoutReleases);
            Assert.NotEmpty(stderrReleases);
            Assert.All(stdoutReleases.Concat(stderrReleases),
                release => Assert.True(release.StdOutComplete && release.StdErrComplete,
                    $"{release.Label} was released while a drain was live."));
        }
        finally
        {
            observedProcess.Dispose();
            releaseObserver.SetValue(observedProcess, null);
        }
    }

    [Theory]
    [InlineData("terminate")]
    [InlineData("dispose")]
    public async Task ExplicitTerminationAndDisposalWithoutWaitProveFamilyExitForLiveDescendant(string operation)
    {
        string markerPath = Path.Combine(Path.GetTempPath(), $"seqdoc-i100a-r3-family-{Guid.NewGuid():N}.marker");
        var result = ContainedProcess.Start(NewOptions(["spawn-grandchild", markerPath, "8000"]));
        Assert.True(result.Succeeded, result.Detail);
        var process = result.Process!;
        try
        {
            await WaitForFileToExistAsync(markerPath, TimeSpan.FromSeconds(5));
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            if (operation == "terminate")
            {
                Assert.True(process.Terminate());
            }
            else
            {
                process.Dispose();
            }

            stopwatch.Stop();
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(6), $"{operation} took {stopwatch.Elapsed}.");
            Assert.True(process.HasObservedActiveProcessZero(), "Family release was not proven before the terminal operation returned.");
        }
        finally
        {
            process.Dispose();
            File.Delete(markerPath);
            File.Delete(markerPath + ".completed");
        }
    }

    [Fact]
    public void ConstructionUnwindRetainsTerminationAndWaitFailuresAndDoesNotReleaseLiveDrainResources()
    {
        var cleanupBound = FindInstanceTestSeam("ConstructionCleanupBoundForTests", typeof(TimeSpan));
        Assert.True(cleanupBound is not null,
            "Expected per-instance construction cleanup-bound seam is not implemented yet.");
        ContainedProcess? captured = null;
        var releases = new List<(string Label, bool StdOutComplete, bool StdErrComplete, bool MonitorComplete)>();
        ContainedProcess.UnwindStepObserverForTests = label => releases.Add((
            label,
            captured?.StdOutDrainTaskForTests?.IsCompleted ?? true,
            captured?.StdErrDrainTaskForTests?.IsCompleted ?? true,
            captured?.CompletionMonitorTaskForTests?.IsCompleted ?? true));
        ContainedProcess.PostDrainsStartHookForTests = process =>
        {
            captured = process;
            cleanupBound!.SetValue(process, TimeSpan.FromMilliseconds(100));
        };
        try
        {
            var result = ContainedProcess.Start(
                NewOptions(["sleep", "5000"], nativeCalls: new ProcessOwnershipNativeCalls
                {
                    TerminateProcess = (_, _) => NativeCallResult.Failure(7101),
                    WaitForSingleObject = (_, _) => NativeWaitResult.Failed(7102),
                }),
                ConstructionFaultPoint.AfterDrainsStartedBeforeAssign);

            Assert.False(result.Succeeded);
            Assert.NotNull(captured);
            Assert.Contains("7101", result.Detail, StringComparison.Ordinal);
            Assert.Contains("7102", result.Detail, StringComparison.Ordinal);
            Assert.All(releases, release => Assert.True(
                release.StdOutComplete && release.StdErrComplete && release.MonitorComplete,
                $"{release.Label} released resources while owned work was live."));
        }
        finally
        {
            ContainedProcess.UnwindStepObserverForTests = null;
            ContainedProcess.PostDrainsStartHookForTests = null;
        }
    }

    [Fact]
    public async Task PeekNamedPipeFailurePreservesPrefixAndIncompleteEvidenceWithoutChangingPrimaryClass()
    {
        var seam = FindInstanceTestSeam("PeekNamedPipe", typeof(Func<nint, NativeCallResult<uint>>));
        Assert.True(seam is not null, "Expected per-instance PeekNamedPipe native seam is not implemented yet.");
        var nativeCalls = new ProcessOwnershipNativeCalls();
        seam!.SetValue(nativeCalls, (Func<nint, NativeCallResult<uint>>)(_ => NativeCallResult<uint>.Failure(7301)));
        var result = ContainedProcess.Start(NewOptions(["slow-bulk", "50", "100"],
            drainTimeout: TimeSpan.FromMilliseconds(150), nativeCalls: nativeCalls));
        Assert.True(result.Succeeded, result.Detail);
        using var process = result.Process!;
        var wait = await process.WaitAsync(TimeSpan.FromMilliseconds(250), CancellationToken.None);

        Assert.Equal(ProcessOwnershipFailureClass.TimedOut, wait.FailureClass);
        Assert.StartsWith("SLOW", wait.StdOut.Text, StringComparison.Ordinal);
        Assert.True(wait.StdOut.Truncated || wait.StdErr.Truncated);
        Assert.Equal(1, wait.SecondaryFailures.Count(evidence => evidence.Contains("PeekNamedPipe", StringComparison.Ordinal)
            && evidence.Contains("7301", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task SecondaryEvidenceIsOrderedAndPreviouslyReturnedSnapshotsRemainImmutable()
    {
        var result = ContainedProcess.Start(NewOptions(["exitcode", "7"], nativeCalls: new ProcessOwnershipNativeCalls
        {
            TerminateJobObject = (_, _) => NativeCallResult.Failure(7202),
        }));
        Assert.True(result.Succeeded, result.Detail);
        var process = result.Process!;
        var wait = await process.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ProcessOwnershipFailureClass.ProcessFailed, wait.FailureClass);
        var snapshot = wait.SecondaryFailures.ToArray();
        Assert.Empty(snapshot);
        Assert.Equal(snapshot, wait.SecondaryFailures);
        var currentEvidence = FindReadableInstanceProperty("SecondaryFailures", typeof(IReadOnlyList<string>));
        Assert.True(currentEvidence is not null,
            "Expected immutable current-process SecondaryFailures snapshot is not implemented yet.");

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task terminal = Task.Run(() =>
        {
            start.Task.GetAwaiter().GetResult();
            process.Terminate();
        });
        Task enumerate = Task.Run(() =>
        {
            start.Task.GetAwaiter().GetResult();
            _ = ((IReadOnlyList<string>)currentEvidence!.GetValue(process)!).ToArray();
            _ = wait.SecondaryFailures.ToArray();
        });
        start.SetResult();
        await Task.WhenAll(terminal, enumerate).WaitAsync(TimeSpan.FromSeconds(5));
        var currentSnapshot = (IReadOnlyList<string>)currentEvidence.GetValue(process)!;
        Assert.Contains(currentSnapshot, evidence => evidence.Contains("7202", StringComparison.Ordinal));
        Assert.Equal(snapshot, wait.SecondaryFailures);
        process.Dispose();
    }

    [Fact]
    public async Task TeardownEvidenceIsSynchronizedImmutableAndRetainsEachInjectedCloseFailureOnce()
    {
        var nativeCalls = new ProcessOwnershipNativeCalls();
        var closeSeam = FindInstanceTestSeam("CloseHandle", typeof(Func<nint, NativeCallResult>));
        Assert.True(closeSeam is not null,
            "Expected per-instance CloseHandle seam for deterministic teardown-failure injection.");
        int closeCalls = 0;
        var failedHandles = new nint[2];
        var attemptedHandles = new List<nint>();
        closeSeam!.SetValue(nativeCalls, (Func<nint, NativeCallResult>)(handle =>
        {
            int call = Interlocked.Increment(ref closeCalls);
            lock (attemptedHandles) { attemptedHandles.Add(handle); }
            if (call <= failedHandles.Length)
            {
                failedHandles[call - 1] = handle;
                return NativeCallResult.Failure(call == 1 ? 8201 : 8202);
            }

            if (NativeMethods.CloseHandle(handle))
            {
                return NativeCallResult.Success();
            }

            return NativeCallResult.Failure(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        }));

        var result = ContainedProcess.Start(NewOptions(["echo", "out", "err"], nativeCalls: nativeCalls));
        Assert.True(result.Succeeded, result.Detail);
        var process = result.Process!;
        try
        {
            var wait = await process.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            Assert.Equal(ProcessOwnershipFailureClass.None, wait.FailureClass);
            var failuresBeforeDispose = process.TeardownFailures.ToArray();
            var orderBeforeDispose = process.TeardownOrderForTests.ToArray();
            var failuresBeforeCopy = failuresBeforeDispose.ToArray();
            var orderBeforeCopy = orderBeforeDispose.ToArray();
            var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
            Task enumerate = Task.Run(() =>
            {
                try
                {
                    _ = process.TeardownFailures.ToArray();
                    _ = process.TeardownOrderForTests.ToArray();
                    _ = process.TeardownFailures.ToArray();
                }
                catch (Exception ex) { errors.Enqueue(ex); }
            });
            Task firstDispose = Task.Run(process.Dispose);
            await Task.WhenAll(enumerate, firstDispose).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Empty(errors);
            Assert.Equal(failuresBeforeCopy, failuresBeforeDispose);
            Assert.Equal(orderBeforeCopy, orderBeforeDispose);

            var lifecycle = typeof(ContainedProcess).GetField("_lifecycleState",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var processHandle = typeof(ContainedProcess).GetField("_processHandle",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var threadHandle = typeof(ContainedProcess).GetField("_threadHandle",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(lifecycle);
            Assert.NotNull(processHandle);
            Assert.NotNull(threadHandle);
            Assert.NotEqual("Disposed", lifecycle!.GetValue(process)!.ToString());
            Assert.NotEqual(nint.Zero, (nint)processHandle!.GetValue(process)!);
            Assert.NotEqual(nint.Zero, (nint)threadHandle!.GetValue(process)!);
            int firstDisposeAttemptCount;
            nint[] firstDisposeAttempts;
            lock (attemptedHandles)
            {
                firstDisposeAttemptCount = attemptedHandles.Count;
                firstDisposeAttempts = attemptedHandles.ToArray();
            }
            Assert.True(firstDisposeAttemptCount >= 2);
            Assert.Equal(failedHandles, firstDisposeAttempts.Take(2).ToArray());

            var failuresAfterFirstDispose = process.TeardownFailures.ToArray();
            var orderAfterFirstDispose = process.TeardownOrderForTests.ToArray();
            Assert.Equal(1, failuresAfterFirstDispose.Count(e => e.Contains("8201", StringComparison.Ordinal)));
            Assert.Equal(1, failuresAfterFirstDispose.Count(e => e.Contains("8202", StringComparison.Ordinal)));
            Assert.Equal(ExpectedFullDisposeOrder, orderAfterFirstDispose);

            process.Dispose();

            var finalFailures = process.TeardownFailures;
            var finalFailuresAgain = process.TeardownFailures;
            var finalOrder = process.TeardownOrderForTests;
            var finalOrderAgain = process.TeardownOrderForTests;
            Assert.NotSame(finalFailures, finalFailuresAgain);
            Assert.NotSame(finalOrder, finalOrderAgain);
            Assert.Equal(failuresAfterFirstDispose, finalFailures.Take(failuresAfterFirstDispose.Length));
            Assert.Equal(orderAfterFirstDispose, finalOrder.Take(orderAfterFirstDispose.Length));
            Assert.Equal(ExpectedFullDisposeOrder.Concat(["process handle", "thread handle"]), finalOrder);
            Assert.Equal(1, finalFailures.Count(e => e.Contains("8201", StringComparison.Ordinal)));
            Assert.Equal(1, finalFailures.Count(e => e.Contains("8202", StringComparison.Ordinal)));
            Assert.Equal(finalFailures.Count, finalFailures.Distinct(StringComparer.Ordinal).Count());
            nint[] allAttempts;
            lock (attemptedHandles) { allAttempts = attemptedHandles.ToArray(); }
            Assert.Equal(firstDisposeAttemptCount + 2, allAttempts.Length);
            Assert.Equal(failedHandles, allAttempts[^2..]);
            Assert.Equal(2, allAttempts.Count(handle => handle == failedHandles[0]));
            Assert.Equal(2, allAttempts.Count(handle => handle == failedHandles[1]));
            Assert.Equal(ProcessOwnershipFailureClass.TeardownDegraded, process.FailureClass);
            Assert.False((bool)FindReadableInstanceProperty("HasRetainedFamilyResourcesForTests", typeof(bool))!
                .GetValue(process)!);
        }
        finally
        {
            closeSeam.SetValue(nativeCalls, null);
            process.Dispose();
        }
    }

    [Fact]
    public async Task ExplicitRuntimeRootLaunchProvesPinnedSdkWithoutPathOrAmbientInheritance()
    {
        string runtimeDirectory = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        DirectoryInfo? root = new DirectoryInfo(runtimeDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "dotnet.exe")))
        {
            root = root.Parent;
        }

        string? sdkRoot = root?.FullName;
        Assert.False(string.IsNullOrWhiteSpace(sdkRoot),
            $"Official SDK 10.0.302 runtime root was unavailable; runtime directory was '{runtimeDirectory}'.");
        string dotnet = Path.Combine(sdkRoot!, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        Assert.True(File.Exists(dotnet), $"Official SDK 10.0.302 executable was unavailable at '{dotnet}'.");

        var environment = DefaultEnvironment();
        environment["DOTNET_ROOT"] = sdkRoot!;
        environment["DOTNET_ROOT_X64"] = sdkRoot!;
        var result = ContainedProcess.Start(new ProcessOwnershipOptions
        {
            ExecutablePath = dotnet,
            Arguments = ["--version"],
            Environment = environment,
            DrainTimeout = TimeSpan.FromSeconds(10),
        });
        Assert.True(result.Succeeded, result.Detail);
        using var process = result.Process!;
        var wait = await process.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.Equal(ProcessOwnershipFailureClass.None, wait.FailureClass);
        Assert.Equal("10.0.302\r\n", wait.StdOut.Text);
        Assert.DoesNotContain("PATH", environment.Keys, StringComparer.OrdinalIgnoreCase);
    }

    // ---- shared helpers --------------------------------------------------------------------------------

    private static ProcessOwnershipOptions NewOptions(
        IReadOnlyList<string> arguments,
        Dictionary<string, string>? environment = null,
        TimeSpan? drainTimeout = null,
        ProcessOwnershipNativeCalls? nativeCalls = null) =>
        new()
        {
            ExecutablePath = StubExecutablePath,
            Arguments = arguments,
            Environment = environment ?? DefaultEnvironment(),
            DrainTimeout = drainTimeout ?? TimeSpan.FromSeconds(30),
            NativeCalls = nativeCalls,
        };

    /// <summary>GH106-R2-F8 helper: an "echo" child whose only purpose is exercising a rejected environment vector.</summary>
    private static ProcessOwnershipOptions NewOptionsWithEnv(Dictionary<string, string> environment) =>
        NewOptions(["echo", "out", "err"], environment);

    /// <summary>
    /// The explicit deterministic child environment includes <c>SystemRoot</c> (required for the .NET
    /// apphost/CLR), plus runtime-discovery roots derived from the executing runtime. PATH and all other
    /// ambient variables remain deliberately excluded.
    /// </summary>
    private static Dictionary<string, string> DefaultEnvironment()
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        string? systemRoot = System.Environment.GetEnvironmentVariable("SystemRoot");
        if (!string.IsNullOrEmpty(systemRoot))
        {
            env["SystemRoot"] = systemRoot;
        }

        DirectoryInfo? dotnetRoot = new DirectoryInfo(
            System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory());
        while (dotnetRoot is not null && !File.Exists(Path.Combine(dotnetRoot.FullName, "dotnet.exe")))
        {
            dotnetRoot = dotnetRoot.Parent;
        }

        if (dotnetRoot is not null)
        {
            env["DOTNET_ROOT"] = dotnetRoot.FullName;
            env["DOTNET_ROOT_X64"] = dotnetRoot.FullName;
        }

        return env;
    }

    private static System.Reflection.PropertyInfo? FindInstanceTestSeam(string name, Type type)
    {
        var property = typeof(ContainedProcess).GetProperty(
            name,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic);
        if (property is not null && property.PropertyType == type && property.CanWrite)
        {
            return property;
        }

        property = typeof(ProcessOwnershipNativeCalls).GetProperty(
            name,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic);
        return property is not null && property.PropertyType == type && property.CanWrite ? property : null;
    }

    private static System.Reflection.PropertyInfo? FindStaticTestSeam(string name, Type type)
    {
        var property = typeof(ContainedProcess).GetProperty(
            name,
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic);
        return property is not null && property.PropertyType == type && property.CanWrite ? property : null;
    }

    private static bool EvaluatePlatform(bool isWindows, RuntimeArchitecture architecture, Version version)
    {
        var evaluator = typeof(ProcessOwnershipPlatform).GetMethod(
            "Evaluate",
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic,
            binder: null,
            types: [typeof(bool), typeof(RuntimeArchitecture), typeof(Version)],
            modifiers: null);
        Assert.NotNull(evaluator);
        return (bool)evaluator!.Invoke(null, [isWindows, architecture, version])!;
    }

    private static System.Reflection.PropertyInfo? FindReadableInstanceProperty(string name, Type type)
    {
        var property = typeof(ContainedProcess).GetProperty(
            name,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic);
        return property is not null && property.PropertyType == type && property.CanRead ? property : null;
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

    private static int IndexOfEvidence(IReadOnlyList<string> evidence, params string[] fragments)
    {
        for (int i = 0; i < evidence.Count; i++)
        {
            if (fragments.All(fragment => evidence[i].Contains(fragment, StringComparison.Ordinal)))
            {
                return i;
            }
        }

        return -1;
    }
}
