using RimAI.Core.Advice;
using RimAI.Core.Briefings;
using RimAI.Core.Ministers;

namespace RimAI.Ministers.Food;

public sealed class Rules : IMinisterRules<FoodBriefing>
{
    private const string MinisterName = "Food";
    private const string Domain = "food";

    public RulesResult Evaluate(FoodBriefing briefing, ColonyContext context)
    {
        if (briefing.EstimatedDaysOfFood is null)
        {
            if (briefing.FoodUnits > 0)
                return DecisionFor(briefing, "nutrition_signal_gap",
                    FoodAdviceType.ManageFoodStockpile,
                    AdviceSeverity.Medium,
                    "Food stockpile needs audit",
                "RIMAPI reports food units but no nutrition. Audit meals/raw food before treating this as a shortage.",
                "Missing nutrition would make days-of-food unreliable; use the fallback counts until upstream data is fixed.",
                [new(ResourceRequestKind.Attention, "manual food stockpile audit", "nutrition_source is unknown or fallback-derived")],
                    [new(SuggestedActionKind.Note, "Check whether stored food is edible meals/raw food and whether it is reachable.")],
                    false);

            return DecisionFor(briefing, "unknown_food_state",
                FoodAdviceType.FoodSecurity,
                AdviceSeverity.High,
                "Food state unknown",
                "No reliable food stockpile signal is available. Treat this as a food-security check, not confirmed starvation.",
                "The food chain cannot safely decide without stockpile visibility.",
                [new(ResourceRequestKind.Attention, "immediate stockpile check", "food_units and nutrition are both unavailable")],
                [new(SuggestedActionKind.Note, "Inspect food stockpiles in-game and refresh RimAI after the stockpile is visible.")],
                true);
        }

        float days = briefing.EstimatedDaysOfFood.Value;

        if (days < 7f)
            return DecisionFor(briefing, "emergency_food_flag",
                FoodAdviceType.FoodSecurity,
                AdviceSeverity.High,
                "Food crisis within a week",
                $"Food covers about {days:F1} days for {briefing.ColonistCount} colonists. Stabilize meals before other routine work.",
                "Food below 7 days is an urgent survival risk but not auto-executed in MVP.",
                [
                    new(ResourceRequestKind.Labor, "PlantCut work today", "food buffer is below 7 days", Priority: AdviceSeverity.High, RequestedFrom: "Labor", WorkType: WorkType.PlantCut, Skill: "Plants"),
                    new(ResourceRequestKind.Labor, "Cook work today", "food buffer is below 7 days", Priority: AdviceSeverity.High, RequestedFrom: "Labor", WorkType: WorkType.Cook, Skill: "Cooking"),
                    new(ResourceRequestKind.TradeCapacity, "buy edible food if a trader is reachable", "rules cannot guarantee a timely harvest")
                ],
                [
                    new(SuggestedActionKind.Note, "Prioritize edible food acquisition: harvest mature crops, forage safe wild plants, or buy food."),
                    new(SuggestedActionKind.SetPriority, "Raise plant cutting/cooking manually if those jobs are lagging.")
                ],
                true);

        if (briefing.ReadyToHarvest > 0)
            return DecisionFor(briefing, "harvest_mature_crops",
                FoodAdviceType.HarvestNow,
                days < 15f ? AdviceSeverity.High : AdviceSeverity.Medium,
                "Mature crops are ready",
                $"{briefing.ReadyToHarvest} crop tiles are ready to harvest. Pull them in before weather, rot, or labor drift wastes the buffer.",
                "Mature crops are a deterministic food-chain opportunity.",
                [new(ResourceRequestKind.Labor, "PlantCut work for ready crops", "mature crops only help once harvested", RequestedFrom: "Labor", WorkType: WorkType.PlantCut, Skill: "Plants")],
                [new(SuggestedActionKind.Note, "Manually prioritize harvest designations for the ready food crops.")],
                days < 15f);

        if (briefing.MealsCount < briefing.ColonistCount * 2 && days > 7f && briefing.RawFoodCount > 0)
            return DecisionFor(briefing, "meals_understocked",
                FoodAdviceType.ManageCookBills,
                AdviceSeverity.Medium,
                "Cooked meals are understocked",
                $"Only {briefing.MealsCount} meals are reported for {briefing.ColonistCount} colonists while raw food exists.",
                "A raw-food buffer still needs cooking throughput to become safe daily nutrition.",
                [
                    new(ResourceRequestKind.Bill, "cook simple meals to a small buffer", "meal count is below two per colonist"),
                    new(ResourceRequestKind.Labor, "Cook work time", "raw food has to become meals", RequestedFrom: "Labor", WorkType: WorkType.Cook, Skill: "Cooking")
                ],
                [new(SuggestedActionKind.Note, "Check stove bills and keep simple meals stocked before upgrading meal quality.")],
                false);

        if (days < 20f && briefing.WildAnimalCount > 0 && briefing.ReadyToHarvest == 0)
            return new Escalate(
                "Food below 20 days with possible hunting path; target risk/value needs judgment.",
                new { briefing.WildAnimalCount, briefing.ActiveThreat, briefing.Skills.BestCooking });

        if (days < 20f && briefing.WildHarvestCandidates > 0 && briefing.ReadyToHarvest == 0)
            return DecisionFor(briefing, "wild_harvest_available",
                FoodAdviceType.WildHarvest,
                AdviceSeverity.Medium,
                "Wild food can extend the buffer",
                $"{briefing.WildHarvestCandidates} harvestable wild plants are visible while food is below 20 days.",
                "Wild harvest is lower-risk than hunting when no mature crops are ready.",
                [new(ResourceRequestKind.Labor, "PlantCut work for wild harvest", "wild harvest requires plant work", RequestedFrom: "Labor", WorkType: WorkType.PlantCut, Skill: "Plants")],
                [new(SuggestedActionKind.Note, "Designate safe nearby edible wild plants for harvest.")],
                false);

        if (briefing.Season.DaysToWinter is < 20 && days < 30f)
            return new Escalate(
                "Winter is close and food buffer is below target; crop/freezer/labor tradeoff needs guide-grounded judgment.",
                new { briefing.Season.DaysToWinter, DaysOfFood = days, briefing.CropBreakdown });

        if (briefing.Infrastructure.Coolers == 0 && days >= 20f && briefing.FoodUnits > 0)
            return DecisionFor(briefing, "freezer_missing",
                FoodAdviceType.ManageFreezer,
                AdviceSeverity.Medium,
                "Food storage needs freezer support",
                "Food exists but no cooler is visible. Preserve surplus before warm weather or large harvests.",
                "The Food minister owns freezer need; Construction owns the actual build work.",
                [new(ResourceRequestKind.Building, "cooler-backed freezer or cold room", "stored food can spoil without temperature control", RequestedFrom: "Construction")],
                [new(SuggestedActionKind.Build, "Plan a freezer/cold-room upgrade near food storage.")],
                false);

        if (days >= 30f)
            return new Decision([], [], "maintain_security_threshold");

        return new Escalate(
            "Food state is below ideal but no deterministic rule cleanly chooses the next action.",
            new { DaysOfFood = days, briefing.ReadyToHarvest, briefing.WildHarvestCandidates, briefing.WildAnimalCount });
    }

