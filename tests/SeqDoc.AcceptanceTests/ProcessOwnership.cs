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
    AfterProcessCreatedBeforeResume,

    /// <summary>
    /// GH106-R2-F1: faults after <see cref="ContainedProcess.StartDrains"/>/
    /// <see cref="ContainedProcess.StartCompletionMonitor"/> have started background work but before
    /// resume completes, proving the unwind path cancels/awaits that background work instead of
    /// leaking it.
    /// </summary>
    AfterDrainsStartedBeforeResume,

    AfterOwnershipTransferBeforeMonitor,
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

internal enum ReleaseObservationMode
{
    Cleanup,
    ConstructionParentCopy,
}

internal sealed class ConstructionCleanupCapture
{
    internal ContainedProcess? Owner { get; set; }
    internal ContainedProcess? Process { get; set; }
    internal ProcessOwnershipSnapshot? Snapshot { get; set; }
}

internal sealed class ProcessOwnershipNativeCalls
{
    internal Func<nint, uint, NativeCallResult>? TerminateJobObject { get; init; }
    internal Func<nint, int, NativeWaitResult>? WaitForSingleObject { get; init; }
    internal Func<nint, NativeCallResult<uint>>? ResumeThread { get; init; }
    internal Func<nint, uint, NativeCallResult>? TerminateProcess { get; init; }
    internal Func<nint, NativeCallResult<uint>>? PeekNamedPipe { get; init; }
    internal Func<nint, NativeCallResult>? CloseHandle { get; init; }
    internal Func<nint, NativeCallResult>? InitializeProcThreadAttributeList { get; init; }
    internal ProcessOwnershipLifecycleBarriers? LifecycleBarriers { get; init; }
}

public sealed class ProcessOwnershipConstructionResult
{
    private ProcessOwnershipConstructionResult(
        ContainedProcess? process, ContainedProcess? cleanupOwner, ProcessOwnershipFailureClass failureClass, string? detail,
        ProcessOwnershipSnapshot? snapshot = null)
    {
        Process = process;
        CleanupOwner = cleanupOwner;
        FailureClass = failureClass;
        Detail = detail;
        OwnershipSnapshot = snapshot ?? process?.OwnershipSnapshot;
    }

    public ContainedProcess? Process { get; }

    /// <summary>
    /// Gets the owner retained after failed construction when bounded family or managed-task cleanup was
    /// not proven. Callers must retry <see cref="ContainedProcess.Terminate"/> or <see cref="Dispose"/>;
    /// successful construction cleanup returns <see langword="null"/>. <see cref="Succeeded"/> depends
    /// only on <see cref="Process"/>.
    /// </summary>
    public ContainedProcess? CleanupOwner { get; }

    public ProcessOwnershipFailureClass FailureClass { get; }

    public string? Detail { get; }

    internal ProcessOwnershipSnapshot? OwnershipSnapshot { get; }

    public bool Succeeded => Process is not null;

    internal static ProcessOwnershipConstructionResult Success(ContainedProcess process) =>
        new(process, null, ProcessOwnershipFailureClass.None, null, process.OwnershipSnapshot);

    internal static ProcessOwnershipConstructionResult Failure(string detail, ContainedProcess? cleanupOwner = null,
        ProcessOwnershipSnapshot? snapshot = null) =>
        new(null, cleanupOwner, ProcessOwnershipFailureClass.ProcessConstructionFailed, detail,
            snapshot ?? cleanupOwner?.OwnershipSnapshot);
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
/// Owns a Windows-contained child process family: created suspended and admitted to a kill-on-close,
/// breakaway-denied Job Object before first resume, drained concurrently on both standard streams, and
/// proven fully exited (including any descendant) via a Job Object I/O completion port
/// <c>JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO</c> message rather than only the immediate child's exit code.
/// </summary>
public sealed partial class ContainedProcess : IDisposable
{
    private readonly ProcessOwnershipLifecycleCoordinator _lifecycle = new();
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
    private long _drainOperationId;
    private CancellationTokenSource? _completionMonitorCts;
    private CancellationTokenSource? _drainCts;
    private volatile bool _activeProcessZeroObserved;
    private volatile bool _completionObservationStoppedForTests;
    private bool _terminateJobObjectCalled;
    private bool _familyProofFailureRecorded;
    private bool _familyRetentionEvidenceRecorded;
    private bool _managedLifecycleFailureRecorded;
    private bool _teardownSummaryRecorded;
    private readonly object _resourceGate = new();
    private readonly object _completionMonitorGate = new();
    private readonly object _teardownEvidenceGate = new();
    private readonly ProcessOwnershipNativeCalls _nativeCalls;
    private readonly NativeOwnershipLedger _ownershipLedger = new();
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

    internal ProcessOwnershipSnapshot OwnershipSnapshot => _ownershipLedger.Snapshot();

