using System.Runtime.InteropServices;
using System.Text;

namespace SeqDoc.AcceptanceTests;

/// <summary>
/// GH-106 / I100-A: test-only Windows x64 contained-process ownership primitive. First native-interop
/// code in the repository — see <c>docs/work/quality/I100-A/checkpoint.md</c> for the frozen contract,
/// the native API admission table, and the failure-class precedence this class implements.
///
/// The primitive never performs a PATH/cwd search: <see cref="ProcessOwnershipOptions.ExecutablePath"/>
/// must already be rooted and existing (finalized contract decision 2), and it fails closed
/// (<see cref="ProcessOwnershipFailureClass.ProcessConstructionFailed"/>) otherwise.
/// </summary>
public enum ProcessOwnershipFailureClass
{
    None = 0,
    ProcessConstructionFailed = 1,
    TimedOut = 2,
    ProcessFailed = 3,
    DrainIncomplete = 4,
    TeardownDegraded = 5,
}

/// <summary>
/// Internal-only fault-injection seam (same assembly as the tests — no production surface, never used
/// outside <c>ProcessOwnershipTests.cs</c>) used to prove S0-S3 partial-construction unwind at exact
/// acquisition boundaries without depending on real native-API failure conditions that cannot be
/// reliably forced from a test.
/// </summary>
internal enum ConstructionFaultPoint
{
    None = 0,
    AfterPipesCreated,
    AfterAttributeListBuilt,
    AfterJobCreated,
    AfterProcessCreatedBeforeAssign,
}

