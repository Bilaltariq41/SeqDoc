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
            if (slot.State == NativeResourceState.Owned) return;
            slot.Sequence = ++_nextSequence;
            slot.Value = value;
            slot.State = NativeResourceState.Owned;
            slot.Evidence = evidence;
            slot.Error = null;
        }
    }

    internal void AttemptRelease(NativeResourceKind kind, bool success, int error, string evidence, nint value = default)
    {
        lock (_gate)
        {
            Slot slot = _slots[(int)kind];
            if (slot.State != NativeResourceState.Owned)
            {
                if (success) return;
                slot.Sequence = ++_nextSequence;
                slot.Value = value;
                slot.State = NativeResourceState.Owned;
                slot.Attempts = 1;
                slot.Evidence = evidence;
                slot.Error = error.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                slot.Attempts++;
                if (!success)
                {
                    slot.Error ??= error.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    slot.Evidence = string.IsNullOrEmpty(slot.Evidence) ? evidence : slot.Evidence + "; " + evidence;
                    return;
                }
                slot.State = NativeResourceState.Released;
                slot.Value = nint.Zero;
                slot.ReleaseOrder = ++_nextReleaseOrder;
                slot.Evidence = string.IsNullOrEmpty(slot.Evidence) ? evidence : slot.Evidence + "; " + evidence;
                slot.Error = null;
            }
        }
    }

    internal ProcessOwnershipSnapshot Snapshot()
    {
        lock (_gate)
        {
            var resources = _slots.Where(slot => slot.Sequence != 0).Select(slot => new NativeResourceSnapshot
            {
                Kind = slot.Kind, AcquisitionSequence = slot.Sequence, Value = slot.Value,
                State = slot.State, Attempts = slot.Attempts, Evidence = slot.Evidence,
                Error = slot.Error, ReleaseOrder = slot.ReleaseOrder,
            }).ToArray();
            return new ProcessOwnershipSnapshot { NativeResources = resources, ManagedResources = Array.Empty<string>() };
        }
    }

    internal bool HasOwned
    {
        get { lock (_gate) return _slots.Any(slot => slot.Sequence != 0 && slot.State == NativeResourceState.Owned); }
    }
}
