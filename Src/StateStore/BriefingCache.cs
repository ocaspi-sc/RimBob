using System.Text.Json;
using Microsoft.Extensions.Logging;
using RimBob.Core.Briefings;
using RimBob.State.Derivations;

namespace RimBob.State;

/// <summary>
/// Versioned view cache. Recomputes briefings only when their input aggregate
/// versions change. Lazy, pull-based, no reactive framework.
/// </summary>
public sealed class BriefingCache(ColonyState state, ILogger<BriefingCache> log)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private readonly CachedBriefing<MayorBriefing> _mayor = new(
        "MayorBriefing",
        state.GetVersionsForMayorBriefing,
        ColonyState.MayorBriefingAggregateNames,
        version => MayorBriefingDerivation.Compute(state, version),
        log,
        JsonOpts);

    private readonly CachedBriefing<FoodBriefing> _food = new(
        "FoodBriefing",
        state.GetVersionsForFoodBriefing,
        ColonyState.FoodBriefingAggregateNames,
        version => FoodBriefingDerivation.Compute(state, version),
        log,
        JsonOpts);

    public MayorBriefing GetMayorBriefing() => _mayor.Get();

    public FoodBriefing GetFoodBriefing() => _food.Get();
}
