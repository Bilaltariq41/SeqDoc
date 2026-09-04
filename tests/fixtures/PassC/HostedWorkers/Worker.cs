using Microsoft.Extensions.Hosting;

namespace HostedWorkers
{
public sealed class ExactWorker : IHostedService
{
    private Timer? timer;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        timer = new Timer(RunJob, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        return Task.CompletedTask;
    }

    public Task ExecuteAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    private void RunJob(object? state)
    {
    }
}

public sealed class LookalikeWorker : FakeHosting.IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class BackgroundWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var iteration = 0;
        while (iteration++ < 2)
        {
            foreach (var item in Array.Empty<int>())
            {
                _ = item;
            }

            await Task.Delay(1, stoppingToken);
        }
    }
}

public sealed class RetryWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var iteration = 0;
        var completed = false;
        while (iteration++ < 2)
        {
            try
            {
                await Task.Delay(1, stoppingToken);
                WorkerCallbackContracts.RunOnce(() => WorkerCallbackContracts.Observe(iteration));

                WorkerCallbackContracts.RunWhen(iteration > 0, () => WorkerCallbackContracts.Observe(iteration));
                WorkerCallbackContracts.RunRepeated(() => WorkerCallbackContracts.Observe(iteration));

                void ObserveLocally()
                    => WorkerCallbackContracts.Observe(iteration);

                WorkerCallbackContracts.RunOnce(ObserveLocally);
                WorkerCallbackContracts.RunOnce(WorkerCallbackContracts.ObserveCurrentIteration);
                completed = true;
            }
            catch (InvalidOperationException)
            {
                WorkerCallbackContracts.RunOnce(() => WorkerCallbackContracts.Observe(iteration));
                continue;
            }

            if (completed)
            {
                break;
            }
        }
        if (!completed)
        {
            throw new InvalidOperationException("bounded retry exhausted");
        }

        await Task.Yield();
        return;
    }
}

// A source-backed callback contract used only by the accepted RetryWorker fixture.  Its callback
// body is intentionally a single non-durable operation; the callback must remain a nested region
// of the worker's retry/loop/catch context rather than becoming unconditional worker behavior.
internal static class WorkerCallbackContracts
{
    public static void RunOnce(Action callback)
    {
        callback();
    }

    public static void RunWhen(bool enabled, Action callback)
    {
        if (enabled)
        {
            callback();
        }
    }

    public static void RunRepeated(Action callback)
    {
        for (var i = 0; i < 2; i++)
        {
            callback();
        }
    }

    public static void Observe(int value)
    {
        _ = value;
    }

    public static void ObserveCurrentIteration()
    {
        Observe(0);
    }
}

// This type proves that framework capability extraction is not registration admission.
public sealed class UnregisteredWorker : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ExecuteCallbackAsync(CancellationToken cancellationToken)
    {
        WorkerCallbackContracts.RunOnce(WorkerCallbackContracts.ObserveCurrentIteration);
        return Task.CompletedTask;
    }
}

public static class UnsupportedTimerShapes
{
    public static void RegisterLambda()
    {
        _ = new Timer(_ => { }, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }
}

public sealed class SemaphoreProofWorker : BackgroundService
{
    private readonly SemaphoreSlim gate = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var iteration = 0;
        while (iteration++ < 2)
        {
            stoppingToken.ThrowIfCancellationRequested();
            await gate.WaitAsync(stoppingToken);
            try
            {
                await Task.Delay(1, stoppingToken);
            }
            finally
            {
                gate.Release();
            }
        }
    }
}

public sealed class DirectSemaphoreWorker : BackgroundService
{
    private readonly SemaphoreSlim gate = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var iteration = 0; iteration < 2; iteration++)
        {
            await gate.WaitAsync(stoppingToken);
            gate.Release();
        }
    }
}

public sealed class SemaphoreNegativeShapesWorker : BackgroundService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SemaphoreSlim other = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        gate.Wait(1);
        await gate.WaitAsync();
        await gate.WaitAsync(TimeSpan.FromSeconds(1));
        await gate.WaitAsync(1, stoppingToken);
        gate.Release(1);
        gate.Release();
        await other.WaitAsync(stoppingToken);
        gate.Release();
    }
}

