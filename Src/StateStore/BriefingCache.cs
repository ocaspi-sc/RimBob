using System.Text.Json;
using Microsoft.Extensions.Logging;
using RimAI.Core.Briefings;
using RimAI.State.Derivations;

namespace RimAI.State;

/// <summary>
/// Versioned view cache. Recomputes briefings only when their input aggregate
/// versions change. Lazy, pull-based, no reactive framework.
/// </summary>
public sealed class BriefingCache(ColonyState state, ILogger<BriefingCache> log)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private MayorBriefing? _mayor;
    private long[] _mayorInputs = [];
    private long _mayorVersion;

    private FoodBriefing? _food;
    private long[] _foodInputs = [];
    private long _foodVersion;

    public MayorBriefing GetMayorBriefing()
    {
        long[] current = state.GetVersionsForMayorBriefing();
        if (_mayor is not null && current.AsSpan().SequenceEqual(_mayorInputs))
            return _mayor;

        long version = ++_mayorVersion;
        IEnumerable<string> changed = ChangedAggregates(_mayorInputs, current, ColonyState.MayorBriefingAggregateNames);
        _mayor = MayorBriefingDerivation.Compute(state, version);
        _mayorInputs = current;

        log.LogDebug(
            "MayorBriefing recompute version={Version} updatedAggregates=[{Updated}] briefing={BriefingJson}",
            version,
            string.Join(',', changed),
            JsonSerializer.Serialize(_mayor, JsonOpts));

        return _mayor;
    }

    public FoodBriefing GetFoodBriefing()
    {
        long[] current = state.GetVersionsForFoodBriefing();
        if (_food is not null && current.AsSpan().SequenceEqual(_foodInputs))
            return _food;

        long version = ++_foodVersion;
        IEnumerable<string> changed = ChangedAggregates(_foodInputs, current, ColonyState.FoodBriefingAggregateNames);
        _food = FoodBriefingDerivation.Compute(state, version);
        _foodInputs = current;

        log.LogDebug(
            "FoodBriefing recompute version={Version} updatedAggregates=[{Updated}] briefing={BriefingJson}",
            version,
            string.Join(',', changed),
            JsonSerializer.Serialize(_food, JsonOpts));

        return _food;
    }

    private static IEnumerable<string> ChangedAggregates(long[] previous, long[] current, string[] names)
    {
        if (previous.Length == 0) return ["initial"];

        List<string> changed = new();
        for (int i = 0; i < current.Length && i < names.Length; i++)
            if (i >= previous.Length || current[i] != previous[i])
                changed.Add(names[i]);
        return changed;
    }
}