public sealed class ProcessOwnershipOptions
{
    public required string ExecutablePath { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Explicit deterministic child environment. Never the ambient/inherited environment — the caller
    /// supplies exactly the variables the child should see.
    /// </summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

public sealed class ProcessOwnershipConstructionResult
{
    private ProcessOwnershipConstructionResult(
        ContainedProcess? process, ProcessOwnershipFailureClass failureClass, string? detail)
    {
        Process = process;
        FailureClass = failureClass;
        Detail = detail;
    }

    public ContainedProcess? Process { get; }

    public ProcessOwnershipFailureClass FailureClass { get; }

    public string? Detail { get; }

    public bool Succeeded => Process is not null;

    internal static ProcessOwnershipConstructionResult Success(ContainedProcess process) =>
        new(process, ProcessOwnershipFailureClass.None, null);

    internal static ProcessOwnershipConstructionResult Failure(string detail) =>
        new(null, ProcessOwnershipFailureClass.ProcessConstructionFailed, detail);
}

public sealed class ProcessOwnershipStreamResult
{
    public ProcessOwnershipStreamResult(string text, bool truncated)
    {
        Text = text;
        Truncated = truncated;
    }

    public string Text { get; }

    public bool Truncated { get; }
}

public sealed class ProcessOwnershipWaitResult
{
    public ProcessOwnershipFailureClass FailureClass { get; internal set; }

    public string? Detail { get; internal set; }

    public int? ExitCode { get; internal set; }

    public bool TimedOut { get; internal set; }

    public bool Cancelled { get; internal set; }

    public bool ActiveProcessZeroObserved { get; internal set; }

    public required ProcessOwnershipStreamResult StdOut { get; init; }

    public required ProcessOwnershipStreamResult StdErr { get; init; }
}

/// <summary>
/// First-write-wins tracker implementing the checkpoint's monotonic failure-class precedence: a later
/// class never overwrites an earlier one already recorded.
/// </summary>
internal sealed class FailureClassTracker
{
    public ProcessOwnershipFailureClass Class { get; private set; } = ProcessOwnershipFailureClass.None;

    public string? Detail { get; private set; }

    public bool HasFailure => Class != ProcessOwnershipFailureClass.None;

    public void Record(ProcessOwnershipFailureClass failureClass, string detail)
    {
        if (Class == ProcessOwnershipFailureClass.None)
        {
            Class = failureClass;
            Detail = detail;
        }
    }
}

/// <summary>
/// Runtime platform admission gate (finalized contract decision 1): Windows x64 only. Exposed as a pure
/// evaluator over injected values so the negative claim (unsupported platform fails closed) can be
/// proven at the unit level without OS mocking, per the checkpoint's "unit-level fake/seam" requirement.
/// </summary>
public static class ProcessOwnershipPlatform
{
    public static bool IsSupported() => Evaluate(OperatingSystem.IsWindows(), RuntimeInformation.ProcessArchitecture);

    internal static bool Evaluate(bool isWindows, System.Runtime.InteropServices.Architecture architecture) =>
        isWindows && architecture == System.Runtime.InteropServices.Architecture.X64;
}

/// <summary>
/// Windows argument (CommandLineToArgvW round-trip) and environment-block construction. Pure and
/// independently testable.
/// </summary>
internal static class ProcessOwnershipEncoding
{
    internal static string QuoteArgument(string argument)
    {
        if (argument.Length > 0 && argument.AsSpan().IndexOfAny(" \t\n\v\"") < 0)
        {
            return argument;
        }

        var builder = new StringBuilder();
        builder.Append('"');
        int backslashes = 0;
        foreach (char c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                builder.Append('\\', backslashes * 2 + 1);
                builder.Append('"');
                backslashes = 0;
                continue;
            }

            builder.Append('\\', backslashes);
            backslashes = 0;
            builder.Append(c);
        }

        builder.Append('\\', backslashes * 2);
        builder.Append('"');
        return builder.ToString();
    }

    internal static string BuildCommandLine(string executablePath, IReadOnlyList<string> arguments)
    {
        var builder = new StringBuilder();
        builder.Append(QuoteArgument(executablePath));
        foreach (string argument in arguments)
        {
            builder.Append(' ');
            builder.Append(QuoteArgument(argument));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Sorted (ordinal), deduplicated (last-write-wins on a duplicate key), double-null-terminated
    /// Unicode environment block for <c>CREATE_UNICODE_ENVIRONMENT</c>.
    /// </summary>
    internal static string BuildEnvironmentBlock(IReadOnlyDictionary<string, string> environment)
    {
        var deduped = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in environment)
        {
            deduped[pair.Key] = pair.Value;
        }

        var builder = new StringBuilder();
        foreach (var pair in deduped)
        {
            builder.Append(pair.Key).Append('=').Append(pair.Value).Append('\0');
        }

        if (builder.Length == 0)
        {
            builder.Append('\0');
        }

        return builder.ToString();
    }
}

/// <summary>
/// Owns a Windows-contained child process family: created suspended, assigned to a kill-on-close,
/// breakaway-denied Job Object before first resume, drained concurrently on both standard streams, and
/// proven fully exited (including any descendant) via a Job Object I/O completion port
/// <c>JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO</c> message rather than only the immediate child's exit code.
/// </summary>
public sealed class ContainedProcess : IDisposable
{
    private readonly FailureClassTracker _failures = new();
    private readonly List<string> _teardownFailures = new();

    private nint _processHandle;
    private nint _threadHandle;
    private nint _jobHandle;
    private nint _completionPortHandle;
    private nint _attributeListBuffer;
    private nint _environmentBlockBuffer;
    private nint _commandLineBuffer;
    private nint _handleListBuffer;
    private nint _parentStdOutRead;
    private nint _parentStdErrRead;
    private nint _parentStdInWrite;

    private Task<(string Text, bool Truncated)>? _stdOutDrain;
    private Task<(string Text, bool Truncated)>? _stdErrDrain;
    private Task? _completionMonitor;
    private CancellationTokenSource? _completionMonitorCts;
    private volatile bool _activeProcessZeroObserved;
    private bool _terminateJobObjectCalled;
    private bool _disposed;

    private ContainedProcess()
    {
    }

    /// <summary>Test-only chronology seam — see the invocation site in <see cref="StartCore"/>.</summary>
    internal static Action<nint, nint>? AssignBeforeResumeHookForTests;

    public int ProcessId { get; private set; }

    /// <summary>Test-only observability: never used to gate production semantics.</summary>
    internal bool TerminateJobObjectWasCalled => _terminateJobObjectCalled;

    internal nint ProcessHandleForTests => _processHandle;

    internal nint StdOutChildHandleValueForTests { get; private set; }

    internal nint StdErrChildHandleValueForTests { get; private set; }

    internal nint StdInChildHandleValueForTests { get; private set; }

    public static ProcessOwnershipConstructionResult Start(ProcessOwnershipOptions options) =>
        Start(options, ConstructionFaultPoint.None);

    internal static ProcessOwnershipConstructionResult Start(
        ProcessOwnershipOptions options, ConstructionFaultPoint faultPoint)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!ProcessOwnershipPlatform.IsSupported())
        {
            return ProcessOwnershipConstructionResult.Failure(
                "Unsupported platform: this primitive requires Windows x64 (RID-equivalent), matching "
                + "finalized contract decision 1. No PATH/version-floor fallback is attempted.");
        }

        if (!Path.IsPathRooted(options.ExecutablePath) || !File.Exists(options.ExecutablePath))
        {
            return ProcessOwnershipConstructionResult.Failure(
                $"Executable path '{options.ExecutablePath}' must be rooted and already exist; this "
                + "primitive never performs a PATH/cwd search (finalized contract decision 2).");
        }

        var unwind = new Stack<Action>();
        try
        {
            return StartCore(options, faultPoint, unwind);
        }
        catch (Exception ex)
        {
            while (unwind.Count > 0)
            {
                try
                {
                    unwind.Pop().Invoke();
                }
                catch
                {
                    // Best-effort unwind of a failed construction; the primary ProcessConstructionFailed
                    // failure below is authoritative regardless of secondary teardown noise here.
                }
            }

            return ProcessOwnershipConstructionResult.Failure(ex.Message);
        }
    }