    /// <summary>Test-only chronology seam — see the invocation site in <see cref="StartCore"/>.</summary>
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
    public ProcessOwnershipFailureClass FailureClass => _lifecycle.Class;

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
            lock (_resourceGate)
            {
                return _lifecycle.IsState(LifecycleState.FamilyResourcesRetained)
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

    internal IReadOnlyList<string> SecondaryFailures => _lifecycle.SecondaryFailures;

    internal IReadOnlyList<LifecycleOperationReceipt> LifecycleOperationReceiptsForTests =>
        _lifecycle.OperationReceipts.ToArray();

    /// <summary>GH106-R2-F2 test-only seam: forces the completion monitor to stop observing ACTIVE_PROCESS_ZERO.</summary>
    internal void StopCompletionMonitorForTests()
    {
        // This seam models an unavailable completion observation channel, even if a very fast child
        // posted its zero message before the test could cancel the monitor.
        _completionObservationStoppedForTests = true;
        lock (_resourceGate) { _activeProcessZeroObserved = false; }
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

        var unwindFailures = new List<string>();
        var cleanupCapture = new ConstructionCleanupCapture();
        var process = new ContainedProcess(options.NativeCalls);
        cleanupCapture.Process = process;
        cleanupCapture.Snapshot = process.OwnershipSnapshot;
        try
        {
            return StartCore(options, faultPoint, process, unwindFailures, cleanupCapture);
        }
        catch (Exception ex)
        {
            try
            {
                process.ConstructionCleanup(options, unwindFailures);
            }
            catch (Exception cleanupException)
            {
                unwindFailures.Add($"Construction cleanup failed: {cleanupException.Message}");
            }
            cleanupCapture.Snapshot = process.OwnershipSnapshot;
            cleanupCapture.Owner = process.HasOwnedTrackedResource() ? process : null;

            string detail = unwindFailures.Count == 0 ? ex.Message
                : $"{ex.Message} Cleanup failures: {string.Join("; ", unwindFailures)}";
            return ProcessOwnershipConstructionResult.Failure(detail, cleanupCapture.Owner, cleanupCapture.Snapshot);
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
        ProcessOwnershipOptions options, ConstructionFaultPoint faultPoint, ContainedProcess process,
        List<string> unwindFailures, ConstructionCleanupCapture cleanupCapture)
    {
        cleanupCapture.Process = process;

        // --- S1: three std pipes, created non-inheritable by default, then the exact child-side end of
        // each is explicitly marked inheritable (never the parent-side end). ---
        CreatePipePair(out nint stdInRead, out nint stdInWrite);
        process._ownershipLedger.Acquire(NativeResourceKind.StdinChildRead, stdInRead, "CreatePipe(stdin child read)");
        process._ownershipLedger.Acquire(NativeResourceKind.StdinParentWrite, stdInWrite, "CreatePipe(stdin parent write)");
        CreatePipePair(out nint stdOutRead, out nint stdOutWrite);
        process._ownershipLedger.Acquire(NativeResourceKind.StdoutParentRead, stdOutRead, "CreatePipe(stdout parent read)");
        process._ownershipLedger.Acquire(NativeResourceKind.StdoutChildWrite, stdOutWrite, "CreatePipe(stdout child write)");
        CreatePipePair(out nint stdErrRead, out nint stdErrWrite);
        process._ownershipLedger.Acquire(NativeResourceKind.StderrParentRead, stdErrRead, "CreatePipe(stderr parent read)");
        process._ownershipLedger.Acquire(NativeResourceKind.StderrChildWrite, stdErrWrite, "CreatePipe(stderr child write)");

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
        process._ownershipLedger.Acquire(NativeResourceKind.HandleListBuffer, handleListBuffer, "Alloc handle list");

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
        process._ownershipLedger.Acquire(NativeResourceKind.JobHandle, jobHandle, "CreateJobObjectW");

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
        process._ownershipLedger.Acquire(NativeResourceKind.CompletionPortHandle, completionPort, "CreateIoCompletionPort");

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
        process._ownershipLedger.Acquire(NativeResourceKind.JobListBuffer, jobListBuffer, "Alloc job list");
        Marshal.WriteIntPtr(jobListBuffer, jobHandle);

        nint attributeListBuffer = BuildAttributeList(
            handleListBuffer, inheritable.Length, jobListBuffer, options.NativeCalls, process._ownershipLedger);

        if (faultPoint == ConstructionFaultPoint.AfterAttributeListBuilt)
        {
            throw new InvalidOperationException("fault-injected: AfterAttributeListBuilt");
        }

        // --- S4: CreateProcessW, suspended, with the restricted attribute list. ---
        string commandLine = ProcessOwnershipEncoding.BuildCommandLine(options.ExecutablePath, options.Arguments);
        nint commandLineBuffer = Marshal.StringToHGlobalUni(commandLine);
        process._ownershipLedger.Acquire(NativeResourceKind.CommandLineBuffer, commandLineBuffer, "Alloc command line");

        string environmentBlock = ProcessOwnershipEncoding.BuildEnvironmentBlock(options.Environment);
        nint environmentBuffer = Marshal.StringToHGlobalUni(environmentBlock);
        process._ownershipLedger.Acquire(NativeResourceKind.EnvironmentBlockBuffer, environmentBuffer, "Alloc environment block");

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
        process._ownershipLedger.Acquire(NativeResourceKind.PrimaryThreadHandle, processInformation.hThread, "CreateProcessW primary thread");
        process._ownershipLedger.Acquire(NativeResourceKind.ProcessHandle, processInformation.hProcess, "CreateProcessW process");

        // JOB_LIST admission has completed atomically with CreateProcess. Observe it immediately after
        // both process/thread unwind entries exist, while the new thread is still suspended.
        Exception? postCreateObserverFailure = null;
        try
        {
            PostCreateProcessObserverForTests?.Invoke(jobHandle, processInformation.hProcess);
        }
        catch (Exception ex)
        {
            // The observer is a test-only receipt. Defer its failure until the transferred owner exists,
            // so even an asserting observer cannot create a pre-owner post-create unwind window.
            postCreateObserverFailure = ex;
        }

        // Creation-time PROC_THREAD_ATTRIBUTE_JOB_LIST admission has completed.  Transfer every
        // remaining resource to one owner before any injectable post-create fault; this closes the
        // suspended-child window without leaving a copied local and owner with the same handle.
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

        process._processHandle = processInformation.hProcess;
        process._threadHandle = processInformation.hThread;
        process._jobHandle = jobHandle;
        process._completionPortHandle = completionPort;
        process._attributeListBuffer = attributeListBuffer;
        process._environmentBlockBuffer = environmentBuffer;
        process._commandLineBuffer = commandLineBuffer;
        process._handleListBuffer = handleListBuffer;
        process._jobListBuffer = jobListBuffer;
        process._parentStdOutRead = stdOutRead;
        process._parentStdErrRead = stdErrRead;
        process._parentStdInWrite = stdInWrite;

        bool parentCopiesClosed = true;
        parentCopiesClosed &= process.ReleaseOwned(NativeResourceKind.StdinChildRead, "stdin child handle", unwindFailures,
            ReleaseObservationMode.ConstructionParentCopy);
        parentCopiesClosed &= process.ReleaseOwned(NativeResourceKind.StdoutChildWrite, "stdout child handle", unwindFailures,
            ReleaseObservationMode.ConstructionParentCopy);
        parentCopiesClosed &= process.ReleaseOwned(NativeResourceKind.StderrChildWrite, "stderr child handle", unwindFailures,
            ReleaseObservationMode.ConstructionParentCopy);
        parentCopiesClosed &= process.ReleaseOwned(NativeResourceKind.StdinParentWrite, "stdin parent handle", unwindFailures,
            ReleaseObservationMode.ConstructionParentCopy);
        if (!parentCopiesClosed)
        {
            throw Win32("CloseHandle(parent copy)", 8301);
        }

        process.ProcessId = processInformation.dwProcessId;
        process.StdOutChildHandleValueForTests = stdOutChildHandleValue;
        process.StdErrChildHandleValueForTests = stdErrChildHandleValue;
        process.StdInChildHandleValueForTests = stdInChildHandleValue;

        if (faultPoint == ConstructionFaultPoint.AfterOwnershipTransferBeforeMonitor)
        {
            throw new InvalidOperationException("fault-injected: AfterOwnershipTransferBeforeMonitor");
        }

        process.StartCompletionMonitor();

        if (faultPoint == ConstructionFaultPoint.AfterProcessCreatedBeforeResume)
        {
            throw new InvalidOperationException("fault-injected: AfterProcessCreatedBeforeResume");
        }

        if (postCreateObserverFailure is not null)
        {
            throw postCreateObserverFailure;
        }

        process.StartDrains(options.DrainTimeout);

        PostDrainsStartHookForTests?.Invoke(process);

        if (faultPoint == ConstructionFaultPoint.AfterDrainsStartedBeforeResume)
        {
            throw new InvalidOperationException("fault-injected: AfterDrainsStartedBeforeResume");
        }

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

        return ProcessOwnershipConstructionResult.Success(process);
    }

    private bool ConstructionCleanup(ProcessOwnershipOptions options, List<string> failures)
    {
        // Before a process exists there is no family or managed-task proof to establish.  The ledger is
        // already the sole owner, so release every still-owned slot in reverse acquisition order.
        if (_ownershipLedger.CurrentOwned(NativeResourceKind.ProcessHandle) is null)
        {
            foreach (NativeResourceSnapshot resource in _ownershipLedger.ReverseOwned())
            {
                ReleaseOwned(resource.Kind, ResourceLabel(resource.Kind), failures);
            }
            bool retained = HasOwnedTrackedResource();
            _lifecycle.CompleteDispose(retained);
            return !retained;
        }

        try
        {
            StartCompletionMonitor();
        }
        catch (Exception ex)
        {
            failures.Add($"Completion monitor startup failed: {ex.Message}");
            _lifecycle.Record(ProcessOwnershipFailureClass.ProcessConstructionFailed,
                $"Completion monitor startup failed: {ex.Message}");
            _lifecycle.SetState(LifecycleState.FamilyResourcesRetained);
            return false;
        }

        nint processHandle = _ownershipLedger.CurrentOwned(NativeResourceKind.ProcessHandle)?.Value ?? nint.Zero;
        nint jobHandle = _ownershipLedger.CurrentOwned(NativeResourceKind.JobHandle)?.Value ?? nint.Zero;
        NativeCallResult directTermination = InvokeTerminateProcess(options, processHandle);
        if (!directTermination.Succeeded)
        {
            failures.Add($"TerminateProcess failed with Win32 error {directTermination.Win32Error}.");
        }

        NativeWaitResult directWait = InvokeWaitForSingleObject(options, processHandle,
            (int)Math.Min(int.MaxValue, _constructionCleanupBound.TotalMilliseconds));
        if (directWait.Result != NativeMethods.WAIT_OBJECT_0)
        {
            failures.Add($"WaitForSingleObject failed with Win32 error {directWait.Win32Error}.");
        }

        _terminateJobObjectCalled = true;
        NativeCallResult jobTermination = InvokeTerminateJobObject(jobHandle, uint.MaxValue);
        if (!jobTermination.Succeeded)
        {
            failures.Add($"TerminateJobObject failed with Win32 error {jobTermination.Win32Error}.");
        }
        else
        {
        }

        bool familyProven = EnsureFamilyProofAsync(_constructionCleanupBound, CancellationToken.None)
            .GetAwaiter().GetResult();
        if (!familyProven)
        {
            failures.Add("ACTIVE_PROCESS_ZERO was not observed within the construction cleanup bound.");
        }
        bool monitorComplete = WaitConstructionTaskResult(_completionMonitor, "completion monitor", failures);
        bool stdoutComplete = WaitConstructionTaskResult(_stdOutDrain, "stdout drain", failures);
        bool stderrComplete = WaitConstructionTaskResult(_stdErrDrain, "stderr drain", failures);
        bool managedComplete = monitorComplete && stdoutComplete && stderrComplete;
        if (!familyProven || !managedComplete || !jobTermination.Succeeded)
        {
            _lifecycle.Record(ProcessOwnershipFailureClass.ProcessConstructionFailed,
                "Construction cleanup retained ownership because family zero or managed quiescence was not proven.");
            _lifecycle.SetState(LifecycleState.FamilyResourcesRetained);
            return false;
        }

        _completionMonitorCts?.Cancel();
        _completionMonitorCts?.Dispose();
        _completionMonitorCts = null;
        _drainCts?.Dispose();
        _drainCts = null;

        foreach (NativeResourceSnapshot resource in _ownershipLedger.ReverseOwned())
        {
            ReleaseOwned(resource.Kind, ResourceLabel(resource.Kind), failures);
        }
        bool resourcesRetained = HasOwnedTrackedResource();
        _lifecycle.CompleteDispose(resourcesRetained);
        return !resourcesRetained;
    }

    private static string ResourceLabel(NativeResourceKind kind) => kind switch
    {
        NativeResourceKind.AttributeListBuffer => "attribute list buffer",
        NativeResourceKind.HandleListBuffer => "handle list buffer",
        NativeResourceKind.JobListBuffer => "job list buffer",
        NativeResourceKind.CommandLineBuffer => "command line buffer",
        NativeResourceKind.EnvironmentBlockBuffer => "environment block buffer",
        NativeResourceKind.ProcessHandle => "process handle",
        NativeResourceKind.PrimaryThreadHandle => "thread handle",
        NativeResourceKind.JobHandle => "job handle",
        NativeResourceKind.CompletionPortHandle => "completion port handle",
        NativeResourceKind.StdinChildRead or NativeResourceKind.StdinParentWrite => "stdin pipe handle",
        NativeResourceKind.StdoutParentRead or NativeResourceKind.StdoutChildWrite => "stdout pipe handle",
        NativeResourceKind.StderrParentRead or NativeResourceKind.StderrChildWrite => "stderr pipe handle",
        _ => kind.ToString(),
    };

    private bool WaitConstructionTaskResult(Task? task, string label, List<string> failures)
    {
        if (task is null)
        {
            return true;
        }
        try
        {
            if (!task.Wait(_constructionCleanupBound))
            {
                failures.Add($"{label} did not complete within {_constructionCleanupBound}.");
                return false;
            }
            return true;
        }
        catch (AggregateException ex)
        {
            failures.Add($"{label} failed: {ex.InnerException?.Message ?? ex.Message}");
            return false;
        }
    }

    public Task<ProcessOwnershipWaitResult> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        => _lifecycle.GetOrStartWait(() => WaitCoreAsync(timeout, cancellationToken));

    private async Task<ProcessOwnershipWaitResult> WaitCoreAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
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
                _lifecycle.Record(
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
                _lifecycle.Record(ProcessOwnershipFailureClass.TimedOut, "Wait was cancelled by the caller.");
            }
            else if (deadlineExpired || !waitFailed)
            {
                _lifecycle.Record(ProcessOwnershipFailureClass.TimedOut, $"Wait exceeded {timeout}.");
            }

            // Admission table: explicit TerminateJobObject for timeout/cancellation (never deferred to
            // Dispose's kill-on-close). This also unblocks the concurrent stream drains below by making
            // the child's inherited pipe handles close, which yields EOF on the parent's read ends.
            await EnsureTerminalAsync().ConfigureAwait(false);

            // Ensure the shared uncancelled proof task records the family outcome before the result is
            // ever snapshotted. Forced cleanup must not erase a ProcessFailed proof failure.
            await EnsureFamilyProofAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false);

            // GH106-R2-F3: terminate AND await family exit — a forced termination without waiting for
            // proof is only half the checkpoint's "terminate and await" contract. `linked` is already
            // cancelled here (that is exactly why this branch was reached), so a fresh, uncancelled token
            // is required for the bound below to genuinely apply rather than returning instantly.
        }
        else
        {
            nint processHandle = _ownershipLedger.CurrentOwned(NativeResourceKind.ProcessHandle)?.Value ?? nint.Zero;
            if (processHandle == nint.Zero || !NativeMethods.GetExitCodeProcess(processHandle, out uint code))
            {
                _lifecycle.Record(ProcessOwnershipFailureClass.ProcessFailed, "GetExitCodeProcess failed.");
            }
            else
            {
                exitCode = unchecked((int)code);
                if (code != 0)
                {
                    _lifecycle.Record(
                        ProcessOwnershipFailureClass.ProcessFailed, $"Child exited with code {code}.");
                }
            }

            // Give the job's completion port a bounded chance to report ACTIVE_PROCESS_ZERO (every
            // process in the job, including descendants, has exited) before declaring the wait complete.
            Task familyProof = EnsureFamilyProofAsync(cancellationToken: CancellationToken.None);
            Task drainCompletion = Task.WhenAll(DrainOrTruncate(_stdOutDrain), DrainOrTruncate(_stdErrDrain));
            Task waitDeadline = Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
            Task raceWinner = await Task.WhenAny(familyProof, drainCompletion, waitDeadline).ConfigureAwait(false);
            bool deadlineForcedFamilyTermination = raceWinner == waitDeadline
                && !familyProof.IsCompletedSuccessfully;
            bool accountingForcedFamilyTermination = false;
            if (deadlineForcedFamilyTermination)
            {
                RecordFamilyProofFailure();
                await EnsureTerminalAsync().ConfigureAwait(false);
            }
            else if (raceWinner == drainCompletion && !familyProof.IsCompletedSuccessfully)
            {
                NativeCallResult<uint> activeProcesses = QueryActiveProcessCount();
                if (activeProcesses.Succeeded && activeProcesses.Value > 0)
                {
                    RecordFamilyProofFailure();
                    await EnsureTerminalAsync().ConfigureAwait(false);
                    accountingForcedFamilyTermination = true;
                }
            }

            // Complete the shared proof task before constructing the immutable wait result. If proof
            // failed, retain its ProcessFailed classification even when forced cleanup follows.
            bool familyProven = await EnsureFamilyProofAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false);
            if (!familyProven && !deadlineForcedFamilyTermination && !accountingForcedFamilyTermination)
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
            // Drains are evidence only. The shared family-proof epoch owns classification.
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
            _lifecycle.Record(
                ProcessOwnershipFailureClass.DrainIncomplete,
                "A stream did not reach EOF within the wait's own bound; job termination was forced to unblock it.");
        }

