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

    public MayorBriefing GetMayorBriefing()
    {
        var current = state.GetVersionsForMayorBriefing();
        if (_mayor is not null && current.AsSpan().SequenceEqual(_mayorInputs))
            return _mayor;

        var version = ++_mayorVersion;
        _mayor = MayorBriefingDerivation.Compute(state, version);
        _mayorInputs = current;

        log.LogDebug(
            "MayorBriefing recompute version={Version} aggregateVersions=[{Versions}] briefing={BriefingJson}",
            version,
            string.Join(',', current),
            JsonSerializer.Serialize(_mayor, JsonOpts));

        return _mayor;
    }
}