    private static ProcessOwnershipConstructionResult StartCore(
        ProcessOwnershipOptions options, ConstructionFaultPoint faultPoint, Stack<Action> unwind)
    {
        // --- S1: three std pipes, created non-inheritable by default, then the exact child-side end of
        // each is explicitly marked inheritable (never the parent-side end). ---
        CreatePipePair(out nint stdInRead, out nint stdInWrite);
        unwind.Push(() => NativeMethods.CloseHandle(stdInRead));
        unwind.Push(() => NativeMethods.CloseHandle(stdInWrite));
        CreatePipePair(out nint stdOutRead, out nint stdOutWrite);
        unwind.Push(() => NativeMethods.CloseHandle(stdOutRead));
        unwind.Push(() => NativeMethods.CloseHandle(stdOutWrite));
        CreatePipePair(out nint stdErrRead, out nint stdErrWrite);
        unwind.Push(() => NativeMethods.CloseHandle(stdErrRead));
        unwind.Push(() => NativeMethods.CloseHandle(stdErrWrite));

        SetInheritable(stdInRead);
        SetInheritable(stdOutWrite);
        SetInheritable(stdErrWrite);

        if (faultPoint == ConstructionFaultPoint.AfterPipesCreated)
        {
            throw new InvalidOperationException("fault-injected: AfterPipesCreated");
        }

        // --- S2: attribute list restricting inheritance to exactly the 3 child-side handles. ---
        nint[] inheritable = [stdInRead, stdOutWrite, stdErrWrite];
        nint handleListBuffer = Marshal.AllocHGlobal(nint.Size * inheritable.Length);
        unwind.Push(() => Marshal.FreeHGlobal(handleListBuffer));

        // Finalized contract decision 3: this is the exact unsafe native pointer block — the raw
        // PROC_THREAD_ATTRIBUTE_HANDLE_LIST payload UpdateProcThreadAttribute reads directly out of
        // process memory — that requires AllowUnsafeBlocks on this project (and only this project).
        unsafe
        {
            nint* handles = (nint*)handleListBuffer;
            for (int i = 0; i < inheritable.Length; i++)
            {
                handles[i] = inheritable[i];
            }
        }

        nint attributeListBuffer = BuildAttributeList(handleListBuffer, inheritable.Length, unwind);

        if (faultPoint == ConstructionFaultPoint.AfterAttributeListBuilt)
        {
            throw new InvalidOperationException("fault-injected: AfterAttributeListBuilt");
        }

        // --- S3: Job Object with kill-on-close and breakaway denied. ---
        nint jobHandle = NativeMethods.CreateJobObjectW(nint.Zero, null);
        if (jobHandle == nint.Zero)
        {
            throw Win32("CreateJobObjectW");
        }

        unwind.Push(() => NativeMethods.CloseHandle(jobHandle));

        var limits = default(NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION);
        limits.BasicLimitInformation.LimitFlags = NativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        int limitsSize = Marshal.SizeOf<NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        nint limitsBuffer = Marshal.AllocHGlobal(limitsSize);
        try
        {
            Marshal.StructureToPtr(limits, limitsBuffer, false);
            if (!NativeMethods.SetInformationJobObject(
                jobHandle, NativeMethods.JobObjectExtendedLimitInformation, limitsBuffer, (uint)limitsSize))
            {
                throw Win32("SetInformationJobObject(ExtendedLimitInformation)");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(limitsBuffer);
        }

        if (faultPoint == ConstructionFaultPoint.AfterJobCreated)
        {
            throw new InvalidOperationException("fault-injected: AfterJobCreated");
        }

        // --- S3.5: IOCP associated to the job so ACTIVE_PROCESS_ZERO can be observed later. ---
        nint completionPort = NativeMethods.CreateIoCompletionPort(
            new nint(-1), nint.Zero, nint.Zero, 1);
        if (completionPort == nint.Zero)
        {
            throw Win32("CreateIoCompletionPort");
        }

        unwind.Push(() => NativeMethods.CloseHandle(completionPort));

        var associate = new NativeMethods.JOBOBJECT_ASSOCIATE_COMPLETION_PORT
        {
            CompletionKey = jobHandle,
            CompletionPort = completionPort,
        };
        int associateSize = Marshal.SizeOf<NativeMethods.JOBOBJECT_ASSOCIATE_COMPLETION_PORT>();
        nint associateBuffer = Marshal.AllocHGlobal(associateSize);
        try
        {
            Marshal.StructureToPtr(associate, associateBuffer, false);
            if (!NativeMethods.SetInformationJobObject(
                jobHandle,
                NativeMethods.JobObjectAssociateCompletionPortInformation,
                associateBuffer,
                (uint)associateSize))
            {
                throw Win32("SetInformationJobObject(AssociateCompletionPortInformation)");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(associateBuffer);
        }

        // --- S4: CreateProcessW, suspended, with the restricted attribute list. ---
        string commandLine = ProcessOwnershipEncoding.BuildCommandLine(options.ExecutablePath, options.Arguments);
        nint commandLineBuffer = Marshal.StringToHGlobalUni(commandLine);
        unwind.Push(() => Marshal.FreeHGlobal(commandLineBuffer));

        string environmentBlock = ProcessOwnershipEncoding.BuildEnvironmentBlock(options.Environment);
        nint environmentBuffer = Marshal.StringToHGlobalUni(environmentBlock);
        unwind.Push(() => Marshal.FreeHGlobal(environmentBuffer));

        var startupInfoEx = default(NativeMethods.STARTUPINFOEXW);
        startupInfoEx.StartupInfo.cb = Marshal.SizeOf<NativeMethods.STARTUPINFOEXW>();
        startupInfoEx.StartupInfo.dwFlags = NativeMethods.STARTF_USESTDHANDLES;
        startupInfoEx.StartupInfo.hStdInput = stdInRead;
        startupInfoEx.StartupInfo.hStdOutput = stdOutWrite;
        startupInfoEx.StartupInfo.hStdError = stdErrWrite;
        startupInfoEx.lpAttributeList = attributeListBuffer;

        var processInformation = default(NativeMethods.PROCESS_INFORMATION);
        const int Flags = NativeMethods.CREATE_SUSPENDED
            | NativeMethods.CREATE_UNICODE_ENVIRONMENT
            | NativeMethods.EXTENDED_STARTUPINFO_PRESENT;

        bool created = NativeMethods.CreateProcessW(
            options.ExecutablePath,
            commandLineBuffer,
            nint.Zero,
            nint.Zero,
            true,
            Flags,
            environmentBuffer,
            null,
            ref startupInfoEx,
            out processInformation);

        if (!created)
        {
            throw Win32("CreateProcessW");
        }

        unwind.Push(() => NativeMethods.CloseHandle(processInformation.hThread));
        unwind.Push(() =>
        {
            NativeMethods.TerminateProcess(processInformation.hProcess, uint.MaxValue);
            NativeMethods.CloseHandle(processInformation.hProcess);
        });

        if (faultPoint == ConstructionFaultPoint.AfterProcessCreatedBeforeAssign)
        {
            // Deliberately fault before any background reader/monitor thread exists and before the
            // child-side pipe handles are closed, so unwind only has to reverse plain handle/buffer
            // acquisitions here — never a pending synchronous read racing a CloseHandle.
            throw new InvalidOperationException("fault-injected: AfterProcessCreatedBeforeAssign");
        }

        // The child-side pipe ends have been duplicated into the child's handle table by inheritance;
        // the parent no longer needs (and must not keep) them open, or EOF on the parent's read ends
        // would never be observable once the child itself exits.
        NativeMethods.CloseHandle(stdInRead);
        NativeMethods.CloseHandle(stdOutWrite);
        NativeMethods.CloseHandle(stdErrWrite);

        var process = new ContainedProcess
        {
            _processHandle = processInformation.hProcess,
            _threadHandle = processInformation.hThread,
            _jobHandle = jobHandle,
            _completionPortHandle = completionPort,
            _attributeListBuffer = attributeListBuffer,
            _environmentBlockBuffer = environmentBuffer,
            _commandLineBuffer = commandLineBuffer,
            _handleListBuffer = handleListBuffer,
            _parentStdOutRead = stdOutRead,
            _parentStdErrRead = stdErrRead,
            _parentStdInWrite = stdInWrite,
            ProcessId = processInformation.dwProcessId,
            StdOutChildHandleValueForTests = stdOutWrite,
            StdErrChildHandleValueForTests = stdErrWrite,
            StdInChildHandleValueForTests = stdInRead,
        };

        // Reads and the completion-port monitor are both started before ResumeThread returns control to
        // any wait loop (contract point 5 / 4): the child can produce output or exit the instant it is
        // resumed, and nothing here may race that.
        process.StartDrains(options.DrainTimeout);
        process.StartCompletionMonitor();

        // --- S5: assign to the job BEFORE first resume — containment precedes any executed instruction. ---
        if (!NativeMethods.AssignProcessToJobObject(jobHandle, processInformation.hProcess))
        {
            process._completionMonitorCts?.Cancel();
            throw Win32("AssignProcessToJobObject");
        }

        // Test-only chronology seam (same assembly only): invoked while the thread is still suspended,
        // strictly between AssignProcessToJobObject and ResumeThread, so a test can prove job membership
        // is already established before the child ever executes an instruction.
        AssignBeforeResumeHookForTests?.Invoke(jobHandle, processInformation.hProcess);

        // --- S6: resume. ---
        if (NativeMethods.ResumeThread(processInformation.hThread) == uint.MaxValue)
        {
            process._completionMonitorCts?.Cancel();
            throw Win32("ResumeThread");
        }

        unwind.Clear();
        return ProcessOwnershipConstructionResult.Success(process);
    }

    public async Task<ProcessOwnershipWaitResult> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

        int exitCode = 0;
        bool exited = false;
        try
        {
            exited = await Task.Run(() => WaitForSingleProcessExit(linked.Token), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            exited = false;
        }

        if (!exited)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _failures.Record(ProcessOwnershipFailureClass.TimedOut, "Wait was cancelled by the caller.");
            }
            else
            {
                _failures.Record(ProcessOwnershipFailureClass.TimedOut, $"Wait exceeded {timeout}.");
            }

            // Admission table: explicit TerminateJobObject for timeout/cancellation (never deferred to
            // Dispose's kill-on-close). This also unblocks the concurrent stream drains below by making
            // the child's inherited pipe handles close, which yields EOF on the parent's read ends.
            _terminateJobObjectCalled = true;
            NativeMethods.TerminateJobObject(_jobHandle, uint.MaxValue);
        }
        else
        {
            if (!NativeMethods.GetExitCodeProcess(_processHandle, out uint code))
            {
                _failures.Record(ProcessOwnershipFailureClass.ProcessFailed, "GetExitCodeProcess failed.");
            }
            else
            {
                exitCode = unchecked((int)code);
                if (code != 0)
                {
                    _failures.Record(
                        ProcessOwnershipFailureClass.ProcessFailed, $"Child exited with code {code}.");
                }
            }

            // Give the job's completion port a bounded chance to report ACTIVE_PROCESS_ZERO (every
            // process in the job, including descendants, has exited) before declaring the wait complete.
            await WaitForActiveProcessZero(linked.Token).ConfigureAwait(false);
        }

        var (stdOutText, stdOutTruncated) = await DrainOrTruncate(_stdOutDrain).ConfigureAwait(false);
        var (stdErrText, stdErrTruncated) = await DrainOrTruncate(_stdErrDrain).ConfigureAwait(false);
        if (stdOutTruncated || stdErrTruncated)
        {
            _failures.Record(ProcessOwnershipFailureClass.DrainIncomplete, "A stream did not reach EOF in time.");
        }

        return new ProcessOwnershipWaitResult
        {
            FailureClass = _failures.Class,
            Detail = _failures.Detail,
            ExitCode = exited ? exitCode : null,
            TimedOut = !exited && !cancellationToken.IsCancellationRequested,
            Cancelled = !exited && cancellationToken.IsCancellationRequested,
            ActiveProcessZeroObserved = _activeProcessZeroObserved,
            StdOut = new ProcessOwnershipStreamResult(stdOutText, stdOutTruncated),
            StdErr = new ProcessOwnershipStreamResult(stdErrText, stdErrTruncated),
        };
    }

