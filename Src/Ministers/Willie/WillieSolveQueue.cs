namespace RimBob.Ministers.Willie;

public enum WillieSolveQueueItemState
{
    Queued,
    Running,
    CompletedInline,
    Duplicate,
    Rejected
}

public sealed record WillieSolveEnqueueResult(
    WillieSolveQueueItemState State,
    PlacementSolveAttempt? InlineAttempt = null);

public sealed record WillieSolveQueueStatus(
    int Queued,
    int Running,
    long EnqueuedThisSession,
    long CompletedThisSession,
    long RejectedThisSession);

public interface IWillieSolveQueue
{
    WillieSolveEnqueueResult TryEnqueue(IWillieSolveJob job);
    WillieSolveQueueStatus Status { get; }
}

public sealed class InlineWillieSolveQueue(WillieSolveExecutor executor, WillieSolverStore solverStore) : IWillieSolveQueue
{
    private long _enqueued;
    private long _completed;

    public WillieSolveQueueStatus Status => new(
        Queued: 0,
        Running: 0,
        EnqueuedThisSession: Interlocked.Read(ref _enqueued),
        CompletedThisSession: Interlocked.Read(ref _completed),
        RejectedThisSession: 0);

    public WillieSolveEnqueueResult TryEnqueue(IWillieSolveJob job)
    {
        Interlocked.Increment(ref _enqueued);
        solverStore.RecordQueued(job);
        solverStore.RecordRunning(job);
        PlacementSolveAttempt attempt = executor
            .SolveAndRecordAsync(job, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Interlocked.Increment(ref _completed);
        return new WillieSolveEnqueueResult(WillieSolveQueueItemState.CompletedInline, attempt);
    }
}

public sealed class WillieSolveOptions
{
    public int Degree { get; init; } = 2;
    public int RimApiGate { get; init; } = 1;
    public int QueueCap { get; init; } = 64;
    public string WorkerPriority { get; init; } = nameof(ThreadPriority.BelowNormal);

    public int EffectiveDegree => Math.Clamp(Degree, 1, 8);
    public int EffectiveRimApiGate => Math.Clamp(RimApiGate, 1, 4);
    public int EffectiveQueueCap => Math.Clamp(QueueCap, 1, 512);

    public ThreadPriority EffectiveWorkerPriority =>
        Enum.TryParse(WorkerPriority, ignoreCase: true, out ThreadPriority priority)
            ? priority
            : ThreadPriority.BelowNormal;
}
