using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using RimBob.Host;

namespace RimBob.Host.Endpoints;

public static class DevBlogEndpoints
{
    private static readonly DevBlogHistoryCache HistoryCache = new(new DevBlogHistoryAnalyzer(), TimeSpan.FromSeconds(30));

    public static IEndpointRouteBuilder MapDevBlogEndpoints(this IEndpointRouteBuilder app)
    {
        EndpointCoverageCatalog coverage = app.ServiceProvider.GetRequiredService<EndpointCoverageCatalog>();
        coverage.Register("/api/dev-blog/history", "available", "Read-only Git master history analytics for the Dev Blog dashboard scope.");

        app.MapGet("/api/dev-blog/history", async Task<IResult> (
            IWebHostEnvironment env,
            CancellationToken ct) =>
        {
            string repositoryRoot = HostLogPaths.ResolveRuntimeRoot(env.ContentRootPath);
            DevBlogHistoryReadResult result = await HistoryCache.ReadMasterHistoryAsync(repositoryRoot, ct);

            return result.Report is null
                ? Results.Problem(
                    title: "Dev Blog history unavailable",
                    detail: result.Error ?? "Git history could not be read.",
                    statusCode: StatusCodes.Status503ServiceUnavailable)
                : Results.Ok(result.Report);
        });

        return app;
    }
}