    /// <summary>Query phase: has ACTIVE_PROCESS_ZERO been observed for this job yet?</summary>
    public bool HasObservedActiveProcessZero() => _activeProcessZeroObserved;

    /// <summary>Terminate phase: forcibly ends every process in the job.</summary>
    public bool Terminate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _terminateJobObjectCalled = true;
        return NativeMethods.TerminateJobObject(_jobHandle, uint.MaxValue);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _completionMonitorCts?.Cancel();
        try
        {
            _completionMonitor?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // The monitor's own cancellation is expected here; nothing further to report.
        }

        _completionMonitorCts?.Dispose();

        // Reverse acquisition order: process handle, thread handle, job handle, IOCP handle, pipe
        // handles, attribute-list buffer, environment/command-line buffers.
        CloseTracked(ref _processHandle, "process handle");
        CloseTracked(ref _threadHandle, "thread handle");
        CloseTracked(ref _jobHandle, "job handle");
        CloseTracked(ref _completionPortHandle, "completion port handle");
        CloseTracked(ref _parentStdOutRead, "stdout pipe handle");
        CloseTracked(ref _parentStdErrRead, "stderr pipe handle");
        CloseTracked(ref _parentStdInWrite, "stdin pipe handle");
        FreeTracked(ref _attributeListBuffer, "attribute list buffer", deleteAttributeList: true);
        FreeTracked(ref _handleListBuffer, "handle list buffer", deleteAttributeList: false);
        FreeTracked(ref _environmentBlockBuffer, "environment block buffer", deleteAttributeList: false);
        FreeTracked(ref _commandLineBuffer, "command line buffer", deleteAttributeList: false);

