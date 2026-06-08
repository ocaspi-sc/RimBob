using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RimBob.Coordination;
using System.Threading.Channels;

namespace RimBob.Ministers.Willie;

public sealed class WillieSolveWorker : IHostedService, IDisposable
{
    private readonly ChannelWillieSolveQueue _queue;
    private readonly WillieSolveExecutor _executor;
    private readonly WillieSolverStore _solverStore;
    private readonly AdviceBus _bus;
    private readonly ILogger<WillieSolveWorker> _log;
    private readonly WillieSolveOptions _options;
    private readonly List<Thread> _threads = [];
    private readonly SemaphoreSlim _rimApiGate;
    private CancellationTokenSource? _stopping;

    public WillieSolveWorker(
        ChannelWillieSolveQueue queue,
        WillieSolveExecutor executor,
        WillieSolverStore solverStore,
        AdviceBus bus,
        IOptions<WillieSolveOptions> options,
        ILogger<WillieSolveWorker> log)
    {
        _queue = queue;
        _executor = executor;
        _solverStore = solverStore;
        _bus = bus;
        _log = log;
        _options = options.Value;
        _rimApiGate = new SemaphoreSlim(_options.EffectiveRimApiGate, _options.EffectiveRimApiGate);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stopping = new CancellationTokenSource();
        for (int i = 0; i < _options.EffectiveDegree; i++)
        {
            Thread thread = new(() => WorkerLoop(_stopping.Token))
            {
                IsBackground = true,
                Name = $"WillieSolveWorker-{i + 1}",
                Priority = _options.EffectiveWorkerPriority
            };
            _threads.Add(thread);
            thread.Start();
        }

        _log.LogInformation(
            "Started Willie solve worker pool degree={Degree} priority={Priority} rimapi_gate={Gate}",
            _options.EffectiveDegree,
            _options.EffectiveWorkerPriority,
            _options.EffectiveRimApiGate);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping?.Cancel();
        foreach (Thread thread in _threads)
        {
            while (thread.IsAlive && !cancellationToken.IsCancellationRequested)
            {
                thread.Join(TimeSpan.FromMilliseconds(100));
            }
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _stopping?.Cancel();
        _stopping?.Dispose();
        _rimApiGate.Dispose();
    }

    private void WorkerLoop(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            IWillieSolveJob job;
            try
            {
                job = _queue.DequeueAsync(stoppingToken).AsTask().GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ChannelClosedException)
            {
                return;
            }

            RunJobAsync(job, stoppingToken).GetAwaiter().GetResult();
        }
    }

    private async Task RunJobAsync(IWillieSolveJob job, CancellationToken stoppingToken)
    {
        using CancellationTokenSource jobCts = _queue.BeginRun(job, stoppingToken);
        try
        {
            if (jobCts.IsCancellationRequested)
            {
                _solverStore.RecordStale(job, "Solve job was superseded before it started.");
                return;
            }

            PlacementSolveAttempt attempt = job.Kind == WillieSolveKind.Building
                ? await RunBuildingJobAsync(job, jobCts.Token)
                : await _executor.SolveAndRecordAsync(job, jobCts.Token);

            if (jobCts.IsCancellationRequested)
            {
                _solverStore.RecordStale(job, "Solve job was superseded before it could patch advice.");
                return;
            }

            TryPatchAdvice(job, attempt);
        }
        catch (OperationCanceledException)
        {
            _solverStore.RecordStale(job, "Solve job was canceled or superseded.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unhandled Willie solve worker failure for job {JobId}", job.JobId);
            _solverStore.RecordStale(job, $"Solve worker failed unexpectedly: {ex.GetType().Name}.");
        }
        finally
        {
            _queue.Complete(job);
        }
    }

    private async Task<PlacementSolveAttempt> RunBuildingJobAsync(
        IWillieSolveJob job,
        CancellationToken ct)
    {
        await _rimApiGate.WaitAsync(ct);
        try
        {
            return await _executor.SolveAndRecordAsync(job, ct);
        }
        finally
        {
            _rimApiGate.Release();
        }
    }

    private void TryPatchAdvice(IWillieSolveJob job, PlacementSolveAttempt attempt)
    {
        if (string.IsNullOrWhiteSpace(job.DrivingAdviceId))
            return;
        if (!attempt.IsTerminal)
            return;
        if (!_solverStore.CanPatchAdvice(job))
        {
            _solverStore.RecordStale(job, "Solve result no longer matches the current request board.");
            return;
        }

        bool patched = _bus.TryPatchMinisterAdvice(job.Minister, job.DrivingAdviceId, item =>
        {
            if (WillieAdviceComposer.HasApplyResult(item))
                return null;
            if (!_solverStore.CanPatchAdvice(job))
                return null;

            return WillieAdviceComposer.EnrichAdviceItem(
                item,
                attempt,
                job.RemoveFallbackWhenApplyReady);
        });

        if (!patched)
            _solverStore.RecordStale(job, "Solve result could not patch the current advice card.");
    }
}
