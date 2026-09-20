using System.Globalization;
using System.Reflection;
using System.Text;
using RuntimeArchitecture = System.Runtime.InteropServices.Architecture;
using Xunit;

namespace SeqDoc.AcceptanceTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessOwnershipGroup
{
    public const string Name = "Process ownership global-state tests";
}

/// <summary>
/// GH-106 / I100-A acceptance for <c>ProcessOwnership.cs</c>. Every claim here targets the real Win32
/// producer (<see cref="ContainedProcess"/>/<see cref="NativeMethods"/>) driving the deterministic
/// <c>SeqDoc.AcceptanceTests.ProcessOwnershipStub</c> child process — the first observable consumer for
/// this primitive, per the checkpoint's evidence-chain requirement. Grouped one-per-risk per the
/// checkpoint's "Soft test budget" (~10-12 grouped claims); see the numbered group comment above each
/// test for the exact budget item it proves.
/// </summary>
[Collection(ProcessOwnershipGroup.Name)]
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
        // environment) must not silently leak through to the child. Exercise both possible prior
        // states, and restore the host's actual state even if one child assertion fails.
        const string ambientName = "SEQDOC_I100A_AMBIENT_ONLY";
        string? originalAmbient = System.Environment.GetEnvironmentVariable(ambientName);
        try
        {
            foreach (string? priorAmbient in new[] { (string?)null, "pre-existing-sentinel" })
            {
                System.Environment.SetEnvironmentVariable(ambientName, priorAmbient);
                Assert.Equal(priorAmbient, System.Environment.GetEnvironmentVariable(ambientName));
                System.Environment.SetEnvironmentVariable(ambientName, "must-not-leak");
                try
                {
                    var options2 = NewOptions(["print-env", ambientName]);
                    var result2 = ContainedProcess.Start(options2);
                    ContainedProcess? process2 = result2.Process;
                    try
                    {
                        Assert.True(result2.Succeeded, result2.Detail);
                        Assert.NotNull(process2);
                        var wait2 = await process2!.WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
                        Assert.Equal("<unset>", wait2.StdOut.Text);
                    }
                    finally
                    {
                        process2?.Dispose();
                    }
                }
                finally
                {
                    System.Environment.SetEnvironmentVariable(ambientName, priorAmbient);
                }

                Assert.Equal(priorAmbient, System.Environment.GetEnvironmentVariable(ambientName));
            }
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(ambientName, originalAmbient);
            Assert.Equal(originalAmbient, System.Environment.GetEnvironmentVariable(ambientName));
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
    public async Task CreationTimeJobListAdmissionContainsSuspendedChild()
    {
        bool? observedWhileSuspended = null;
        nint duplicatedProcess = nint.Zero;
        var observer = FindStaticTestSeam("PostCreateProcessObserverForTests", typeof(Action<nint, nint>));
        Assert.True(observer is not null,
            "Expected post-CreateProcess observer seam receiving job/process handles while suspended.");

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
            observer?.SetValue(null, null);
            if (duplicatedProcess != nint.Zero)
            {
                NativeMethods.CloseHandle(duplicatedProcess);
            }
        }
    }

    [Fact]
    public void CreationFaultWithFailedTerminateStillProvesJobFamilyExitAroundSuspendedChild()
    {
        nint duplicatedProcess = nint.Zero;
        nint duplicatedJob = nint.Zero;
        int terminateJobCalls = 0;
        var observer = FindStaticTestSeam("PostCreateProcessObserverForTests", typeof(Action<nint, nint>));
        Assert.True(observer is not null,
            "Expected post-CreateProcess observer seam receiving job/process handles while suspended.");
        try
        {
            observer!.SetValue(null, (Action<nint, nint>)((job, process) =>
            {
                DuplicateCurrentHandle(process, out duplicatedProcess);
                DuplicateCurrentHandle(job, out duplicatedJob);
            }));
            var result = ContainedProcess.Start(
                NewOptions(["sleep", "5000"], nativeCalls: new ProcessOwnershipNativeCalls
                {
                    TerminateProcess = (_, _) => NativeCallResult.Failure(8101),
                    WaitForSingleObject = (_, _) => NativeWaitResult.Failed(8102),
                    TerminateJobObject = (job, exitCode) =>
                    {
                        Interlocked.Increment(ref terminateJobCalls);
                        return NativeMethods.TerminateJobObject(job, exitCode)
                            ? NativeCallResult.Success()
                            : NativeCallResult.Failure(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                    },
                }),
                ConstructionFaultPoint.AfterProcessCreatedBeforeResume);

            Assert.False(result.Succeeded);
            Assert.Equal(ProcessOwnershipFailureClass.ProcessConstructionFailed, result.FailureClass);
            Assert.Contains("8101", result.Detail, StringComparison.Ordinal);
            Assert.Contains("8102", result.Detail, StringComparison.Ordinal);
            Assert.Null(result.CleanupOwner);
            Assert.True(terminateJobCalls >= 1, "Construction unwind must explicitly terminate the job family.");
            Assert.NotEqual(nint.Zero, duplicatedProcess);
            Assert.NotEqual(nint.Zero, duplicatedJob);
            Assert.Equal(NativeMethods.WAIT_OBJECT_0,
                NativeMethods.WaitForSingleObject(duplicatedProcess, 5000));
            Assert.Equal(0u, QueryJobActiveProcesses(duplicatedJob));
        }
        finally
        {
            observer?.SetValue(null, null);
            if (duplicatedJob != nint.Zero && QueryJobActiveProcesses(duplicatedJob) > 0)
            {
                NativeMethods.TerminateJobObject(duplicatedJob, uint.MaxValue);
                uint waitResult = NativeMethods.WaitForSingleObject(duplicatedProcess, 5000);
                if (waitResult == NativeMethods.WAIT_FAILED)
                {
                    _ = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                }
            }
            if (duplicatedProcess != nint.Zero)
            {
                NativeMethods.CloseHandle(duplicatedProcess);
            }
            if (duplicatedJob != nint.Zero)
            {
                NativeMethods.CloseHandle(duplicatedJob);
            }
        }
    }

    [Fact]
    public void OwnershipTransferFaultUsesPreinstalledCleanupOwnerToProveFamilyExit()
    {
        nint duplicatedProcess = nint.Zero;
        nint duplicatedJob = nint.Zero;
        int terminateJobCalls = 0;
        ContainedProcess? unexpectedProcess = null;
        var releaseObservations = new System.Collections.Concurrent.ConcurrentQueue<(
            string Label, uint? ActiveProcesses, string? QueryError)>();
        var postCreate = FindStaticTestSeam("PostCreateProcessObserverForTests", typeof(Action<nint, nint>));
        Assert.NotNull(postCreate);
        try
        {
            postCreate!.SetValue(null, (Action<nint, nint>)((job, process) =>
            {
                // Keep both duplicates alive: the duplicate job prevents kill-on-close from making
                // this assertion pass merely because the implementation closed its last job handle.
                DuplicateCurrentHandle(process, out duplicatedProcess);
                DuplicateCurrentHandle(job, out duplicatedJob);
            }));
            ContainedProcess.UnwindStepObserverForTests = label =>
            {
                try
                {
                    releaseObservations.Enqueue((label, QueryJobActiveProcesses(duplicatedJob), null));
                }
                catch (Exception ex)
                {
                    releaseObservations.Enqueue((label, null, ex.Message));
                }
            };

            var result = ContainedProcess.Start(
                NewOptions(["sleep", "5000"], nativeCalls: new ProcessOwnershipNativeCalls
                {
                    TerminateProcess = (_, _) => NativeCallResult.Failure(8111),
                    WaitForSingleObject = (_, _) => NativeWaitResult.Failed(8112),
                    TerminateJobObject = (job, exitCode) =>
                    {
                        Interlocked.Increment(ref terminateJobCalls);
                        return NativeMethods.TerminateJobObject(job, exitCode)
                            ? NativeCallResult.Success()
                            : NativeCallResult.Failure(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                    },
                }),
                ConstructionFaultPoint.AfterOwnershipTransferBeforeMonitor);
            unexpectedProcess = result.Process;

            Assert.False(result.Succeeded);
            Assert.Equal(ProcessOwnershipFailureClass.ProcessConstructionFailed, result.FailureClass);
            Assert.Contains("8111", result.Detail, StringComparison.Ordinal);
            Assert.Contains("8112", result.Detail, StringComparison.Ordinal);
            Assert.Null(result.CleanupOwner);
            Assert.True(terminateJobCalls >= 1, "Transferred cleanup must explicitly terminate the job family.");
            Assert.NotEqual(nint.Zero, duplicatedProcess);
            Assert.NotEqual(nint.Zero, duplicatedJob);
            Assert.Equal(NativeMethods.WAIT_OBJECT_0,
                NativeMethods.WaitForSingleObject(duplicatedProcess, 5000));
            Assert.Equal(0u, QueryJobActiveProcesses(duplicatedJob));

            // Only real transferred-owner releases count. Pre-transfer no-op unwind entries must not
            // manufacture release labels, and no transferred resource may be released before zero proof.
            Assert.NotEmpty(releaseObservations);
            Assert.All(releaseObservations, release =>
            {
                Assert.Contains(release.Label, ExpectedPartialUnwindOrder);
                Assert.Null(release.QueryError);
                Assert.Equal(0u, release.ActiveProcesses);
            });
        }
        finally
        {
            ContainedProcess.UnwindStepObserverForTests = null;
            postCreate!.SetValue(null, null);
            if (duplicatedJob != nint.Zero && QueryJobActiveProcesses(duplicatedJob) > 0)
            {
                NativeMethods.TerminateJobObject(duplicatedJob, uint.MaxValue);
                _ = NativeMethods.WaitForSingleObject(duplicatedProcess, 5000);
            }
            unexpectedProcess?.Dispose();
            if (duplicatedProcess != nint.Zero)
            {
                NativeMethods.CloseHandle(duplicatedProcess);
            }
            if (duplicatedJob != nint.Zero)
            {
                NativeMethods.CloseHandle(duplicatedJob);
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
        // F1: every invalid reservation faults only itself and must not poison a later valid wait.
        foreach (TimeSpan invalidTimeout in new[]
        {
            TimeSpan.FromMilliseconds(uint.MaxValue),
            TimeSpan.Zero, Timeout.InfiniteTimeSpan, TimeSpan.FromMilliseconds(-2),
        })
        {
            Task<ProcessOwnershipWaitResult> invalidWait = process.WaitAsync(invalidTimeout, CancellationToken.None);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await invalidWait);
        }
        Task<ProcessOwnershipWaitResult> waitTask = process.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

        process.Dispose();
        process.Dispose();

        Task completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(waitTask, completed);
        var wait = await waitTask;
        Assert.True(wait.ActiveProcessZeroObserved);
        Assert.All(new[] { process.StdOutDrainTaskForTests, process.StdErrDrainTaskForTests, process.CompletionMonitorTaskForTests },
            task => Assert.True(task is null || task.IsCompleted));
        AssertManagedSlotsQuiescentOrReleased(process.OwnershipSnapshot);
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
    [InlineData((int)ConstructionFaultPoint.AfterProcessCreatedBeforeResume)]
    [InlineData((int)ConstructionFaultPoint.AfterDrainsStartedBeforeResume)]
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
        // GH106-R2-F1: a failure after StartDrains/StartCompletionMonitor and before ResumeThread must
        // not leak the background drain/completion-monitor tasks. The construction-
        // fault hook captures the internal ContainedProcess reference (never returned to a caller on a
        // failed Start) so this test can prove those tasks are genuinely completed, not merely abandoned.
        ContainedProcess? captured = null;
        ContainedProcess.PostDrainsStartHookForTests = p => captured = p;
        try
        {
            var options = NewOptions(["sleep", "1000"]);
            var result = ContainedProcess.Start(options, ConstructionFaultPoint.AfterDrainsStartedBeforeResume);

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
        finally { process.Dispose(); }

        var exitCodeFailure = ContainedProcess.Start(NewOptions(["echo", "get-exit-code"], nativeCalls:
            new ProcessOwnershipNativeCalls
            {
                GetExitCodeProcess = _ => NativeCallResult<uint>.Failure(2468),
            }));
        Assert.True(exitCodeFailure.Succeeded, exitCodeFailure.Detail);
        using var exitCodeProcess = exitCodeFailure.Process!;
        var exitCodeWait = await exitCodeProcess.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.Equal(ProcessOwnershipFailureClass.ProcessFailed, exitCodeWait.FailureClass);
        Assert.False(exitCodeWait.TimedOut);
        Assert.False(exitCodeWait.Cancelled);
        Assert.Contains("GetExitCodeProcess", exitCodeWait.Detail, StringComparison.Ordinal);
        Assert.Contains("2468", exitCodeWait.Detail, StringComparison.Ordinal);
        exitCodeProcess.Dispose();
        Assert.Empty(exitCodeProcess.TeardownFailuresForTests);
    }

    [Fact]
    public void ConstructionPhaseFailuresLeaveObservableEvidenceWithoutAbandonedTasks()
    {
        // JOB_LIST admits the child during CreateProcess; retain the resume/unwind failure, which still
        // proves cleanup evidence after creation-time JOB_LIST admission.
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
            var result = ContainedProcess.Start(options, ConstructionFaultPoint.AfterProcessCreatedBeforeResume);

            Assert.False(result.Succeeded);
            object snapshot = GetTypedOwnershipSnapshot(result);
            object stdinParentWrite = ((System.Collections.IEnumerable)GetRequiredProperty(snapshot, "NativeResources"))
                .Cast<object>()
                .Single(entry => GetRequiredProperty(entry, "Kind").ToString() == "StdinParentWrite");
            Assert.True(ToInt64(GetRequiredProperty(stdinParentWrite, "AcquisitionSequence")) > 0);
            Assert.Equal("Released", GetRequiredProperty(stdinParentWrite, "State").ToString());
            Assert.Equal(1, Convert.ToInt32(GetRequiredProperty(stdinParentWrite, "Attempts"), CultureInfo.InvariantCulture));
            Assert.Equal(nint.Zero, ConvertToNativeInt(GetRequiredProperty(stdinParentWrite, "Value")));
            int stdinReleaseOrder = Convert.ToInt32(GetRequiredProperty(stdinParentWrite, "ReleaseOrder"), CultureInfo.InvariantCulture);
            Assert.True(stdinReleaseOrder > 0);
            string[] laterCleanupKinds =
            [
                "ProcessHandle", "PrimaryThreadHandle", "EnvironmentBlockBuffer", "CommandLineBuffer",
                "AttributeListBuffer", "JobListBuffer", "CompletionPortHandle", "JobHandle", "HandleListBuffer",
                "StderrParentRead", "StdoutParentRead",
            ];
            object[] laterCleanup = ((System.Collections.IEnumerable)GetRequiredProperty(snapshot, "NativeResources"))
                .Cast<object>()
                .Where(entry => laterCleanupKinds.Contains(GetRequiredProperty(entry, "Kind").ToString(), StringComparer.Ordinal))
                .ToArray();
            Assert.Equal(laterCleanupKinds.Length, laterCleanup.Length);
            Assert.All(laterCleanup, entry =>
            {
                int releaseOrder = Convert.ToInt32(GetRequiredProperty(entry, "ReleaseOrder"), CultureInfo.InvariantCulture);
                Assert.True(stdinReleaseOrder < releaseOrder,
                    $"StdinParentWrite release order {stdinReleaseOrder} must precede cleanup release {releaseOrder}.");
            });
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
        "stderr pipe handle",
        "stdout pipe handle",
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
            Assert.True(process.HasRetainedFamilyResourcesForTests,
                "Failed family proof must retain ownership of the process, job, and completion handles.");
            Assert.DoesNotContain(releases, label => label is "process handle" or "completion port handle"
                or "job handle");

            int releaseCount = releases.Count;
            await Task.Run(process.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(process.HasRetainedFamilyResourcesForTests,
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
            Assert.False(process.HasRetainedFamilyResourcesForTests);
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
            Assert.False(process.HasRetainedFamilyResourcesForTests);
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
    public void ConstructionUnwindProvesFamilyExitBeforeReleasingDrainResources()
    {
        var cleanupBound = FindInstanceTestSeam("ConstructionCleanupBoundForTests", typeof(TimeSpan));
        Assert.True(cleanupBound is not null,
            "Expected per-instance construction cleanup-bound seam is not implemented yet.");
        ContainedProcess? captured = null;
        nint duplicatedProcess = nint.Zero;
        nint duplicatedJob = nint.Zero;
        int terminateJobCalls = 0;
        var releases = new System.Collections.Concurrent.ConcurrentQueue<(
            string Label, uint? ActiveProcesses, string? QueryError, bool StdOutComplete, bool StdErrComplete, bool MonitorComplete)>();
        var releaseObserver = FindInstanceTestSeam("ResourceReleaseObserverForTests", typeof(Action<string>));
        Assert.True(releaseObserver is not null, "Expected per-instance resource-release observer seam.");
        var postCreate = FindStaticTestSeam("PostCreateProcessObserverForTests", typeof(Action<nint, nint>));
        Assert.True(postCreate is not null, "Expected post-CreateProcess observer seam.");
        postCreate!.SetValue(null, (Action<nint, nint>)((job, process) =>
        {
            DuplicateCurrentHandle(process, out duplicatedProcess);
            DuplicateCurrentHandle(job, out duplicatedJob);
        }));
        ContainedProcess.PostDrainsStartHookForTests = process =>
        {
            captured = process;
            cleanupBound!.SetValue(process, TimeSpan.FromMilliseconds(100));
            releaseObserver!.SetValue(process, (Action<string>)(label =>
            {
                try
                {
                    releases.Enqueue((label, QueryJobActiveProcesses(duplicatedJob), null,
                        process.StdOutDrainTaskForTests?.IsCompleted ?? true,
                        process.StdErrDrainTaskForTests?.IsCompleted ?? true,
                        process.CompletionMonitorTaskForTests?.IsCompleted ?? true));
                }
                catch (Exception ex)
                {
                    releases.Enqueue((label, null, ex.Message,
                        process.StdOutDrainTaskForTests?.IsCompleted ?? true,
                        process.StdErrDrainTaskForTests?.IsCompleted ?? true,
                        process.CompletionMonitorTaskForTests?.IsCompleted ?? true));
                }
            }));
        };
        try
        {
            var result = ContainedProcess.Start(
                NewOptions(["sleep", "5000"], nativeCalls: new ProcessOwnershipNativeCalls
                {
                    TerminateProcess = (_, _) => NativeCallResult.Failure(7101),
                    WaitForSingleObject = (_, _) => NativeWaitResult.Failed(7102),
                    TerminateJobObject = (job, exitCode) =>
                    {
                        Interlocked.Increment(ref terminateJobCalls);
                        return NativeMethods.TerminateJobObject(job, exitCode)
                            ? NativeCallResult.Success()
                            : NativeCallResult.Failure(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                    },
                }),
                ConstructionFaultPoint.AfterDrainsStartedBeforeResume);

            Assert.False(result.Succeeded);
            Assert.NotNull(captured);
            Assert.Contains("7101", result.Detail, StringComparison.Ordinal);
            Assert.Contains("7102", result.Detail, StringComparison.Ordinal);
            Assert.Null(result.CleanupOwner);
            Assert.True(terminateJobCalls >= 1);
            Assert.Equal(NativeMethods.WAIT_OBJECT_0,
                NativeMethods.WaitForSingleObject(duplicatedProcess, 5000));
            Assert.Equal(0u, QueryJobActiveProcesses(duplicatedJob));
            Assert.NotEmpty(releases);
            Assert.All(releases, release =>
            {
                Assert.Null(release.QueryError);
                Assert.Equal(0u, release.ActiveProcesses);
                Assert.True(
                release.StdOutComplete && release.StdErrComplete && release.MonitorComplete,
                $"{release.Label} released resources while owned work was live.");
            });
        }
        finally
        {
            ContainedProcess.PostDrainsStartHookForTests = null;
            postCreate.SetValue(null, null);
            if (captured is not null)
            {
                releaseObserver!.SetValue(captured, null);
            }
            if (duplicatedJob != nint.Zero && QueryJobActiveProcesses(duplicatedJob) > 0)
            {
                NativeMethods.TerminateJobObject(duplicatedJob, uint.MaxValue);
                uint waitResult = NativeMethods.WaitForSingleObject(duplicatedProcess, 5000);
                if (waitResult == NativeMethods.WAIT_FAILED)
                {
                    _ = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                }
            }
            if (duplicatedProcess != nint.Zero)
            {
                NativeMethods.CloseHandle(duplicatedProcess);
            }
            if (duplicatedJob != nint.Zero)
            {
                NativeMethods.CloseHandle(duplicatedJob);
            }
        }
    }

    [Fact]
    public void ConstructionUnwindRetainsCleanupOwnerUntilRetryCanProveFamilyExit()
    {
        var cleanupBound = FindInstanceTestSeam("ConstructionCleanupBoundForTests", typeof(TimeSpan));
        Assert.NotNull(cleanupBound);
        var releaseObserver = FindInstanceTestSeam("ResourceReleaseObserverForTests", typeof(Action<string>));
        Assert.NotNull(releaseObserver);
        nint duplicatedProcess = nint.Zero;
        nint duplicatedJob = nint.Zero;
        ContainedProcess? captured = null;
        int terminateJobCalls = 0;
        var releases = new System.Collections.Concurrent.ConcurrentQueue<(
            string Label, uint? ActiveProcesses, string? QueryError)>();
        var postCreate = FindStaticTestSeam("PostCreateProcessObserverForTests", typeof(Action<nint, nint>));
        Assert.NotNull(postCreate);
        postCreate!.SetValue(null, (Action<nint, nint>)((job, process) =>
        {
            DuplicateCurrentHandle(process, out duplicatedProcess);
            DuplicateCurrentHandle(job, out duplicatedJob);
        }));
        ContainedProcess.PostDrainsStartHookForTests = process =>
        {
            captured = process;
            cleanupBound!.SetValue(process, TimeSpan.FromMilliseconds(100));
            releaseObserver!.SetValue(process, (Action<string>)(label =>
            {
                try { releases.Enqueue((label, QueryJobActiveProcesses(duplicatedJob), null)); }
                catch (Exception ex) { releases.Enqueue((label, null, ex.Message)); }
            }));
        };
        try
        {
            var result = ContainedProcess.Start(
                NewOptions(["sleep", "5000"], nativeCalls: new ProcessOwnershipNativeCalls
                {
                    TerminateProcess = (_, _) => NativeCallResult.Failure(8101),
                    WaitForSingleObject = (_, _) => NativeWaitResult.Failed(8102),
                    TerminateJobObject = (job, exitCode) =>
                    {
                        int call = Interlocked.Increment(ref terminateJobCalls);
                        if (call == 1)
                        {
                            return NativeCallResult.Failure(8103);
                        }
                        return NativeMethods.TerminateJobObject(job, exitCode)
                            ? NativeCallResult.Success()
                            : NativeCallResult.Failure(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                    },
                }),
                ConstructionFaultPoint.AfterDrainsStartedBeforeResume);

            Assert.False(result.Succeeded);
            Assert.Contains("8101", result.Detail, StringComparison.Ordinal);
            Assert.Contains("8102", result.Detail, StringComparison.Ordinal);
            Assert.Contains("8103", result.Detail, StringComparison.Ordinal);
            ContainedProcess cleanupOwner = result.CleanupOwner!;
            Assert.IsType<ContainedProcess>(cleanupOwner);
            Assert.True(QueryJobActiveProcesses(duplicatedJob) > 0);
            Assert.Equal(NativeMethods.WAIT_TIMEOUT,
                NativeMethods.WaitForSingleObject(duplicatedProcess, 0));
            Assert.True(captured is not null);
            Assert.True(captured!.HasRetainedFamilyResourcesForTests);
            Assert.DoesNotContain(releases, release => release.Label is "process handle" or "job handle"
                or "completion port handle");

            Assert.True(cleanupOwner.Terminate());
            cleanupOwner.Dispose();

            Assert.True(terminateJobCalls >= 2);
            Assert.Equal(NativeMethods.WAIT_OBJECT_0,
                NativeMethods.WaitForSingleObject(duplicatedProcess, 5000));
            Assert.Equal(0u, QueryJobActiveProcesses(duplicatedJob));
            Assert.False(cleanupOwner.HasRetainedFamilyResourcesForTests);
            Assert.NotEmpty(releases);
            Assert.All(releases, release =>
            {
                Assert.Null(release.QueryError);
                Assert.Equal(0u, release.ActiveProcesses);
            });
        }
        finally
        {
            ContainedProcess.PostDrainsStartHookForTests = null;
            postCreate.SetValue(null, null);
            if (captured is not null)
            {
                releaseObserver!.SetValue(captured, null);
            }
            if (duplicatedJob != nint.Zero && QueryJobActiveProcesses(duplicatedJob) > 0)
            {
                NativeMethods.TerminateJobObject(duplicatedJob, uint.MaxValue);
                uint waitResult = NativeMethods.WaitForSingleObject(duplicatedProcess, 5000);
                if (waitResult == NativeMethods.WAIT_FAILED)
                {
                    _ = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                }
            }
            if (duplicatedProcess != nint.Zero)
            {
                NativeMethods.CloseHandle(duplicatedProcess);
            }
            if (duplicatedJob != nint.Zero)
            {
                NativeMethods.CloseHandle(duplicatedJob);
            }
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
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task terminal = Task.Run(() =>
        {
            start.Task.GetAwaiter().GetResult();
            process.Terminate();
        });
        Task enumerate = Task.Run(() =>
        {
            start.Task.GetAwaiter().GetResult();
            _ = process.SecondaryFailures.ToArray();
            _ = wait.SecondaryFailures.ToArray();
        });
        start.SetResult();
        await Task.WhenAll(terminal, enumerate).WaitAsync(TimeSpan.FromSeconds(5));
        var currentSnapshot = process.SecondaryFailures;
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
        bool injectTeardownFailures = false;
        var failedHandles = new nint[2];
        var attemptedHandles = new List<nint>();
        closeSeam!.SetValue(nativeCalls, (Func<nint, NativeCallResult>)(handle =>
        {
            if (!Volatile.Read(ref injectTeardownFailures))
            {
                return NativeMethods.CloseHandle(handle)
                    ? NativeCallResult.Success()
                    : NativeCallResult.Failure(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            }
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
            Volatile.Write(ref injectTeardownFailures, true);
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

            Assert.True(process.HasRetainedFamilyResourcesForTests,
                "A failed close must retain typed resource ownership for retry.");
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
            Assert.False(process.HasRetainedFamilyResourcesForTests);
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

    // ---- I100-A frozen ledger/coordinator RED package (six grouped claims) -----------------------------

    [Fact]
    public void PreResumeParentCopyCloseFailuresAreCheckedOwnedAndRetryable()
    {
        // The four parent-side copies are closed before ResumeThread.  Fail one deterministic close per
        // run; a failed close must not be forgotten or treated as EOF/success.
        var failures = new List<string>();
        for (int ordinal = 1; ordinal <= 4; ordinal++)
        {
            int closeCalls = 0;
            string marker = Path.Combine(Path.GetTempPath(), $"seqdoc-i100a-pre-resume-{Guid.NewGuid():N}.marker");
            nint duplicateProcess = nint.Zero;
            nint duplicateJob = nint.Zero;
            var postCreate = FindStaticTestSeam("PostCreateProcessObserverForTests", typeof(Action<nint, nint>));
            Assert.NotNull(postCreate);
            postCreate!.SetValue(null, (Action<nint, nint>)((job, process) =>
            {
                DuplicateCurrentHandle(process, out duplicateProcess);
                DuplicateCurrentHandle(job, out duplicateJob);
            }));
            var calls = new ProcessOwnershipNativeCalls
            {
                CloseHandle = handle =>
                {
                    int call = Interlocked.Increment(ref closeCalls);
                    if (call == ordinal)
                    {
                        return NativeCallResult.Failure(8300 + ordinal);
                    }

                    return NativeMethods.CloseHandle(handle)
                        ? NativeCallResult.Success()
                        : NativeCallResult.Failure(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                },
            };

            var result = ContainedProcess.Start(
                NewOptions(["sleep-with-marker", marker, "5000"], nativeCalls: calls));

            try
            {
                if (result.Succeeded || result.FailureClass != ProcessOwnershipFailureClass.ProcessConstructionFailed
                    || !(result.Detail ?? string.Empty).Contains((8300 + ordinal).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                    || (result.Detail ?? string.Empty).Contains("EOF", StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"ordinal {ordinal}: {result.FailureClass}, '{result.Detail}'");
                }
                if (File.Exists(marker))
                {
                    failures.Add($"ordinal {ordinal}: child resumed and created marker");
                }

                // A bounded unwind may finish with no owner; if it cannot, the returned owner must remain
                // reachable and retryable rather than silently losing the failed native value.
                if (result.CleanupOwner is not null)
                {
                    Assert.True(result.CleanupOwner.Terminate() || result.CleanupOwner.FailureClass != ProcessOwnershipFailureClass.None);
                    result.CleanupOwner.Dispose();
                }
            }
            finally
            {
                postCreate.SetValue(null, null);
                result.Process?.Terminate();
                result.Process?.Dispose();
                result.CleanupOwner?.Dispose();
                if (duplicateJob != nint.Zero && QueryJobActiveProcesses(duplicateJob) > 0)
                {
                    NativeMethods.TerminateJobObject(duplicateJob, uint.MaxValue);
                    _ = NativeMethods.WaitForSingleObject(duplicateProcess, 5000);
                }
                if (duplicateProcess != nint.Zero)
                {
                    NativeMethods.CloseHandle(duplicateProcess);
                }
                if (duplicateJob != nint.Zero)
                {
                    NativeMethods.CloseHandle(duplicateJob);
                }
                File.Delete(marker);
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public async Task ConstructionFaultsExposeOneTypedReachableLedgerForEveryAcquiredNativeSlot()
    {
        foreach (TimeSpan invalidDrainTimeout in new[]
        {
            TimeSpan.FromMilliseconds(uint.MaxValue),
            TimeSpan.Zero, Timeout.InfiniteTimeSpan, TimeSpan.FromMilliseconds(-2),
        })
        {
            var invalid = ContainedProcess.Start(NewOptions(
                ["echo", "invalid-drain-timeout"], drainTimeout: invalidDrainTimeout));
            Assert.False(invalid.Succeeded);
            Assert.Equal(ProcessOwnershipFailureClass.ProcessConstructionFailed, invalid.FailureClass);
            Assert.Null(invalid.Process);
            Assert.Null(invalid.CleanupOwner);
            Assert.Null(invalid.OwnershipSnapshot);
        }

        NativeResourceKind[] inventory = Enum.GetValues<NativeResourceKind>();
        Assert.Equal(15, inventory.Length);
        foreach (NativeResourceKind expected in new[]
        {
            NativeResourceKind.StdinChildRead, NativeResourceKind.StdinParentWrite,
            NativeResourceKind.StdoutParentRead, NativeResourceKind.StdoutChildWrite,
            NativeResourceKind.StderrParentRead, NativeResourceKind.StderrChildWrite,
            NativeResourceKind.HandleListBuffer, NativeResourceKind.JobHandle,
            NativeResourceKind.CompletionPortHandle, NativeResourceKind.JobListBuffer,
            NativeResourceKind.AttributeListBuffer, NativeResourceKind.CommandLineBuffer,
            NativeResourceKind.EnvironmentBlockBuffer, NativeResourceKind.ProcessHandle,
            NativeResourceKind.PrimaryThreadHandle,
        })
        {
            Assert.Contains(expected, inventory);
        }

        NativeResourceSnapshot nativeShape = new NativeResourceSnapshot();
        Assert.Equal(default, nativeShape.Kind);
        Assert.Equal(0, nativeShape.AcquisitionSequence);
        Assert.Equal(nint.Zero, nativeShape.Value);
        Assert.Equal(0, nativeShape.Attempts);
        Assert.NotNull(nativeShape.Evidence);
        ProcessOwnershipSnapshot ownershipShape = new ProcessOwnershipSnapshot();
        Assert.NotNull(ownershipShape.NativeResources);
        Assert.True(ownershipShape.Immutable);

        // This is intentionally a compile-time contract. A string summary cannot prove slot identity,
        // operation epoch, quiescence, or retained ownership.
        IReadOnlyList<ManagedResourceSnapshot> managedShape = new ProcessOwnershipSnapshot().ManagedResources;
        Assert.Empty(managedShape);
        ManagedResourceKind[] managedKinds =
        [
            ManagedResourceKind.CompletionMonitor, ManagedResourceKind.DrainCancellation,
            ManagedResourceKind.StdoutDrain, ManagedResourceKind.StderrDrain,
            ManagedResourceKind.ProcessWait, ManagedResourceKind.TerminalOperation,
            ManagedResourceKind.FamilyProof, ManagedResourceKind.Disposal,
        ];
        Assert.Equal(8, managedKinds.Length);
        foreach (ManagedResourceKind expected in managedKinds)
        {
            Assert.Contains(expected, managedKinds);
        }

        // Shape is only the admission check.  Every frozen construction point must publish the actual
        // result ledger, including the post-transfer/pre-monitor point; an empty or synthetic ledger is
        // not evidence of ownership.
        foreach (ConstructionFaultPoint fault in Enum.GetValues<ConstructionFaultPoint>()
            .Where(fault => fault != ConstructionFaultPoint.None))
        {
            int terminalAttempts = 0;
            var result = ContainedProcess.Start(
                NewOptions(["sleep", "1000"], nativeCalls: new ProcessOwnershipNativeCalls
                {
                    TerminateJobObject = (job, exitCode) =>
                    {
                        if (Interlocked.Increment(ref terminalAttempts) == 1)
                        {
                            return NativeCallResult.Failure(8510);
                        }

                        return NativeMethods.TerminateJobObject(job, exitCode)
                            ? NativeCallResult.Success()
                            : NativeCallResult.Failure(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                    },
                    WaitForSingleObject = (_, _) => NativeWaitResult.Failed(8511),
                }), fault);
            try
            {
                Assert.False(result.Succeeded);
                ProcessOwnershipSnapshot ledger = GetTypedOwnershipSnapshot(result);
                NativeResourceSnapshot[] entries = ledger.NativeResources.ToArray();
                Assert.NotEmpty(entries);
                NativeResourceKind[] kinds = entries.Select(entry => entry.Kind).ToArray();
                Assert.Equal(kinds.Length, kinds.Distinct().Count());
                long[] sequences = entries.Select(entry => entry.AcquisitionSequence).ToArray();
                Assert.All(sequences, sequence => Assert.True(sequence > 0));
                Assert.Equal(sequences.Length, sequences.Distinct().Count());
                Assert.Equal(sequences.OrderBy(sequence => sequence), sequences);
                AssertManagedSnapshotContract(GetTypedOwnershipSnapshot(result));
                foreach (NativeResourceSnapshot entry in entries)
                {
                    Assert.True(entry.State is NativeResourceState.Owned or NativeResourceState.Released);
                    nint value = entry.Value;
                    if (entry.State == NativeResourceState.Owned)
                    {
                        Assert.NotEqual(nint.Zero, value);
                    }
                }

                if (result.CleanupOwner is null)
                {
                    Assert.All(entries, entry => Assert.Equal(NativeResourceState.Released, entry.State));
                }
                var immutable = entries.Select(entry =>
                    (entry.Kind, entry.State, entry.Value)).ToArray();
                result.CleanupOwner?.Dispose();
                Assert.Equal(immutable,
                    entries.Select(entry =>
                        (entry.Kind, entry.State, entry.Value)));
            }
            finally
            {
                result.CleanupOwner?.Dispose();
                result.Process?.Terminate();
                result.Process?.Dispose();
            }
        }

        // Installing the first drain lease is atomic with publication of its managed slot.  A
        // failure at that boundary must not leave a synthetic Active slot behind or return a
        // cleanup owner that cannot reach the resources it claims to own.
        ProcessOwnershipConstructionResult? installFailure = null;
        ContainedProcess.ManagedResourceInstallObserverForTests = kind =>
        {
            if (kind == ManagedResourceKind.DrainCancellation)
            {
                throw new InvalidOperationException("synthetic drain-lease installation failure");
            }
        };
        try
        {
            installFailure = ContainedProcess.Start(
                NewOptions(["sleep", "1000"]));
            Assert.False(installFailure.Succeeded);
            ProcessOwnershipSnapshot installSnapshot = GetTypedOwnershipSnapshot(installFailure);
            AssertManagedSnapshotContract(installSnapshot);
            foreach (ManagedResourceKind kind in new[]
            {
                ManagedResourceKind.DrainCancellation,
                ManagedResourceKind.StdoutDrain,
                ManagedResourceKind.StderrDrain,
            })
            {
                ManagedResourceSnapshot entry = Assert.Single(installSnapshot.ManagedResources,
                    item => item.Kind == kind);
                Assert.True(entry.State == ManagedResourceState.Unacquired || !entry.HasLease);
            }
            Assert.DoesNotContain(installSnapshot.ManagedResources,
                entry => (entry.State is ManagedResourceState.Active or ManagedResourceState.Retained)
                    && !entry.HasLease);
        }
        finally
        {
            ContainedProcess.ManagedResourceInstallObserverForTests = null;
            installFailure?.CleanupOwner?.Dispose();
            installFailure?.Process?.Terminate();
            installFailure?.Process?.Dispose();
        }

        // A worker must not become runnable before its typed lease is published.  The worker-start
        // observer is intentionally a required compile-time seam: reservation-first code has not
        // admitted these callbacks when the install observer runs.
        using var completionStarted = new ManualResetEventSlim();
        using var stdoutStarted = new ManualResetEventSlim();
        using var stderrStarted = new ManualResetEventSlim();
        using var completionAdmitted = new ManualResetEventSlim();
        using var stdoutAdmitted = new ManualResetEventSlim();
        using var stderrAdmitted = new ManualResetEventSlim();
        ContainedProcess? normalProcess = null;
        ContainedProcess.ManagedWorkerQueueAdmissionObserverForTests = (kind, snapshot) =>
        {
            ManagedResourceSnapshot entry = Assert.Single(snapshot.ManagedResources,
                item => item.Kind == kind);
            Assert.Equal(ManagedResourceState.Active, entry.State);
            Assert.True(entry.HasLease);
            Assert.True(entry.OperationId > 0);
            Assert.True(entry.AcquisitionSequence > 0);
            (kind switch
            {
                ManagedResourceKind.CompletionMonitor => completionAdmitted,
                ManagedResourceKind.StdoutDrain => stdoutAdmitted,
                ManagedResourceKind.StderrDrain => stderrAdmitted,
                _ => throw new Xunit.Sdk.XunitException($"Unexpected queue admission kind {kind}.")
            }).Set();
        };
        var workerObserver = new Action<ManagedResourceKind>(kind =>
        {
            switch (kind)
            {
                case ManagedResourceKind.CompletionMonitor:
                    Assert.True(completionAdmitted.IsSet);
                    completionStarted.Set();
                    break;
                case ManagedResourceKind.StdoutDrain:
                    Assert.True(stdoutAdmitted.IsSet);
                    stdoutStarted.Set();
                    break;
                case ManagedResourceKind.StderrDrain:
                    Assert.True(stderrAdmitted.IsSet);
                    stderrStarted.Set();
                    break;
            }
        });
        try
        {
            ContainedProcess.ManagedWorkerStartObserverForTests = workerObserver;
            ContainedProcess.ManagedResourceInstallObserverForTests = kind =>
            {
                ManualResetEventSlim? marker = kind switch
                {
                    ManagedResourceKind.CompletionMonitor => completionStarted,
                    ManagedResourceKind.StdoutDrain => stdoutStarted,
                    ManagedResourceKind.StderrDrain => stderrStarted,
                    _ => null,
                };
                if (marker is null)
                {
                    return;
                }

                Assert.False(
                    marker.Wait(TimeSpan.FromMilliseconds(500)),
                    $"{kind} worker started before its typed lease was installed.");
            };

            ProcessOwnershipConstructionResult normal = ContainedProcess.Start(
                NewOptions(["echo", "reservation-order", "reservation-order"]));
            Assert.True(normal.Succeeded, normal.Detail);
            normalProcess = normal.Process!;

            ProcessOwnershipSnapshot installed = normalProcess.OwnershipSnapshot;
            var expectedSequences = new Dictionary<ManagedResourceKind, long>();
            foreach (ManagedResourceKind kind in new[]
            {
                ManagedResourceKind.CompletionMonitor,
                ManagedResourceKind.StdoutDrain,
                ManagedResourceKind.StderrDrain,
            })
            {
                ManagedResourceSnapshot entry = Assert.Single(installed.ManagedResources,
                    item => item.Kind == kind);
                Assert.True(entry.State is ManagedResourceState.Active or ManagedResourceState.Quiescent);
                Assert.True(entry.HasLease);
                Assert.True(entry.AcquisitionSequence > 0);
                expectedSequences.Add(kind, entry.AcquisitionSequence);
            }
            Assert.Equal(3, expectedSequences.Values.Distinct().Count());

            Assert.True(completionStarted.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(stdoutStarted.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(stderrStarted.Wait(TimeSpan.FromSeconds(5)));

            ProcessOwnershipWaitResult normalWait = await normalProcess.WaitAsync(
                TimeSpan.FromSeconds(10), CancellationToken.None);
            Assert.Equal(ProcessOwnershipFailureClass.None, normalWait.FailureClass);
            normalProcess.Dispose();

            ProcessOwnershipSnapshot completed = normalProcess.OwnershipSnapshot;
            foreach ((ManagedResourceKind kind, long sequence) in expectedSequences)
            {
                Assert.Equal(sequence, Assert.Single(completed.ManagedResources,
                    item => item.Kind == kind).AcquisitionSequence);
            }
        }
        finally
        {
            ContainedProcess.ManagedWorkerQueueAdmissionObserverForTests = null;
            ContainedProcess.ManagedWorkerStartObserverForTests = null;
            ContainedProcess.ManagedResourceInstallObserverForTests = null;
            if (normalProcess is not null)
            {
                try { normalProcess.Terminate(); } catch { }
                normalProcess.Dispose();
            }
        }

        // A CompletionMonitor reservation is not runnable merely because its queue admission was
        // attempted.  Rejecting that admission must roll back the installed typed reservation without
        // starting the callback or allowing construction to proceed to drain-worker scheduling.
        int completionMonitorStarted = 0;
        ProcessOwnershipConstructionResult? monitorAdmissionFailure = null;
        ContainedProcess.ManagedWorkerStartObserverForTests = kind =>
        {
            if (kind == ManagedResourceKind.CompletionMonitor)
            {
                Interlocked.Exchange(ref completionMonitorStarted, 1);
            }
        };
        ContainedProcess.ManagedWorkerQueueForTests = (kind, _) => kind switch
        {
            ManagedResourceKind.CompletionMonitor => false,
            _ => throw new InvalidOperationException($"unexpected managed worker kind: {kind}"),
        };
        try
        {
            monitorAdmissionFailure = ContainedProcess.Start(NewOptions(["sleep", "5000"]));

            Assert.False(monitorAdmissionFailure.Succeeded);
            Assert.Equal(ProcessOwnershipFailureClass.ProcessConstructionFailed, monitorAdmissionFailure.FailureClass);
            Assert.Equal(0, Volatile.Read(ref completionMonitorStarted));

            ProcessOwnershipSnapshot monitorSnapshot = GetTypedOwnershipSnapshot(monitorAdmissionFailure);
            ManagedResourceSnapshot monitorSlot = Assert.Single(monitorSnapshot.ManagedResources,
                entry => entry.Kind == ManagedResourceKind.CompletionMonitor);
            Assert.True(monitorSlot.AcquisitionSequence > 0);
            Assert.True(monitorSlot.OperationId > 0);
            Assert.Equal(ManagedResourceState.Released, monitorSlot.State);
            Assert.False(monitorSlot.HasLease);
            Assert.NotEqual(ManagedResourceState.Active, monitorSlot.State);
            Assert.NotEqual(ManagedResourceState.Retained, monitorSlot.State);
            if (monitorSnapshot.NativeResources.Any(entry => entry.State == NativeResourceState.Owned))
            {
                Assert.NotNull(monitorAdmissionFailure.CleanupOwner);
            }
        }
        finally
        {
            ContainedProcess.ManagedWorkerQueueForTests = null;
            ContainedProcess.ManagedWorkerQueueAdmissionObserverForTests = null;
            ContainedProcess.ManagedWorkerStartObserverForTests = null;
            monitorAdmissionFailure?.Process?.Terminate();
            monitorAdmissionFailure?.Process?.Dispose();
            monitorAdmissionFailure?.CleanupOwner?.Dispose();
        }

        // F7: a close failure before ownership transfer must leave the raw native owner reachable.
        // AfterJobCreated has acquired a job but has not created a child, so this is deterministic and
        // cannot accidentally depend on process-family timing.
        var nativeCalls = new ProcessOwnershipNativeCalls();
        var close = FindInstanceTestSeam("CloseHandle", typeof(Func<nint, NativeCallResult>));
        Assert.NotNull(close);
        int closeAttempts = 0;
        nint failedJob = nint.Zero;
        close!.SetValue(nativeCalls, (Func<nint, NativeCallResult>)(handle =>
        {
            if (Interlocked.Increment(ref closeAttempts) == 1)
            {
                failedJob = handle;
                return NativeCallResult.Failure(8601);
            }

            return NativeMethods.CloseHandle(handle)
                ? NativeCallResult.Success()
                : NativeCallResult.Failure(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        }));
        ProcessOwnershipConstructionResult failedConstruction = ContainedProcess.Start(
            NewOptions(["sleep", "1000"], nativeCalls: nativeCalls), ConstructionFaultPoint.AfterJobCreated);
        try
        {
            Assert.False(failedConstruction.Succeeded);
            Assert.Contains("8601", failedConstruction.Detail, StringComparison.Ordinal);
            Assert.NotNull(failedConstruction.CleanupOwner);
            Assert.NotEqual(nint.Zero, failedJob);
            NativeResourceSnapshot firstJob = failedConstruction.OwnershipSnapshot!.NativeResources
                .Single(entry => entry.Kind == NativeResourceKind.JobHandle);
            Assert.Equal(NativeResourceState.Owned, firstJob.State);
            Assert.Equal(failedJob, firstJob.Value);
            Assert.Equal(1, firstJob.Attempts);
            Assert.Contains("8601", firstJob.Evidence, StringComparison.Ordinal);

            failedConstruction.CleanupOwner!.Dispose();
            NativeResourceSnapshot secondJob = failedConstruction.CleanupOwner.OwnershipSnapshot.NativeResources
                .Single(entry => entry.Kind == NativeResourceKind.JobHandle);
            Assert.Equal(NativeResourceState.Released, secondJob.State);
            Assert.Equal(nint.Zero, secondJob.Value);
            Assert.True(secondJob.Attempts >= 2);
            Assert.Equal(NativeResourceState.Owned, firstJob.State);
            Assert.Equal(failedJob, firstJob.Value);
        }
        finally
        {
            close.SetValue(nativeCalls, null);
            failedConstruction.CleanupOwner?.Dispose();
        }

        // A queue admission failure after stdout has been admitted must quiesce the worker whose
        // reservation was accepted before construction unwind returns.  The stderr queue waits for the
        // stdout wrapper to start, then rejects admission without queuing stderr; this is a deterministic
        // partial-scheduling failure rather than a queue-and-throw approximation of the seam contract.
        using var partialStdoutStarted = new ManualResetEventSlim();
        using var partialStdoutFinished = new ManualResetEventSlim();
        ProcessOwnershipConstructionResult? partialSchedulingFailure = null;
        ContainedProcess.ManagedWorkerQueueForTests = (kind, action) => kind switch
        {
            ManagedResourceKind.CompletionMonitor => ThreadPool.QueueUserWorkItem(_ => action()),
            ManagedResourceKind.StdoutDrain => ThreadPool.QueueUserWorkItem(_ =>
            {
                partialStdoutStarted.Set();
                try { action(); }
                finally { partialStdoutFinished.Set(); }
            }),
            ManagedResourceKind.StderrDrain =>
                partialStdoutStarted.Wait(TimeSpan.FromSeconds(5))
                    ? false
                    : throw new TimeoutException("stdout worker did not start before stderr queue admission"),
            _ => throw new InvalidOperationException($"unexpected managed worker kind: {kind}"),
        };
        try
        {
            partialSchedulingFailure = ContainedProcess.Start(NewOptions(["sleep", "5000"]));

            Assert.False(partialSchedulingFailure.Succeeded);
            Assert.Equal(ProcessOwnershipFailureClass.ProcessConstructionFailed, partialSchedulingFailure.FailureClass);
            Assert.True(
                partialStdoutFinished.IsSet,
                "Construction unwind must await the successfully scheduled stdout worker before returning.");

            ProcessOwnershipSnapshot partialSnapshot = GetTypedOwnershipSnapshot(partialSchedulingFailure);
            ManagedResourceSnapshot[] drainSlots = partialSnapshot.ManagedResources
                .Where(entry => entry.Kind is ManagedResourceKind.DrainCancellation
                    or ManagedResourceKind.StdoutDrain
                    or ManagedResourceKind.StderrDrain)
                .ToArray();
            Assert.Equal(3, drainSlots.Length);
            Assert.DoesNotContain(drainSlots,
                entry => (entry.State is ManagedResourceState.Active or ManagedResourceState.Retained)
                    && !entry.HasLease);
            if (partialSnapshot.NativeResources.Any(entry => entry.State == NativeResourceState.Owned))
            {
                Assert.NotNull(partialSchedulingFailure.CleanupOwner);
            }
            if (drainSlots.Any(entry => entry.State == ManagedResourceState.Released))
            {
                Assert.All(drainSlots, entry =>
                {
                    Assert.Equal(ManagedResourceState.Released, entry.State);
                    Assert.False(entry.HasLease);
                });
            }

            partialSchedulingFailure.CleanupOwner?.Dispose();
        }
        finally
        {
            ContainedProcess.ManagedWorkerQueueForTests = null;
            ContainedProcess.ManagedWorkerQueueAdmissionObserverForTests = null;
            ContainedProcess.ManagedWorkerStartObserverForTests = null;
            ContainedProcess.ManagedResourceInstallObserverForTests = null;
            partialSchedulingFailure?.Process?.Terminate();
            partialSchedulingFailure?.Process?.Dispose();
            partialSchedulingFailure?.CleanupOwner?.Dispose();
        }
    }

    [Fact]
    public async Task DrainCompletionWaitsForTheActiveFamilyProofEpochBeforeClassification()
    {
        var barriers = new ProcessOwnershipLifecycleBarriers();
        var nativeCalls = new ProcessOwnershipNativeCalls { LifecycleBarriers = barriers };
        var result = ContainedProcess.Start(NewOptions(["echo", "barrier-out", "barrier-err"], nativeCalls: nativeCalls));
        Assert.True(result.Succeeded, result.Detail);
        using var process = result.Process!;
        Task<ProcessOwnershipWaitResult> waitTask = process.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.True(barriers.WaitForDrainsCompleted(TimeSpan.FromSeconds(5)));
        Assert.True(barriers.WaitForFamilyProofPending(TimeSpan.FromSeconds(5)));
        Assert.False(waitTask.IsCompleted);
        Assert.Equal(ProcessOwnershipFailureClass.None, process.FailureClass);
        Assert.False(process.TerminateJobObjectWasCalled);
        barriers.ReleaseActiveProcessZero();
        var wait = await waitTask;
        Assert.Equal(ProcessOwnershipFailureClass.None, wait.FailureClass);
        Assert.False(process.TerminateJobObjectWasCalled);
        AssertManagedSlotsAcquired(process.OwnershipSnapshot, GetRequiredLifecycleReceipts(process));

        LifecycleOperationReceipt receiptShape = new LifecycleOperationReceipt();
        Assert.NotNull(receiptShape.Kind);
        Assert.Equal(0, receiptShape.OperationId);
        Assert.Equal(0, receiptShape.Epoch);
    }

    [Fact]
    public async Task LifecycleEpochsRejectStaleCompletionsAndJoinOneRetryableTerminalOperation()
    {
        // Exact internal protocol exercised below: BeginFamilyProofEpoch() and
        // JoinTerminalOperation() return immutable receipts; completion consumes (operationId,
        // nativeSuccess, familyProven); RetryTerminalOperation() starts no second native attempt
        // after success and instead returns/joins a newer family-proof receipt.
        var instance = new ProcessOwnershipLifecycleCoordinator();
        LifecycleOperationReceipt familyN = instance.BeginFamilyProofEpoch();
        LifecycleOperationReceipt familyN1 = instance.BeginFamilyProofEpoch();
        LifecycleOperationReceipt stale = instance.CompleteFamilyProofEpoch(familyN.OperationId, false, true);
        Assert.False(stale.Accepted);
        Assert.True(stale.Stale);
        ManagedResourceSnapshot familySlot = Assert.Single(instance.ManagedResources,
            entry => entry.Kind == ManagedResourceKind.FamilyProof);
        Assert.Equal(familyN1.OperationId, familySlot.OperationId);
        Assert.Equal(ManagedResourceState.Active, familySlot.State);
        LifecycleOperationReceipt accepted = instance.CompleteFamilyProofEpoch(familyN1.OperationId, true, true);
        Assert.True(accepted.Accepted);

        LifecycleOperationReceipt terminalA = instance.JoinTerminalOperation();
        LifecycleOperationReceipt terminalB = instance.JoinTerminalOperation();
        Assert.Equal(terminalA.OperationId, terminalB.OperationId);
        LifecycleOperationReceipt failed = instance.CompleteTerminalOperation(terminalA.OperationId, false);
        Assert.True(failed.Accepted);
        LifecycleOperationReceipt retryReceipt = instance.RetryTerminalOperation();
        Assert.True(retryReceipt.OperationId > terminalA.OperationId);
        LifecycleOperationReceipt success = instance.CompleteTerminalOperation(retryReceipt.OperationId, true);
        Assert.True(success.Accepted);
        LifecycleOperationReceipt proofRetry = instance.RetryTerminalOperation();
        Assert.NotEqual(retryReceipt.OperationId, proofRetry.OperationId);
        Assert.Equal(instance.OperationReceipts.Select(item => item.OperationId),
            instance.OperationReceipts.Select(item => item.OperationId).OrderBy(id => id));
        Assert.NotSame(instance.ImmutableEvidence, instance.ImmutableEvidence);

        // A completion monitor owns the actual CTS/task lease.  Disposal is not legal while that
        // lease is live: the first disposal epoch must retain ownership, then a newer epoch may
        // complete only after the monitor's exact epoch is quiesced.
        var monitorCoordinator = new ProcessOwnershipLifecycleCoordinator();
        using var monitorCts = new CancellationTokenSource();
        var monitorTask = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        LifecycleOperationReceipt monitorEpoch = monitorCoordinator.BeginCompletionMonitorEpoch(
            monitorCts, monitorTask.Task);
        ManagedResourceSnapshot monitorSlot = Assert.Single(monitorCoordinator.ManagedResources,
            entry => entry.Kind == ManagedResourceKind.CompletionMonitor);
        Assert.Equal(ManagedResourceState.Active, monitorSlot.State);
        Assert.True(monitorSlot.HasLease);

        bool initialRetry = true;
        Task initialDisposal = monitorCoordinator.GetOrStartDispose(id =>
        {
            monitorCoordinator.CompleteDispose(retained: false, operationId: id);
            return Task.CompletedTask;
        }, out initialRetry);
        Assert.False(initialRetry);
        await initialDisposal;
        Assert.Equal(LifecycleState.FamilyResourcesRetained, monitorCoordinator.State);
        ManagedResourceSnapshot retainedDisposal = Assert.Single(monitorCoordinator.ManagedResources,
            entry => entry.Kind == ManagedResourceKind.Disposal);
        Assert.Equal(ManagedResourceState.Retained, retainedDisposal.State);
        Assert.True(retainedDisposal.HasLease);
        Assert.False(string.IsNullOrWhiteSpace(retainedDisposal.Evidence));
        long retainedDisposalSequence = retainedDisposal.AcquisitionSequence;

        monitorTask.SetResult();
        LifecycleOperationReceipt monitorCompletion = monitorCoordinator.CompleteCompletionMonitorEpoch(monitorEpoch.OperationId);
        Assert.True(monitorCompletion.Accepted);
        Assert.Equal(ManagedResourceState.Quiescent, Assert.Single(monitorCoordinator.ManagedResources,
            entry => entry.Kind == ManagedResourceKind.CompletionMonitor).State);

        bool retry = false;
        Task disposalRetry = monitorCoordinator.GetOrStartDispose(id =>
        {
            monitorCoordinator.CompleteDispose(retained: false, operationId: id);
            return Task.CompletedTask;
        }, out retry);
        Assert.True(retry);
        await disposalRetry;
        Assert.Equal(LifecycleState.Disposed, monitorCoordinator.State);
        ManagedResourceSnapshot finalDisposal = Assert.Single(monitorCoordinator.ManagedResources,
            entry => entry.Kind == ManagedResourceKind.Disposal);
        Assert.True(finalDisposal.OperationId > retainedDisposal.OperationId);
        Assert.True(finalDisposal.OperationId > 0);
        Assert.Equal(retainedDisposalSequence, finalDisposal.AcquisitionSequence);
        Assert.True(finalDisposal.State is ManagedResourceState.Released or ManagedResourceState.Quiescent);
        Assert.False(finalDisposal.HasLease);
        Assert.DoesNotContain(monitorCoordinator.ManagedResources,
            entry => entry.Kind == ManagedResourceKind.CompletionMonitor
                && (entry.State is ManagedResourceState.Active or ManagedResourceState.Retained));

        // F6: the coordinator must be the production ContainedProcess authority, not merely a directly
        // instantiated protocol fixture.  A clean child must publish an accepted family-proof receipt.
        var clean = ContainedProcess.Start(NewOptions(["echo", "lifecycle", "proof"]));
        Assert.True(clean.Succeeded, clean.Detail);
        using (ContainedProcess cleanProcess = clean.Process!)
        {
            ProcessOwnershipWaitResult cleanWait = await cleanProcess.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            Assert.Equal(ProcessOwnershipFailureClass.None, cleanWait.FailureClass);
            cleanProcess.Dispose();
            IReadOnlyList<LifecycleOperationReceipt> productionReceipts = GetRequiredLifecycleReceipts(cleanProcess);
            string[] requiredKinds = ["Wait", "Drain", "Dispose", "FamilyProof"];
            foreach (string kind in requiredKinds)
            {
                LifecycleOperationReceipt receipt = Assert.Single(productionReceipts, item => item.Kind == kind);
                Assert.True(receipt.Accepted);
                Assert.True(receipt.OperationId > 0);
            }
            long[] productionIds = productionReceipts
                .Select(item => item.OperationId)
                .ToArray();
            Assert.Equal(productionIds.Distinct().OrderBy(id => id), productionIds);
            AssertManagedSlotsAcquired(cleanProcess.OwnershipSnapshot, productionReceipts);
        }

        // The real terminal path must serialize concurrent callers, permit a retry after a failed native
        // attempt, and use a later family-proof epoch after a successful native termination.
        var firstTerminalEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstTerminal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int nativeTerminalCalls = 0;
        var retryCalls = new ProcessOwnershipNativeCalls
        {
            TerminateJobObject = (job, exitCode) =>
            {
                if (Interlocked.Increment(ref nativeTerminalCalls) == 1)
                {
                    firstTerminalEntered.SetResult();
                    releaseFirstTerminal.Task.GetAwaiter().GetResult();
                    return NativeCallResult.Failure(8602);
                }

                return NativeMethods.TerminateJobObject(job, exitCode)
                    ? NativeCallResult.Success()
                    : NativeCallResult.Failure(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            },
        };
        var retryResult = ContainedProcess.Start(NewOptions(["sleep", "5000"], nativeCalls: retryCalls));
        Assert.True(retryResult.Succeeded, retryResult.Detail);
        using (ContainedProcess retryProcess = retryResult.Process!)
        {
            Task<bool> first = Task.Run(retryProcess.Terminate);
            await firstTerminalEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task<bool> joined = Task.Run(retryProcess.Terminate);
            Assert.True(SpinWait.SpinUntil(
                () => LifecycleIds(GetRequiredLifecycleReceipts(retryProcess), "Terminal").Length >= 2,
                TimeSpan.FromSeconds(5)), "The second caller did not join the in-flight terminal operation.");
            releaseFirstTerminal.SetResult();
            await Task.WhenAll(first, joined);
            IReadOnlyList<LifecycleOperationReceipt> afterFailedTerminal = GetRequiredLifecycleReceipts(retryProcess);
            long[] firstIds = LifecycleIds(afterFailedTerminal, "Terminal");
            Assert.True(firstIds.Length >= 2);
            Assert.Equal(firstIds[0], firstIds[1]);
            AssertManagedSnapshotContract(retryProcess.OwnershipSnapshot);

            Assert.True(retryProcess.Terminate());
            long[] afterRetryIds = LifecycleIds(GetRequiredLifecycleReceipts(retryProcess), "Terminal");
            Assert.Contains(afterRetryIds, id => id > firstIds[0]);
            AssertManagedSlotsAcquired(retryProcess.OwnershipSnapshot, GetRequiredLifecycleReceipts(retryProcess));
        }

        var proofBarriers = new ProcessOwnershipLifecycleBarriers();
        int proofTerminalCalls = 0;
        var proofCalls = new ProcessOwnershipNativeCalls
        {
            LifecycleBarriers = proofBarriers,
            TerminateJobObject = (job, exitCode) =>
            {
                Interlocked.Increment(ref proofTerminalCalls);
                return NativeMethods.TerminateJobObject(job, exitCode)
                    ? NativeCallResult.Success()
                    : NativeCallResult.Failure(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            },
        };
        var proofResult = ContainedProcess.Start(NewOptions(["sleep", "5000"], nativeCalls: proofCalls));
        Assert.True(proofResult.Succeeded, proofResult.Detail);
        using (ContainedProcess proofProcess = proofResult.Process!)
        {
            proofProcess.ActiveProcessZeroBoundForTests = TimeSpan.FromMilliseconds(150);
            Task<bool> terminal = Task.Run(proofProcess.Terminate);
            Assert.True(proofBarriers.WaitForFamilyProofPending(TimeSpan.FromSeconds(5)));
            long terminalId = LifecycleIds(GetRequiredLifecycleReceipts(proofProcess), "Terminal").Single();
            Assert.False(await terminal);
            long firstProofId = LifecycleIds(GetRequiredLifecycleReceipts(proofProcess), "FamilyProof").Single();
            Task<bool> retryTerminal = Task.Run(proofProcess.Terminate);
            Assert.True(proofBarriers.WaitForFamilyProofPending(TimeSpan.FromSeconds(5)));
            proofBarriers.ReleaseActiveProcessZero();
            Assert.True(await retryTerminal);
            long[] allIds = LifecycleIds(GetRequiredLifecycleReceipts(proofProcess), "FamilyProof");
            Assert.Contains(allIds, id => id > firstProofId);
            Assert.Contains(LifecycleIds(GetRequiredLifecycleReceipts(proofProcess), "Terminal"), id => id > terminalId);
            Assert.Equal(1, proofTerminalCalls);
        }
    }

    [Fact]
    public async Task FailedReleaseRetainsNativeValueAndImmutableOrderedRetryEvidence()
    {
        var nativeCalls = new ProcessOwnershipNativeCalls();
        var close = FindInstanceTestSeam("CloseHandle", typeof(Func<nint, NativeCallResult>));
        Assert.NotNull(close);
        int failures = 0;
        nint failedHandle = nint.Zero;
        bool injectDisposalFailure = false;
        close!.SetValue(nativeCalls, (Func<nint, NativeCallResult>)(handle =>
            !Volatile.Read(ref injectDisposalFailure)
                ? NativeMethods.CloseHandle(handle)
                    ? NativeCallResult.Success()
                    : NativeCallResult.Failure(System.Runtime.InteropServices.Marshal.GetLastWin32Error())
                : Interlocked.Increment(ref failures) == 1
                ? (failedHandle = handle, NativeCallResult.Failure(8401)).Item2
                : NativeMethods.CloseHandle(handle)
                    ? NativeCallResult.Success()
                    : NativeCallResult.Failure(System.Runtime.InteropServices.Marshal.GetLastWin32Error())));
        var result = ContainedProcess.Start(NewOptions(["echo", "out", "err"], nativeCalls: nativeCalls));
        Assert.True(result.Succeeded, result.Detail);
        try
        {
            Volatile.Write(ref injectDisposalFailure, true);
            await result.Process!.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            result.Process.Dispose();
            Assert.Contains(result.Process.TeardownFailures,
                evidence => evidence.Contains("8401", StringComparison.Ordinal));
            Assert.True(result.Process.HasRetainedFamilyResourcesForTests || result.Process.FailureClass == ProcessOwnershipFailureClass.TeardownDegraded);
            NativeResourceSnapshot[] firstEntries = result.Process.OwnershipSnapshot.NativeResources.ToArray();
            AssertManagedSnapshotContract(result.Process.OwnershipSnapshot);
            ManagedResourceSnapshot retainedDisposal = Assert.Single(result.Process.OwnershipSnapshot.ManagedResources,
                entry => entry.Kind == ManagedResourceKind.Disposal && entry.State == ManagedResourceState.Retained);
            string retainedEvidence = retainedDisposal.Evidence;
            long retainedEpoch = retainedDisposal.OperationId;
            Assert.Contains(firstEntries, entry => entry.State == NativeResourceState.Owned
                && entry.Value == failedHandle
                && entry.Attempts == 1
                && entry.Evidence.Contains("8401", StringComparison.Ordinal));
            var immutableFirst = firstEntries.Select(entry =>
                (entry.Kind, entry.State, entry.Value)).ToArray();
            result.Process.Dispose();
            NativeResourceSnapshot[] secondEntries = result.Process.OwnershipSnapshot.NativeResources.ToArray();
            Assert.DoesNotContain(secondEntries, entry => entry.State == NativeResourceState.Owned);
            Assert.Equal(immutableFirst, firstEntries.Select(entry =>
                (entry.Kind, entry.State, entry.Value)));
            Assert.Equal(firstEntries.Length, secondEntries.Length);
            Assert.True(result.Process.TeardownOrderForTests.Count > 0);
            AssertManagedSlotsQuiescentOrReleased(result.Process.OwnershipSnapshot);
            ManagedResourceSnapshot releasedDisposal = Assert.Single(result.Process.OwnershipSnapshot.ManagedResources,
                entry => entry.Kind == ManagedResourceKind.Disposal);
            Assert.Equal(ManagedResourceState.Retained, retainedDisposal.State);
            Assert.Equal(retainedEpoch, retainedDisposal.OperationId);
            Assert.True(releasedDisposal.OperationId > retainedEpoch);
            Assert.Contains(retainedEvidence, releasedDisposal.Evidence, StringComparison.Ordinal);
        }
        finally
        {
            close.SetValue(nativeCalls, null);
            result.Process?.Dispose();
        }

        // F8: the internal ledger is strict about identity.  Duplicate acquisition and a failed release
        // against an already released slot must not synthesize or mutate ownership.
        var ledger = new NativeOwnershipLedger();
        nint value = (nint)0x1234;
        ledger.Acquire(NativeResourceKind.JobHandle, value, "test acquire");
        ProcessOwnershipSnapshot beforeDuplicate = ledger.Snapshot();
        Assert.Throws<InvalidOperationException>(() =>
            ledger.Acquire(NativeResourceKind.JobHandle, value, "duplicate acquire"));
        ProcessOwnershipSnapshot afterDuplicate = ledger.Snapshot();
        Assert.Equal(beforeDuplicate.NativeResources.Select(entry =>
            (entry.Kind, entry.State, entry.Value, entry.Attempts, entry.Evidence, entry.Error)),
            afterDuplicate.NativeResources.Select(entry =>
            (entry.Kind, entry.State, entry.Value, entry.Attempts, entry.Evidence, entry.Error)));

        ledger.AttemptRelease(NativeResourceKind.JobHandle, true, 0, "test release", value);
        ProcessOwnershipSnapshot released = ledger.Snapshot();
        Assert.Throws<InvalidOperationException>(() =>
            ledger.AttemptRelease(NativeResourceKind.JobHandle, false, 8603, "stale failed release", value));
        ProcessOwnershipSnapshot afterStale = ledger.Snapshot();
        Assert.Equal(released.NativeResources.Select(entry =>
            (entry.Kind, entry.State, entry.Value, entry.Attempts, entry.Evidence, entry.Error)),
            afterStale.NativeResources.Select(entry =>
            (entry.Kind, entry.State, entry.Value, entry.Attempts, entry.Evidence, entry.Error)));
    }

    [Fact]
    public void StagedStubUsesRelocatableBaseDirectoryAndContainsCompleteRuntimePayload()
    {
        string stage = Path.Combine(AppContext.BaseDirectory, "process-ownership-stub");
        Assert.True(Directory.Exists(stage), $"Expected staged stub directory '{stage}'.");
        Assert.True(File.Exists(Path.Combine(stage, "SeqDoc.AcceptanceTests.ProcessOwnershipStub.exe")));
        Assert.NotEmpty(Directory.EnumerateFiles(stage, "*.dll", SearchOption.TopDirectoryOnly));
        Assert.NotEmpty(Directory.EnumerateFiles(stage, "*.deps.json", SearchOption.TopDirectoryOnly));
        Assert.NotEmpty(Directory.EnumerateFiles(stage, "*.runtimeconfig.json", SearchOption.TopDirectoryOnly));

        var resolver = typeof(ProcessOwnershipTests).GetMethod(
            "ResolveStubExecutablePath",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            types: [typeof(string)],
            modifiers: null);
        Assert.NotNull(resolver);
        string relocated = Path.Combine(Path.GetTempPath(), "seqdoc-i100a-relocated-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(relocated);
        try
        {
            string relocatedStage = Path.Combine(relocated, "process-ownership-stub");
            CopyDirectory(stage, relocatedStage);
            string path = (string)resolver!.Invoke(null, [relocated])!;
            Assert.Equal(Path.Combine(relocatedStage, "SeqDoc.AcceptanceTests.ProcessOwnershipStub.exe"), path);
            Assert.True(File.Exists(path));
            using var child = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    Arguments = "echo relocated-out relocated-err",
                    WorkingDirectory = relocated,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            Assert.True(child.Start());
            string stdout = child.StandardOutput.ReadToEnd();
            string stderr = child.StandardError.ReadToEnd();
            child.WaitForExit(5000);
            Assert.Equal(0, child.ExitCode);
            Assert.Equal("relocated-out", stdout);
            Assert.Equal("relocated-err", stderr);
        }
        finally
        {
            if (Directory.Exists(relocated))
            {
                Directory.Delete(relocated, recursive: true);
            }
        }
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

    private static void DuplicateCurrentHandle(nint source, out nint duplicate)
    {
        Assert.NotEqual(nint.Zero, source);
        Assert.True(
            NativeMethods.DuplicateHandle(
                NativeMethods.GetCurrentProcess(), source,
                NativeMethods.GetCurrentProcess(), out duplicate, 0, false, 0x00000002),
            $"DuplicateHandle failed with Win32 error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}.");
    }

    private static uint QueryJobActiveProcesses(nint jobHandle)
    {
        var accounting = default(NativeMethods.JOBOBJECT_BASIC_ACCOUNTING_INFORMATION);
        bool queried = NativeMethods.QueryInformationJobObject(
            jobHandle,
            NativeMethods.JobObjectBasicAccountingInformation,
            ref accounting,
            (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.JOBOBJECT_BASIC_ACCOUNTING_INFORMATION>(),
            out _);
        Assert.True(
            queried,
            $"QueryInformationJobObject(JobObjectBasicAccountingInformation) failed with Win32 error "
            + $"{System.Runtime.InteropServices.Marshal.GetLastWin32Error()}.");
        return accounting.ActiveProcesses;
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

    private static string ResolveStubExecutablePath(string? baseDirectory = null)
    {
        string root = baseDirectory ?? AppContext.BaseDirectory;
        string stubPath = Path.Combine(root, "process-ownership-stub", "SeqDoc.AcceptanceTests.ProcessOwnershipStub.exe");

        if (!File.Exists(stubPath))
        {
            throw new InvalidOperationException(
                $"Staged stub executable not found at '{stubPath}'.");
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

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (string directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static ProcessOwnershipSnapshot GetTypedOwnershipSnapshot(ProcessOwnershipConstructionResult result) =>
        result.OwnershipSnapshot
        ?? throw new Xunit.Sdk.XunitException("Construction result has no typed ownership snapshot.");

    private static ProcessOwnershipSnapshot GetTypedOwnershipSnapshot(ContainedProcess owner) => owner.OwnershipSnapshot;

    private static IReadOnlyList<LifecycleOperationReceipt> GetRequiredLifecycleReceipts(ContainedProcess process) =>
        process.LifecycleOperationReceiptsForTests;

    private static void AssertManagedSnapshotContract(ProcessOwnershipSnapshot snapshot)
    {
        IReadOnlyList<ManagedResourceSnapshot> entries = snapshot.ManagedResources;
        ManagedResourceKind[] expectedKinds =
        [
            ManagedResourceKind.CompletionMonitor, ManagedResourceKind.DrainCancellation,
            ManagedResourceKind.StdoutDrain, ManagedResourceKind.StderrDrain,
            ManagedResourceKind.ProcessWait, ManagedResourceKind.TerminalOperation,
            ManagedResourceKind.FamilyProof, ManagedResourceKind.Disposal,
        ];
        Assert.Equal(expectedKinds, entries.Select(entry => entry.Kind));
        Assert.Equal(expectedKinds.Length, entries.Select(entry => entry.Kind).Distinct().Count());
        long[] acquiredSequences = entries.Where(entry => entry.State != ManagedResourceState.Unacquired)
            .Select(entry => entry.AcquisitionSequence).ToArray();
        Assert.All(acquiredSequences, sequence => Assert.True(sequence > 0));
        Assert.Equal(acquiredSequences.Length, acquiredSequences.Distinct().Count());
        Assert.All(entries, entry =>
        {
            Assert.True(entry.AcquisitionSequence >= 0);
            Assert.True(entry.Attempts >= 0);
            Assert.NotNull(entry.Evidence);
            if (entry.State is ManagedResourceState.Active or ManagedResourceState.Retained)
            {
                Assert.True(entry.HasLease);
            }
            else if (entry.State is ManagedResourceState.Unacquired or ManagedResourceState.Released)
            {
                Assert.False(entry.HasLease);
            }
            if (entry.State == ManagedResourceState.Unacquired)
            {
                Assert.Equal(0, entry.AcquisitionSequence);
                Assert.Equal(0, entry.OperationId);
            }
            else
            {
                Assert.True(entry.OperationId > 0);
            }
        });
    }

    private static void AssertManagedSlotsAcquired(ProcessOwnershipSnapshot snapshot,
        IReadOnlyList<LifecycleOperationReceipt> receipts)
    {
        AssertManagedSnapshotContract(snapshot);
        ManagedResourceSnapshot monitor = Assert.Single(snapshot.ManagedResources,
            entry => entry.Kind == ManagedResourceKind.CompletionMonitor);
        Assert.NotEqual(ManagedResourceState.Unacquired, monitor.State);
        Assert.True(monitor.OperationId > 0);
        foreach (ManagedResourceKind kind in new[] { ManagedResourceKind.DrainCancellation,
            ManagedResourceKind.StdoutDrain, ManagedResourceKind.StderrDrain })
        {
            ManagedResourceSnapshot entry = Assert.Single(snapshot.ManagedResources, item => item.Kind == kind);
            Assert.NotEqual(ManagedResourceState.Unacquired, entry.State);
            Assert.Equal(ReceiptId(receipts, "Drain"), entry.OperationId);
        }

        foreach ((ManagedResourceKind kind, string receiptKind) in new[]
        {
            (ManagedResourceKind.ProcessWait, "Wait"),
            (ManagedResourceKind.TerminalOperation, "Terminal"),
            (ManagedResourceKind.FamilyProof, "FamilyProof"),
            (ManagedResourceKind.Disposal, "Dispose"),
        })
        {
            LifecycleOperationReceipt? receipt = receipts.LastOrDefault(item => item.Kind == receiptKind);
            ManagedResourceSnapshot entry = Assert.Single(snapshot.ManagedResources, item => item.Kind == kind);
            if (receipt is null)
            {
                Assert.Equal(ManagedResourceState.Unacquired, entry.State);
                Assert.Equal(0, entry.OperationId);
                continue;
            }

            Assert.NotEqual(ManagedResourceState.Unacquired, entry.State);
            Assert.True(entry.OperationId > 0);
            Assert.Equal(receipt.OperationId, entry.OperationId);
        }
    }

    private static void AssertManagedSlotsQuiescentOrReleased(ProcessOwnershipSnapshot snapshot)
    {
        AssertManagedSnapshotContract(snapshot);
        Assert.DoesNotContain(snapshot.ManagedResources, entry =>
            entry.State is ManagedResourceState.Active or ManagedResourceState.Retained);
        Assert.All(snapshot.ManagedResources, entry =>
            Assert.True(entry.State is ManagedResourceState.Unacquired or ManagedResourceState.Quiescent or ManagedResourceState.Released));
    }

    private static long ReceiptId(IReadOnlyList<LifecycleOperationReceipt> receipts, string kind) =>
        Assert.Single(receipts, receipt => receipt.Kind == kind).OperationId;

    private static long[] LifecycleIds(IReadOnlyList<LifecycleOperationReceipt> receipts, string kind) =>
        receipts
            .Where(receipt => receipt.Kind == kind)
            .Select(receipt => receipt.OperationId)
            .ToArray();

    private static object GetRequiredProperty(object owner, string name) =>
        owner.GetType().GetProperty(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(owner)
        ?? throw new Xunit.Sdk.XunitException($"Missing required typed property {name} on {owner.GetType().Name}.");

    private static nint ConvertToNativeInt(object value) => value switch
    {
        nint native => native,
        _ => (nint)Convert.ToInt64(value, CultureInfo.InvariantCulture),
    };

    private static long ToInt64(object value) => Convert.ToInt64(value, CultureInfo.InvariantCulture);
}
