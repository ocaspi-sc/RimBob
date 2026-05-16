using System.Text.RegularExpressions;
using RimBob.Core.Briefings;

namespace RimBob.State.Parsing;

/// <summary>
/// Best-effort parser for RIMAPI's datetime string ("5th of Aprimay, 5500, 14h").
/// Returns a DateStamp with structured fields populated when parseable, raw-only
/// otherwise. The Mayor's prompt always gets at least the raw form.
/// </summary>
public static class RimDateParser
{
    // "5th of Aprimay, 5500, 14h"
    // "1st of Aprimay, 5500, 0h"
    private static readonly Regex Pattern = new(
        @"^(?<day>\d{1,2})(?:st|nd|rd|th)?\s+of\s+(?<quadrum>\w+),\s*(?<year>\d+),\s*(?<hour>\d{1,2})h?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static DateStamp Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new DateStamp(raw ?? "", null, null, null, null);

        var m = Pattern.Match(raw.Trim());
        if (!m.Success)
            return new DateStamp(raw, null, null, null, null);

        return new DateStamp(
            Raw:     raw,
            Year:    int.Parse(m.Groups["year"].Value),
            Quadrum: m.Groups["quadrum"].Value,
            Day:     int.Parse(m.Groups["day"].Value),
            Hour:    int.Parse(m.Groups["hour"].Value)
        );
    }
}
