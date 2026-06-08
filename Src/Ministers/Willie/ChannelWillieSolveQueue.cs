using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RimBob.Ministers.Willie;

public sealed class ChannelWillieSolveQueue : IWillieSolveQueue, IDisposable
{
    private readonly object _lock = new();
    private readonly Channel<IWillieSolveJob> _channel;
    private readonly WillieSolverStore _solverStore;
    private readonly ILogger<ChannelWillieSolveQueue> _log;
    private readonly Dictionary<string, JobQueueState> _jobsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _currentJobByRequest = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CancellationTokenSource> _cancellations = new(StringComparer.OrdinalIgnoreCase);
    private long _enqueued;
    private long _completed;
    private long _rejected;

    public ChannelWillieSolveQueue(
        IOptions<WillieSolveOptions> options,
        WillieSolverStore solverStore,
        ILogger<ChannelWillieSolveQueue> log)
    {
        WillieSolveOptions opts = options.Value;
        _solverStore = solverStore;
        _log = log;
        _channel = Channel.CreateBounded<IWillieSolveJob>(new BoundedChannelOptions(opts.EffectiveQueueCap)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });
    }

    public WillieSolveQueueStatus Status
    {
        get
        {
            lock (_lock)
            {
                return new WillieSolveQueueStatus(
                    Queued: _jobsByKey.Values.Count(state => state.State == WillieSolveQueueItemState.Queued),
                    Running: _jobsByKey.Values.Count(state => state.State == WillieSolveQueueItemState.Running),
                    EnqueuedThisSession: Interlocked.Read(ref _enqueued),
                    CompletedThisSession: Interlocked.Read(ref _completed),
                    RejectedThisSession: Interlocked.Read(ref _rejected));
            }
        }
    }

    public WillieSolveEnqueueResult TryEnqueue(IWillieSolveJob job)
    {
        string jobKey = JobKey(job);
        string requestLane = RequestLane(job);
        CancellationTokenSource jobCancellation = new();

        lock (_lock)
        {
            if (_jobsByKey.TryGetValue(jobKey, out JobQueueState? existing) &&
                existing.State is WillieSolveQueueItemState.Queued or WillieSolveQueueItemState.Running)
            {
                jobCancellation.Dispose();
                return new WillieSolveEnqueueResult(WillieSolveQueueItemState.Duplicate);
            }

            if (_currentJobByRequest.TryGetValue(requestLane, out string? oldJobKey) &&
                _cancellations.TryGetValue(oldJobKey, out CancellationTokenSource? oldCancellation))
            {
                oldCancellation.Cancel();
            }

            _jobsByKey[jobKey] = new JobQueueState(job, WillieSolveQueueItemState.Queued);
            _currentJobByRequest[requestLane] = jobKey;
            _cancellations[jobKey] = jobCancellation;
        }

        if (!_channel.Writer.TryWrite(job))
        {
            lock (_lock)
            {
                _jobsByKey.Remove(jobKey);
                _cancellations.Remove(jobKey);
                if (_currentJobByRequest.TryGetValue(requestLane, out string? current) &&
                    string.Equals(current, jobKey, StringComparison.OrdinalIgnoreCase))
                {
                    _currentJobByRequest.Remove(requestLane);
                }
            }
            jobCancellation.Dispose();
            Interlocked.Increment(ref _rejected);
            _log.LogWarning(
                "Willie solve queue rejected job {JobId}; queue is full.",
                job.JobId);
            return new WillieSolveEnqueueResult(WillieSolveQueueItemState.Rejected);
        }

        Interlocked.Increment(ref _enqueued);
        _solverStore.RecordQueued(job);
        return new WillieSolveEnqueueResult(WillieSolveQueueItemState.Queued);
    }

    internal async ValueTask<IWillieSolveJob> DequeueAsync(CancellationToken ct) =>
        await _channel.Reader.ReadAsync(ct);

    internal CancellationTokenSource BeginRun(IWillieSolveJob job, CancellationToken workerStopping)
    {
        string jobKey = JobKey(job);
        CancellationTokenSource? jobCancellation;
        lock (_lock)
        {
            if (_jobsByKey.TryGetValue(jobKey, out JobQueueState? state))
                _jobsByKey[jobKey] = state with { State = WillieSolveQueueItemState.Running };

            _cancellations.TryGetValue(jobKey, out jobCancellation);
        }

        if (jobCancellation?.IsCancellationRequested != true)
            _solverStore.RecordRunning(job);
        return jobCancellation is null
            ? CancellationTokenSource.CreateLinkedTokenSource(workerStopping)
            : CancellationTokenSource.CreateLinkedTokenSource(workerStopping, jobCancellation.Token);
    }

    internal void Complete(IWillieSolveJob job)
    {
        string jobKey = JobKey(job);
        string requestLane = RequestLane(job);
        CancellationTokenSource? cancellation = null;
        lock (_lock)
        {
            _jobsByKey.Remove(jobKey);
            if (_currentJobByRequest.TryGetValue(requestLane, out string? current) &&
                string.Equals(current, jobKey, StringComparison.OrdinalIgnoreCase))
            {
                _currentJobByRequest.Remove(requestLane);
            }

            if (_cancellations.Remove(jobKey, out CancellationTokenSource? stored))
                cancellation = stored;
        }

        cancellation?.Dispose();
        Interlocked.Increment(ref _completed);
    }

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        lock (_lock)
        {
            foreach (CancellationTokenSource cancellation in _cancellations.Values)
            {
                cancellation.Cancel();
                cancellation.Dispose();
            }
            _cancellations.Clear();
            _jobsByKey.Clear();
            _currentJobByRequest.Clear();
        }
    }

    private static string JobKey(IWillieSolveJob job) =>
        $"{job.Kind}|{job.RequestKey}|{job.InputFingerprint}";

    private static string RequestLane(IWillieSolveJob job) =>
        $"{job.Kind}|{job.RequestKey}";

    private sealed record JobQueueState(
        IWillieSolveJob Job,
        WillieSolveQueueItemState State);
}
