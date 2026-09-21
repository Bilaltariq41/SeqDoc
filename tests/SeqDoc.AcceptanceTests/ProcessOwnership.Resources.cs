#pragma warning disable IDE0011, IDE0055, CA1001, CA1822

namespace SeqDoc.AcceptanceTests;

internal enum NativeResourceKind
{
    StdinChildRead, StdinParentWrite, StdoutParentRead, StdoutChildWrite,
    StderrParentRead, StderrChildWrite, HandleListBuffer, JobHandle,
    CompletionPortHandle, JobListBuffer, AttributeListBuffer, CommandLineBuffer,
    EnvironmentBlockBuffer, ProcessHandle, PrimaryThreadHandle,
}

internal enum NativeResourceState { Owned, Released }
internal enum NativeReleasePrerequisiteState { None, Uninitialized, Ready, Completed }

internal enum ManagedResourceKind
{
    CompletionMonitor, DrainCancellation, StdoutDrain, StderrDrain,
    ProcessWait, TerminalOperation, FamilyProof, Disposal,
}

internal enum ManagedResourceState { Unacquired, Active, Quiescent, Retained, Released }

internal abstract class ManagedResourceLease : IDisposable
{
    private int _disposed;
    public abstract object Value { get; }
    public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) DisposeCore(); }
    protected abstract void DisposeCore();
}
internal sealed class CompletionMonitorLease : ManagedResourceLease
{
    internal CompletionMonitorLease(CancellationTokenSource cts, Task task) { Cts = cts; Task = task; }
    internal CancellationTokenSource Cts { get; }
    internal Task Task { get; }
    public override object Value => Task;
    protected override void DisposeCore() => Cts.Dispose();
}
internal sealed class DrainCancellationLease : ManagedResourceLease
{
    internal DrainCancellationLease(CancellationTokenSource cts) => Cts = cts;
    internal CancellationTokenSource Cts { get; }
    public override object Value => Cts;
    protected override void DisposeCore() => Cts.Dispose();
}
internal sealed class DrainTaskLease : ManagedResourceLease
{
    internal DrainTaskLease(Task task) => Task = task;
    internal Task Task { get; }
    public override object Value => Task;
    protected override void DisposeCore() { }
}
internal sealed class ManagedTaskLease : ManagedResourceLease
{
    internal ManagedTaskLease(Task task) => Task = task;
    internal Task Task { get; }
    public override object Value => Task;
    protected override void DisposeCore() { }
}

internal sealed class ManagedResourceSnapshot
{
    public ManagedResourceKind Kind { get; init; }
    public long OperationId { get; init; }
    public long AcquisitionSequence { get; init; }
    public ManagedResourceState State { get; init; }
    public int Attempts { get; init; }
    public string Evidence { get; init; } = string.Empty;
    public string? Error { get; init; }
    public bool HasLease { get; init; }
    public bool IsImmutable => true;
}

internal sealed class NativeResourceSnapshot
{
    public NativeResourceKind Kind { get; init; }
    public long AcquisitionSequence { get; init; }
    public nint Value { get; init; }
    public NativeResourceState State { get; init; }
    public int Attempts { get; init; }
    public int Attempt => Attempts;
    public string Evidence { get; init; } = string.Empty;
    public string? Error { get; init; }
    public int ReleaseOrder { get; init; }
    public NativeReleasePrerequisiteState ReleasePrerequisite { get; init; }
    public bool IsImmutable => true;
}

internal sealed class ProcessOwnershipSnapshot
{
    public IReadOnlyList<NativeResourceSnapshot> NativeResources { get; init; } = Array.Empty<NativeResourceSnapshot>();
    public IReadOnlyList<ManagedResourceSnapshot> ManagedResources { get; init; } = Array.Empty<ManagedResourceSnapshot>();
    public bool Immutable => true;
    public bool IsImmutable => true;
}

internal sealed class NativeOwnershipLedger
{
    private sealed class Slot
    {
        public NativeResourceKind Kind;
        public long Sequence;
        public nint Value;
        public NativeResourceState State = NativeResourceState.Released;
        public int Attempts;
        public int ReleaseOrder;
        public string Evidence = string.Empty;
        public string? Error;
        public NativeReleasePrerequisiteState ReleasePrerequisite;
    }

    private readonly Slot[] _slots = Enum.GetValues<NativeResourceKind>().Select(kind => new Slot { Kind = kind }).ToArray();
    private readonly object _gate = new();
    private long _nextSequence;
    private int _nextReleaseOrder;
    private sealed class ManagedSlot
    {
        public ManagedResourceKind Kind;
        public long OperationId;
        public long AcquisitionSequence;
        public ManagedResourceState State;
        public int Attempts;
        public string Evidence = string.Empty;
        public string? Error;
        public ManagedResourceLease? Lease;
    }
    private readonly ManagedSlot[] _managed = Enum.GetValues<ManagedResourceKind>()
        .Select(kind => new ManagedSlot { Kind = kind }).ToArray();
    private long _nextManagedSequence;