        if (_teardownFailures.Count > 0)
        {
            _failures.Record(
                ProcessOwnershipFailureClass.TeardownDegraded,
                $"{_teardownFailures.Count} teardown step(s) failed: {string.Join("; ", _teardownFailures)}");
        }
    }

    /// <summary>Aggregated teardown failures recorded during <see cref="Dispose"/>, if any.</summary>
    internal IReadOnlyList<string> TeardownFailuresForTests => _teardownFailures;

    internal ProcessOwnershipFailureClass RecordedFailureClassForTests => _failures.Class;

    private void CloseTracked(ref nint handle, string label)
    {
        if (handle == nint.Zero)
        {
            return;
        }

        nint toClose = handle;
        handle = nint.Zero;
        try
        {
            if (!NativeMethods.CloseHandle(toClose))
            {
                _teardownFailures.Add($"CloseHandle({label}) failed: {Marshal.GetLastWin32Error()}");
            }
        }
        catch (Exception ex)
        {
            _teardownFailures.Add($"CloseHandle({label}) threw: {ex.Message}");
        }
    }

    private void FreeTracked(ref nint buffer, string label, bool deleteAttributeList)
    {
        if (buffer == nint.Zero)
        {
            return;
        }

        nint toFree = buffer;
        buffer = nint.Zero;
        try
        {
            if (deleteAttributeList)
            {
                NativeMethods.DeleteProcThreadAttributeList(toFree);
            }

            Marshal.FreeHGlobal(toFree);
        }
        catch (Exception ex)
        {
            _teardownFailures.Add($"Free({label}) threw: {ex.Message}");
        }
    }

    private void StartDrains(TimeSpan timeout)
    {
        _stdOutDrain = Task.Run(() => DrainPipe(_parentStdOutRead, timeout));
        _stdErrDrain = Task.Run(() => DrainPipe(_parentStdErrRead, timeout));
    }

    private static (string Text, bool Truncated) DrainPipe(nint readHandle, TimeSpan timeout)
    {
        using var stream = new FileStream(
            new Microsoft.Win32.SafeHandles.SafeFileHandle(readHandle, ownsHandle: false),
            FileAccess.Read, bufferSize: 65536, isAsync: false);
        var buffer = new byte[65536];
        var text = new StringBuilder();
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            if (deadline.Elapsed > timeout)
            {
                return (text.ToString(), true);
            }

            int read;
            try
            {
                read = stream.Read(buffer, 0, buffer.Length);
            }
            catch (IOException)
            {
                // The pipe was force-closed during teardown before EOF was reached.
                return (text.ToString(), true);
            }

            if (read == 0)
            {
                return (text.ToString(), false);
            }

            text.Append(Encoding.UTF8.GetString(buffer, 0, read));
        }
    }

    private static async Task<(string Text, bool Truncated)> DrainOrTruncate(
        Task<(string Text, bool Truncated)>? drain)
    {
        // Each drain task is already internally bounded by its own DrainTimeout deadline (see
        // DrainPipe), so this only needs to await the already-bounded result.
        if (drain is null)
        {
            return (string.Empty, true);
        }

        return await drain.ConfigureAwait(false);
    }

    private bool WaitForSingleProcessExit(CancellationToken cancellationToken)
    {
        const int PollMs = 25;
        while (!cancellationToken.IsCancellationRequested)
        {
            uint result = NativeMethods.WaitForSingleObject(_processHandle, PollMs);
            if (result == NativeMethods.WAIT_OBJECT_0)
            {
                return true;
            }

            if (result != NativeMethods.WAIT_TIMEOUT)
            {
                return false;
            }
        }

        return false;
    }

    private void StartCompletionMonitor()
    {
        _completionMonitorCts = new CancellationTokenSource();
        var token = _completionMonitorCts.Token;
        nint port = _completionPortHandle;
        nint expectedKey = _jobHandle;
        _completionMonitor = Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                bool signalled = NativeMethods.GetQueuedCompletionStatus(
                    port, out uint bytes, out nint key, out _, 100);
                if (!signalled)
                {
                    continue;
                }

                if (key == expectedKey && bytes == NativeMethods.JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO)
                {
                    _activeProcessZeroObserved = true;
                    return;
                }
            }
        }, CancellationToken.None);
    }

    private async Task WaitForActiveProcessZero(CancellationToken cancellationToken)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!_activeProcessZeroObserved
            && deadline.Elapsed < TimeSpan.FromSeconds(10)
            && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(25, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static void CreatePipePair(out nint readHandle, out nint writeHandle)
    {
        var security = new NativeMethods.SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<NativeMethods.SECURITY_ATTRIBUTES>(),
            lpSecurityDescriptor = nint.Zero,
            bInheritHandle = false,
        };

        if (!NativeMethods.CreatePipe(out readHandle, out writeHandle, ref security, 0))
        {
            throw Win32("CreatePipe");
        }
    }

    private static void SetInheritable(nint handle)
    {
        if (!NativeMethods.SetHandleInformation(
            handle, NativeMethods.HANDLE_FLAG_INHERIT, NativeMethods.HANDLE_FLAG_INHERIT))
        {
            throw Win32("SetHandleInformation");
        }
    }

    private static nint BuildAttributeList(nint handleListBuffer, int handleCount, Stack<Action> unwind)
    {
        nint listSize = nint.Zero;
        NativeMethods.InitializeProcThreadAttributeList(nint.Zero, 1, 0, ref listSize);
        nint attributeListBuffer = Marshal.AllocHGlobal(listSize);
        unwind.Push(() =>
        {
            NativeMethods.DeleteProcThreadAttributeList(attributeListBuffer);
            Marshal.FreeHGlobal(attributeListBuffer);
        });

        if (!NativeMethods.InitializeProcThreadAttributeList(attributeListBuffer, 1, 0, ref listSize))
        {
            throw Win32("InitializeProcThreadAttributeList");
        }

        if (!NativeMethods.UpdateProcThreadAttribute(
            attributeListBuffer,
            0,
            NativeMethods.PROC_THREAD_ATTRIBUTE_HANDLE_LIST,
            handleListBuffer,
            (nint)(handleCount * nint.Size),
            nint.Zero,
            nint.Zero))
        {
            throw Win32("UpdateProcThreadAttribute");
        }

        return attributeListBuffer;
    }

    private static Win32Exception Win32(string apiName) =>
        new($"{apiName} failed with Win32 error {Marshal.GetLastWin32Error()}.");
}

