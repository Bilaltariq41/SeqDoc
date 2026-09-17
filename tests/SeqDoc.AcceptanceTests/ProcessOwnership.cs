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

    /// <summary>
    /// GH106-R2-F1: faults after <see cref="ContainedProcess.StartDrains"/>/
    /// <see cref="ContainedProcess.StartCompletionMonitor"/> have started background work but before
    /// assign/resume completes, proving the unwind path cancels/awaits that background work instead of
    /// leaking it.
    /// </summary>
    AfterDrainsStartedBeforeAssign,
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

    internal ProcessOwnershipNativeCalls? NativeCalls { get; init; }
}

internal readonly record struct NativeCallResult(bool Succeeded, int Win32Error)
{
    internal static NativeCallResult Success() => new(true, 0);
    internal static NativeCallResult Failure(int error) => new(false, error);
}

internal readonly record struct NativeCallResult<T>(bool Succeeded, T Value, int Win32Error)
{
    internal static NativeCallResult<T> Success(T value) => new(true, value, 0);
    internal static NativeCallResult<T> Failure(int error) => new(false, default!, error);
}

internal readonly record struct NativeWaitResult(uint Result, int Win32Error)
{
    internal static NativeWaitResult Failed(int error) => new(NativeMethods.WAIT_FAILED, error);
}

internal sealed class ProcessOwnershipNativeCalls
{
    internal Func<nint, uint, NativeCallResult>? TerminateJobObject { get; init; }
    internal Func<nint, int, NativeWaitResult>? WaitForSingleObject { get; init; }
    internal Func<nint, nint, NativeCallResult>? AssignProcessToJobObject { get; init; }
    internal Func<nint, NativeCallResult<uint>>? ResumeThread { get; init; }
    internal Func<nint, uint, NativeCallResult>? TerminateProcess { get; init; }
    internal Func<nint, NativeCallResult<uint>>? PeekNamedPipe { get; init; }
    internal Func<nint, NativeCallResult>? CloseHandle { get; init; }
    internal Func<nint, NativeCallResult>? InitializeProcThreadAttributeList { get; init; }
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

    public IReadOnlyList<string> SecondaryFailures { get; internal set; } = Array.Empty<string>();

    public required ProcessOwnershipStreamResult StdOut { get; init; }

    public required ProcessOwnershipStreamResult StdErr { get; init; }
}

/// <summary>
/// First-write-wins tracker implementing the checkpoint's monotonic failure-class precedence: a later
/// class never overwrites an earlier one already recorded.
/// </summary>
internal sealed class FailureClassTracker
{
    private readonly object _gate = new();
    private readonly List<string> _secondaryFailures = new();
    private ProcessOwnershipFailureClass _class;
    private string? _detail;

    public ProcessOwnershipFailureClass Class { get { lock (_gate) { return _class; } } }

    public string? Detail { get { lock (_gate) { return _detail; } } }

    public bool HasFailure => Class != ProcessOwnershipFailureClass.None;

    public IReadOnlyList<string> SecondaryFailures
    {
        get { lock (_gate) { return _secondaryFailures.ToArray(); } }
    }

    public void Record(ProcessOwnershipFailureClass failureClass, string detail)
    {
        lock (_gate)
        {
            if (_class == ProcessOwnershipFailureClass.None)
            {
                _class = failureClass;
                _detail = detail;
            }
            else
            {
                _secondaryFailures.Add(detail);
            }
        }
    }

    public void RecordSecondary(string detail)
    {
        lock (_gate) { _secondaryFailures.Add(detail); }
    }
}

/// <summary>
/// Runtime platform admission gate (finalized contract decision 1): Windows x64 only. Exposed as a pure
/// evaluator over injected values so the negative claim (unsupported platform fails closed) can be
/// proven at the unit level without OS mocking, per the checkpoint's "unit-level fake/seam" requirement.
/// </summary>
public static class ProcessOwnershipPlatform
{
    public static bool IsSupported() => Evaluate(
        OperatingSystem.IsWindows(), RuntimeInformation.ProcessArchitecture, Environment.OSVersion.Version);

    internal static bool Evaluate(
        bool isWindows, System.Runtime.InteropServices.Architecture architecture, Version version) =>
        isWindows && architecture == System.Runtime.InteropServices.Architecture.X64
        && version >= new Version(10, 0);
}

