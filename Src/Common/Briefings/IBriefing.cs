namespace RimBob.Core.Briefings;

/// <summary>
/// Marker for minister briefings. Each minister consumes exactly one briefing type;
/// briefings are derived views over ColonyState aggregates and cached against
/// input aggregate versions. See Docs/design/state-store.md.
/// </summary>
public interface IBriefing { }