    private static Decision DecisionFor(
        FoodBriefing briefing,
        string trace,
        FoodAdviceType type,
        AdviceSeverity severity,
        string title,
        string body,
        string rationale,
        IReadOnlyList<ResourceRequest> requests,
        IReadOnlyList<SuggestedAction> actions,
        bool emitFlag)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string adviceType = ToSnakeCase(type.ToString());
        AdviceItem advice = new(
            Id: $"{MinisterName.ToLowerInvariant()}_{trace}_{briefing.GameTick}",
            Minister: MinisterName,
            AdviceType: adviceType,
            Severity: severity,
            PriorityScore: AdvicePriorityScore.DefaultForSeverity(severity),
            Title: title,
            Body: body,
            Rationale: rationale,
            ResourceRequests: requests,
            SuggestedActions: actions,
            GuideCitationIds: [],
            IssuedAt: now,
            ExpiresAt: now.AddHours(severity >= AdviceSeverity.High ? 4 : 24),
            IssuedInGameTick: FormatTick(briefing),
            BriefingRef: new BriefingRef(MinisterName, briefing.BriefingVersion, $"food:{briefing.BriefingVersion}")
        );

        IReadOnlyList<AgentFlag> flags = emitFlag
            ? [new AgentFlag(
                Id: $"{Domain}:{trace}",
                SourceMinister: MinisterName,
                Severity: ToFlagSeverity(severity),
                Domain: Domain,
                Summary: title,
                Requests: requests,
                Detail: trace,
                ExpiresAt: now.AddHours(24))]
            : [];

        return new Decision([advice], flags, trace);
    }

    private static FlagSeverity ToFlagSeverity(AdviceSeverity severity) => severity switch
    {
        AdviceSeverity.Critical => FlagSeverity.Critical,
        AdviceSeverity.High => FlagSeverity.High,
        AdviceSeverity.Medium => FlagSeverity.Medium,
        _ => FlagSeverity.Low
    };

    private static string FormatTick(FoodBriefing b) =>
        $"Y{b.Date.Year ?? 0}{b.Date.Quadrum ?? "?"}D{b.Date.Day ?? 0}";

    private static string ToSnakeCase(string value)
    {
        List<char> chars = new(value.Length + 4);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsUpper(c) && i > 0) chars.Add('_');
            chars.Add(char.ToLowerInvariant(c));
        }
        return new string(chars.ToArray());
    }
}
