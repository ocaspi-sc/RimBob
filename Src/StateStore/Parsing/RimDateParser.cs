using System.Text.RegularExpressions;
using RimBob.Core.Briefings;

namespace RimBob.State.Parsing;

/// <summary>
/// Best-effort parser for RIMAPI's datetime string ("5th of Aprimay, 5500, 14h").
/// Returns normalized game time with parsed calendar fields when available.
/// The Mayor's prompt always gets the raw form plus tick-derived colony time.
/// </summary>
public static class RimDateParser
{
    // "5th of Aprimay, 5500, 14h"
    // "1st of Aprimay, 5500, 0h"
    private static readonly Regex Pattern = new(
        @"^(?<day>\d{1,2})(?:st|nd|rd|th)?\s+of\s+(?<quadrum>\w+),\s*(?<year>\d+),\s*(?<hour>\d{1,2})h?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static GameDate Parse(string raw, long gameTick)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return GameTime.Create(raw ?? "", gameTick, null, null, null, null);

        Match match = Pattern.Match(raw.Trim());
        if (!match.Success)
            return GameTime.Create(raw, gameTick, null, null, null, null);

        return GameTime.Create(
            rawRimWorldDate: raw,
            gameTick: gameTick,
            rimWorldYear: int.Parse(match.Groups["year"].Value),
            quadrum: match.Groups["quadrum"].Value,
            quadrumDay: int.Parse(match.Groups["day"].Value),
            hour: int.Parse(match.Groups["hour"].Value)
        );
    }
}