public sealed class DevBlogHistoryCache(
    IDevBlogHistoryReader reader,
    TimeSpan ttl,
    Func<DateTimeOffset>? utcNow = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<DateTimeOffset> _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    private DevBlogHistoryCacheEntry? _entry;

    public async Task<DevBlogHistoryReadResult> ReadMasterHistoryAsync(string repositoryRoot, CancellationToken ct)
    {
        DateTimeOffset now = _utcNow();
        DevBlogHistoryCacheEntry? entry = _entry;
        string? observedHead = null;
        if (entry is not null && SameRepository(entry, repositoryRoot))
        {
            DevBlogHeadReadResult head = await reader.ReadMasterHeadAsync(repositoryRoot, ct);
            if (head.HeadHash is null)
            {
                return new DevBlogHistoryReadResult(null, head.Error ?? "Git master head could not be read.");
            }

            observedHead = head.HeadHash;
            if (IsFresh(entry, repositoryRoot, now)
                && string.Equals(entry.MasterHead, observedHead, StringComparison.Ordinal))
            {
                return new DevBlogHistoryReadResult(entry.Report, null, entry.MasterHead);
            }
        }

        await _gate.WaitAsync(ct);
        try
        {
            now = _utcNow();
            entry = _entry;
            if (entry is not null && SameRepository(entry, repositoryRoot))
            {
                if (observedHead is null)
                {
                    DevBlogHeadReadResult head = await reader.ReadMasterHeadAsync(repositoryRoot, ct);
                    if (head.HeadHash is null)
                    {
                        return new DevBlogHistoryReadResult(null, head.Error ?? "Git master head could not be read.");
                    }

                    observedHead = head.HeadHash;
                }

                if (string.Equals(entry.MasterHead, observedHead, StringComparison.Ordinal))
                {
                    DevBlogHistoryCacheEntry extended = entry with { ExpiresAt = now.Add(ttl) };
                    _entry = extended;
                    return new DevBlogHistoryReadResult(extended.Report, null, extended.MasterHead);
                }
            }

            DevBlogHistoryReadResult refreshed = await reader.ReadMasterHistoryAsync(repositoryRoot, ct);
            if (refreshed.Report is null)
            {
                return refreshed;
            }

            string masterHead = refreshed.MasterHead ?? "";
            _entry = new DevBlogHistoryCacheEntry(
                RepositoryRoot: repositoryRoot,
                MasterHead: masterHead,
                Report: refreshed.Report,
                ExpiresAt: now.Add(ttl));
            return refreshed;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsFresh(DevBlogHistoryCacheEntry entry, string repositoryRoot, DateTimeOffset now) =>
        SameRepository(entry, repositoryRoot) && entry.ExpiresAt > now;

    private static bool SameRepository(DevBlogHistoryCacheEntry entry, string repositoryRoot) =>
        string.Equals(entry.RepositoryRoot, repositoryRoot, StringComparison.OrdinalIgnoreCase);

    private sealed record DevBlogHistoryCacheEntry(
        string RepositoryRoot,
        string MasterHead,
        DevBlogHistoryReport Report,
        DateTimeOffset ExpiresAt);
}

public interface IDevBlogHistoryReader
{
    Task<DevBlogHistoryReadResult> ReadMasterHistoryAsync(string repositoryRoot, CancellationToken ct);

    Task<DevBlogHeadReadResult> ReadMasterHeadAsync(string repositoryRoot, CancellationToken ct);
}

public sealed class DevBlogHistoryAnalyzer : IDevBlogHistoryReader
{
    private const char RecordSeparator = '\u001e';
    private const char FieldSeparator = '\u001f';

    public async Task<DevBlogHistoryReadResult> ReadMasterHistoryAsync(string repositoryRoot, CancellationToken ct)
    {
        GitCommandResult git = await ReadGitHistoryAsync(repositoryRoot, ct);
        if (!git.Success)
        {
            return new DevBlogHistoryReadResult(null, git.Error);
        }

        IReadOnlyList<GitCommitRecord> commits = ParseGitLog(git.Stdout);
        return new DevBlogHistoryReadResult(
            Analyze(repositoryRoot, commits),
            null,
            commits.FirstOrDefault()?.Hash);
    }

    public async Task<DevBlogHeadReadResult> ReadMasterHeadAsync(string repositoryRoot, CancellationToken ct)
    {
        GitCommandResult git = await ReadGitHeadAsync(repositoryRoot, ct);
        if (!git.Success)
        {
            return new DevBlogHeadReadResult(null, git.Error);
        }

        string head = git.Stdout.Trim();
        return string.IsNullOrWhiteSpace(head)
            ? new DevBlogHeadReadResult(null, "git rev-parse master returned no commit hash.")
            : new DevBlogHeadReadResult(head, null);
    }

    public static IReadOnlyList<GitCommitRecord> ParseGitLog(string text)
    {
        List<GitCommitRecord> commits = [];
        GitCommitRecord? current = null;

        using StringReader reader = new(text);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0 && line[0] == RecordSeparator)
            {
                if (current is not null)
                {
                    commits.Add(current);
                }

                string[] fields = line[1..].Split(FieldSeparator, 5);
                if (fields.Length < 5 || !DateTimeOffset.TryParse(fields[1], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset at))
                {
                    current = null;
                    continue;
                }

                current = new GitCommitRecord(
                    Hash: fields[0],
                    At: at,
                    Author: fields[2],
                    Subject: fields[3],
                    Refs: fields[4],
                    Files: []);
                continue;
            }

            if (current is null || string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] columns = line.Split('\t', 3);
            if (columns.Length < 3)
            {
                continue;
            }

            current.Files.Add(new GitFileChange(
                Path: NormalizePath(columns[2]),
                Additions: ParseNumstatCount(columns[0]),
                Deletions: ParseNumstatCount(columns[1])));
        }

        if (current is not null)
        {
            commits.Add(current);
        }

        return commits;
    }

    public static DevBlogHistoryReport Analyze(string repositoryRoot, IReadOnlyList<GitCommitRecord> commits)
    {
        GitCommitRecord[] ordered = commits
            .OrderBy(commit => commit.At)
            .ThenBy(commit => commit.Hash, StringComparer.Ordinal)
            .ToArray();

        Dictionary<string, AreaAggregate> areas = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, SliceAggregate> authors = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, SliceAggregate> tags = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<DateTime, DayAggregate> days = new();
        List<int> churnValues = [];

        foreach (GitCommitRecord commit in ordered)
        {
            int additions = commit.Files.Sum(file => file.Additions);
            int deletions = commit.Files.Sum(file => file.Deletions);
            int churn = additions + deletions;
            churnValues.Add(churn);

            DateTime day = commit.At.UtcDateTime.Date;
            DayAggregate dayAggregate = GetDay(days, day);
            dayAggregate.CommitCount++;
            dayAggregate.Additions += additions;
            dayAggregate.Deletions += deletions;

            SliceAggregate author = GetSlice(authors, commit.Author);
            author.Count++;
            author.Churn += churn;

            HashSet<string> commitAreas = new(StringComparer.OrdinalIgnoreCase);
            foreach (GitFileChange file in commit.Files)
            {
                string area = AreaForPath(file.Path);
                commitAreas.Add(area);

                AreaAggregate areaAggregate = GetArea(areas, area);
                areaAggregate.FileCount++;
                areaAggregate.Additions += file.Additions;
                areaAggregate.Deletions += file.Deletions;
            }

            foreach (string area in commitAreas)
            {
                GetArea(areas, area).CommitCount++;
            }

            string[] commitTags = TagsForCommit(commit).OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (string tag in commitTags)
            {
                SliceAggregate tagAggregate = GetSlice(tags, tag);
                tagAggregate.Count++;
                tagAggregate.Churn += churn;

                if (!dayAggregate.TagCounts.TryAdd(tag, 1))
                {
                    dayAggregate.TagCounts[tag]++;
                }
            }
        }

        int totalAdditions = ordered.Sum(commit => commit.Files.Sum(file => file.Additions));
        int totalDeletions = ordered.Sum(commit => commit.Files.Sum(file => file.Deletions));
        int totalFilesChanged = ordered.Sum(commit => commit.Files.Count);
        int totalChurn = totalAdditions + totalDeletions;

        IReadOnlyList<DevBlogAreaSummary> areaSummaries = areas
            .OrderByDescending(pair => pair.Value.Churn)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new DevBlogAreaSummary(
                Area: pair.Key,
                CommitCount: pair.Value.CommitCount,
                FileCount: pair.Value.FileCount,
                Additions: pair.Value.Additions,
                Deletions: pair.Value.Deletions,
                Churn: pair.Value.Churn))
            .ToArray();

        IReadOnlyList<DevBlogSlice> authorSlices = BuildSlices(authors);
        IReadOnlyList<DevBlogSlice> tagSlices = BuildSlices(tags);
        IReadOnlyList<DevBlogCommitSummary> largestCommits = ordered
            .OrderByDescending(commit => commit.Files.Sum(file => file.Additions + file.Deletions))
            .ThenByDescending(commit => commit.Files.Count)
            .Take(10)
            .Select(CommitSummary)
            .ToArray();

        IReadOnlyList<DevBlogTimelinePoint> tagTimeline = BuildTagTimeline(days, tagSlices);
        IReadOnlyList<DevBlogLocPoint> locGrowth = BuildLocGrowth(days);
        IReadOnlyList<DevBlogHistogramBin> histogram = BuildHistogram(churnValues);

        return new DevBlogHistoryReport(
            GeneratedAt: DateTimeOffset.UtcNow,
            RepositoryRoot: repositoryRoot,
            ScannedRef: "master",
            Source: "git log master --numstat --date=iso-strict",
            CommitCount: ordered.Length,
            FirstCommitAt: ordered.FirstOrDefault()?.At,
            LastCommitAt: ordered.LastOrDefault()?.At,
            TotalFilesChanged: totalFilesChanged,
            TotalAdditions: totalAdditions,
            TotalDeletions: totalDeletions,
            NetLoc: totalAdditions - totalDeletions,
            AverageChurn: ordered.Length == 0 ? 0m : Math.Round((decimal)totalChurn / ordered.Length, 1),
            MedianChurn: Median(churnValues),
            LargestCommits: largestCommits,
            TagTimeline: tagTimeline,
            LocGrowth: locGrowth,
            CommitSizeHistogram: histogram,
            AreaSummaries: areaSummaries,
            AuthorSlices: authorSlices,
            TagSlices: tagSlices,
            Suggestions: BuildSuggestions(ordered.Length, totalChurn, areaSummaries, tagSlices, histogram));
    }

    private static async Task<GitCommandResult> ReadGitHistoryAsync(string repositoryRoot, CancellationToken ct)
    {
        string[] arguments =
        [
            "log",
            "master",
            "--numstat",
            "--date=iso-strict",
            $"--pretty=format:%x1e%H%x1f%aI%x1f%an%x1f%s%x1f%D"
        ];
        return await RunGitAsync(repositoryRoot, arguments, TimeSpan.FromSeconds(10), "git log master", ct);
    }

    private static async Task<GitCommandResult> ReadGitHeadAsync(string repositoryRoot, CancellationToken ct)
    {
        string[] arguments = ["rev-parse", "master"];
        return await RunGitAsync(repositoryRoot, arguments, TimeSpan.FromSeconds(3), "git rev-parse master", ct);
    }

    private static async Task<GitCommandResult> RunGitAsync(
        string repositoryRoot,
        IReadOnlyList<string> arguments,
        TimeSpan timeoutDuration,
        string commandDescription,
        CancellationToken ct)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "git",
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            return new GitCommandResult(false, "", $"Could not start git: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return new GitCommandResult(false, "", $"Could not start git: {ex.Message}");
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(timeoutDuration);

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
            string stdout = await stdoutTask;
            string stderr = await stderrTask;

            return process.ExitCode == 0
                ? new GitCommandResult(true, stdout, "")
                : new GitCommandResult(false, stdout, $"{commandDescription} failed with exit code {process.ExitCode}: {stderr.Trim()}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            return new GitCommandResult(false, "", $"{commandDescription} timed out after {timeoutDuration.TotalSeconds:0} seconds.");
        }
    }

    private static IReadOnlyList<DevBlogTimelinePoint> BuildTagTimeline(Dictionary<DateTime, DayAggregate> days, IReadOnlyList<DevBlogSlice> tagSlices)
    {
        if (days.Count == 0)
        {
            return [];
        }

        string[] topTags = tagSlices.Take(12).Select(slice => slice.Label).ToArray();
        DateTime first = days.Keys.Min();
        DateTime last = days.Keys.Max();
        List<DevBlogTimelinePoint> points = [];

        for (DateTime day = first; day <= last; day = day.AddDays(1))
        {
            Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
            foreach (string tag in topTags)
            {
                counts[tag] = days.TryGetValue(day, out DayAggregate? aggregate)
                    && aggregate.TagCounts.TryGetValue(tag, out int count)
                        ? count
                        : 0;
            }

            points.Add(new DevBlogTimelinePoint(
                Date: day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Counts: counts));
        }

        return points;
    }

    private static IReadOnlyList<DevBlogLocPoint> BuildLocGrowth(Dictionary<DateTime, DayAggregate> days)
    {
        if (days.Count == 0)
        {
            return [];
        }

        DateTime first = days.Keys.Min();
        DateTime last = days.Keys.Max();
        List<DevBlogLocPoint> points = [];
        int total = 0;

        for (DateTime day = first; day <= last; day = day.AddDays(1))
        {
            DayAggregate aggregate = days.TryGetValue(day, out DayAggregate? value)
                ? value
                : new DayAggregate();
            int net = aggregate.Additions - aggregate.Deletions;
            total += net;
            points.Add(new DevBlogLocPoint(
                Date: day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Total: total,
                Net: net,
                Additions: aggregate.Additions,
                Deletions: aggregate.Deletions,
                CommitCount: aggregate.CommitCount));
        }

        return points;
    }

    private static IReadOnlyList<DevBlogHistogramBin> BuildHistogram(IReadOnlyList<int> values)
    {
        HistogramSpec[] specs =
        [
            new("0-49", 0, 49),
            new("50-199", 50, 199),
            new("200-499", 200, 499),
            new("500-999", 500, 999),
            new("1000-1999", 1000, 1999),
            new("2000+", 2000, int.MaxValue),
        ];

        return specs
            .Select(spec => new DevBlogHistogramBin(
                Label: spec.Label,
                Min: spec.Min,
                Max: spec.Max == int.MaxValue ? null : spec.Max,
                Count: values.Count(value => value >= spec.Min && value <= spec.Max)))
            .ToArray();
    }

    private static IReadOnlyList<DevBlogSlice> BuildSlices(Dictionary<string, SliceAggregate> aggregates) =>
        aggregates
            .OrderByDescending(pair => pair.Value.Count)
            .ThenByDescending(pair => pair.Value.Churn)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new DevBlogSlice(
                Label: pair.Key,
                Count: pair.Value.Count,
                Churn: pair.Value.Churn))
            .ToArray();

    private static IReadOnlyList<string> BuildSuggestions(
        int commitCount,
        int totalChurn,
        IReadOnlyList<DevBlogAreaSummary> areas,
        IReadOnlyList<DevBlogSlice> tags,
        IReadOnlyList<DevBlogHistogramBin> histogram)
    {
        List<string> suggestions = [];
        DevBlogAreaSummary? docs = areas.FirstOrDefault(area => area.Area == "Docs");
        DevBlogAreaSummary? dashboard = areas.FirstOrDefault(area => area.Area == "Dashboard");
        DevBlogAreaSummary? host = areas.FirstOrDefault(area => area.Area == "Host/API");
        DevBlogAreaSummary? tests = areas.FirstOrDefault(area => area.Area == "Tests");
        int largeCommitCount = histogram.Where(bin => bin.Min >= 1000).Sum(bin => bin.Count);

        if (docs is not null && docs.CommitCount > commitCount / 2)
        {
            suggestions.Add("Turn the Dev Blog into a real release-note extractor: each Docs-heavy surge should produce a short public narrative and a separate operator note.");
        }

        if (largeCommitCount > 0)
        {
            suggestions.Add($"Mark {largeCommitCount} large-spike commits in the timeline and require a follow-up split/retro note when a future commit crosses 1000 changed lines.");
        }

        if (dashboard is not null && host is not null)
        {
            suggestions.Add("Dashboard and Host changes are co-evolving; add a closeout card that pairs every UI scope change with the exact endpoint and smoke URL used to verify it.");
        }

        if (tests is not null && totalChurn > 0 && tests.Churn < totalChurn / 5)
        {
            suggestions.Add("Test churn is trailing implementation churn; use the next minister slice to add one replay or endpoint fixture for every new runtime behavior.");
        }

        DevBlogSlice? topTag = tags.FirstOrDefault();
        if (topTag is not null)
        {
            suggestions.Add($"Use '{topTag.Label}' as the first editorial lane: summarize why it keeps recurring, what stabilized, and what still needs owner attention.");
        }

        suggestions.Add("Add a weekly 'build archaeology' view next: before/after screenshots, biggest reversals, and commits that changed the player-visible advice contract.");
        return suggestions;
    }

    private static DevBlogCommitSummary CommitSummary(GitCommitRecord commit)
    {
        int additions = commit.Files.Sum(file => file.Additions);
        int deletions = commit.Files.Sum(file => file.Deletions);
        string shortHash = commit.Hash.Length <= 8 ? commit.Hash : commit.Hash[..8];

        return new DevBlogCommitSummary(
            Hash: commit.Hash,
            ShortHash: shortHash,
            At: commit.At,
            Author: commit.Author,
            Subject: commit.Subject,
            Additions: additions,
            Deletions: deletions,
            FilesChanged: commit.Files.Count,
            Tags: TagsForCommit(commit).OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static HashSet<string> TagsForCommit(GitCommitRecord commit)
    {
        HashSet<string> tags = new(StringComparer.OrdinalIgnoreCase);
        foreach (GitFileChange file in commit.Files)
        {
            tags.Add(AreaForPath(file.Path));
            AddPathSpecificTags(file.Path, tags);
        }

        string subject = commit.Subject.ToLowerInvariant();
        AddIf(subject, tags, "food", "Food");
        AddIf(subject, tags, "mayor", "Mayor/Agenda");
        AddIf(subject, tags, "agenda", "Mayor/Agenda");
        AddIf(subject, tags, "dashboard", "Dashboard");
        AddIf(subject, tags, "ui", "Dashboard");
        AddIf(subject, tags, "scope", "Dashboard");
        AddIf(subject, tags, "docs", "Docs");
        AddIf(subject, tags, "design", "Docs");
        AddIf(subject, tags, "rimapi", "State/RIMAPI");
        AddIf(subject, tags, "state", "State/RIMAPI");
        AddIf(subject, tags, "briefing", "State/RIMAPI");
        AddIf(subject, tags, "llm", "LLM/RAG");
        AddIf(subject, tags, "rag", "LLM/RAG");
        AddIf(subject, tags, "gemini", "LLM/RAG");
        AddIf(subject, tags, "prompt", "LLM/RAG");
        AddIf(subject, tags, "test", "Tests");
        AddIf(subject, tags, "fixture", "Tests");
        AddIf(subject, tags, "apply", "Assisted Apply");
        AddIf(subject, tags, "unforbid", "Assisted Apply");
        AddIf(subject, tags, "harvest", "Assisted Apply");
        AddIf(subject, tags, "bill", "Assisted Apply");
        AddIf(subject, tags, "icon", "Icons");
        AddIf(subject, tags, "replay", "Replay");
        AddIf(subject, tags, "launcher", "Ops");
        AddIf(subject, tags, "host", "Host/API");
        AddIf(subject, tags, "log", "Ops");
        AddIf(subject, tags, "rename", "Repo Rename");
        AddIf(subject, tags, "refactor", "Refactor");
        AddIf(subject, tags, "cleanup", "Refactor");

        if (tags.Count == 0)
        {
            tags.Add("General");
        }

        return tags;
    }

    private static void AddPathSpecificTags(string path, HashSet<string> tags)
    {
        string lower = path.ToLowerInvariant();
        AddIf(lower, tags, "/food/", "Food");
        AddIf(lower, tags, "/mayor/", "Mayor/Agenda");
        AddIf(lower, tags, "agenda", "Mayor/Agenda");
        AddIf(lower, tags, "llm", "LLM/RAG");
        AddIf(lower, tags, "rag", "LLM/RAG");
        AddIf(lower, tags, "replay", "Replay");
        AddIf(lower, tags, "icon", "Icons");
        AddIf(lower, tags, "apply", "Assisted Apply");
        AddIf(lower, tags, "rimapi", "State/RIMAPI");
    }

    private static void AddIf(string text, HashSet<string> tags, string needle, string tag)
    {
        if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            tags.Add(tag);
        }
    }

    private static string AreaForPath(string path)
    {
        if (path.StartsWith("Dashboard/", StringComparison.OrdinalIgnoreCase)) return "Dashboard";
        if (path.StartsWith("Src/ApiHost/", StringComparison.OrdinalIgnoreCase)) return "Host/API";
        if (path.StartsWith("Src/Ministers/", StringComparison.OrdinalIgnoreCase)) return "Ministers";
        if (path.StartsWith("Src/Tests/", StringComparison.OrdinalIgnoreCase)) return "Tests";
        if (path.StartsWith("Src/GameStateSync/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("Src/StateStore/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("Src/State/", StringComparison.OrdinalIgnoreCase)) return "State/RIMAPI";
        if (path.StartsWith("Src/LlmGateway/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("Src/KnowledgeBase/", StringComparison.OrdinalIgnoreCase)) return "LLM/RAG";
        if (path.StartsWith("Src/Coordination/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("Src/Common/", StringComparison.OrdinalIgnoreCase)) return "Core";
        if (path.StartsWith("Docs/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(".agents/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("AGENTS.md", StringComparison.OrdinalIgnoreCase)
            || path.Equals("HumanTodo.md", StringComparison.OrdinalIgnoreCase)
            || path.Equals("README.md", StringComparison.OrdinalIgnoreCase)) return "Docs";

        return "Tooling/Infra";
    }

    private static AreaAggregate GetArea(Dictionary<string, AreaAggregate> areas, string area)
    {
        if (!areas.TryGetValue(area, out AreaAggregate? aggregate))
        {
            aggregate = new AreaAggregate();
            areas[area] = aggregate;
        }

        return aggregate;
    }

    private static SliceAggregate GetSlice(Dictionary<string, SliceAggregate> slices, string label)
    {
        if (!slices.TryGetValue(label, out SliceAggregate? aggregate))
        {
            aggregate = new SliceAggregate();
            slices[label] = aggregate;
        }

        return aggregate;
    }

    private static DayAggregate GetDay(Dictionary<DateTime, DayAggregate> days, DateTime day)
    {
        if (!days.TryGetValue(day, out DayAggregate? aggregate))
        {
            aggregate = new DayAggregate();
            days[day] = aggregate;
        }

        return aggregate;
    }

    private static int Median(List<int> values)
    {
        if (values.Count == 0) return 0;

        int[] sorted = values.Order().ToArray();
        return sorted[sorted.Length / 2];
    }

    private static int ParseNumstatCount(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
            ? count
            : 0;

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/');

    private sealed record GitCommandResult(bool Success, string Stdout, string Error);

    private sealed record HistogramSpec(string Label, int Min, int Max);

    private sealed class AreaAggregate
    {
        public int CommitCount { get; set; }
        public int FileCount { get; set; }
        public int Additions { get; set; }
        public int Deletions { get; set; }
        public int Churn => Additions + Deletions;
    }

    private sealed class SliceAggregate
    {
        public int Count { get; set; }
        public int Churn { get; set; }
    }

    private sealed class DayAggregate
    {
        public int CommitCount { get; set; }
        public int Additions { get; set; }
        public int Deletions { get; set; }
        public Dictionary<string, int> TagCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record DevBlogHistoryReadResult(DevBlogHistoryReport? Report, string? Error, string? MasterHead = null);

public sealed record DevBlogHeadReadResult(string? HeadHash, string? Error);

public sealed record GitCommitRecord(
    string Hash,
    DateTimeOffset At,
    string Author,
    string Subject,
    string Refs,
    List<GitFileChange> Files);

public sealed record GitFileChange(string Path, int Additions, int Deletions);

public sealed record DevBlogHistoryReport(
    DateTimeOffset GeneratedAt,
    string RepositoryRoot,
    string ScannedRef,
    string Source,
    int CommitCount,
    DateTimeOffset? FirstCommitAt,
    DateTimeOffset? LastCommitAt,
    int TotalFilesChanged,
    int TotalAdditions,
    int TotalDeletions,
    int NetLoc,
    decimal AverageChurn,
    int MedianChurn,
    IReadOnlyList<DevBlogCommitSummary> LargestCommits,
    IReadOnlyList<DevBlogTimelinePoint> TagTimeline,
    IReadOnlyList<DevBlogLocPoint> LocGrowth,
    IReadOnlyList<DevBlogHistogramBin> CommitSizeHistogram,
    IReadOnlyList<DevBlogAreaSummary> AreaSummaries,
    IReadOnlyList<DevBlogSlice> AuthorSlices,
    IReadOnlyList<DevBlogSlice> TagSlices,
    IReadOnlyList<string> Suggestions);

public sealed record DevBlogCommitSummary(
    string Hash,
    string ShortHash,
    DateTimeOffset At,
    string Author,
    string Subject,
    int Additions,
    int Deletions,
    int FilesChanged,
    IReadOnlyList<string> Tags);

public sealed record DevBlogTimelinePoint(
    string Date,
    IReadOnlyDictionary<string, int> Counts);

public sealed record DevBlogLocPoint(
    string Date,
    int Total,
    int Net,
    int Additions,
    int Deletions,
    int CommitCount);

public sealed record DevBlogHistogramBin(
    string Label,
    int Min,
    int? Max,
    int Count);

public sealed record DevBlogAreaSummary(
    string Area,
    int CommitCount,
    int FileCount,
    int Additions,
    int Deletions,
    int Churn);

public sealed record DevBlogSlice(
    string Label,
    int Count,
    int Churn);
