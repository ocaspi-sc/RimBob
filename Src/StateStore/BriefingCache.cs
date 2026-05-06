using RimAI.Core.Briefings;
using RimAI.State.Derivations;

namespace RimAI.State;

/// <summary>
/// Versioned view cache. Recomputes briefings only when their input aggregate
/// versions change. Lazy, pull-based, no reactive framework.
/// </summary>
public sealed class BriefingCache(ColonyState state)
{
    private MayorBriefing? _mayor;
    private long[] _mayorInputs = [];

    public MayorBriefing GetMayorBriefing()
    {
        var current = state.GetVersionsForMayorBriefing();
        if (_mayor is not null && current.AsSpan().SequenceEqual(_mayorInputs))
            return _mayor;

        _mayor = MayorBriefingDerivation.Compute(state);
        _mayorInputs = current;
        return _mayor;
    }
}