public sealed class SemaphoreUnawaitedWorker : BackgroundService
{
    private readonly SemaphoreSlim gate = new(1, 1);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ = gate.WaitAsync(stoppingToken);
        gate.Release();
        return Task.CompletedTask;
    }
}

public sealed class SemaphoreLoopMismatchWorker : BackgroundService
{
    private readonly SemaphoreSlim gate = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var iteration = 0;
        while (iteration++ < 2)
        {
            await gate.WaitAsync(stoppingToken);
        }

        for (var separateIteration = 0; separateIteration < 2; separateIteration++)
        {
            gate.Release();
        }
    }
}

public sealed class SemaphoreBranchWorker : BackgroundService
{
    private readonly SemaphoreSlim gate = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            await gate.WaitAsync(stoppingToken);
        }
        else
        {
            gate.Release();
        }
    }
}

public sealed class SemaphoreConsumptionWorker : BackgroundService
{
    private readonly SemaphoreSlim gate = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await gate.WaitAsync(stoppingToken);
        await gate.WaitAsync(stoppingToken);
        gate.Release();
    }
}

public sealed class SemaphoreReceiverWorker : BackgroundService
{
    private readonly SemaphoreSlim first = new(1, 1);
    private readonly SemaphoreSlim second = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await first.WaitAsync(stoppingToken);
        second.Release();
    }
}

public sealed class SemaphoreLookalikeWorker : BackgroundService
{
    private readonly FakeSemaphore gate = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await gate.WaitAsync(stoppingToken);
        gate.Release();
    }
}

public sealed class SemaphoreDynamicWorker : BackgroundService
{
    private readonly dynamic gate = new SemaphoreSlim(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await gate.WaitAsync(stoppingToken);
        gate.Release();
    }
}

public sealed class SemaphoreExtensionWorker : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var gate = new FakeExtensionSemaphore();
        gate.WaitAsync(stoppingToken);
        gate.Release();
        return Task.CompletedTask;
    }
}

// SemaphoreSlim is inheritable in the target framework.  The call resolves to the
// exact base method, but the receiver's original storage type remains derived.
public sealed class DerivedSemaphoreWorker : BackgroundService
{
    private readonly DerivedSemaphore gate = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await gate.WaitAsync(stoppingToken);
            try
            {
                await Task.Delay(1, stoppingToken);
            }
            finally
            {
                gate.Release();
            }
        }
    }
}

// The acquire and release use the same receiver but different loop occurrences.
// A region-only or loops.Any join must not manufacture a synchronization boundary.
public sealed class SemaphoreNestedLoopWorker : BackgroundService
{
    private readonly SemaphoreSlim gate = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await gate.WaitAsync(stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                gate.Release();
                break;
            }
            break;
        }
    }
}

public sealed class DerivedSemaphore : SemaphoreSlim
{
    public DerivedSemaphore() : base(1, 1) { }
}

public sealed class CancellationNegativeWorker : BackgroundService
{
    private readonly CancellationToken field = default;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var alias = stoppingToken;
        alias.ThrowIfCancellationRequested();
        field.ThrowIfCancellationRequested();
        Check(stoppingToken);
        new FakeCancellationToken().ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private static void Check(CancellationToken token) => token.ThrowIfCancellationRequested();
}

public sealed class TerminalWorker : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class FakeSemaphore
{
    public Task WaitAsync(CancellationToken token) => Task.CompletedTask;
    public int Release() => 1;
}

public static class SemaphoreExtensions
{
    public static Task WaitAsync(this FakeExtensionSemaphore gate, CancellationToken token) => Task.CompletedTask;
    public static int Release(this FakeExtensionSemaphore gate) => 1;
}

public sealed class FakeExtensionSemaphore { }

public sealed class FakeCancellationToken
{
    public void ThrowIfCancellationRequested() { }
}
}

namespace FakeHosting
{
    public interface IHostedService
    {
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync(CancellationToken cancellationToken);
    }
}
