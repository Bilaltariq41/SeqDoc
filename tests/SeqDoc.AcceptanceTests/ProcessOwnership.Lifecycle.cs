#pragma warning disable IDE0011, IDE0055, CA1001
namespace SeqDoc.AcceptanceTests;

internal enum LifecycleState
{
    Running, TerminalRequested, TerminalInProgress, FamilyProofCompleted, FamilyProofFailed,
    DisposalInProgress, FamilyResourcesRetained, Disposed, ConstructionUnwind,
}

internal readonly record struct TerminalAttemptResult(bool NativeSucceeded, bool OverallSucceeded);

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
    private readonly FailureClassTracker _failures = new();
    private LifecycleState _state = LifecycleState.Running;
    private Task<ProcessOwnershipWaitResult>? _waitTask;
    private Task? _disposeTask;
    private Task<bool>? _familyTask;
    private long _familyId;
    private bool _familyProven;
    private Task<TerminalAttemptResult>? _terminalTask;
    private long _terminalId;
    private bool _nativeSucceeded;
    private bool _terminalCompleted;
    private long _next;
    private long _currentEpoch;
    private long _family;
    private long _terminal;
    private bool _terminalSucceeded;
    private readonly List<LifecycleOperationReceipt> _receipts = new();
    private readonly Dictionary<string, long> _current = new(StringComparer.Ordinal);

    public IReadOnlyList<LifecycleOperationReceipt> OperationReceipts
    { get { lock (_gate) return _receipts.OrderBy(r => r.OperationId).ToArray(); } }

    public IReadOnlyList<string> ImmutableEvidence
    { get { lock (_gate) return _receipts.Select(r => $"{r.Kind}:{r.OperationId}:{r.Accepted}").ToArray(); } }

    public LifecycleOperationReceipt BeginEpoch(string kind = "Epoch") => Begin(kind);
    public LifecycleOperationReceipt BeginFamilyProofEpoch() => Begin("FamilyProof");
    public LifecycleOperationReceipt BeginDrainEpoch() => Begin("Drain");

    public LifecycleOperationReceipt CompleteDrainEpoch(long operationId)
    {
        lock (_gate) return NewReceipt("DrainCompletion", operationId, IsCurrent("Drain", operationId), true);
    }

    public LifecycleOperationReceipt AcceptCompletion(long operationId, bool accepted = true)
    {
        lock (_gate)
        {
            // Keep this two-argument compatibility surface for the frozen reflection tests, but
            // resolve the kind by the exact current id.  ContainsValue alone admits a completion
            // through the wrong protocol kind when ids are accidentally reused or supplied by a
            // stale producer.
            string? kind = _current.FirstOrDefault(pair => pair.Value == operationId).Key;
            return NewReceipt(kind ?? "Completion", operationId,
                kind is not null && IsCurrent(kind, operationId) && operationId == _currentEpoch, accepted);
        }
    }

    private LifecycleOperationReceipt AcceptCompletionForKind(string kind, long operationId, bool accepted = true)
    {
        lock (_gate) return NewReceipt(kind, operationId,
            IsCurrent(kind, operationId), accepted);
    }

    public ProcessOwnershipFailureClass Class => _failures.Class;
    public string? Detail => _failures.Detail;
    public IReadOnlyList<string> SecondaryFailures => _failures.SecondaryFailures;
    public void RecordFailure(ProcessOwnershipFailureClass failureClass, string detail) => _failures.Record(failureClass, detail);
    public void Record(ProcessOwnershipFailureClass failureClass, string detail) => _failures.Record(failureClass, detail);
    public void RecordSecondary(string detail) => _failures.RecordSecondary(detail);
    public LifecycleState State { get { lock (_gate) return _state; } }
    public bool IsState(LifecycleState state) => State == state;
    public void SetState(LifecycleState state) { lock (_gate) _state = state; }
    public void SetStateIfNot(LifecycleState state, LifecycleState excluded)
    { lock (_gate) { if (_state != excluded) _state = state; } }
    public bool IsDisposedOrDisposing => State is LifecycleState.DisposalInProgress or LifecycleState.Disposed;
    public bool IsTerminalActive { get { lock (_gate) return _terminalTask is not null; } }
    public Task<ProcessOwnershipWaitResult>? WaitTask { get { lock (_gate) return _waitTask; } }
    public bool NativeAlreadySucceeded { get { lock (_gate) return _nativeSucceeded; } }

    public Task<ProcessOwnershipWaitResult> GetOrStartWait(Func<Task<ProcessOwnershipWaitResult>> factory)
    {
        TaskCompletionSource<ProcessOwnershipWaitResult>? reservation = null;
        long id;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposedOrDisposing, this);
            if (_waitTask is not null)
            {
                if (_waitTask.IsCompleted && (_waitTask.IsFaulted || _waitTask.IsCanceled))
                {
                    _waitTask = null;
                }
                else
                {
                    return _waitTask;
                }
            }
            reservation = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _waitTask = reservation.Task;
            id = ++_next;
            _current["Wait"] = id;
            _currentEpoch = id;
            Receipt("Wait", id, true, true);
        }
            _ = CompleteWaitReservation(reservation, id, factory);
        return reservation.Task;
    }

    public Task GetOrStartDispose(Func<long, Task> factory, out bool retry)
    {
        TaskCompletionSource<bool>? reservation = null;
        long id;
        lock (_gate)
        {
            if (_state == LifecycleState.Disposed)
            {
                retry = false;
                return Task.CompletedTask;
            }
            retry = _state == LifecycleState.FamilyResourcesRetained;
            if (_disposeTask is not null && !(retry && _disposeTask.IsCompleted)) return _disposeTask;
            reservation = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = reservation.Task;
            id = ++_next;
            _current["Dispose"] = id;
            _currentEpoch = id;
            Receipt("Dispose", id, true, true);
            _state = LifecycleState.DisposalInProgress;
            if (retry) _familyTask = null;
        }
        _ = CompleteDisposeReservation(reservation, id, factory);
        return reservation.Task;
    }

    private async Task CompleteWaitReservation(TaskCompletionSource<ProcessOwnershipWaitResult> slot, long id, Func<Task<ProcessOwnershipWaitResult>> factory)
    {
        try { ProcessOwnershipWaitResult result = await factory().ConfigureAwait(false); AcceptCompletionForKind("Wait", id); slot.SetResult(result); }
        catch (Exception ex)
        {
            AcceptCompletionForKind("Wait", id, false);
            lock (_gate) { if (ReferenceEquals(_waitTask, slot.Task)) _waitTask = null; }
            slot.SetException(ex);
        }
    }
    private async Task CompleteDisposeReservation(TaskCompletionSource<bool> slot, long id, Func<long, Task> factory)
    {
        try { await factory(id).ConfigureAwait(false); AcceptCompletionForKind("Dispose", id); slot.SetResult(true); }
        catch (Exception ex) { AcceptCompletionForKind("Dispose", id, false); slot.SetException(ex); }
    }

    public void CompleteDispose(bool retained, long? operationId = null)
    { lock (_gate) { if (operationId is null || IsCurrent("Dispose", operationId.Value)) { _state = retained ? LifecycleState.FamilyResourcesRetained : LifecycleState.Disposed; _disposeTask = null; } } }

    public (Task<TerminalAttemptResult> Task, long OperationId) GetOrStartTerminal(
        Func<long, bool, Task<TerminalAttemptResult>> factory)
    {
        long id;
        bool native;
        TaskCompletionSource<TerminalAttemptResult>? reservation = null;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_state == LifecycleState.Disposed, this);
            if (_state != LifecycleState.DisposalInProgress)
            {
                _state = LifecycleState.TerminalRequested;
            }
            if (_terminalTask is not null
                && (!_terminalTask.IsCompleted || !_terminalCompleted || _terminalSucceeded))
            {
                Receipt("Terminal", _terminalId, true, false);
                return (_terminalTask, _terminalId);
            }
            _terminalId = ++_next; id = _terminalId; native = _nativeSucceeded;
            _current["Terminal"] = id;
            _currentEpoch = id;
            reservation = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _terminalTask = reservation.Task;
            _terminalCompleted = false;
            Receipt("Terminal", id, true, true);
        }
        _ = CompleteTerminalReservation(reservation, id, native, factory);
        return (reservation.Task, id);
    }

    private async Task CompleteTerminalReservation(TaskCompletionSource<TerminalAttemptResult> slot, long id, bool native,
        Func<long, bool, Task<TerminalAttemptResult>> factory)
    {
        try { slot.SetResult(await factory(id, native).ConfigureAwait(false)); }
        catch (Exception ex)
        {
            FaultTerminal(id);
            slot.SetException(ex);
        }
    }

    public void FaultTerminal(long id)
    {
        lock (_gate)
        {
            if (id == _terminalId && IsCurrent("Terminal", id))
            {
                _terminalCompleted = true;
                _terminalSucceeded = false;
            }
        }
    }

    public void CompleteTerminal(long id, TerminalAttemptResult result)
    {
        lock (_gate)
        {
            if (id != _terminalId || !IsCurrent("Terminal", id)) { Receipt("Terminal", id, false, false); return; }
            _nativeSucceeded |= result.NativeSucceeded;
            _terminalSucceeded = result.OverallSucceeded;
            _terminalCompleted = true;
        }
    }

    public (Task<bool> Task, long OperationId) GetOrStartFamilyProof(
        Func<long, Task<bool>> factory, bool allowRetry)
    {
        long id;
        TaskCompletionSource<bool>? reservation = null;
        lock (_gate)
        {
            if (_familyProven)
            {
                id = _familyId == 0 ? ++_next : _familyId;
                return (Task.FromResult(true), id);
            }
            if (_familyTask is not null && !(_familyTask.IsCompleted && allowRetry))
            {
                return (_familyTask, _familyId);
            }
            _familyId = ++_next; id = _familyId;
            _current["FamilyProof"] = id;
            _currentEpoch = id;
            reservation = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _familyTask = reservation.Task;
            Receipt("FamilyProof", id, true, true);
        }
        _ = CompleteFamilyReservation(reservation, id, factory);
        return (reservation.Task, id);
    }

    private static async Task CompleteFamilyReservation(TaskCompletionSource<bool> slot, long id, Func<long, Task<bool>> factory)
    { try { slot.SetResult(await factory(id).ConfigureAwait(false)); } catch (Exception ex) { slot.SetException(ex); } }

    public bool CompleteFamilyProof(long id, bool proven)
    {
        lock (_gate)
        {
            if (id != _familyId || !IsCurrent("FamilyProof", id)) return false;
            if (proven) _familyProven = true;
            return true;
        }
    }

    public LifecycleOperationReceipt CompleteFamilyProofEpoch(long operationId, bool nativeSuccess, bool familyProven)
    {
        lock (_gate)
        {
            bool current = IsCurrent("FamilyProof", operationId) && operationId == _family;
            if (current && familyProven) _familyProven = true;
            return NewReceipt("FamilyProofCompletion", operationId, current, current && nativeSuccess && familyProven);
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
                _current["Terminal"] = _terminal;
                _currentEpoch = _terminal;
            }
            // A join is evidence of reuse, not another accepted start receipt.
            return Receipt("Terminal", _terminal, true, false);
        }
    }

    public LifecycleOperationReceipt CompleteTerminalOperation(long operationId, bool succeeded)
    {
        lock (_gate)
        {
            bool current = IsCurrent("Terminal", operationId) && operationId == _terminal;
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
            _current["Terminal"] = _terminal;
            _currentEpoch = _terminal;
            return Receipt("Terminal", _terminal, true, true);
        }
    }

    private LifecycleOperationReceipt Begin(string kind)
    { lock (_gate) return BeginLocked(kind); }
    private LifecycleOperationReceipt BeginLocked(string kind)
    {
        long id = ++_next;
        _current[kind] = id;
        _currentEpoch = id;
        if (kind == "FamilyProof") _family = id;
        return Receipt(kind, id, true, true);
    }
    private bool IsCurrent(string kind, long id) => _current.TryGetValue(kind, out long current) && current == id;
    private LifecycleOperationReceipt Receipt(string kind, long id, bool current, bool accepted) {
        var receipt = NewReceipt(kind, id, current, accepted);
        _receipts.Add(receipt); return receipt;
    }
    private static LifecycleOperationReceipt NewReceipt(string kind, long id, bool current, bool accepted) {
        return new LifecycleOperationReceipt { Kind = kind, OperationId = id, Epoch = id,
            Accepted = current && accepted, Stale = !current };
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