    internal void Acquire(NativeResourceKind kind, nint value, string evidence)
    {
        lock (_gate)
        {
            Slot slot = _slots[(int)kind];
            if (slot.Sequence != 0)
            {
                throw new InvalidOperationException($"Resource slot {kind} has already been acquired.");
            }
            slot.Sequence = ++_nextSequence;
            slot.Value = value;
            slot.State = NativeResourceState.Owned;
            slot.ReleasePrerequisite = kind == NativeResourceKind.AttributeListBuffer
                ? NativeReleasePrerequisiteState.Uninitialized
                : NativeReleasePrerequisiteState.None;
            slot.Attempts = 0;
            slot.ReleaseOrder = 0;
            slot.Evidence = evidence;
            slot.Error = string.Empty;
        }
    }

    internal void MarkReleasePrerequisiteReady(NativeResourceKind kind)
    {
        lock (_gate)
        {
            Slot slot = _slots[(int)kind];
            if (kind != NativeResourceKind.AttributeListBuffer
                || slot.Sequence == 0
                || slot.State != NativeResourceState.Owned
                || slot.ReleasePrerequisite != NativeReleasePrerequisiteState.Uninitialized)
            {
                throw new InvalidOperationException($"Resource slot {kind} has an invalid release prerequisite transition.");
            }
            slot.ReleasePrerequisite = NativeReleasePrerequisiteState.Ready;
        }
    }

    internal void CompleteReleasePrerequisite(NativeResourceKind kind)
    {
        lock (_gate)
        {
            Slot slot = _slots[(int)kind];
            if (kind != NativeResourceKind.AttributeListBuffer
                || slot.Sequence == 0
                || slot.State != NativeResourceState.Owned
                || slot.ReleasePrerequisite != NativeReleasePrerequisiteState.Ready)
            {
                throw new InvalidOperationException($"Resource slot {kind} has an invalid release prerequisite completion.");
            }
            slot.ReleasePrerequisite = NativeReleasePrerequisiteState.Completed;
        }
    }

