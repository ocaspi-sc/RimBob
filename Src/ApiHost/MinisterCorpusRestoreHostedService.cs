using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Core.Ministers;

namespace RimBob.Host;

public sealed class MinisterCorpusRestoreHostedService(
    ReplayCorpusOutputReader reader,
    FlagChannel flags,
    AdviceBus advice,
    MinisterRegistry registry,
    CorpusRestoreStatusStore status,
    TimeProvider timeProvider,
    ILogger<MinisterCorpusRestoreHostedService> log) : IHostedService
{
    private static readonly TimeSpan RestoredBuildingRequestTtl = TimeSpan.FromHours(24);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        // WHY: Program registers this before DayTickOrchestrator, so guard/publish needs no extra lock.
        foreach (MinisterDescriptor minister in registry.CabinetMinisters)
            await RestoreBuildingRequestFlagsAsync(minister, now, cancellationToken).ConfigureAwait(false);

        foreach (MinisterDescriptor minister in registry.CabinetMinisters)
            await RestoreAdviceOptionsAsync(minister, cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RestoreBuildingRequestFlagsAsync(
        MinisterDescriptor minister,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (HasActiveBuildingRequestFrom(minister))
        {
            log.LogInformation(
                "Replay corpus flag restore skipped for {Minister}; live building request flags are already active.",
                minister.Label);
            return;
        }

        MinisterReplayRecord? record = await reader.LatestAsync(minister.Label, HasBuildingRequestFlag, ct)
            .ConfigureAwait(false);
        if (record is null)
        {
            log.LogInformation(
                "Replay corpus flag restore skipped for {Minister}; no request-bearing record is available.",
                minister.Label);
            return;
        }

        IReadOnlyList<AgentFlag> restoredFlags = record.Flags
            .Where(HasBuildingRequests)
            .Select(flag => flag with { ExpiresAt = now.Add(RestoredBuildingRequestTtl) })
            .ToArray();
        if (restoredFlags.Count == 0) return;

        foreach (AgentFlag flag in restoredFlags)
            flags.Publish(flag);

        status.MarkFlagsRestored(record.Minister, record.CapturedAt, restoredFlags.Count);
        log.LogWarning(
            "Restored {FlagCount} building request flag(s) for {Minister} from replay corpus captured_at={CapturedAt}; TTL re-stamped until {ExpiresAt}.",
            restoredFlags.Count,
            record.Minister,
            record.CapturedAt,
            now.Add(RestoredBuildingRequestTtl));
    }

    private async Task RestoreAdviceOptionsAsync(MinisterDescriptor minister, CancellationToken ct)
    {
        if (HasActiveOptionsAdviceFor(minister))
        {
            log.LogInformation(
                "Replay corpus advice restore skipped for {Minister}; options-bearing advice is already active.",
                minister.Label);
            return;
        }

        MinisterReplayRecord? record = await reader.LatestAsync(minister.Label, HasAdviceOptions, ct)
            .ConfigureAwait(false);
        if (record is null)
        {
            log.LogInformation(
                "Replay corpus advice restore skipped for {Minister}; no options-bearing record is available.",
                minister.Label);
            return;
        }

        try
        {
            advice.ReplaceMinisterAdvice(
                record.Minister,
                record.Advice,
                record.StateSummary,
                record.Chain,
                record.Flags);
        }
        catch (ArgumentException ex)
        {
            log.LogWarning(
                ex,
                "Replay corpus advice restore skipped for {Minister}; recorded advice is inconsistent.",
                record.Minister);
            return;
        }

        status.MarkAdviceRestored(record.Minister, record.CapturedAt, OptionsAdviceCount(record.Advice));
        log.LogWarning(
            "Restored advice/options for {Minister} from replay corpus captured_at={CapturedAt}; marked last-known until the next live cycle.",
            record.Minister,
            record.CapturedAt);
    }

    private bool HasActiveBuildingRequestFrom(MinisterDescriptor minister) =>
        flags.Active().Any(flag =>
            HasBuildingRequests(flag) &&
            IsMinisterMatch(minister, flag.SourceMinister));

    private bool HasActiveOptionsAdviceFor(MinisterDescriptor minister) =>
        advice.ActiveAdvice().Any(item =>
            HasOptions(item) &&
            IsMinisterMatch(minister, item.Minister));

    private static bool HasBuildingRequestFlag(MinisterReplayRecord record) =>
        record.Flags.Any(HasBuildingRequests);

    private static bool HasAdviceOptions(MinisterReplayRecord record) =>
        record.Advice.Any(HasOptions);

    private static bool HasBuildingRequests(AgentFlag flag) =>
        flag.BuildingRequests is { Count: > 0 };

    private static bool HasOptions(AdviceItem advice) =>
        advice.Options is { Count: > 0 };

    private static int OptionsAdviceCount(IEnumerable<AdviceItem> advice) =>
        advice.Count(HasOptions);

    private static bool IsMinisterMatch(MinisterDescriptor descriptor, string minister)
    {
        string normalized = MinisterRegistry.NormalizeKey(minister);
        return normalized.Equals(descriptor.Key, StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals(MinisterRegistry.NormalizeKey(descriptor.Label), StringComparison.OrdinalIgnoreCase);
    }
}