/// <summary>
/// Windows argument (CommandLineToArgvW round-trip) and environment-block construction. Pure and
/// independently testable.
/// </summary>
internal static class ProcessOwnershipEncoding
{
    internal static string DecodeUtf8Chunks(IEnumerable<byte[]> chunks)
    {
        var decoder = Encoding.UTF8.GetDecoder();
        var chars = new char[4096];
        var text = new StringBuilder();
        foreach (byte[] chunk in chunks)
        {
            int count = decoder.GetChars(chunk, 0, chunk.Length, chars, 0, flush: false);
            text.Append(chars, 0, count);
        }

        int finalCount = decoder.GetChars(Array.Empty<byte>(), 0, 0, chars, 0, flush: true);
        text.Append(chars, 0, finalCount);
        return text.ToString();
    }

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
    /// Sorted case-insensitively (<c>OrdinalIgnoreCase</c>, matching Windows environment-variable
    /// semantics), deduplicated (last-write-wins on a case-insensitive duplicate key), double-null-
    /// terminated Unicode environment block for <c>CREATE_UNICODE_ENVIRONMENT</c>.
    /// </summary>
    internal static string BuildEnvironmentBlock(IReadOnlyDictionary<string, string> environment)
    {
        // GH106-R2-F9: Windows environment-variable names are case-insensitive, so dedup/sort must be
        // OrdinalIgnoreCase (matching Windows environment-block conventions) — not Ordinal, which would
        // let e.g. "Path" and "PATH" both survive as if they were distinct variables.
        var deduped = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
    private enum LifecycleState
    {
        Running,
        TerminalRequested,
        TerminalInProgress,
        FamilyProofCompleted,
        FamilyProofFailed,
        DisposalInProgress,
        FamilyResourcesRetained,
        Disposed,
        ConstructionUnwind,
    }

    private readonly FailureClassTracker _failures = new();
    private readonly List<string> _teardownFailures = new();
    private readonly List<string> _teardownOrderForTests = new();

    private nint _processHandle;
    private nint _threadHandle;
    private nint _jobHandle;
    private nint _completionPortHandle;
    private nint _attributeListBuffer;
    private nint _environmentBlockBuffer;
    private nint _commandLineBuffer;
    private nint _handleListBuffer;
    private nint _jobListBuffer;
    private nint _parentStdOutRead;
    private nint _parentStdErrRead;
    private nint _parentStdInWrite;

    private Task<(string Text, bool Truncated)>? _stdOutDrain;
    private Task<(string Text, bool Truncated)>? _stdErrDrain;
    private Task? _completionMonitor;
    private CancellationTokenSource? _completionMonitorCts;
    private CancellationTokenSource? _drainCts;
    private volatile bool _activeProcessZeroObserved;
    private volatile bool _completionObservationStoppedForTests;
    private bool _stdOutDrainCompleted;
    private bool _stdErrDrainCompleted;
    private bool _drainsCompletedBeforeCompletionProof;
    private bool _drainCompletionEvaluated;
    private bool _terminalRequiredByDrainCompletion;
    private bool _terminateJobObjectCalled;
    private bool _familyProofFailureRecorded;
    private bool _familyRetentionEvidenceRecorded;
    private bool _managedLifecycleFailureRecorded;
    private bool _teardownSummaryRecorded;
    private LifecycleState _lifecycleState = LifecycleState.Running;
    private readonly object _lifecycleGate = new();
    private readonly object _teardownEvidenceGate = new();
    private Task<bool>? _terminalTask;
    private Task<bool>? _familyProofTask;
    private Task? _disposeTask;
    private Task<ProcessOwnershipWaitResult>? _waitTask;
    private readonly ProcessOwnershipNativeCalls _nativeCalls;
    private int _peekFailureRecorded;

    // GH106-R2-F2: production default is 10s; only a test seam may shrink it (never reachable from a
    // production caller — there is no public setter).
    private TimeSpan _activeProcessZeroBound = TimeSpan.FromSeconds(10);
    private TimeSpan _constructionCleanupBound = TimeSpan.FromSeconds(5);
    private TimeSpan _drainTimeout = TimeSpan.FromSeconds(30);

    private ContainedProcess(ProcessOwnershipNativeCalls? nativeCalls)
    {
        _nativeCalls = nativeCalls ?? new ProcessOwnershipNativeCalls();
    }

    /// <summary>Test-only chronology seam — see the invocation site in <see cref="StartCore"/>.</summary>
    internal static Action<nint, nint>? AssignBeforeResumeHookForTests;

    internal static Action<nint, nint>? PostCreateProcessObserverForTests { get; set; }

    /// <summary>
    /// GH106-R2-F1 test-only seam: invoked with the constructed <see cref="ContainedProcess"/> right
    /// after background drains/completion-monitor work has started, so a test can capture the instance
    /// even when a subsequent fault point causes <see cref="Start(ProcessOwnershipOptions)"/> to report
    /// construction failure (which never returns a <see cref="ProcessOwnershipConstructionResult.Process"/>).
    /// </summary>
    internal static Action<ContainedProcess>? PostDrainsStartHookForTests;

    /// <summary>
    /// GH106-R2-F11 test-only seam: invoked with a resource label immediately before each partial-
    /// construction unwind step runs, proving the unwind stack's actual close/free order.
    /// </summary>
    internal static Action<string>? UnwindStepObserverForTests;

    public int ProcessId { get; private set; }

    /// <summary>
    /// GH106-R2-F7: the aggregated failure class, reflecting whatever <see cref="WaitAsync"/> and/or
    /// <see cref="Dispose"/> have recorded so far (including <see cref="ProcessOwnershipFailureClass.TeardownDegraded"/>
    /// after disposal) — a real caller's only way to learn teardown degraded, since prior test-only
    /// accessors are not production surface.
    /// </summary>
    public ProcessOwnershipFailureClass FailureClass => _failures.Class;

    /// <summary>GH106-R2-F7: the aggregated teardown failure details recorded during <see cref="Dispose"/>.</summary>
    public IReadOnlyList<string> TeardownFailures
    {
        get { lock (_teardownEvidenceGate) { return _teardownFailures.ToArray(); } }
    }

    /// <summary>Test-only observability: never used to gate production semantics.</summary>
    internal bool TerminateJobObjectWasCalled => _terminateJobObjectCalled;

    internal nint ProcessHandleForTests => _processHandle;

    internal bool HasRetainedFamilyResourcesForTests
    {
        get
        {
            lock (_lifecycleGate)
            {
                return _lifecycleState == LifecycleState.FamilyResourcesRetained
                    && HasOwnedTrackedResource();
            }
        }
    }

    internal static Action<nint>? AttributeListDeleteObserverForTests { get; set; }

    internal nint StdOutChildHandleValueForTests { get; private set; }

    internal nint StdErrChildHandleValueForTests { get; private set; }

    internal nint StdInChildHandleValueForTests { get; private set; }

    /// <summary>GH106-R2-F2 test-only seam: shrinks the active-zero proof bound; never used in production.</summary>
    internal TimeSpan ActiveProcessZeroBoundForTests { set => _activeProcessZeroBound = value; }

    internal TimeSpan ConstructionCleanupBoundForTests { get => _constructionCleanupBound; set => _constructionCleanupBound = value; }

    internal Action<string>? ResourceReleaseObserverForTests { get; set; }

    internal IReadOnlyList<string> SecondaryFailures => _failures.SecondaryFailures;

    /// <summary>GH106-R2-F2 test-only seam: forces the completion monitor to stop observing ACTIVE_PROCESS_ZERO.</summary>
    internal void StopCompletionMonitorForTests()
    {
        // This seam models an unavailable completion observation channel, even if a very fast child
        // posted its zero message before the test could cancel the monitor.
        _completionObservationStoppedForTests = true;
        lock (_lifecycleGate) { _activeProcessZeroObserved = false; }
        _completionMonitorCts?.Cancel();
    }

    internal Task? CompletionMonitorTaskForTests => _completionMonitor;

    internal Task<(string Text, bool Truncated)>? StdOutDrainTaskForTests => _stdOutDrain;

    internal Task<(string Text, bool Truncated)>? StdErrDrainTaskForTests => _stdErrDrain;

    /// <summary>GH106-R2-F11: ordered trace of every resource label actually closed/freed by <see cref="Dispose"/>.</summary>
    internal IReadOnlyList<string> TeardownOrderForTests
    {
        get { lock (_teardownEvidenceGate) { return _teardownOrderForTests.ToArray(); } }
    }

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
                + "Windows 10 / Server 2016 or newer. No PATH/version-floor fallback is attempted.");
        }

        // GH106-R2-F8/F13: reject embedded NUL and malformed environment-name vectors before any
        // filesystem probe or native call — native marshaling would otherwise silently truncate at the
        // first embedded NUL while construction still reported success.
        string? vectorError = ValidateVectors(options);
        if (vectorError is not null)
        {
            return ProcessOwnershipConstructionResult.Failure(vectorError);
        }

        if (!Path.IsPathRooted(options.ExecutablePath) || !File.Exists(options.ExecutablePath))
        {
            return ProcessOwnershipConstructionResult.Failure(
                $"Executable path '{options.ExecutablePath}' must be rooted and already exist; this "
                + "primitive never performs a PATH/cwd search (finalized contract decision 2).");
        }

        var unwind = new Stack<Action>();
        var unwindFailures = new List<string>();
        try
        {
            return StartCore(options, faultPoint, unwind, unwindFailures);
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

            string detail = unwindFailures.Count == 0 ? ex.Message
                : $"{ex.Message} Cleanup failures: {string.Join("; ", unwindFailures)}";
            return ProcessOwnershipConstructionResult.Failure(detail);
        }
    }

    /// <summary>GH106-R2-F8/F13: fails closed on embedded-NUL or malformed environment-name vectors.</summary>
    private static string? ValidateVectors(ProcessOwnershipOptions options)
    {
        if (options.ExecutablePath.Contains('\0'))
        {
            return "Executable path must not contain an embedded NUL character.";
        }

        foreach (string argument in options.Arguments)
        {
            if (argument.Contains('\0'))
            {
                return $"Argument '{argument}' must not contain an embedded NUL character.";
            }
        }

        foreach (var pair in options.Environment)
        {
            if (pair.Key.Contains('\0') || pair.Value.Contains('\0'))
            {
                return $"Environment variable '{pair.Key}' must not contain an embedded NUL character.";
            }

            if (pair.Key.Contains('='))
            {
                return $"Environment variable name '{pair.Key}' must not contain '=' (Windows "
                    + "environment-block encoding cannot represent this unambiguously).";
            }
        }

        return null;
    }

    private static ProcessOwnershipConstructionResult StartCore(
        ProcessOwnershipOptions options, ConstructionFaultPoint faultPoint, Stack<Action> unwind,
        List<string> unwindFailures)
    {
        bool backgroundQuiesced = true;
        // GH106-R2-F1: closures below capture these locals by reference. Guarding on non-zero and
        // zeroing after close means a handle already closed manually (see the S4 std-handle cleanup
        // below) can never be double-closed by a later unwind pop — the exact "closed-once" idiom
        // Dispose()'s CloseTracked already uses.
        static void CloseIfOpen(ref nint handle)
        {
            if (handle != nint.Zero)
            {
                NativeMethods.CloseHandle(handle);
                handle = nint.Zero;
            }
        }

        // GH106-R2-F11 test-only observability: records the exact label order the unwind stack actually
        // pops in, without changing production behavior (the observer is null in production).
        void PushUnwind(string label, Action action) => unwind.Push(() =>
        {
            if (!backgroundQuiesced && label != "background drains and completion monitor")
            {
                unwindFailures.Add($"{label} retained because background work did not quiesce.");
                return;
            }
            try { action(); }
            catch (Exception ex) { unwindFailures.Add($"{label}: {ex.Message}"); }
            finally { UnwindStepObserverForTests?.Invoke(label); }
        });

        // --- S1: three std pipes, created non-inheritable by default, then the exact child-side end of
        // each is explicitly marked inheritable (never the parent-side end). ---
        CreatePipePair(out nint stdInRead, out nint stdInWrite);
        PushUnwind("stdin read handle", () => CloseIfOpen(ref stdInRead));
        PushUnwind("stdin write handle", () => CloseIfOpen(ref stdInWrite));
        CreatePipePair(out nint stdOutRead, out nint stdOutWrite);
        PushUnwind("stdout read handle", () => CloseIfOpen(ref stdOutRead));
        PushUnwind("stdout write handle", () => CloseIfOpen(ref stdOutWrite));
        CreatePipePair(out nint stdErrRead, out nint stdErrWrite);
        PushUnwind("stderr read handle", () => CloseIfOpen(ref stdErrRead));
        PushUnwind("stderr write handle", () => CloseIfOpen(ref stdErrWrite));

        SetInheritable(stdInRead);
        SetInheritable(stdOutWrite);
        SetInheritable(stdErrWrite);

        if (faultPoint == ConstructionFaultPoint.AfterPipesCreated)
        {
            throw new InvalidOperationException("fault-injected: AfterPipesCreated");
        }

        // --- S2: persistent handle-list payload restricting inheritance to exactly the 3 child-side handles. ---
        nint[] inheritable = [stdInRead, stdOutWrite, stdErrWrite];
        nint handleListBuffer = Marshal.AllocHGlobal(nint.Size * inheritable.Length);
        PushUnwind("handle list buffer", () => Marshal.FreeHGlobal(handleListBuffer));

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

        // --- S3: Job Object with kill-on-close and breakaway denied. ---
        nint jobHandle = NativeMethods.CreateJobObjectW(nint.Zero, null);
        if (jobHandle == nint.Zero)
        {
            throw Win32("CreateJobObjectW");
        }

        PushUnwind("job handle", () => NativeMethods.CloseHandle(jobHandle));

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

        // --- S3.5: IOCP associated to the inactive job so ACTIVE_PROCESS_ZERO can be observed later. ---
        nint completionPort = NativeMethods.CreateIoCompletionPort(
            new nint(-1), nint.Zero, nint.Zero, 1);
        if (completionPort == nint.Zero)
        {
            throw Win32("CreateIoCompletionPort");
        }

        PushUnwind("completion port handle", () => NativeMethods.CloseHandle(completionPort));

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

        // JOB_LIST is a creation-time admission payload. Keep it alive until the attribute list has
        // been deleted during teardown; freeing it after CreateProcess would violate the native API's
        // lifetime contract.
        nint jobListBuffer = Marshal.AllocHGlobal(nint.Size);
        Marshal.WriteIntPtr(jobListBuffer, jobHandle);
        PushUnwind("job list buffer", () => Marshal.FreeHGlobal(jobListBuffer));

        nint attributeListBuffer = BuildAttributeList(
            handleListBuffer, inheritable.Length, jobListBuffer, unwind, options.NativeCalls);
        PushUnwind("attribute list buffer", () =>
        {
            DeleteAttributeList(attributeListBuffer);
            Marshal.FreeHGlobal(attributeListBuffer);
        });

        if (faultPoint == ConstructionFaultPoint.AfterAttributeListBuilt)
        {
            throw new InvalidOperationException("fault-injected: AfterAttributeListBuilt");
        }

        // --- S4: CreateProcessW, suspended, with the restricted attribute list. ---
        string commandLine = ProcessOwnershipEncoding.BuildCommandLine(options.ExecutablePath, options.Arguments);
        nint commandLineBuffer = Marshal.StringToHGlobalUni(commandLine);
        PushUnwind("command line buffer", () => Marshal.FreeHGlobal(commandLineBuffer));

        string environmentBlock = ProcessOwnershipEncoding.BuildEnvironmentBlock(options.Environment);
        nint environmentBuffer = Marshal.StringToHGlobalUni(environmentBlock);
        PushUnwind("environment block buffer", () => Marshal.FreeHGlobal(environmentBuffer));

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

        PushUnwind("thread handle", () => NativeMethods.CloseHandle(processInformation.hThread));
        bool constructionTerminationAttempted = false;
        PushUnwind("process handle", () =>
        {
            if (!constructionTerminationAttempted)
            {
                NativeCallResult termination = InvokeTerminateProcess(options, processInformation.hProcess);
                if (!termination.Succeeded)
                {
                    unwindFailures.Add($"TerminateProcess failed with Win32 error {termination.Win32Error}.");
                }
            }
            constructionTerminationAttempted = true;
            NativeMethods.CloseHandle(processInformation.hProcess);
        });

        // JOB_LIST admission has completed atomically with CreateProcess. Observe it immediately after
        // both process/thread unwind entries exist, while the new thread is still suspended.
        PostCreateProcessObserverForTests?.Invoke(jobHandle, processInformation.hProcess);

        if (faultPoint == ConstructionFaultPoint.AfterProcessCreatedBeforeAssign)
        {
            // Deliberately fault before any background reader/monitor thread exists and before the
            // child-side pipe handles are closed, so unwind only has to reverse plain handle/buffer
            // acquisitions here — never a pending synchronous read racing a CloseHandle.
            throw new InvalidOperationException("fault-injected: AfterProcessCreatedBeforeAssign");
        }

        // The child-side pipe ends have been duplicated into the child's handle table by inheritance;
        // the parent no longer needs (and must not keep) them open, or EOF on the parent's read ends
        // would never be observable once the child itself exits. GH106-R2-F1: zero the locals after
        // closing (shared "closed-once" state with the unwind closures above, which capture these same
        // locals by reference) so a later unwind pop can never double-close an already-closed handle.
        //
        // GH106-R2-F6: also close the parent's own stdin *write* end immediately. This checkpoint scopes
        // out interactive stdin input entirely, so closing it here (rather than only in Dispose) gives
        // any stdin-reading child immediate EOF instead of an unusable, silently-retained handle.
        //
        // CloseIfOpen zeroes the ref'd local, so capture the raw handle *values* (still meaningful as
        // identifiers for the child's inherited handle table entries, even once closed on the parent
        // side) before closing, for the test-only observability properties below.
        nint stdOutChildHandleValue = stdOutWrite;
        nint stdErrChildHandleValue = stdErrWrite;
        nint stdInChildHandleValue = stdInRead;

        CloseIfOpen(ref stdInRead);
        CloseIfOpen(ref stdOutWrite);
        CloseIfOpen(ref stdErrWrite);
        CloseIfOpen(ref stdInWrite);

        var process = new ContainedProcess(options.NativeCalls)
        {
            _processHandle = processInformation.hProcess,
            _threadHandle = processInformation.hThread,
            _jobHandle = jobHandle,
            _completionPortHandle = completionPort,
            _attributeListBuffer = attributeListBuffer,
            _environmentBlockBuffer = environmentBuffer,
            _commandLineBuffer = commandLineBuffer,
            _handleListBuffer = handleListBuffer,
            _jobListBuffer = jobListBuffer,
            _parentStdOutRead = stdOutRead,
            _parentStdErrRead = stdErrRead,
            _parentStdInWrite = stdInWrite,
            ProcessId = processInformation.dwProcessId,
            StdOutChildHandleValueForTests = stdOutChildHandleValue,
            StdErrChildHandleValueForTests = stdErrChildHandleValue,
            StdInChildHandleValueForTests = stdInChildHandleValue,
        };

        // Reads and the completion-port monitor are both started before ResumeThread returns control to
        // any wait loop (contract point 5 / 4): the child can produce output or exit the instant it is
        // resumed, and nothing here may race that.
        process.StartDrains(options.DrainTimeout);
        process.StartCompletionMonitor();

        // GH106-R2-F1: a failure past this point (the test hook or
        // ResumeThread) must not leak the background drain/completion-monitor tasks just started. This
        // single unwind entry is the one cleanup mechanism for that background work — terminating the
        // process directly (rather than relying on pop order against the separately-pushed "process
        // handle" entry, since Stack<Action> pops most-recently-pushed first and this entry is pushed
        // after that one) is what actually unblocks a blocked pipe Read() so the bounded awaits below
        // return quickly instead of running out their full budget.
        PushUnwind("background drains and completion monitor", () =>
        {
            NativeCallResult termination = InvokeTerminateProcess(options, processInformation.hProcess);
            constructionTerminationAttempted = true;
            if (!termination.Succeeded)
            {
                unwindFailures.Add($"TerminateProcess failed with Win32 error {termination.Win32Error}.");
            }
            NativeWaitResult wait = InvokeWaitForSingleObject(options, processInformation.hProcess,
                (int)Math.Min(int.MaxValue, process.ConstructionCleanupBoundForTests.TotalMilliseconds));
            if (wait.Result != NativeMethods.WAIT_OBJECT_0)
            {
                unwindFailures.Add($"WaitForSingleObject failed with Win32 error {wait.Win32Error}.");
            }
            process._completionMonitorCts?.Cancel();
            process._drainCts?.Cancel();
            process.WaitConstructionTask(process._completionMonitor, "completion monitor", unwindFailures);
            process.WaitConstructionTask(process._stdOutDrain, "stdout drain", unwindFailures);
            process.WaitConstructionTask(process._stdErrDrain, "stderr drain", unwindFailures);
            backgroundQuiesced = (process._completionMonitor is null || process._completionMonitor.IsCompleted)
                && (process._stdOutDrain is null || process._stdOutDrain.IsCompleted)
                && (process._stdErrDrain is null || process._stdErrDrain.IsCompleted);
        });

        PostDrainsStartHookForTests?.Invoke(process);

        if (faultPoint == ConstructionFaultPoint.AfterDrainsStartedBeforeAssign)
        {
            throw new InvalidOperationException("fault-injected: AfterDrainsStartedBeforeAssign");
        }

        // Historical pre-resume membership observer; it is no longer an assignment point.
        AssignBeforeResumeHookForTests?.Invoke(jobHandle, processInformation.hProcess);

        // --- S6: resume. ---
        NativeCallResult<uint> resume;
        if (options.NativeCalls?.ResumeThread is not null)
        {
            resume = options.NativeCalls.ResumeThread(processInformation.hThread);
        }
        else
        {
            uint value = NativeMethods.ResumeThread(processInformation.hThread);
            resume = value == uint.MaxValue
                ? NativeCallResult<uint>.Failure(Marshal.GetLastWin32Error())
                : NativeCallResult<uint>.Success(value);
        }
        if (!resume.Succeeded)
        {
            throw Win32("ResumeThread", resume.Win32Error);
        }

        unwind.Clear();
        return ProcessOwnershipConstructionResult.Success(process);
    }

    public Task<ProcessOwnershipWaitResult> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(
                _lifecycleState is LifecycleState.DisposalInProgress or LifecycleState.Disposed, this);
            return _waitTask ??= WaitCoreAsync(timeout, cancellationToken);
        }
    }

    private async Task<ProcessOwnershipWaitResult> WaitCoreAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

        int exitCode = 0;
        bool exited = false;
        bool waitFailed = false;
        bool deadlineExpired = false;
        bool callerCancelled = false;
        try
        {
            var outcome = await Task.Run(() => WaitForSingleProcessExit(linked.Token), CancellationToken.None)
                .ConfigureAwait(false);
            exited = outcome.Exited;
            waitFailed = outcome.WaitFailed;
            callerCancelled = cancellationToken.IsCancellationRequested;
            deadlineExpired = timeoutCts.IsCancellationRequested;

            // GH106-R2-F5: a genuine WAIT_FAILED/WAIT_ABANDONED result is a real wait failure, not a
            // benign timeout — record it distinctly so it is never misreported as TimedOut.
            if (outcome.WaitFailed)
            {
                _failures.Record(
                    ProcessOwnershipFailureClass.ProcessFailed,
                    $"WaitForSingleObject failed with Win32 error {outcome.Win32Error}.");
            }
        }
        catch (OperationCanceledException)
        {
            exited = false;
            callerCancelled = cancellationToken.IsCancellationRequested;
            deadlineExpired = timeoutCts.IsCancellationRequested;
        }

        if (!exited)
        {
            if (callerCancelled)
            {
                _failures.Record(ProcessOwnershipFailureClass.TimedOut, "Wait was cancelled by the caller.");
            }
            else if (deadlineExpired || !waitFailed)
            {
                _failures.Record(ProcessOwnershipFailureClass.TimedOut, $"Wait exceeded {timeout}.");
            }

            // Admission table: explicit TerminateJobObject for timeout/cancellation (never deferred to
            // Dispose's kill-on-close). This also unblocks the concurrent stream drains below by making
            // the child's inherited pipe handles close, which yields EOF on the parent's read ends.
            await EnsureTerminalAsync().ConfigureAwait(false);

            // Ensure the shared uncancelled proof task records the family outcome before the result is
            // ever snapshotted. Forced cleanup must not erase a ProcessFailed proof failure.
            await EnsureFamilyProofAsync(CancellationToken.None).ConfigureAwait(false);

            // GH106-R2-F3: terminate AND await family exit — a forced termination without waiting for
            // proof is only half the checkpoint's "terminate and await" contract. `linked` is already
            // cancelled here (that is exactly why this branch was reached), so a fresh, uncancelled token
            // is required for the bound below to genuinely apply rather than returning instantly.
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
            Task familyProof = EnsureFamilyProofAsync(CancellationToken.None);
            Task drainCompletion = Task.WhenAll(DrainOrTruncate(_stdOutDrain), DrainOrTruncate(_stdErrDrain));
            Task waitDeadline = Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
            Task raceWinner = await Task.WhenAny(familyProof, drainCompletion, waitDeadline).ConfigureAwait(false);
            bool activeZeroObservedAtRace = _activeProcessZeroObserved;
            if (raceWinner != familyProof && !activeZeroObservedAtRace)
            {
                _failures.Record(
                    ProcessOwnershipFailureClass.ProcessFailed,
                    "Family exit was not proven before forced terminal enforcement.");
            }
            if (raceWinner == waitDeadline)
            {
                await EnsureTerminalAsync().ConfigureAwait(false);
            }
            else if (drainCompletion.IsCompleted)
            {
                await EvaluateCompletedDrainsAsync().ConfigureAwait(false);
            }

            // Complete the shared proof task before constructing the immutable wait result. If proof
            // failed, retain its ProcessFailed classification even when forced cleanup follows.
            bool familyProven = await EnsureFamilyProofAsync(CancellationToken.None).ConfigureAwait(false);
            if (!familyProven)
            {
                await EnsureTerminalAsync().ConfigureAwait(false);
            }
        }

        // Bound the drain: DrainPipe's own deadline check only runs between completed reads, so it
        // cannot interrupt a single blocked FileStream.Read call held open by a live job member (for
        // example, a descendant that inherited the write handle but never writes and never exits). Race
        // the drain tasks against the same `linked` deadline WaitAsync already constructs from its own
        // `timeout` and `cancellationToken`; if the deadline wins and TerminateJobObject has not already
        // been called (the exited == false branch above already calls it), call it now. That force-closes
        // every handle held by every process in the job — including the silent descendant's inherited
        // write end — which is what actually unblocks the in-flight blocked Read() (via EOF or the
        // existing IOException catch in DrainPipe). No change to DrainPipe itself is required: once
        // unblocked, its existing EOF/IOException handling completes it and marks truncation.
        var stdOutDrainTask = DrainOrTruncate(_stdOutDrain);
        var stdErrDrainTask = DrainOrTruncate(_stdErrDrain);
        var drainsTask = Task.WhenAll(stdOutDrainTask, stdErrDrainTask);
        var deadlineTask = Task.Delay(_drainTimeout, linked.Token);
        var drainRaceWinner = await Task.WhenAny(drainsTask, deadlineTask).ConfigureAwait(false);
        bool drainDeadlineForcedTermination = drainRaceWinner == deadlineTask;
        if (drainDeadlineForcedTermination)
        {
            bool terminalSucceeded = await EnsureTerminalAsync().ConfigureAwait(false);
            if (!terminalSucceeded)
            {
                // A failed terminal operation cannot leave a synchronous read live indefinitely. On
                // success, leave drains running to consume buffered bytes and observe EOF.
                _drainCts?.Cancel();
            }
        }
        else
        {
            await EvaluateCompletedDrainsAsync().ConfigureAwait(false);
        }

        var (stdOutText, stdOutTruncated) = await AwaitDrainBounded(stdOutDrainTask).ConfigureAwait(false);
        var (stdErrText, stdErrTruncated) = await AwaitDrainBounded(stdErrDrainTask).ConfigureAwait(false);
        if (drainDeadlineForcedTermination)
        {
            // Force-closing every job member's handles to unblock a stuck read yields a real, clean EOF
            // rather than an I/O error on Windows anonymous pipes, so DrainPipe's own truncated flag can
            // legitimately come back false even though we had to intervene. That intervention itself
            // means the drain did not reach EOF within its own bound naturally, so it is recorded as
            // DrainIncomplete regardless — never TimedOut, since the process itself already exited (or,
            // for the exited == false branch above, TimedOut was already recorded first and wins by the
            // tracker's first-recorded precedence).
            _failures.Record(
                ProcessOwnershipFailureClass.DrainIncomplete,
                "A stream did not reach EOF within the wait's own bound; job termination was forced to unblock it.");
        }

        if (stdOutTruncated || stdErrTruncated)
        {
            _failures.Record(ProcessOwnershipFailureClass.DrainIncomplete, "A stream did not reach EOF in time.");
        }

        return new ProcessOwnershipWaitResult
        {
            FailureClass = _failures.Class,
            Detail = _failures.Detail,
            ExitCode = exited ? exitCode : null,
            TimedOut = !exited && deadlineExpired && !callerCancelled && !waitFailed,
            Cancelled = !exited && callerCancelled && !waitFailed,
            ActiveProcessZeroObserved = _activeProcessZeroObserved,
            SecondaryFailures = _failures.SecondaryFailures.ToArray(),
            StdOut = new ProcessOwnershipStreamResult(stdOutText, stdOutTruncated),
            StdErr = new ProcessOwnershipStreamResult(stdErrText, stdErrTruncated),
        };
    }

    /// <summary>Query phase: has ACTIVE_PROCESS_ZERO been observed for this job yet?</summary>
    public bool HasObservedActiveProcessZero() => _activeProcessZeroObserved;

    /// <summary>Terminate phase: forcibly ends every process in the job.</summary>
    public bool Terminate()
    {
        lock (_lifecycleGate) { ObjectDisposedException.ThrowIf(_lifecycleState == LifecycleState.Disposed, this); }
        return EnsureTerminalAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        Task disposeTask;
        lock (_lifecycleGate)
        {
            if (_lifecycleState == LifecycleState.Disposed)
            {
                return;
            }

            // Claim disposal synchronously, before scheduling any asynchronous cleanup.  In particular,
            // this closes the interval in which a caller could otherwise publish a new shared wait after
            // disposal had begun but before DisposeCoreAsync reached its first lifecycle lock.
            if (_disposeTask is null)
            {
                bool retryRetainedProof = _lifecycleState == LifecycleState.FamilyResourcesRetained;
                _lifecycleState = LifecycleState.DisposalInProgress;
                if (retryRetainedProof)
                {
                    // The previous bounded proof attempt is no longer authoritative: a completion
                    // notification may have arrived while the retained state was idle.
                    _familyProofTask = null;
                }
                _disposeTask = Task.Run(DisposeCoreAsync);
            }

            disposeTask = _disposeTask;
        }

        disposeTask.GetAwaiter().GetResult();
    }

    private async Task DisposeCoreAsync()
    {
        if (!_activeProcessZeroObserved || _terminalTask is not null)
        {
            await EnsureTerminalAsync().ConfigureAwait(false);
        }
        bool familyProven = await EnsureFamilyProofAsync().ConfigureAwait(false);
        _drainCts?.Cancel();
        bool drainsComplete = await AwaitTaskBounded(_stdOutDrain, _constructionCleanupBound).ConfigureAwait(false)
            & await AwaitTaskBounded(_stdErrDrain, _constructionCleanupBound).ConfigureAwait(false);
        if (familyProven)
        {
            _completionMonitorCts?.Cancel();
        }
        bool monitorComplete = await AwaitTaskBounded(_completionMonitor, _constructionCleanupBound).ConfigureAwait(false);
        if (!drainsComplete || !monitorComplete)
        {
            bool recordLifecycleFailure;
            lock (_lifecycleGate)
            {
                recordLifecycleFailure = !_managedLifecycleFailureRecorded;
                _managedLifecycleFailureRecorded = true;
            }
            if (recordLifecycleFailure)
            {
                _failures.Record(ProcessOwnershipFailureClass.DrainIncomplete,
                    "Managed lifecycle work did not quiesce within the cleanup bound; owned handles were retained.");
            }
        }

        // WaitCore can still be between its process wait and GetExitCodeProcess/Job Object accounting.
        // Snapshot the already-published shared task without holding the lifecycle gate while waiting;
        // closing either native handle before this task quiesces would make that work use a released
        // (and potentially reused) handle.  A fault is observed deliberately so its exact evidence is
        // not lost behind Task.WhenAny-style completion handling.
        Task<ProcessOwnershipWaitResult>? waitTask;
        lock (_lifecycleGate) { waitTask = _waitTask; }
        bool waitComplete = await AwaitTaskBounded(waitTask, _constructionCleanupBound).ConfigureAwait(false);
        if (waitTask is not null && waitComplete)
        {
            try
            {
                await waitTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _failures.Record(ProcessOwnershipFailureClass.ProcessFailed,
                    $"WaitAsync failed during disposal: {ex.Message}");
            }
        }
        else if (waitTask is not null)
        {
            bool recordWaitFailure;
            lock (_lifecycleGate)
            {
                recordWaitFailure = !_managedLifecycleFailureRecorded;
                _managedLifecycleFailureRecorded = true;
            }
            if (recordWaitFailure)
            {
                _failures.Record(ProcessOwnershipFailureClass.TeardownDegraded,
                    $"WaitAsync did not quiesce within {_constructionCleanupBound}; process and job handles were retained.");
            }
        }

        // A live wait is the last permitted user of the process handle; the completion monitor is the
        // corresponding last user of the job handle.  Retain both when either proof is incomplete rather
        // than reporting successful teardown after closing beneath live work.
        familyProven = await EnsureFamilyProofAsync(CancellationToken.None).ConfigureAwait(false);
        bool nativeHandlesSafe = familyProven && waitComplete && monitorComplete;
        if (!familyProven)
        {
            bool recordRetention;
            lock (_lifecycleGate)
            {
                recordRetention = !_familyRetentionEvidenceRecorded;
                _familyRetentionEvidenceRecorded = true;
            }
            if (recordRetention)
            {
                lock (_teardownEvidenceGate)
                {
                    _teardownFailures.Add(
                        "ACTIVE_PROCESS_ZERO was not proven; retained process handle, completion port handle, and job handle.");
                }
                _failures.Record(ProcessOwnershipFailureClass.TeardownDegraded,
                    "ACTIVE_PROCESS_ZERO was not proven; retained process handle, completion port handle, and job handle.");
            }
        }
        if (familyProven && monitorComplete)
        {
            CancellationTokenSource? completionMonitorCts = _completionMonitorCts;
            _completionMonitorCts = null;
            completionMonitorCts?.Dispose();
        }
        if (drainsComplete)
        {
            CancellationTokenSource? drainCts = _drainCts;
            _drainCts = null;
            drainCts?.Dispose();
        }
        if (nativeHandlesSafe)
        {
            CloseTracked(ref _processHandle, "process handle");
        }
        CloseTracked(ref _threadHandle, "thread handle");
        FreeTracked(ref _environmentBlockBuffer, "environment block buffer", deleteAttributeList: false);
        FreeTracked(ref _commandLineBuffer, "command line buffer", deleteAttributeList: false);
        FreeTracked(ref _attributeListBuffer, "attribute list buffer", deleteAttributeList: true);
        FreeTracked(ref _jobListBuffer, "job list buffer", deleteAttributeList: false);
        if (nativeHandlesSafe)
        {
            CloseTracked(ref _completionPortHandle, "completion port handle");
            CloseTracked(ref _jobHandle, "job handle");
        }
        FreeTracked(ref _handleListBuffer, "handle list buffer", deleteAttributeList: false);
        if (drainsComplete)
        {
            CloseTracked(ref _parentStdErrRead, "stderr pipe handle");
            CloseTracked(ref _parentStdOutRead, "stdout pipe handle");
        }
        CloseTracked(ref _parentStdInWrite, "stdin pipe handle");
        string[] teardownFailures;
        lock (_teardownEvidenceGate) { teardownFailures = _teardownFailures.ToArray(); }
        if (teardownFailures.Length > 0)
        {
            bool recordSummary;
            lock (_lifecycleGate)
            {
                recordSummary = !_teardownSummaryRecorded;
                _teardownSummaryRecorded = true;
            }
            if (recordSummary)
            {
                _failures.Record(ProcessOwnershipFailureClass.TeardownDegraded,
                    $"{teardownFailures.Length} teardown step(s) failed: {string.Join("; ", teardownFailures)}");
            }
        }
        lock (_lifecycleGate)
        {
            bool resourcesRetained = !nativeHandlesSafe || HasOwnedTrackedResource();
            _lifecycleState = resourcesRetained
                ? LifecycleState.FamilyResourcesRetained
                : LifecycleState.Disposed;
            _disposeTask = null;
        }
    }

    private Task<bool> EnsureTerminalAsync()
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_lifecycleState == LifecycleState.Disposed, this);
            if (_terminalTask is null && _lifecycleState != LifecycleState.DisposalInProgress)
            {
                _lifecycleState = LifecycleState.TerminalRequested;
            }
            return _terminalTask ??= RunTerminalAsync();
        }
    }

    private async Task<bool> RunTerminalAsync()
    {
        // Never execute an injected/native call while the lifecycle monitor is held. Apart from avoiding
        // re-entrancy deadlocks, this lets Dispose join the same idempotent terminal operation.
        await Task.Yield();
        lock (_lifecycleGate)
        {
            if (_lifecycleState != LifecycleState.DisposalInProgress)
            {
                _lifecycleState = LifecycleState.TerminalInProgress;
            }
        }
        if (_jobHandle == nint.Zero) { return true; }
        _terminateJobObjectCalled = true;
        NativeCallResult result = InvokeTerminateJobObject(_jobHandle, uint.MaxValue);
        if (!result.Succeeded)
        {
            _failures.Record(ProcessOwnershipFailureClass.ProcessFailed,
                $"TerminateJobObject failed with Win32 error {result.Win32Error}.");
        }

        bool familyProven = await EnsureFamilyProofAsync(CancellationToken.None).ConfigureAwait(false);
        return result.Succeeded && familyProven;
    }

    private Task<bool> EnsureFamilyProofAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycleGate) { return _familyProofTask ??= RunFamilyProofAsync(cancellationToken); }
    }

    private void MarkDrainCompleted(bool standardError)
    {
        lock (_lifecycleGate)
        {
            if (standardError)
            {
                _stdErrDrainCompleted = true;
            }
            else
            {
                _stdOutDrainCompleted = true;
            }

            if (_stdOutDrainCompleted && _stdErrDrainCompleted && !_activeProcessZeroObserved)
            {
                _drainsCompletedBeforeCompletionProof = true;
            }
        }
    }

    private async Task EvaluateCompletedDrainsAsync()
    {
        lock (_lifecycleGate)
        {
            if (!_drainsCompletedBeforeCompletionProof || _drainCompletionEvaluated)
            {
                return;
            }

            _drainCompletionEvaluated = true;
        }

        NativeCallResult<uint> activeProcesses = QueryActiveProcessCount();
        if (!activeProcesses.Succeeded)
        {
            _failures.Record(
                ProcessOwnershipFailureClass.ProcessFailed,
                $"QueryInformationJobObject failed with Win32 error {activeProcesses.Win32Error}.");
            lock (_lifecycleGate) { _terminalRequiredByDrainCompletion = true; }
        }
        else if (activeProcesses.Value > 0)
        {
            _failures.Record(
                ProcessOwnershipFailureClass.ProcessFailed,
                "Family exit was not proven before forced terminal enforcement.");
            lock (_lifecycleGate) { _terminalRequiredByDrainCompletion = true; }
        }

        bool terminalRequired;
        lock (_lifecycleGate) { terminalRequired = _terminalRequiredByDrainCompletion; }
        if (terminalRequired)
        {
            await EnsureTerminalAsync().ConfigureAwait(false);
        }
    }

    private async Task<bool> RunFamilyProofAsync(CancellationToken cancellationToken)
    {
        await WaitForActiveProcessZero(cancellationToken).ConfigureAwait(false);
        bool proven = _activeProcessZeroObserved && !_completionObservationStoppedForTests;
        if (!proven)
        {
            bool recordFailure;
            lock (_lifecycleGate)
            {
                recordFailure = !_familyProofFailureRecorded;
                _familyProofFailureRecorded = true;
                if (_lifecycleState != LifecycleState.DisposalInProgress)
                {
                    _lifecycleState = LifecycleState.FamilyProofFailed;
                }
            }
            if (recordFailure)
            {
                _failures.Record(ProcessOwnershipFailureClass.ProcessFailed,
                    "Family exit could not be proven within the bound: ACTIVE_PROCESS_ZERO was not observed.");
            }
        }
        else
        {
            lock (_lifecycleGate)
            {
                if (_lifecycleState != LifecycleState.DisposalInProgress)
                {
                    _lifecycleState = LifecycleState.FamilyProofCompleted;
                }
            }
        }
        if (proven)
        {
            // Once ACTIVE_PROCESS_ZERO is proven, no later completion notification is needed.
            // On failure, retain the monitor so a later retained-state retry can still observe it.
            _completionMonitorCts?.Cancel();
        }
        return proven;
    }

    private async Task<(string Text, bool Truncated)> AwaitDrainBounded(Task<(string Text, bool Truncated)> task)
    {
        if (await AwaitTaskBounded(task, _drainTimeout).ConfigureAwait(false))
        {
            return await task.ConfigureAwait(false);
        }
        _failures.Record(ProcessOwnershipFailureClass.DrainIncomplete, "A stream did not reach EOF within the cleanup bound.");
        return (string.Empty, true);
    }

    private static async Task<bool> AwaitTaskBounded(Task? task, TimeSpan bound)
    {
        if (task is null)
        {
            return true;
        }
        Task winner = await Task.WhenAny(task, Task.Delay(bound)).ConfigureAwait(false);
        return winner == task;
    }

    private bool HasOwnedTrackedResource() =>
        _processHandle != nint.Zero
        || _threadHandle != nint.Zero
        || _jobHandle != nint.Zero
        || _completionPortHandle != nint.Zero
        || _attributeListBuffer != nint.Zero
        || _environmentBlockBuffer != nint.Zero
        || _commandLineBuffer != nint.Zero
        || _handleListBuffer != nint.Zero
        || _jobListBuffer != nint.Zero
        || _parentStdOutRead != nint.Zero
        || _parentStdErrRead != nint.Zero
        || _parentStdInWrite != nint.Zero;

    private void WaitConstructionTask(Task? task, string label, List<string> failures)
    {
        if (task is null) { return; }
        try
        {
            if (!task.Wait(_constructionCleanupBound))
            {
                failures.Add($"{label} did not complete within {_constructionCleanupBound}.");
            }
        }
        catch (AggregateException ex)
        {
            failures.Add($"{label} failed: {ex.InnerException?.Message ?? ex.Message}");
        }
    }

    private static NativeCallResult InvokeTerminateProcess(ProcessOwnershipOptions options, nint handle)
    {
        if (options.NativeCalls?.TerminateProcess is not null)
        {
            return options.NativeCalls.TerminateProcess(handle, uint.MaxValue);
        }
        return NativeMethods.TerminateProcess(handle, uint.MaxValue)
            ? NativeCallResult.Success() : NativeCallResult.Failure(Marshal.GetLastWin32Error());
    }

    private static NativeWaitResult InvokeWaitForSingleObject(ProcessOwnershipOptions options, nint handle, int timeout)
    {
        if (options.NativeCalls?.WaitForSingleObject is not null)
        {
            return options.NativeCalls.WaitForSingleObject(handle, timeout);
        }
        uint result = NativeMethods.WaitForSingleObject(handle, timeout);
        return new NativeWaitResult(result, result == NativeMethods.WAIT_FAILED ? Marshal.GetLastWin32Error() : 0);
    }

    /// <summary>Aggregated teardown failures recorded during <see cref="Dispose"/>, if any.</summary>
    internal IReadOnlyList<string> TeardownFailuresForTests
    {
        get { lock (_teardownEvidenceGate) { return _teardownFailures.ToArray(); } }
    }

    internal ProcessOwnershipFailureClass RecordedFailureClassForTests => _failures.Class;

    private void CloseTracked(ref nint handle, string label)
    {
        if (handle == nint.Zero)
        {
            return;
        }

        nint toClose = handle;
        try
        {
            ResourceReleaseObserverForTests?.Invoke(label);
            NativeCallResult result = _nativeCalls.CloseHandle?.Invoke(toClose)
                ?? (NativeMethods.CloseHandle(toClose)
                    ? NativeCallResult.Success()
                    : NativeCallResult.Failure(Marshal.GetLastWin32Error()));
            if (result.Succeeded)
            {
                lock (_lifecycleGate)
                {
                    if (handle == toClose)
                    {
                        handle = nint.Zero;
                    }
                }
            }
            lock (_teardownEvidenceGate)
            {
                _teardownOrderForTests.Add(label);
                if (!result.Succeeded)
                {
                    _teardownFailures.Add($"CloseHandle({label}) failed: {result.Win32Error}");
                }
            }
        }
        catch (Exception ex)
        {
            lock (_teardownEvidenceGate)
            {
                _teardownOrderForTests.Add(label);
                _teardownFailures.Add($"CloseHandle({label}) threw: {ex.Message}");
            }
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
                DeleteAttributeList(toFree);
            }

            Marshal.FreeHGlobal(toFree);
            lock (_teardownEvidenceGate) { _teardownOrderForTests.Add(label); }
        }
        catch (Exception ex)
        {
            lock (_teardownEvidenceGate)
            {
                _teardownOrderForTests.Add(label);
                _teardownFailures.Add($"Free({label}) threw: {ex.Message}");
            }
        }

        try
        {
            ResourceReleaseObserverForTests?.Invoke(label);
        }
        catch
        {
            // Test-only release notifications are non-authoritative and must not affect cleanup.
        }
    }

    private void StartDrains(TimeSpan timeout)
    {
        _drainTimeout = timeout;
        _drainCts = new CancellationTokenSource();
        _stdOutDrain = Task.Run(() => DrainPipe(_parentStdOutRead, timeout, false, _drainCts.Token));
        _stdErrDrain = Task.Run(() => DrainPipe(_parentStdErrRead, timeout, true, _drainCts.Token));
    }

    private (string Text, bool Truncated) DrainPipe(
        nint readHandle, TimeSpan timeout, bool standardError, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(
            new Microsoft.Win32.SafeHandles.SafeFileHandle(readHandle, ownsHandle: false),
            FileAccess.Read, bufferSize: 65536, isAsync: false);
        var buffer = new byte[65536];
        var charBuffer = new char[65536];

        // GH106-R2-F10: a single Decoder held across the whole read loop correctly reassembles a
        // multibyte UTF-8 sequence that straddles two independent 64 KiB reads. Decoding each chunk in
        // isolation (the prior behavior) corrupts any character split across a read boundary while still
        // reporting Truncated == false — a real correctness bug, not merely cosmetic.
        var decoder = Encoding.UTF8.GetDecoder();
        var text = new StringBuilder();
        bool peekFailed = false;
        uint available;
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            if (cancellationToken.IsCancellationRequested || (deadline.Elapsed > timeout && !peekFailed))
            {
                return (text.ToString(), true);
            }

            int read;
            try
            {
                NativeCallResult<uint> peek = InvokePeekNamedPipe(readHandle);
                if (!peek.Succeeded)
                {
                    if (Interlocked.Exchange(ref _peekFailureRecorded, 1) == 0)
                    {
                        _failures.RecordSecondary($"PeekNamedPipe failed with Win32 error {peek.Win32Error}.");
                    }
                    peekFailed = true;
                    // A failed probe does not establish that a synchronous read is safe. Preserve the
                    // prefix already obtained, and wait for the family proof; only then can buffered
                    // bytes be drained without risking an unbounded read against a live descendant.
                    if (!_activeProcessZeroObserved)
                    {
                        Thread.Sleep(10);
                        continue;
                    }
                    available = (uint)buffer.Length;
                }
                else
                {
                    available = peek.Value;
                }

                bool pipeClosed = available == uint.MaxValue;
                if (available == 0)
                {
                    if (_activeProcessZeroObserved)
                    {
                        available = (uint)buffer.Length;
                    }
                    else
                    {
                        Thread.Sleep(10);
                        continue;
                    }
                }
                else if (pipeClosed)
                {
                    available = (uint)buffer.Length;
                }

                read = stream.Read(buffer, 0, (int)Math.Min((uint)buffer.Length, available));
            }
            catch (IOException)
            {
                // The pipe was force-closed during teardown before EOF was reached.
                return (text.ToString(), true);
            }

            if (read == 0)
            {
                MarkDrainCompleted(standardError);
                int flushedChars = decoder.GetChars(ReadOnlySpan<byte>.Empty, charBuffer, flush: true);
                if (flushedChars > 0)
                {
                    text.Append(charBuffer, 0, flushedChars);
                }

                return (text.ToString(), peekFailed);
            }

            int charCount = decoder.GetChars(buffer, 0, read, charBuffer, 0);
            text.Append(charBuffer, 0, charCount);
        }
    }

    private static async Task<(string Text, bool Truncated)> DrainOrTruncate(
        Task<(string Text, bool Truncated)>? drain)
    {
        // DrainPipe's own DrainTimeout deadline is only checked between completed reads, so it cannot
        // bound a single in-flight blocked Read(). The real bound comes from WaitAsync's race against its
        // `linked` cancellation token, which force-terminates the job (closing every inherited pipe
        // handle) to unblock a stuck read before awaiting this task to completion.
        if (drain is null)
        {
            return (string.Empty, true);
        }

        return await drain.ConfigureAwait(false);
    }

    /// <summary>
    /// GH106-R2-F5: distinguishes a genuine exit (<c>WAIT_OBJECT_0</c>) from a benign timeout
    /// (<c>WAIT_TIMEOUT</c>, keep polling) from a real wait failure (<c>WAIT_FAILED</c>/
    /// <c>WAIT_ABANDONED</c>) — the prior implementation collapsed the last two into an identical
    /// <c>false</c> return, so <see cref="WaitAsync"/> misreported a genuine wait failure as TimedOut.
    /// </summary>
    private (bool Exited, bool WaitFailed, int Win32Error) WaitForSingleProcessExit(
        CancellationToken cancellationToken)
    {
        const int PollMs = 25;
        while (!cancellationToken.IsCancellationRequested)
        {
            NativeWaitResult call = InvokeWaitForSingleObject(_processHandle, PollMs);
            uint result = call.Result;
            if (result == NativeMethods.WAIT_OBJECT_0)
            {
                return (true, false, 0);
            }

            if (result == NativeMethods.WAIT_TIMEOUT)
            {
                continue;
            }

            // WAIT_FAILED or WAIT_ABANDONED: capture the error immediately, before any other Win32 call
            // on this thread can clobber it.
            return (false, true, call.Win32Error);
        }

        return (false, false, 0);
    }

    /// <summary>GH106-R2-F5 seam indirection: real production calls always go through this.</summary>
    private NativeWaitResult InvokeWaitForSingleObject(nint handle, int timeoutMs)
    {
        if (_nativeCalls.WaitForSingleObject is not null)
        {
            return _nativeCalls.WaitForSingleObject(handle, timeoutMs);
        }

        uint result = NativeMethods.WaitForSingleObject(handle, timeoutMs);
        return new NativeWaitResult(result, result == NativeMethods.WAIT_FAILED ? Marshal.GetLastWin32Error() : 0);
    }

    /// <summary>GH106-R2-F4 seam indirection: real production calls always go through this.</summary>
    private NativeCallResult InvokeTerminateJobObject(nint jobHandle, uint exitCode)
    {
        if (_nativeCalls.TerminateJobObject is not null)
        {
            return _nativeCalls.TerminateJobObject(jobHandle, exitCode);
        }

        return NativeMethods.TerminateJobObject(jobHandle, exitCode)
            ? NativeCallResult.Success()
            : NativeCallResult.Failure(Marshal.GetLastWin32Error());
    }

    private NativeCallResult<uint> QueryActiveProcessCount()
    {
        var accounting = default(NativeMethods.JOBOBJECT_BASIC_ACCOUNTING_INFORMATION);
        if (!NativeMethods.QueryInformationJobObject(
            _jobHandle,
            NativeMethods.JobObjectBasicAccountingInformation,
            ref accounting,
            (uint)Marshal.SizeOf<NativeMethods.JOBOBJECT_BASIC_ACCOUNTING_INFORMATION>(),
            out _))
        {
            int error = Marshal.GetLastWin32Error();
            return NativeCallResult<uint>.Failure(error);
        }

        return NativeCallResult<uint>.Success(accounting.ActiveProcesses);
    }

    private NativeCallResult<uint> InvokePeekNamedPipe(nint handle)
    {
        if (_nativeCalls.PeekNamedPipe is not null)
        {
            return _nativeCalls.PeekNamedPipe(handle);
        }
        if (!NativeMethods.PeekNamedPipe(handle, nint.Zero, 0, out _, out uint available, nint.Zero))
        {
            int error = Marshal.GetLastWin32Error();
            // ERROR_BROKEN_PIPE is the normal anonymous-pipe EOF indication, not a probe failure.
            // Preserve the broken-pipe EOF indication distinctly from a live pipe with no buffered
            // bytes. The sentinel is outside the DWORD byte-count domain and lets DrainPipe perform
            // the bounded synchronous read that observes EOF without treating ordinary emptiness as
            // family proof.
            return error is 109 or 232
                ? NativeCallResult<uint>.Success(uint.MaxValue)
                : NativeCallResult<uint>.Failure(error);
        }
        return NativeCallResult<uint>.Success(available);
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
                    if (_completionObservationStoppedForTests)
                    {
                        return;
                    }
                    lock (_lifecycleGate) { _activeProcessZeroObserved = true; }
                    return;
                }
            }
        }, CancellationToken.None);
    }

    private async Task WaitForActiveProcessZero(CancellationToken cancellationToken)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!_activeProcessZeroObserved
            && deadline.Elapsed < _activeProcessZeroBound
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

    private static nint BuildAttributeList(
        nint handleListBuffer, int handleCount, nint jobListBuffer, Stack<Action> unwind,
        ProcessOwnershipNativeCalls? nativeCalls)
    {
        nint listSize = nint.Zero;
        NativeMethods.InitializeProcThreadAttributeList(nint.Zero, 2, 0, ref listSize);
        nint attributeListBuffer = Marshal.AllocHGlobal(listSize);
        bool initialized = false;
        try
        {
            NativeCallResult initialization = nativeCalls?.InitializeProcThreadAttributeList is { } initialize
                ? initialize(attributeListBuffer)
                : NativeMethods.InitializeProcThreadAttributeList(attributeListBuffer, 2, 0, ref listSize)
                    ? NativeCallResult.Success()
                    : NativeCallResult.Failure(Marshal.GetLastWin32Error());
            if (!initialization.Succeeded)
            {
                throw Win32("InitializeProcThreadAttributeList", initialization.Win32Error);
            }
            initialized = true;

            if (!NativeMethods.UpdateProcThreadAttribute(
                attributeListBuffer, 0, NativeMethods.PROC_THREAD_ATTRIBUTE_HANDLE_LIST,
                handleListBuffer, (nint)(handleCount * nint.Size), nint.Zero, nint.Zero)
                || !NativeMethods.UpdateProcThreadAttribute(
                attributeListBuffer, 0, NativeMethods.PROC_THREAD_ATTRIBUTE_JOB_LIST,
                jobListBuffer, (nint)nint.Size, nint.Zero, nint.Zero))
            {
                throw Win32("UpdateProcThreadAttribute");
            }

            return attributeListBuffer;
        }
        catch
        {
            if (initialized)
            {
                DeleteAttributeList(attributeListBuffer);
            }
            Marshal.FreeHGlobal(attributeListBuffer);
            throw;
        }
    }

    private static void DeleteAttributeList(nint attributeList)
    {
        NativeMethods.DeleteProcThreadAttributeList(attributeList);
        try
        {
            AttributeListDeleteObserverForTests?.Invoke(attributeList);
        }
        catch
        {
            // Test-only observation must never interfere with native cleanup or its caller's free.
        }
    }

    private static Win32Exception Win32(string apiName) =>
        new($"{apiName} failed with Win32 error {Marshal.GetLastWin32Error()}.");

    private static Win32Exception Win32(string apiName, int error) =>
        new($"{apiName} failed with Win32 error {error}.");
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
    internal const int JobObjectBasicAccountingInformation = 1;
    internal const int JobObjectAssociateCompletionPortInformation = 7;
    internal const uint JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO = 4;
    internal const nint PROC_THREAD_ATTRIBUTE_HANDLE_LIST = 0x00020002;
    internal const nint PROC_THREAD_ATTRIBUTE_JOB_LIST = 0x0002000D;
    internal const int HANDLE_FLAG_INHERIT = 1;
    internal const uint WAIT_OBJECT_0 = 0x00000000;
    internal const uint WAIT_TIMEOUT = 0x00000102;
    internal const uint WAIT_FAILED = 0xFFFFFFFF;

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
    internal struct JOBOBJECT_BASIC_ACCOUNTING_INFORMATION
    {
        public long TotalUserTime;
        public long TotalKernelTime;
        public long ThisPeriodTotalUserTime;
        public long ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount;
        public uint TotalProcesses;
        public uint ActiveProcesses;
        public uint TotalTerminatedProcesses;
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
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PeekNamedPipe(
        nint hNamedPipe, nint lpBuffer, uint nBufferSize, out uint lpBytesRead,
        out uint lpTotalBytesAvail, nint lpBytesLeftThisMessage);

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
    internal static extern bool QueryInformationJobObject(
        nint hJob,
        int JobObjectInformationClass,
        ref JOBOBJECT_BASIC_ACCOUNTING_INFORMATION lpJobObjectInformation,
        uint cbJobObjectInformationLength,
        out uint lpReturnLength);

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
