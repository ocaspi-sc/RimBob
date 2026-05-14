using RimAI.Core.Briefings;

namespace RimAI.State.Derivations.Common;

public static class SeasonDeriver
{
    private static readonly string[] Quadrums = ["Aprimay", "Jugust", "Septober", "Decembary"];
    private const int DaysPerQuadrum = 15;

    public static SeasonContext Derive(DateStamp date)
    {
        if (date.Quadrum is null || date.Day is null)
            return new SeasonContext(null, null, null);

        int index = Array.FindIndex(Quadrums,
            quadrum => quadrum.Equals(date.Quadrum, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return new SeasonContext(date.Quadrum, null, null);

        int daysToNext = DaysPerQuadrum - date.Day.Value + 1;
        int winterIndex = Array.IndexOf(Quadrums, "Decembary");
        int quadrumsAway = (winterIndex - index + Quadrums.Length) % Quadrums.Length;
        int daysToWinter = quadrumsAway == 0
            ? 0
            : (quadrumsAway - 1) * DaysPerQuadrum + daysToNext;

        return new SeasonContext(Quadrums[index], daysToNext, daysToWinter);
    }
}
