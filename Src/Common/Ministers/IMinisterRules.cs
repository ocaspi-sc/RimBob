namespace RimBob.Core.Ministers;

public interface IMinisterRules<TBriefing>
{
    RuleRun Evaluate(TBriefing briefing, ColonyContext context);
}
