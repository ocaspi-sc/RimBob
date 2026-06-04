using RimBob.Core.Advice;

namespace RimBob.Core.Ministers;

public abstract record Decision(RuleId Rule);

public sealed record Advise(
    RuleId Rule,
    Priority Priority,
    string Title,
    string Body,
    string Rationale,
    IReadOnlyList<AdviceAction> Actions,
    IReadOnlyList<string>? GuideCitationIds = null,
    IReadOnlyList<AdviceOption>? Options = null) : Decision(Rule);

public sealed record RequestBuild(
    RuleId Rule,
    BuildingRequest Request,
    string To,
    Priority Priority) : Decision(Rule);

public sealed record RequestLabor(
    RuleId Rule,
    LaborRequest Request,
    string To,
    Priority Priority) : Decision(Rule);

public sealed record RequestItem(
    RuleId Rule,
    ItemRequest Request,
    string To,
    Priority Priority) : Decision(Rule);

public sealed record RequestAttention(
    RuleId Rule,
    AttentionRequest Request,
    string To,
    Priority Priority) : Decision(Rule);

public sealed record Escalate(
    RuleId Rule,
    string Reason,
    object? Context = null) : Decision(Rule);