    internal void AttemptRelease(NativeResourceKind kind, bool success, int error, string evidence, nint value = default)
    {
        lock (_gate)
        {
            Slot slot = _slots[(int)kind];
            if (slot.Sequence == 0)
            {
                throw new InvalidOperationException($"Resource slot {kind} was never acquired.");
            }
            if (slot.State != NativeResourceState.Owned)
            {
                throw new InvalidOperationException($"Resource slot {kind} is already released.");
            }
            if (value != nint.Zero && value != slot.Value)
            {
                throw new InvalidOperationException($"Resource slot {kind} has a mismatched native value.");
            }

            slot.Attempts++;
            if (!success)
            {
                if (string.IsNullOrEmpty(slot.Error))
                {
                    slot.Error = error.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                slot.Evidence = string.IsNullOrEmpty(slot.Evidence) ? evidence : slot.Evidence + "; " + evidence;
                return;
            }
            slot.State = NativeResourceState.Released;
            slot.Value = nint.Zero;
            slot.ReleaseOrder = ++_nextReleaseOrder;
            slot.Evidence = string.IsNullOrEmpty(slot.Evidence) ? evidence : slot.Evidence + "; " + evidence;
            slot.Error = string.Empty;
        }
    }

    internal ProcessOwnershipSnapshot Snapshot()
    {
        lock (_gate)
        {
            var resources = _slots.Where(slot => slot.Sequence != 0)
                .OrderBy(slot => slot.Sequence)
                .Select(Copy)
                .ToArray();
            return new ProcessOwnershipSnapshot
            {
                NativeResources = Array.AsReadOnly(resources),
                ManagedResources = ManagedSnapshotLocked(),
            };
        }
    }

    internal IReadOnlyList<ManagedResourceSnapshot> ManagedSnapshot()
    {
        lock (_gate) return ManagedSnapshotLocked();
    }

    private System.Collections.ObjectModel.ReadOnlyCollection<ManagedResourceSnapshot> ManagedSnapshotLocked() => _managed
        .Select(slot => new ManagedResourceSnapshot
        {
            Kind = slot.Kind, OperationId = slot.OperationId,
            AcquisitionSequence = slot.AcquisitionSequence, State = slot.State,
            Attempts = slot.Attempts, Evidence = slot.Evidence, Error = slot.Error,
            HasLease = slot.Lease is not null && slot.State is not (ManagedResourceState.Unacquired or ManagedResourceState.Released),
        }).ToArray().AsReadOnly();

    internal void BeginManaged(ManagedResourceKind kind, long operationId, ManagedResourceLease lease, string evidence)
    {
        lock (_gate)
        {
            ManagedSlot slot = _managed[(int)kind];
            if (slot.State == ManagedResourceState.Active && slot.OperationId == operationId) return;
            // Task-only reservations are replaceable after a failed epoch. Disposable leases are not:
            // replacing one before its explicit quiescent release would make its CTS unreachable.
            if (slot.Lease is CompletionMonitorLease or DrainCancellationLease)
            {
                throw new InvalidOperationException($"Managed lease {kind} must be quiescent and released before replacement.");
            }
            slot.Lease = lease;
            slot.OperationId = operationId;
            if (slot.AcquisitionSequence == 0) slot.AcquisitionSequence = ++_nextManagedSequence;
            slot.State = ManagedResourceState.Active;
            slot.Attempts++;
            slot.Evidence = Append(slot.Evidence, evidence);
            slot.Error = null;
        }
    }

    internal void BeginManagedBatch(long operationId, string evidence,
        params (ManagedResourceKind Kind, ManagedResourceLease Lease)[] entries)
    {
        lock (_gate)
        {
            foreach ((ManagedResourceKind kind, ManagedResourceLease lease) in entries)
            {
                ManagedSlot slot = _managed[(int)kind];
                if ((slot.State == ManagedResourceState.Active && slot.OperationId == operationId)
                    || slot.Lease is CompletionMonitorLease or DrainCancellationLease)
                {
                    throw new InvalidOperationException($"Managed lease {kind} cannot be installed for this epoch.");
                }
            }

            foreach ((ManagedResourceKind kind, ManagedResourceLease lease) in entries)
            {
                ManagedSlot slot = _managed[(int)kind];
                slot.Lease = lease;
                slot.OperationId = operationId;
                if (slot.AcquisitionSequence == 0) slot.AcquisitionSequence = ++_nextManagedSequence;
                slot.State = ManagedResourceState.Active;
                slot.Attempts++;
                slot.Evidence = Append(slot.Evidence, evidence);
                slot.Error = null;
            }
        }
    }

    internal IReadOnlyList<IDisposable> RollbackManagedBatch(long operationId, params ManagedResourceKind[] kinds)
    {
        lock (_gate)
        {
            var released = new List<IDisposable>();
            foreach (ManagedResourceKind kind in kinds)
            {
                ManagedSlot slot = _managed[(int)kind];
                if (slot.OperationId != operationId || slot.State != ManagedResourceState.Active || slot.Lease is null)
                {
                    continue;
                }

                released.Add(slot.Lease);
                slot.Lease = null;
                slot.State = ManagedResourceState.Released;
                slot.Attempts++;
                slot.Evidence = Append(slot.Evidence, "Managed reservation rolled back");
            }
            return released.AsReadOnly();
        }
    }

    internal T? CurrentManagedLease<T>(ManagedResourceKind kind) where T : ManagedResourceLease
    { lock (_gate) return _managed[(int)kind].Lease as T; }

    internal bool HasManagedBlocker(ManagedResourceKind except)
    { lock (_gate) return _managed.Any(slot => slot.Kind != except && slot.Lease is not null && slot.State is ManagedResourceState.Active or ManagedResourceState.Retained); }

    internal IReadOnlyList<IDisposable> ReleaseQuiescentManaged()
    {
        lock (_gate)
        {
            var released = new List<IDisposable>();
            foreach (ManagedSlot slot in _managed.Where(slot => slot.State == ManagedResourceState.Quiescent && slot.Lease is not null))
            {
                released.Add(slot.Lease!);
                slot.Lease = null;
                slot.State = ManagedResourceState.Released;
                slot.Evidence = Append(slot.Evidence, "Managed lease released");
            }
            return released.AsReadOnly();
        }
    }

    internal bool CompleteManaged(ManagedResourceKind kind, long operationId, ManagedResourceState state, string evidence)
    {
        lock (_gate)
        {
            ManagedSlot slot = _managed[(int)kind];
            if (slot.State == ManagedResourceState.Unacquired || slot.OperationId != operationId) return false;
            slot.State = state;
            slot.Attempts++;
            slot.Evidence = Append(slot.Evidence, evidence);
            return true;
        }
    }

    private static string Append(string current, string next) => string.IsNullOrEmpty(current) ? next : current + "; " + next;

    private static NativeResourceSnapshot Copy(Slot slot) => new()
    {
        Kind = slot.Kind, AcquisitionSequence = slot.Sequence, Value = slot.Value,
        State = slot.State, Attempts = slot.Attempts, Evidence = slot.Evidence,
        Error = slot.Error, ReleaseOrder = slot.ReleaseOrder,
        ReleasePrerequisite = slot.ReleasePrerequisite,
    };

    internal IReadOnlyList<NativeResourceSnapshot> ReverseOwned()
    {
        lock (_gate)
        {
            return _slots.Where(slot => slot.Sequence != 0 && slot.State == NativeResourceState.Owned)
                .OrderByDescending(slot => slot.Sequence)
                .Select(Copy)
                .ToArray()
                .AsReadOnly();
        }
    }

    internal NativeResourceSnapshot? CurrentOwned(NativeResourceKind kind)
    {
        lock (_gate)
        {
            Slot slot = _slots[(int)kind];
            return slot.Sequence != 0 && slot.State == NativeResourceState.Owned ? Copy(slot) : null;
        }
    }

    internal bool HasOwned
    {
        get { lock (_gate) return _slots.Any(slot => slot.Sequence != 0 && slot.State == NativeResourceState.Owned); }
    }
}
