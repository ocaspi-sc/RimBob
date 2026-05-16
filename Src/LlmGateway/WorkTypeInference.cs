using RimBob.Core.Advice;

namespace RimBob.LLM;

internal static class WorkTypeInference
{
    public static WorkType? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string normalized = LlmResponseParser.NormalizeIdentifier(raw);
        foreach (WorkType workType in Enum.GetValues<WorkType>())
        {
            if (LlmResponseParser.NormalizeIdentifier(workType.ToString()) == normalized)
                return workType;
        }

        return FromAlias(normalized);
    }

    public static WorkType? Infer(params string?[] values)
    {
        string joined = string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
        string normalized = LlmResponseParser.NormalizeIdentifier(joined);
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        return FromAlias(normalized);
    }

    public static string? DefaultSkill(WorkType? workType) => workType switch
    {
        WorkType.Doctor => "Medicine",
        WorkType.Warden => "Social",
        WorkType.Handle => "Animals",
        WorkType.Cook => "Cooking",
        WorkType.Hunt => "Shooting",
        WorkType.Construct => "Construction",
        WorkType.Grow => "Plants",
        WorkType.Mine => "Mining",
        WorkType.PlantCut => "Plants",
        WorkType.Smith => "Crafting",
        WorkType.Tailor => "Crafting",
        WorkType.Art => "Artistic",
        WorkType.Craft => "Crafting",
        WorkType.Research => "Intellectual",
        _ => null
    };

    private static WorkType? FromAlias(string normalized)
    {
        if (normalized.Contains("firefight")) return WorkType.Firefight;
        if (normalized.Contains("patient")) return WorkType.Patient;
        if (normalized.Contains("doctor") || normalized.Contains("tend") || normalized.Contains("medicine")) return WorkType.Doctor;
        if (normalized.Contains("bedrest")) return WorkType.BedRest;
        if (normalized.Contains("warden")) return WorkType.Warden;
        if (normalized.Contains("handle") || normalized.Contains("animal")) return WorkType.Handle;
        if (normalized.Contains("cook") || normalized.Contains("meal") || normalized.Contains("stove")) return WorkType.Cook;
        if (normalized.Contains("hunt")) return WorkType.Hunt;
        if (normalized.Contains("construct") || normalized.Contains("build") || normalized.Contains("repair")) return WorkType.Construct;
        if (normalized.Contains("grow") || normalized.Contains("sow") || normalized.Contains("farm")) return WorkType.Grow;
        if (normalized.Contains("mine")) return WorkType.Mine;
        if (normalized.Contains("plantcut") || normalized.Contains("harvest") || normalized.Contains("forage") || normalized.Contains("berry")) return WorkType.PlantCut;
        if (normalized.Contains("smith")) return WorkType.Smith;
        if (normalized.Contains("tailor")) return WorkType.Tailor;
        if (normalized.Contains("art") || normalized.Contains("sculpt")) return WorkType.Art;
        if (normalized.Contains("craft")) return WorkType.Craft;
        if (normalized.Contains("haul")) return WorkType.Haul;
        if (normalized.Contains("clean")) return WorkType.Clean;
        if (normalized.Contains("research")) return WorkType.Research;
        if (normalized.Contains("basic")) return WorkType.Basic;
        return null;
    }
}
