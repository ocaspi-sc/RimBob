using RimBob.Core.Aggregates;
using RimBob.Ingestion.Dtos;

namespace RimBob.State;

public static class PawnAggregateMapper
{
    public static ColonistRegistry FromColonists(IReadOnlyList<ColonistDetailedDto> pawns) =>
        new(pawns
            .Where(pawn => pawn.Pawn is not null)
            .Select(MapColonist)
            .ToList());

    private static ColonistRecord MapColonist(ColonistDetailedDto pawn)
    {
        ColonistBasicDto basic = pawn.Pawn!;
        PawnWorkInfoDto? work = pawn.Detailes?.WorkInfo;
        PawnMedicalInfoDto? medical = pawn.Detailes?.MedicalInfo;
        IReadOnlyList<SkillDto> skills = work?.Skills ?? [];
        IReadOnlyList<TraitDto> traits = work?.Traits ?? [];

        return new ColonistRecord(
            Id: basic.Id.ToString(),
            Name: basic.Name ?? $"pawn_{basic.Id}",
            Age: basic.Age,
            Gender: basic.Gender ?? "",
            Health: basic.Health,
            Mood: basic.Mood,
            Hunger: basic.Hunger,
            IsDowned: medical?.IsDowned ?? false,
            IsDead: medical?.IsDead ?? false,
            Position: MapAggregateMapper.MapPosition(basic.Position),
            CurrentJob: work?.CurrentJob,
            Skills: skills.Select(skill => new ColonistSkill(skill.Name, skill.Level, PassionName(skill.Passion))).ToList(),
            Traits: traits.Select(trait => trait.Name).ToList());
    }

    private static string PassionName(int passion) => passion switch
    {
        1 => "Minor",
        2 => "Major",
        _ => "None"
    };
}