/// <summary>Lightweight substitute for <c>System.ComponentModel.Win32Exception</c> to avoid a new dependency.</summary>
internal sealed class Win32Exception : Exception
{
    public Win32Exception(string message) : base(message)
    {
    }
}

internal static unsafe class NativeMethods
{
    internal const int CREATE_SUSPENDED = 0x00000004;
    internal const int CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    internal const int EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    internal const int STARTF_USESTDHANDLES = 0x00000100;
    internal const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
    internal const int JobObjectExtendedLimitInformation = 9;
    internal const int JobObjectAssociateCompletionPortInformation = 7;
    internal const uint JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO = 4;
    internal const nint PROC_THREAD_ATTRIBUTE_HANDLE_LIST = 0x00020002;
    internal const int HANDLE_FLAG_INHERIT = 1;
    internal const uint WAIT_OBJECT_0 = 0x00000000;
    internal const uint WAIT_TIMEOUT = 0x00000102;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct STARTUPINFOW
    {
        public int cb;
        public nint lpReserved;
        public nint lpDesktop;
        public nint lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public nint lpReserved2;
        public nint hStdInput;
        public nint hStdOutput;
        public nint hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct STARTUPINFOEXW
    {
        public STARTUPINFOW StartupInfo;
        public nint lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESS_INFORMATION
    {
        public nint hProcess;
        public nint hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public nint lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JOBOBJECT_ASSOCIATE_COMPLETION_PORT
    {
        public nint CompletionKey;
        public nint CompletionPort;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool CreateProcessW(
        string? lpApplicationName,
        nint lpCommandLine,
        nint lpProcessAttributes,
        nint lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        int dwCreationFlags,
        nint lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFOEXW lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CreatePipe(
        out nint hReadPipe, out nint hWritePipe, ref SECURITY_ATTRIBUTES lpPipeAttributes, uint nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool SetHandleInformation(nint hObject, int dwMask, int dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(nint hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint ResumeThread(nint hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TerminateProcess(nint hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TerminateJobObject(nint hJob, uint uExitCode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CreateJobObjectW(nint lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetInformationJobObject(
        nint hJob, int JobObjectInformationClass, nint lpJobObjectInformation, uint cbJobObjectInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AssignProcessToJobObject(nint hJob, nint hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint CreateIoCompletionPort(
        nint FileHandle, nint ExistingCompletionPort, nint CompletionKey, uint NumberOfConcurrentThreads);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetQueuedCompletionStatus(
        nint CompletionPort, out uint lpNumberOfBytesTransferred, out nint lpCompletionKey,
        out nint lpOverlapped, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InitializeProcThreadAttributeList(
        nint lpAttributeList, int dwAttributeCount, int dwFlags, ref nint lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateProcThreadAttribute(
        nint lpAttributeList, uint dwFlags, nint Attribute, nint lpValue, nint cbSize,
        nint lpPreviousValue, nint lpReturnSize);

    [DllImport("kernel32.dll")]
    internal static extern void DeleteProcThreadAttributeList(nint lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetExitCodeProcess(nint hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(nint hHandle, int dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DuplicateHandle(
        nint hSourceProcessHandle, nint hSourceHandle, nint hTargetProcessHandle, out nint lpTargetHandle,
        uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwOptions);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsProcessInJob(nint ProcessHandle, nint JobHandle, [MarshalAs(UnmanagedType.Bool)] out bool Result);
}
