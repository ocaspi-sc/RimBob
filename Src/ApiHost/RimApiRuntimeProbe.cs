using RimBob.Ingestion;
using RimBob.Ingestion.Dtos;

namespace RimBob.Host;

public sealed class RimApiRuntimeProbe(RimApiClient rimApi)
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    public async Task<RimApiRuntimeSnapshot> ProbeAsync(CancellationToken ct = default)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ProbeTimeout);

        try
        {
            GameStateDto gameState = await rimApi.GetGameStateAsync(timeout.Token);
            return RimApiRuntimeSnapshot.FromGameState(gameState);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return RimApiRuntimeSnapshot.Offline("Timed out querying RIMAPI game state.");
        }
        catch (HttpRequestException ex)
        {
            return RimApiRuntimeSnapshot.Offline($"RIMAPI unreachable: {ex.Message}");
        }
        catch (Exception ex)
        {
            return RimApiRuntimeSnapshot.Offline($"RIMAPI game state failed: {ex.Message}");
        }
    }
}

public sealed record RimApiRuntimeSnapshot(
    bool Reachable,
    string? LastError,
    long? GameTick,
    float? ColonyWealth,
    int? ColonistCount,
    string? Storyteller,
    bool? Paused,
    string? ProgramState,
    int? MapCount)
{
    public bool HasLoadedColony =>
        Reachable &&
        string.Equals(ProgramState, "Playing", StringComparison.OrdinalIgnoreCase) &&
        MapCount.GetValueOrDefault() > 0;

    public static RimApiRuntimeSnapshot FromGameState(GameStateDto gameState) =>
        new(
            Reachable: true,
            LastError: null,
            GameTick: gameState.Tick,
            ColonyWealth: gameState.Wealth,
            ColonistCount: gameState.ColonistCount,
            Storyteller: gameState.Storyteller,
            Paused: gameState.Paused,
            ProgramState: gameState.ProgramState,
            MapCount: gameState.MapCount);

    public static RimApiRuntimeSnapshot Offline(string error) =>
        new(
            Reachable: false,
            LastError: error,
            GameTick: null,
            ColonyWealth: null,
            ColonistCount: null,
            Storyteller: null,
            Paused: null,
            ProgramState: null,
            MapCount: null);
}
