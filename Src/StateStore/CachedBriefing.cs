using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace RimBob.State;

public sealed class CachedBriefing<TBriefing> where TBriefing : notnull
{
    private readonly string _name;
    private readonly Func<long[]> _currentVersions;
    private readonly string[] _aggregateNames;
    private readonly Func<long, TBriefing> _compute;
    private readonly ILogger _log;
    private readonly JsonSerializerOptions _jsonOptions;

    private TBriefing? _current;
    private long[] _inputs = [];
    private long _version;

    public CachedBriefing(
        string name,
        Func<long[]> currentVersions,
        string[] aggregateNames,
        Func<long, TBriefing> compute,
        ILogger log,
        JsonSerializerOptions jsonOptions)
    {
        _name = name;
        _currentVersions = currentVersions;
        _aggregateNames = aggregateNames;
        _compute = compute;
        _log = log;
        _jsonOptions = jsonOptions;
    }

    public TBriefing Get()
    {
        long[] current = _currentVersions();
        if (_current is not null && current.AsSpan().SequenceEqual(_inputs))
            return _current;

        long version = ++_version;
        IEnumerable<string> changed = ChangedAggregates(_inputs, current, _aggregateNames);
        _current = _compute(version);
        _inputs = current;

        _log.LogDebug(
            "{BriefingName} recompute version={Version} updatedAggregates=[{Updated}] briefing={BriefingJson}",
            _name,
            version,
            string.Join(',', changed),
            JsonSerializer.Serialize(_current, _jsonOptions));

        return _current;
    }

    private static IEnumerable<string> ChangedAggregates(long[] previous, long[] current, string[] names)
    {
        if (previous.Length == 0) return ["initial"];

        List<string> changed = new();
        for (int i = 0; i < current.Length && i < names.Length; i++)
        {
            if (i >= previous.Length || current[i] != previous[i])
                changed.Add(names[i]);
        }

        return changed;
    }
}
