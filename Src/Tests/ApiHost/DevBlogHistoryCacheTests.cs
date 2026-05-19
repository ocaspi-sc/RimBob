using FluentAssertions;
using RimBob.Host.Endpoints;

namespace RimBob.Tests.ApiHost;

public sealed class DevBlogHistoryCacheTests
{
    [Fact]
    public async Task ReadMasterHistoryAsync_WhenMasterHeadMatches_ReusesParsedReport()
    {
        DateTimeOffset now = new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);
        FakeDevBlogHistoryReader reader = new();
        DevBlogHistoryReport report = NewReport("C:\\repo", 1);
        reader.HistoryResults.Enqueue(new DevBlogHistoryReadResult(report, null, "head-1"));
        reader.HeadResults.Enqueue(new DevBlogHeadReadResult("head-1", null));
        DevBlogHistoryCache sut = new(reader, TimeSpan.FromMinutes(1), () => now);

        DevBlogHistoryReadResult first = await sut.ReadMasterHistoryAsync("C:\\repo", CancellationToken.None);
        DevBlogHistoryReadResult second = await sut.ReadMasterHistoryAsync("C:\\repo", CancellationToken.None);

        first.Report.Should().BeSameAs(report);
        second.Report.Should().BeSameAs(report);
        reader.HistoryCalls.Should().Be(1);
        reader.HeadCalls.Should().Be(1);
    }

    [Fact]
    public async Task ReadMasterHistoryAsync_WhenTtlExpiresAndMasterHeadMatches_ExtendsCacheWithoutReparsingHistory()
    {
        DateTimeOffset now = new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);
        FakeDevBlogHistoryReader reader = new();
        DevBlogHistoryReport report = NewReport("C:\\repo", 1);
        reader.HistoryResults.Enqueue(new DevBlogHistoryReadResult(report, null, "head-1"));
        reader.HeadResults.Enqueue(new DevBlogHeadReadResult("head-1", null));
        DevBlogHistoryCache sut = new(reader, TimeSpan.FromSeconds(30), () => now);

        await sut.ReadMasterHistoryAsync("C:\\repo", CancellationToken.None);
        now = now.AddSeconds(31);
        DevBlogHistoryReadResult second = await sut.ReadMasterHistoryAsync("C:\\repo", CancellationToken.None);

        second.Report.Should().BeSameAs(report);
        reader.HistoryCalls.Should().Be(1);
        reader.HeadCalls.Should().Be(1);
    }

    [Fact]
    public async Task ReadMasterHistoryAsync_WhenTtlExpiresAndMasterHeadChanged_RefreshesHistory()
    {
        DateTimeOffset now = new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);
        FakeDevBlogHistoryReader reader = new();
        DevBlogHistoryReport firstReport = NewReport("C:\\repo", 1);
        DevBlogHistoryReport secondReport = NewReport("C:\\repo", 2);
        reader.HistoryResults.Enqueue(new DevBlogHistoryReadResult(firstReport, null, "head-1"));
        reader.HeadResults.Enqueue(new DevBlogHeadReadResult("head-2", null));
        reader.HistoryResults.Enqueue(new DevBlogHistoryReadResult(secondReport, null, "head-2"));
        DevBlogHistoryCache sut = new(reader, TimeSpan.FromSeconds(30), () => now);

        await sut.ReadMasterHistoryAsync("C:\\repo", CancellationToken.None);
        now = now.AddSeconds(31);
        DevBlogHistoryReadResult second = await sut.ReadMasterHistoryAsync("C:\\repo", CancellationToken.None);

        second.Report.Should().BeSameAs(secondReport);
        second.Report?.CommitCount.Should().Be(2);
        reader.HistoryCalls.Should().Be(2);
        reader.HeadCalls.Should().Be(1);
    }

    private static DevBlogHistoryReport NewReport(string repositoryRoot, int commitCount) =>
        new(
            GeneratedAt: new DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero),
            RepositoryRoot: repositoryRoot,
            ScannedRef: "master",
            Source: "git log master --numstat --date=iso-strict",
            CommitCount: commitCount,
            FirstCommitAt: null,
            LastCommitAt: null,
            TotalFilesChanged: 0,
            TotalAdditions: 0,
            TotalDeletions: 0,
            NetLoc: 0,
            AverageChurn: 0m,
            MedianChurn: 0,
            LargestCommits: Array.Empty<DevBlogCommitSummary>(),
            TagTimeline: Array.Empty<DevBlogTimelinePoint>(),
            LocGrowth: Array.Empty<DevBlogLocPoint>(),
            CommitSizeHistogram: Array.Empty<DevBlogHistogramBin>(),
            AreaSummaries: Array.Empty<DevBlogAreaSummary>(),
            AuthorSlices: Array.Empty<DevBlogSlice>(),
            TagSlices: Array.Empty<DevBlogSlice>(),
            Suggestions: Array.Empty<string>());

    private sealed class FakeDevBlogHistoryReader : IDevBlogHistoryReader
    {
        public Queue<DevBlogHistoryReadResult> HistoryResults { get; } = new();

        public Queue<DevBlogHeadReadResult> HeadResults { get; } = new();

        public int HistoryCalls { get; private set; }

        public int HeadCalls { get; private set; }

        public Task<DevBlogHistoryReadResult> ReadMasterHistoryAsync(string repositoryRoot, CancellationToken ct)
        {
            HistoryCalls++;
            return Task.FromResult(HistoryResults.Dequeue());
        }

        public Task<DevBlogHeadReadResult> ReadMasterHeadAsync(string repositoryRoot, CancellationToken ct)
        {
            HeadCalls++;
            return Task.FromResult(HeadResults.Dequeue());
        }
    }
}
