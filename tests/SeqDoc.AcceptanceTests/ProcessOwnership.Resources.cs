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
    public IReadOnlyList<string> ManagedResources { get; init; } = Array.Empty<string>();
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
                ManagedResources = Array.AsReadOnly(Array.Empty<string>()),
            };
        }
    }

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
