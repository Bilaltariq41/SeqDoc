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
    private readonly NativeOwnershipLedger _ledger;
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

    public ProcessOwnershipLifecycleCoordinator() : this(new NativeOwnershipLedger()) { }
    internal ProcessOwnershipLifecycleCoordinator(NativeOwnershipLedger ledger) => _ledger = ledger;
    public IReadOnlyList<ManagedResourceSnapshot> ManagedResources => _ledger.ManagedSnapshot();
    internal T? CurrentManagedLease<T>(ManagedResourceKind kind) where T : ManagedResourceLease
        => _ledger.CurrentManagedLease<T>(kind);

    public IReadOnlyList<LifecycleOperationReceipt> OperationReceipts
    { get { lock (_gate) return _receipts.OrderBy(r => r.OperationId).ToArray(); } }

    public IReadOnlyList<string> ImmutableEvidence
    { get { lock (_gate) return _receipts.Select(r => $"{r.Kind}:{r.OperationId}:{r.Accepted}").ToArray(); } }

    public LifecycleOperationReceipt BeginEpoch(string kind = "Epoch") => Begin(kind);
    public LifecycleOperationReceipt BeginFamilyProofEpoch() => Begin("FamilyProof");
    public LifecycleOperationReceipt BeginDrainEpoch() => Begin("Drain");
    internal LifecycleOperationReceipt BeginDrainEpoch(CancellationTokenSource cts, Task stdout, Task stderr)
    {
        lock (_gate)
        {
            long id = ++_next;
            _current["Drain"] = id;
            _currentEpoch = id;
            _ledger.BeginManagedBatch(id, "Drain reservations published",
                (ManagedResourceKind.DrainCancellation, new DrainCancellationLease(cts)),
                (ManagedResourceKind.StdoutDrain, new DrainTaskLease(stdout)),
                (ManagedResourceKind.StderrDrain, new DrainTaskLease(stderr)));
            return Receipt("Drain", id, true, true);
        }
    }
    public LifecycleOperationReceipt BeginCompletionMonitorEpoch() => Begin("CompletionMonitor");
    internal LifecycleOperationReceipt BeginCompletionMonitorEpoch(CancellationTokenSource cts, Task task)
        => Begin("CompletionMonitor", new CompletionMonitorLease(cts, task));

    internal IReadOnlyList<IDisposable> RollbackDrainEpoch(long operationId)
    {
        lock (_gate)
        {
            if (!IsCurrent("Drain", operationId)) return Array.Empty<IDisposable>();
            _current.Remove("Drain");
            return _ledger.RollbackManagedBatch(operationId,
                ManagedResourceKind.DrainCancellation, ManagedResourceKind.StdoutDrain, ManagedResourceKind.StderrDrain);
        }
    }

    internal void RetainDrainEpoch(long operationId, string evidence)
    {
        lock (_gate)
        {
            if (!IsCurrent("Drain", operationId)) return;
            foreach (ManagedResourceKind kind in new[]
            {
                ManagedResourceKind.DrainCancellation,
                ManagedResourceKind.StdoutDrain,
                ManagedResourceKind.StderrDrain,
            })
            {
                _ledger.CompleteManaged(kind, operationId, ManagedResourceState.Retained, evidence);
            }
        }
    }

    internal IReadOnlyList<IDisposable> RollbackCompletionMonitorEpoch(long operationId)
    {
        lock (_gate)
        {
            if (!IsCurrent("CompletionMonitor", operationId)) return Array.Empty<IDisposable>();
            _current.Remove("CompletionMonitor");
            return _ledger.RollbackManagedBatch(operationId, ManagedResourceKind.CompletionMonitor);
        }
    }

    internal void RetainCompletionMonitorEpoch(long operationId, string evidence)
    {
        lock (_gate)
        {
            if (!IsCurrent("CompletionMonitor", operationId)) return;
            _ledger.CompleteManaged(ManagedResourceKind.CompletionMonitor, operationId,
                ManagedResourceState.Retained, evidence);
        }
    }

    public LifecycleOperationReceipt CompleteCompletionMonitorEpoch(long operationId, bool retained = false)
    {
        lock (_gate)
        {
            bool current = IsCurrent("CompletionMonitor", operationId);
            if (current) _ledger.CompleteManaged(ManagedResourceKind.CompletionMonitor, operationId,
                retained ? ManagedResourceState.Retained : ManagedResourceState.Quiescent,
                retained ? "Completion monitor retained" : "Completion monitor completed");
            return NewReceipt("CompletionMonitorCompletion", operationId, current, current);
        }
    }

    public void CompleteLatestCompletionMonitor(bool retained = false)
    {
        lock (_gate)
        {
            if (_current.TryGetValue("CompletionMonitor", out long id))
                _ledger.CompleteManaged(ManagedResourceKind.CompletionMonitor, id,
                    retained ? ManagedResourceState.Retained : ManagedResourceState.Quiescent,
                    retained ? "Completion monitor retained" : "Completion monitor completed");
        }
    }

    public LifecycleOperationReceipt CompleteDrainEpoch(long operationId)
    {
        lock (_gate)
        {
            bool current = IsCurrent("Drain", operationId);
            if (current)
            {
                foreach (ManagedResourceKind kind in new[] { ManagedResourceKind.DrainCancellation, ManagedResourceKind.StdoutDrain, ManagedResourceKind.StderrDrain })
                    _ledger.CompleteManaged(kind, operationId, ManagedResourceState.Quiescent, "Drain completed");
            }
            return NewReceipt("DrainCompletion", operationId, current, true);
        }
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
    public Task<ProcessOwnershipWaitResult>? WaitTask =>
        _ledger.CurrentManagedLease<ManagedTaskLease>(ManagedResourceKind.ProcessWait)?.Task
            as Task<ProcessOwnershipWaitResult>;
    public bool NativeAlreadySucceeded { get { lock (_gate) return _nativeSucceeded; } }

    public Task<ProcessOwnershipWaitResult> GetOrStartWait(Func<Task<ProcessOwnershipWaitResult>> factory)
    {
        TaskCompletionSource<ProcessOwnershipWaitResult> reservation =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
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
            _waitTask = reservation.Task;
            id = ++_next;
            _current["Wait"] = id;
            _currentEpoch = id;
            _ledger.BeginManaged(ManagedResourceKind.ProcessWait, id, new ManagedTaskLease(reservation.Task), "Wait started");
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
            // Keep the reservation published through out-of-lock lease disposal and final state
            // publication. Joiners must not create a retry epoch while that wrapper is still settling.
            if (_disposeTask is not null)
            {
                retry = false;
                return _disposeTask;
            }
            retry = _state == LifecycleState.FamilyResourcesRetained;
            id = ++_next;
            _current["Dispose"] = id;
            _currentEpoch = id;
            reservation = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _ledger.BeginManaged(ManagedResourceKind.Disposal, id, new ManagedTaskLease(reservation.Task), "Disposal started");
            _disposeTask = reservation.Task;
            Receipt("Dispose", id, true, true);
            _state = LifecycleState.DisposalInProgress;
            if (retry) _familyTask = null;
        }
        _ = CompleteDisposeReservation(reservation, id, factory);
        return reservation.Task;
    }

    private async Task CompleteWaitReservation(TaskCompletionSource<ProcessOwnershipWaitResult> slot, long id, Func<Task<ProcessOwnershipWaitResult>> factory)
    {
        try { ProcessOwnershipWaitResult result = await factory().ConfigureAwait(false); AcceptCompletionForKind("Wait", id); _ledger.CompleteManaged(ManagedResourceKind.ProcessWait, id, ManagedResourceState.Quiescent, "Wait completed"); slot.SetResult(result); }
        catch (Exception ex)
        {
            AcceptCompletionForKind("Wait", id, false);
            _ledger.CompleteManaged(ManagedResourceKind.ProcessWait, id, ManagedResourceState.Quiescent, "Wait faulted");
            lock (_gate) { if (ReferenceEquals(_waitTask, slot.Task)) _waitTask = null; }
            slot.SetException(ex);
        }
    }
    private async Task CompleteDisposeReservation(TaskCompletionSource<bool> slot, long id, Func<long, Task> factory)
    {
        try
        {
            await factory(id).ConfigureAwait(false);
            AcceptCompletionForKind("Dispose", id);
            slot.SetResult(true);
        }
        catch (Exception ex)
        {
            AcceptCompletionForKind("Dispose", id, false);
            _ledger.CompleteManaged(ManagedResourceKind.Disposal, id, ManagedResourceState.Retained, "Disposal faulted");
            lock (_gate)
            {
                if (IsCurrent("Dispose", id)) _state = LifecycleState.FamilyResourcesRetained;
            }
            slot.SetException(ex);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_disposeTask, slot.Task)) _disposeTask = null;
            }
        }
    }

    public IReadOnlyList<IDisposable> CompleteDispose(bool retained, long? operationId = null)
    {
        IReadOnlyList<IDisposable> leases;
        long id;
        lock (_gate)
        {
            if (operationId is not null && !IsCurrent("Dispose", operationId.Value)) return Array.Empty<IDisposable>();
            id = operationId ?? (_current.TryGetValue("Dispose", out long existing) ? existing : BeginLocked("Dispose").OperationId);
            bool blocked = retained || _ledger.HasManagedBlocker(ManagedResourceKind.Disposal);
            _ledger.CompleteManaged(ManagedResourceKind.Disposal, id,
                blocked ? ManagedResourceState.Retained : ManagedResourceState.Quiescent,
                blocked ? "Cleanup retained" : "Disposal quiescent");
            if (blocked)
            {
                _state = LifecycleState.FamilyResourcesRetained;
                return Array.Empty<IDisposable>();
            }
            leases = _ledger.ReleaseQuiescentManaged();
        }

        // CTS/task lease disposal is outside both coordinator and ledger locks. Publish Disposed only
        // after every detached managed lease has finished its disposal.
        foreach (IDisposable lease in leases)
        {
            lease.Dispose();
        }
        lock (_gate)
        {
            if (IsCurrent("Dispose", id)) _state = LifecycleState.Disposed;
        }
        return Array.Empty<IDisposable>();
    }

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
            _ledger.BeginManaged(ManagedResourceKind.TerminalOperation, id, new ManagedTaskLease(reservation.Task), "Terminal started");
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
                _ledger.CompleteManaged(ManagedResourceKind.TerminalOperation, id, ManagedResourceState.Retained, "Terminal faulted");
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
            _ledger.CompleteManaged(ManagedResourceKind.TerminalOperation, id,
                result.OverallSucceeded ? ManagedResourceState.Quiescent : ManagedResourceState.Retained,
                result.OverallSucceeded ? "Terminal completed" : "Terminal failed");
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
            _ledger.BeginManaged(ManagedResourceKind.FamilyProof, id, new ManagedTaskLease(reservation.Task), "Family proof started");
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
            _ledger.CompleteManaged(ManagedResourceKind.FamilyProof, id, proven ? ManagedResourceState.Quiescent : ManagedResourceState.Retained, proven ? "Family proof established" : "Family proof unproven");
            return true;
        }
    }

    public LifecycleOperationReceipt CompleteFamilyProofEpoch(long operationId, bool nativeSuccess, bool familyProven)
    {
        lock (_gate)
        {
            bool current = IsCurrent("FamilyProof", operationId) && operationId == _family;
            if (current && familyProven) _familyProven = true;
            if (current) _ledger.CompleteManaged(ManagedResourceKind.FamilyProof, operationId, familyProven ? ManagedResourceState.Quiescent : ManagedResourceState.Retained, familyProven ? "Family proof established" : "Family proof unproven");
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
                _ledger.BeginManaged(ManagedResourceKind.TerminalOperation, _terminal, new ManagedTaskLease(Task.CompletedTask), "Terminal joined");
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
            if (current) _ledger.CompleteManaged(ManagedResourceKind.TerminalOperation, operationId, succeeded ? ManagedResourceState.Quiescent : ManagedResourceState.Retained, succeeded ? "Terminal completed" : "Terminal failed");
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
            _ledger.BeginManaged(ManagedResourceKind.TerminalOperation, _terminal, new ManagedTaskLease(Task.CompletedTask), "Terminal retry started");
            return Receipt("Terminal", _terminal, true, true);
        }
    }

    private LifecycleOperationReceipt Begin(string kind) => Begin(kind, new ManagedTaskLease(Task.CompletedTask));
    private LifecycleOperationReceipt Begin(string kind, ManagedResourceLease lease)
    { lock (_gate) return BeginLocked(kind, lease); }
    private LifecycleOperationReceipt BeginLocked(string kind) => BeginLocked(kind, new ManagedTaskLease(Task.CompletedTask));
    private LifecycleOperationReceipt BeginLocked(string kind, ManagedResourceLease lease)
    {
        long id = ++_next;
        _current[kind] = id;
        _currentEpoch = id;
        if (kind == "FamilyProof") _family = id;
        if (kind == "FamilyProof") _ledger.BeginManaged(ManagedResourceKind.FamilyProof, id, lease, "Family proof started");
        else if (kind == "Drain")
            foreach (ManagedResourceKind managed in new[] { ManagedResourceKind.DrainCancellation, ManagedResourceKind.StdoutDrain, ManagedResourceKind.StderrDrain }) _ledger.BeginManaged(managed, id, managed == ManagedResourceKind.DrainCancellation ? new DrainCancellationLease(new CancellationTokenSource()) : new DrainTaskLease(Task.CompletedTask), "Drain started");
        else if (kind == "CompletionMonitor") _ledger.BeginManaged(ManagedResourceKind.CompletionMonitor, id, lease, "Completion monitor started");
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
