#pragma warning disable IDE0011, IDE0055, CA1001
namespace SeqDoc.AcceptanceTests;

internal sealed class LifecycleOperationReceipt
{
    public string Kind { get; init; } = string.Empty;
    public long OperationId { get; init; }
    public long Epoch { get; init; }
    public bool Accepted { get; init; }
    public bool Stale { get; init; }
}

internal sealed class ProcessOwnershipLifecycleCoordinator
{
    private readonly object _gate = new();
    private long _next;
    private long _family;
    private long _terminal;
    private bool _terminalSucceeded;
    private bool _terminalCompleted;
    private readonly List<LifecycleOperationReceipt> _receipts = new();

    public IReadOnlyList<LifecycleOperationReceipt> OperationReceipts
    { get { lock (_gate) return _receipts.OrderBy(r => r.OperationId).ToArray(); } }

    public IReadOnlyList<string> ImmutableEvidence
    { get { lock (_gate) return _receipts.Select(r => $"{r.Kind}:{r.OperationId}:{r.Accepted}").ToArray(); } }

    public LifecycleOperationReceipt BeginEpoch(string kind = "Epoch") => Begin(kind);
    public LifecycleOperationReceipt BeginFamilyProofEpoch() => Begin("FamilyProof");

    public LifecycleOperationReceipt AcceptCompletion(long operationId, bool accepted = true)
    {
        lock (_gate) return Receipt("Completion", operationId, IsCurrent(operationId), accepted);
    }

    public LifecycleOperationReceipt CompleteFamilyProofEpoch(long operationId, bool nativeSuccess, bool familyProven)
    {
        lock (_gate)
        {
            bool current = operationId == _family;
            return Receipt("FamilyProof", operationId, current, current && nativeSuccess && familyProven);
        }
    }

    public LifecycleOperationReceipt JoinTerminalOperation()
    {
        lock (_gate)
        {
            if (_terminal == 0 || _terminalCompleted)
            {
                _terminal = ++_next;
                _terminalCompleted = false;
            }
            return Receipt("Terminal", _terminal, true, true);
        }
    }

    public LifecycleOperationReceipt CompleteTerminalOperation(long operationId, bool succeeded)
    {
        lock (_gate)
        {
            bool current = operationId == _terminal;
            if (current) { _terminalSucceeded = succeeded; _terminalCompleted = true; }
            return Receipt("Terminal", operationId, current, current);
        }
    }

    public LifecycleOperationReceipt RetryTerminalOperation()
    {
        lock (_gate)
        {
            if (_terminalSucceeded) return BeginLocked("FamilyProof");
            _terminal = ++_next;
            _terminalCompleted = false;
            return Receipt("Terminal", _terminal, true, true);
        }
    }

    private LifecycleOperationReceipt Begin(string kind)
    { lock (_gate) return BeginLocked(kind); }
    private LifecycleOperationReceipt BeginLocked(string kind)
    {
        long id = ++_next;
        if (kind == "FamilyProof") _family = id;
        return Receipt(kind, id, true, true);
    }
    private bool IsCurrent(long id) => id == _family || id == _terminal;
    private LifecycleOperationReceipt Receipt(string kind, long id, bool current, bool accepted) {
        var receipt = new LifecycleOperationReceipt { Kind = kind, OperationId = id, Epoch = id,
            Accepted = current && accepted, Stale = !current };
        _receipts.Add(receipt); return receipt;
    }
}

internal sealed class ProcessOwnershipLifecycleBarriers
{
    private readonly ManualResetEventSlim _drains = new(false);
    private readonly ManualResetEventSlim _familyPending = new(false);
    private readonly ManualResetEventSlim _release = new(false);
    public bool ActiveProcessZeroReleased => _release.IsSet;
    public bool DrainsCompleted => _drains.IsSet;
    public bool FamilyProofPending => _familyPending.IsSet && !_release.IsSet;
    public bool WaitForDrainsCompleted(TimeSpan timeout) => _drains.Wait(timeout);
    public bool WaitForFamilyProofPending(TimeSpan timeout) => _familyPending.Wait(timeout);
    public void ReleaseActiveProcessZero() => _release.Set();
    internal void SignalDrainsCompleted() => _drains.Set();
    internal bool HoldActiveProcessZero => _familyPending.IsSet && !_release.IsSet;
    internal void SignalFamilyProofPending() => _familyPending.Set();
    internal void WaitForRelease() => _release.Wait();
}
