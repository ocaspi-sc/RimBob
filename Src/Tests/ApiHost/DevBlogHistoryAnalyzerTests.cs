using FluentAssertions;
using RimBob.Host.Endpoints;

namespace RimBob.Tests.ApiHost;

public sealed class DevBlogHistoryAnalyzerTests
{
    [Fact]
    public void ParseGitLog_CountsWordDiffTokens()
    {
        string text = "\u001eabcdef0123456789\u001f2026-05-19T12:00:00+00:00\u001forca\u001f[dashboard] Tune label\u001fmaster\n"
            + "1\t1\tDashboard/src/types/devBlog.ts\n"
            + "\n"
            + "diff --git a/Dashboard/src/types/devBlog.ts b/Dashboard/src/types/devBlog.ts\n"
            + "--- a/Dashboard/src/types/devBlog.ts\n"
            + "+++ b/Dashboard/src/types/devBlog.ts\n"
            + "@@ -1 +1 @@\n"
            + "-old dashboard label\n"
            + "+new dashboard velocity label\n"
            + "~\n";

        IReadOnlyList<GitCommitRecord> commits = DevBlogHistoryAnalyzer.ParseGitLog(text);

        commits.Should().HaveCount(1);
        commits[0].Files.Should().HaveCount(1);
        commits[0].Files[0].WordDeletions.Should().Be(3);
        commits[0].Files[0].WordAdditions.Should().Be(4);
    }

    [Fact]
    public void Analyze_TreatsDashboardMirrorAsSupportingForFoodWork()
    {
        GitCommitRecord commit = new(
            Hash: "1111111111111111111111111111111111111111",
            At: DateTimeOffset.Parse("2026-05-19T12:00:00+00:00"),
            Author: "orca",
            Subject: "[food] Add rule emission provenance",
            Refs: "master",
            Files:
            [
                new("Src/Ministers/Food/Rules.cs", 2, 1, 10, 3),
                new("Dashboard/src/types/system.ts", 1, 0, 4, 0),
            ]);

        DevBlogHistoryReport report = DevBlogHistoryAnalyzer.Analyze("C:/dev/RimBob", [commit]);

        DevBlogDailyAreaVelocityRow row = report.DailyAreaVelocity.Should().ContainSingle().Subject;
        DevBlogDailyAreaLane food = row.Lanes.Single(lane => lane.Area == "Food/Apply");
        DevBlogDailyAreaLane appRuntime = row.Lanes.Single(lane => lane.Area == "App/Runtime");

        food.CommitCount.Should().Be(1);
        appRuntime.CommitCount.Should().Be(0);
        food.Commits[0].SupportingAreas.Should().Contain("Dashboard");
    }

    [Fact]
    public void Analyze_ListsDashboardWhenCommitOwnsDashboardUi()
    {
        GitCommitRecord commit = new(
            Hash: "2222222222222222222222222222222222222222",
            At: DateTimeOffset.Parse("2026-05-19T12:00:00+00:00"),
            Author: "orca",
            Subject: "[dashboard] Add Dev Blog velocity panel",
            Refs: "master",
            Files:
            [
                new("Dashboard/src/components/devBlog/DevBlogOverview.tsx", 20, 2, 80, 5),
                new("Dashboard/src/styles.css", 30, 0, 90, 0),
            ]);

        DevBlogHistoryReport report = DevBlogHistoryAnalyzer.Analyze("C:/dev/RimBob", [commit]);

        DevBlogDailyAreaVelocityRow row = report.DailyAreaVelocity.Should().ContainSingle().Subject;
        DevBlogDailyAreaLane appRuntime = row.Lanes.Single(lane => lane.Area == "App/Runtime");

        appRuntime.CommitCount.Should().Be(1);
        appRuntime.ScopePoints.Should().BeGreaterThan(0);
        appRuntime.Commits[0].MaterialAreas.Should().Contain("Dashboard");
        report.VelocityLanes.Should().Contain("Dashboard/Icons");
    }

    [Fact]
    public void Analyze_DedupesCommitsInsideConsolidatedGridLane()
    {
        GitCommitRecord commit = new(
            Hash: "3333333333333333333333333333333333333333",
            At: DateTimeOffset.Parse("2026-05-19T12:00:00+00:00"),
            Author: "orca",
            Subject: "Refactor mayor agenda naming and snapshot routing",
            Refs: "master",
            Files:
            [
                new("Src/ApiHost/Endpoints/MinisterOutputEndpoints.cs", 6, 2, 24, 4),
                new("Src/StateStore/ColonyStateSnapshotStore.cs", 5, 1, 18, 3),
            ]);

        DevBlogHistoryReport report = DevBlogHistoryAnalyzer.Analyze("C:/dev/RimBob", [commit]);

        DevBlogDailyAreaVelocityRow row = report.DailyAreaVelocity.Should().ContainSingle().Subject;
        DevBlogDailyAreaLane appRuntime = row.Lanes.Single(lane => lane.Area == "App/Runtime");

        report.DailyVelocity.Should().ContainSingle().Subject.LaneCommitCounts["Host/API"].Should().Be(1);
        report.DailyVelocity.Should().ContainSingle().Subject.LaneCommitCounts["State/Core"].Should().Be(1);
        appRuntime.CommitCount.Should().Be(1);
        appRuntime.Commits.Select(item => item.Hash).Should().OnlyHaveUniqueItems();
        appRuntime.Commits[0].MaterialAreas.Should().Contain("Host/API");
        appRuntime.Commits[0].MaterialAreas.Should().Contain("State/RIMAPI");
    }

    [Fact]
    public void Analyze_DedupesCommitsInsideConsolidatedVelocityLane()
    {
        GitCommitRecord commit = new(
            Hash: "4444444444444444444444444444444444444444",
            At: DateTimeOffset.Parse("2026-05-19T12:00:00+00:00"),
            Author: "orca",
            Subject: "Split shared state and minister naming",
            Refs: "master",
            Files:
            [
                new("Src/StateStore/ColonyStateSnapshotStore.cs", 5, 1, 18, 3),
                new("Src/Coordination/MinisterOutputStore.cs", 4, 1, 12, 2),
            ]);

        DevBlogHistoryReport report = DevBlogHistoryAnalyzer.Analyze("C:/dev/RimBob", [commit]);

        DevBlogDailyVelocityPoint point = report.DailyVelocity.Should().ContainSingle().Subject;
        DevBlogDailyAreaVelocityRow row = report.DailyAreaVelocity.Should().ContainSingle().Subject;
        DevBlogDailyAreaLane appRuntime = row.Lanes.Single(lane => lane.Area == "App/Runtime");

        point.LaneCommitCounts["State/Core"].Should().Be(1);
        appRuntime.CommitCount.Should().Be(1);
        appRuntime.Commits.Should().ContainSingle();
    }
}