        if (stdOutTruncated || stdErrTruncated)
        {
            _lifecycle.Record(ProcessOwnershipFailureClass.DrainIncomplete, "A stream did not reach EOF in time.");
        }

        return new ProcessOwnershipWaitResult
        {
            FailureClass = _lifecycle.Class,
            Detail = _lifecycle.Detail,
            ExitCode = exited ? exitCode : null,
            TimedOut = !exited && deadlineExpired && !callerCancelled && !waitFailed,
            Cancelled = !exited && callerCancelled && !waitFailed,
            ActiveProcessZeroObserved = _activeProcessZeroObserved,
            SecondaryFailures = _lifecycle.SecondaryFailures.ToArray(),
            StdOut = new ProcessOwnershipStreamResult(stdOutText, stdOutTruncated),
            StdErr = new ProcessOwnershipStreamResult(stdErrText, stdErrTruncated),
        };
    }

    /// <summary>Query phase: has ACTIVE_PROCESS_ZERO been observed for this job yet?</summary>
    public bool HasObservedActiveProcessZero() => _activeProcessZeroObserved;

    /// <summary>Terminate phase: forcibly ends every process in the job.</summary>
    public bool Terminate()
    {
        return EnsureTerminalAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        bool retry;
        _lifecycle.GetOrStartDispose(DisposeCoreAsync, out retry).GetAwaiter().GetResult();
    }

    private async Task DisposeCoreAsync(long disposeOperationId)
    {
        if (_ownershipLedger.CurrentOwned(NativeResourceKind.ProcessHandle) is null)
        {
            foreach (NativeResourceSnapshot resource in _ownershipLedger.ReverseOwned())
            {
                ReleaseOwned(resource.Kind, ResourceLabel(resource.Kind));
            }
            _lifecycle.CompleteDispose(HasOwnedTrackedResource(), disposeOperationId);
            return;
        }

        if (!_activeProcessZeroObserved || _lifecycle.IsTerminalActive)
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
            lock (_resourceGate)
            {
                recordLifecycleFailure = !_managedLifecycleFailureRecorded;
                _managedLifecycleFailureRecorded = true;
            }
            if (recordLifecycleFailure)
            {
                _lifecycle.Record(ProcessOwnershipFailureClass.DrainIncomplete,
                    "Managed lifecycle work did not quiesce within the cleanup bound; owned handles were retained.");
            }
        }

        // WaitCore can still be between its process wait and GetExitCodeProcess/Job Object accounting.
        // Snapshot the already-published shared task without holding the lifecycle gate while waiting;
        // closing either native handle before this task quiesces would make that work use a released
        // (and potentially reused) handle.  A fault is observed deliberately so its exact evidence is
        // not lost behind Task.WhenAny-style completion handling.
        Task<ProcessOwnershipWaitResult>? waitTask;
        waitTask = _lifecycle.WaitTask;
        bool waitComplete = await AwaitTaskBounded(waitTask, _constructionCleanupBound).ConfigureAwait(false);
        if (waitTask is not null && waitComplete)
        {
            try
            {
                await waitTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _lifecycle.Record(ProcessOwnershipFailureClass.ProcessFailed,
                    $"WaitAsync failed during disposal: {ex.Message}");
            }
        }
        else if (waitTask is not null)
        {
            bool recordWaitFailure;
            lock (_resourceGate)
            {
                recordWaitFailure = !_managedLifecycleFailureRecorded;
                _managedLifecycleFailureRecorded = true;
            }
            if (recordWaitFailure)
            {
                _lifecycle.Record(ProcessOwnershipFailureClass.TeardownDegraded,
                    $"WaitAsync did not quiesce within {_constructionCleanupBound}; process and job handles were retained.");
            }
        }

        // A live wait is the last permitted user of the process handle; the completion monitor is the
        // corresponding last user of the job handle.  Retain both when either proof is incomplete rather
        // than reporting successful teardown after closing beneath live work.
        familyProven = await EnsureFamilyProofAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false);
        bool nativeHandlesSafe = familyProven && waitComplete && monitorComplete;
        if (!familyProven)
        {
            bool recordRetention;
            lock (_resourceGate)
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
                _lifecycle.Record(ProcessOwnershipFailureClass.TeardownDegraded,
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
            CloseTracked(ref _processHandle, NativeResourceKind.ProcessHandle, "process handle");
        }
        CloseTracked(ref _threadHandle, NativeResourceKind.PrimaryThreadHandle, "thread handle");
        FreeTracked(ref _environmentBlockBuffer, NativeResourceKind.EnvironmentBlockBuffer, "environment block buffer", deleteAttributeList: false);
        FreeTracked(ref _commandLineBuffer, NativeResourceKind.CommandLineBuffer, "command line buffer", deleteAttributeList: false);
        FreeTracked(ref _attributeListBuffer, NativeResourceKind.AttributeListBuffer, "attribute list buffer", deleteAttributeList: true);
        FreeTracked(ref _jobListBuffer, NativeResourceKind.JobListBuffer, "job list buffer", deleteAttributeList: false);
        if (nativeHandlesSafe)
        {
            CloseTracked(ref _completionPortHandle, NativeResourceKind.CompletionPortHandle, "completion port handle");
            CloseTracked(ref _jobHandle, NativeResourceKind.JobHandle, "job handle");
        }
        FreeTracked(ref _handleListBuffer, NativeResourceKind.HandleListBuffer, "handle list buffer", deleteAttributeList: false);
        if (drainsComplete)
        {
            CloseTracked(ref _parentStdErrRead, NativeResourceKind.StderrParentRead, "stderr pipe handle");
            CloseTracked(ref _parentStdOutRead, NativeResourceKind.StdoutParentRead, "stdout pipe handle");
        }
        CloseTracked(ref _parentStdInWrite, NativeResourceKind.StdinParentWrite, "stdin pipe handle");
        string[] teardownFailures;
        lock (_teardownEvidenceGate) { teardownFailures = _teardownFailures.ToArray(); }
        if (teardownFailures.Length > 0)
        {
            bool recordSummary;
            lock (_resourceGate)
            {
                recordSummary = !_teardownSummaryRecorded;
                _teardownSummaryRecorded = true;
            }
            if (recordSummary)
            {
                _lifecycle.Record(ProcessOwnershipFailureClass.TeardownDegraded,
                    $"{teardownFailures.Length} teardown step(s) failed: {string.Join("; ", teardownFailures)}");
            }
        }
        _lifecycle.CompleteDispose(!nativeHandlesSafe || HasOwnedTrackedResource(), disposeOperationId);
    }

    private Task<bool> EnsureTerminalAsync()
    {
        var reservation = _lifecycle.GetOrStartTerminal(RunTerminalAsync);
        return AwaitTerminal(reservation.Task, reservation.OperationId);
    }

    private async Task<bool> AwaitTerminal(Task<TerminalAttemptResult> task, long id)
    {
        TerminalAttemptResult result = await task.ConfigureAwait(false);
        _lifecycle.CompleteTerminal(id, result);
        return result.OverallSucceeded;
    }

    private async Task<TerminalAttemptResult> RunTerminalAsync(long operationId, bool nativeAlreadySucceeded)
    {
        // Never execute an injected/native call while the lifecycle monitor is held. Apart from avoiding
        // re-entrancy deadlocks, this lets Dispose join the same idempotent terminal operation.
        await Task.Yield();
        _lifecycle.SetStateIfNot(LifecycleState.TerminalInProgress, LifecycleState.DisposalInProgress);

        if (_ownershipLedger.CurrentOwned(NativeResourceKind.JobHandle) is not null && _completionMonitor is null)
        {
            try
            {
                StartCompletionMonitor();
            }
            catch (Exception ex)
            {
                _lifecycle.Record(ProcessOwnershipFailureClass.ProcessFailed,
                    $"Completion monitor startup failed during termination: {ex.Message}");
                return new TerminalAttemptResult(false, false);
            }
        }

        nint jobHandle = _ownershipLedger.CurrentOwned(NativeResourceKind.JobHandle)?.Value ?? nint.Zero;
        if (jobHandle == nint.Zero) { return new TerminalAttemptResult(true, true); }
        NativeCallResult result = nativeAlreadySucceeded
            ? NativeCallResult.Success()
            : InvokeTerminateJobObject(jobHandle, uint.MaxValue);
        if (!nativeAlreadySucceeded)
        {
            _terminateJobObjectCalled = true;
        }
        if (result.Succeeded)
        {
        }
        else
        {
            _lifecycle.Record(ProcessOwnershipFailureClass.ProcessFailed,
                $"TerminateJobObject failed with Win32 error {result.Win32Error}.");
        }

        bool familyProven = await EnsureFamilyProofAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false);
        bool succeeded = result.Succeeded && familyProven;
        return new TerminalAttemptResult(result.Succeeded, succeeded);
    }

    private Task<bool> EnsureFamilyProofAsync(TimeSpan? bound = null, CancellationToken cancellationToken = default)
    {
        var reservation = _lifecycle.GetOrStartFamilyProof(
            id => RunFamilyProofAsync(bound ?? _activeProcessZeroBound, id, cancellationToken), allowRetry: true);
        return AwaitFamilyProof(reservation.Task, reservation.OperationId);
    }

    private async Task<bool> AwaitFamilyProof(Task<bool> task, long id)
    {
        bool proven = await task.ConfigureAwait(false);
        bool current = _lifecycle.CompleteFamilyProof(id, proven);
        if (current && !proven)
        {
            // Only the active proof epoch may publish classification.  A timed-out or cancelled
            // reservation that became stale must not poison a later successful proof epoch.
            RecordFamilyProofFailure();
        }
        else if (current && proven)
        {
            _lifecycle.SetStateIfNot(LifecycleState.FamilyProofCompleted, LifecycleState.DisposalInProgress);
        }
        return proven;
    }

    private void MarkDrainCompleted(bool standardError)
    {
        _nativeCalls.LifecycleBarriers?.SignalDrainsCompleted();
    }

    private void RecordFamilyProofFailure()
    {
        bool recordFailure;
        recordFailure = Interlocked.Exchange(ref _familyProofFailureRecorded, true) == false;
        _lifecycle.SetStateIfNot(LifecycleState.FamilyProofFailed, LifecycleState.DisposalInProgress);

        if (recordFailure)
        {
            _lifecycle.Record(
                ProcessOwnershipFailureClass.ProcessFailed,
                "Family exit could not be proven within the requested wait bound: ACTIVE_PROCESS_ZERO was not observed.");
        }
    }

    private async Task<bool> RunFamilyProofAsync(TimeSpan bound, long operationId, CancellationToken cancellationToken)
    {
        await WaitForActiveProcessZero(bound, cancellationToken).ConfigureAwait(false);
        bool barrierProof = _nativeCalls.LifecycleBarriers is { ActiveProcessZeroReleased: true };
        bool proven = (_activeProcessZeroObserved || barrierProof) && !_completionObservationStoppedForTests;
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
        _lifecycle.Record(ProcessOwnershipFailureClass.DrainIncomplete, "A stream did not reach EOF within the cleanup bound.");
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

    private bool HasOwnedTrackedResource() => _ownershipLedger.HasOwned;

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

    internal ProcessOwnershipFailureClass RecordedFailureClassForTests => _lifecycle.Class;

    private bool ReleaseOwned(
        NativeResourceKind kind, string label, List<string>? failures = null,
        ReleaseObservationMode observationMode = ReleaseObservationMode.Cleanup)
    {
        NativeResourceSnapshot? owned = _ownershipLedger.CurrentOwned(kind);
        if (owned is null)
        {
            return true;
        }

        NativeCallResult result = NativeCallResult.Success();
        bool buffer = kind is NativeResourceKind.HandleListBuffer or NativeResourceKind.JobListBuffer
            or NativeResourceKind.AttributeListBuffer or NativeResourceKind.CommandLineBuffer
            or NativeResourceKind.EnvironmentBlockBuffer;
        try
        {
            if (buffer)
            {
                if (kind == NativeResourceKind.AttributeListBuffer
                    && owned.ReleasePrerequisite == NativeReleasePrerequisiteState.Ready)
                {
                    DeleteAttributeList(owned.Value);
                    _ownershipLedger.CompleteReleasePrerequisite(kind);
                }
                Marshal.FreeHGlobal(owned.Value);
            }
            else
            {
                result = _nativeCalls.CloseHandle?.Invoke(owned.Value)
                    ?? (NativeMethods.CloseHandle(owned.Value)
                        ? NativeCallResult.Success()
                        : NativeCallResult.Failure(Marshal.GetLastWin32Error()));
            }
        }
        catch (Exception ex)
        {
            result = NativeCallResult.Failure(ex.HResult);
            failures?.Add($"{label}: {ex.Message}");
        }

        _ownershipLedger.AttemptRelease(kind, result.Succeeded, result.Win32Error,
            result.Succeeded ? $"Released {label}" : $"Release {label} failed: {result.Win32Error}", owned.Value);
        if (observationMode == ReleaseObservationMode.Cleanup)
        {
            lock (_teardownEvidenceGate) { _teardownOrderForTests.Add(label); }
        }
        if (result.Succeeded)
        {
            SetRawMirror(kind, nint.Zero);
        }
        else
        {
            string evidence = $"Release {label} failed: {result.Win32Error}";
            failures?.Add(evidence);
            lock (_teardownEvidenceGate) { _teardownFailures.Add(evidence); }
        }
        if (observationMode == ReleaseObservationMode.Cleanup)
        {
            try { UnwindStepObserverForTests?.Invoke(label); }
            catch { /* construction trace is test-only and non-authoritative */ }
            try { ResourceReleaseObserverForTests?.Invoke(label); }
            catch { /* release observers are non-authoritative */ }
        }
        return result.Succeeded;
    }

    private void SetRawMirror(NativeResourceKind kind, nint value)
    {
        switch (kind)
        {
            case NativeResourceKind.ProcessHandle: _processHandle = value; break;
            case NativeResourceKind.PrimaryThreadHandle: _threadHandle = value; break;
            case NativeResourceKind.JobHandle: _jobHandle = value; break;
            case NativeResourceKind.CompletionPortHandle: _completionPortHandle = value; break;
            case NativeResourceKind.AttributeListBuffer: _attributeListBuffer = value; break;
            case NativeResourceKind.EnvironmentBlockBuffer: _environmentBlockBuffer = value; break;
            case NativeResourceKind.CommandLineBuffer: _commandLineBuffer = value; break;
            case NativeResourceKind.HandleListBuffer: _handleListBuffer = value; break;
            case NativeResourceKind.JobListBuffer: _jobListBuffer = value; break;
            case NativeResourceKind.StdoutParentRead: _parentStdOutRead = value; break;
            case NativeResourceKind.StderrParentRead: _parentStdErrRead = value; break;
            case NativeResourceKind.StdinParentWrite: _parentStdInWrite = value; break;
        }
    }

    private void CloseTracked(ref nint handle, NativeResourceKind kind, string label)
    {
        if (ReleaseOwned(kind, label))
        {
            handle = nint.Zero;
        }
    }

    private void FreeTracked(ref nint buffer, NativeResourceKind kind, string label, bool deleteAttributeList)
    {
        if (ReleaseOwned(kind, label))
        {
            buffer = nint.Zero;
        }
    }

    private void StartDrains(TimeSpan timeout)
    {
        _drainTimeout = timeout;
        long drainOperationId = _lifecycle.BeginDrainEpoch().OperationId;
        _drainOperationId = drainOperationId;
        CancellationTokenSource drainCts = new();
        _drainCts = drainCts;
        nint stdout = _ownershipLedger.CurrentOwned(NativeResourceKind.StdoutParentRead)?.Value ?? nint.Zero;
        nint stderr = _ownershipLedger.CurrentOwned(NativeResourceKind.StderrParentRead)?.Value ?? nint.Zero;
        CancellationToken token = drainCts.Token;
        _stdOutDrain = Task.Run(() => DrainPipe(stdout, timeout, false, token));
        _stdErrDrain = Task.Run(() => DrainPipe(stderr, timeout, true, token));
        _ = Task.WhenAll(_stdOutDrain, _stdErrDrain).ContinueWith(
            _ => _lifecycle.CompleteDrainEpoch(drainOperationId),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
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
                        _lifecycle.RecordSecondary($"PeekNamedPipe failed with Win32 error {peek.Win32Error}.");
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
            nint processHandle = _ownershipLedger.CurrentOwned(NativeResourceKind.ProcessHandle)?.Value ?? nint.Zero;
            NativeWaitResult call = InvokeWaitForSingleObject(processHandle, PollMs);
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
        nint jobHandle = _ownershipLedger.CurrentOwned(NativeResourceKind.JobHandle)?.Value ?? nint.Zero;
        if (jobHandle == nint.Zero || !NativeMethods.QueryInformationJobObject(
            jobHandle,
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
        lock (_completionMonitorGate)
        {
            if (_completionMonitor is not null)
            {
                return;
            }

            CancellationTokenSource? attemptedCts = null;
            try
            {
                attemptedCts = new CancellationTokenSource();
                var token = attemptedCts.Token;
                nint port = _ownershipLedger.CurrentOwned(NativeResourceKind.CompletionPortHandle)?.Value ?? nint.Zero;
                nint expectedKey = _ownershipLedger.CurrentOwned(NativeResourceKind.JobHandle)?.Value ?? nint.Zero;
                Task monitor = Task.Run(() =>
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
                            _nativeCalls.LifecycleBarriers?.SignalFamilyProofPending();
                            _nativeCalls.LifecycleBarriers?.WaitForRelease();
                            lock (_resourceGate) { _activeProcessZeroObserved = true; }
                            return;
                        }
                    }
                }, CancellationToken.None);
                _completionMonitorCts = attemptedCts;
                _completionMonitor = monitor;
            }
            catch
            {
                attemptedCts?.Dispose();
                _completionMonitorCts = null;
                _completionMonitor = null;
                throw;
            }
        }
    }

    private async Task WaitForActiveProcessZero(TimeSpan bound, CancellationToken cancellationToken)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!_activeProcessZeroObserved
            && deadline.Elapsed < bound
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
        nint handleListBuffer, int handleCount, nint jobListBuffer,
        ProcessOwnershipNativeCalls? nativeCalls, NativeOwnershipLedger ledger)
    {
        nint listSize = nint.Zero;
        NativeMethods.InitializeProcThreadAttributeList(nint.Zero, 2, 0, ref listSize);
        nint attributeListBuffer = Marshal.AllocHGlobal(listSize);
        ledger.Acquire(NativeResourceKind.AttributeListBuffer, attributeListBuffer, "Alloc attribute list");
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
            ledger.MarkReleasePrerequisiteReady(NativeResourceKind.AttributeListBuffer);
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
