using RimBob.Core.Aggregates;

namespace RimBob.State.Derivations.Common;

public static class PawnDeriver
{
    public static IReadOnlyList<ColonistRecord> LivingColonists(IReadOnlyList<ColonistRecord> pawns) =>
        pawns.Where(pawn => !pawn.IsDead).ToList();

    public static IReadOnlyList<ColonistSkill> SkillsFor(IReadOnlyList<ColonistRecord> pawns, string def) =>
        pawns
            .SelectMany(pawn => pawn.Skills)
            .Where(skill => skill.Def.Equals(def, StringComparison.OrdinalIgnoreCase))
            .ToList();

    public static int BestSkill(IReadOnlyList<ColonistRecord> pawns, string def) =>
        SkillsFor(pawns, def)
            .Select(skill => skill.Level)
            .DefaultIfEmpty(0)
            .Max();

    public static int QualifiedSkillCount(IReadOnlyList<ColonistRecord> pawns, string def, int minimumLevel) =>
        pawns.Count(pawn => pawn.Skills.Any(skill =>
            skill.Def.Equals(def, StringComparison.OrdinalIgnoreCase) && skill.Level >= minimumLevel));
}
