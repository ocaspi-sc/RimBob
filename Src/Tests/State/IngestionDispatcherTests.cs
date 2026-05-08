using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using RimAI.Ingestion;
using RimAI.Ingestion.Dtos;
using RimAI.State;
using RimAI.Tests.Infrastructure;

namespace RimAI.Tests.State;

public sealed class IngestionDispatcherTests
{
    [Fact]
    public async Task RefreshAllAsync_HappyPath_BumpsEveryAggregateVersion()
    {
        var router = StandardRouter();
        using var http = MakeClient(router);
        var rim = new RimApiClient(http);
        var s = new ColonyState();
        var dispatcher = new IngestionDispatcher(rim, s, new TestLogger<IngestionDispatcher>());

        await dispatcher.RefreshAllAsync();

        s.Map.Version.Should().Be(1);
        s.Economy.Version.Should().Be(1);
        s.Colonists.Version.Should().Be(1);
        s.Stockpiles.Version.Should().Be(1);
        s.Buildings.Version.Should().Be(1);
        s.Power.Version.Should().Be(1);
        s.Threats.Version.Should().Be(1);
        s.Weather.Version.Should().Be(1);
        s.Farm.Version.Should().Be(1);
        s.Resources.Version.Should().Be(1);
        s.Research.Version.Should().Be(1);
    }

    [Fact]
    public async Task RefreshAllAsync_PopulatesAggregatesFromDtos()
    {
        using var http = MakeClient(StandardRouter());
        var s = new ColonyState();
        var dispatcher = new IngestionDispatcher(
            new RimApiClient(http), s, new TestLogger<IngestionDispatcher>());

        await dispatcher.RefreshAllAsync();

        s.Economy.Value.ColonyWealth.Should().Be(7500f);
        s.Economy.Value.DateTimeRaw.Should().Be("5th of Aprimay, 5500, 14h");
        s.Colonists.Value.Colonists.Should().HaveCount(1);
        s.Colonists.Value.Colonists[0].Name.Should().Be("Alice");
        s.Colonists.Value.Colonists[0].IsDowned.Should().BeFalse();
        s.Colonists.Value.Colonists[0].IsDead.Should().BeFalse();
        s.Power.Value.ProductionW.Should().Be(2000f);
        s.Threats.Value.Lords.Should().ContainSingle()
            .Which.JobType.Should().Be("Raid");
    }

    [Fact]
    public async Task RefreshAllAsync_PawnMedicalInfo_FlowsThroughToColonistRecord()
    {
        // Use the standard router but swap in a colonists payload with a downed pawn
        // and a pawn with null medical_info, to cover both branches of the mapper.
        var pawnDowned = new ColonistDetailedDto(
            Pawn: new ColonistBasicDto(2, "Bob", "Male", 35, 0.9f, 0.6f, 1.0f, null),
            Detailes: new ColonistDetailsDto(
                WorkInfo: null,
                MedicalInfo: new PawnMedicalInfoDto(IsDead: false, IsDowned: true, Hediffs: [])));
        var pawnNoMedical = new ColonistDetailedDto(
            Pawn: new ColonistBasicDto(3, "Carol", "Female", 22, 1.0f, 0.7f, 1.0f, null),
            Detailes: new ColonistDetailsDto(WorkInfo: null, MedicalInfo: null));
        var router = StandardRouterWithoutColonists()
            .Add("api/v2/colonists/detailed",
                 Envelope(new List<ColonistDetailedDto> { pawnDowned, pawnNoMedical }));

        using var http = MakeClient(router);
        var s = new ColonyState();
        var dispatcher = new IngestionDispatcher(
            new RimApiClient(http), s, new TestLogger<IngestionDispatcher>());

        await dispatcher.RefreshAllAsync();

        var bob   = s.Colonists.Value.Colonists.Single(c => c.Name == "Bob");
        var carol = s.Colonists.Value.Colonists.Single(c => c.Name == "Carol");

        bob.IsDowned.Should().BeTrue();
        bob.IsDead.Should().BeFalse();
        carol.IsDowned.Should().BeFalse();   // null medical_info defaults to false
        carol.IsDead.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshAllAsync_NoMaps_Throws()
    {
        var router = new PathRouter()
            .Add("api/v1/maps", Envelope(new List<MapInfoDto>()));
        using var http = MakeClient(router);
        var dispatcher = new IngestionDispatcher(
            new RimApiClient(http), new ColonyState(), new TestLogger<IngestionDispatcher>());

        var act = () => dispatcher.RefreshAllAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no maps*");
    }

    // ── fixtures ──────────────────────────────────────────────────────────────

    private static PathRouter StandardRouter()
    {
        var pawn = new ColonistDetailedDto(
            Pawn: new ColonistBasicDto(
                Id: 1, Name: "Alice", Gender: "Female", Age: 28,
                Health: 1.0f, Mood: 0.7f, Hunger: 1.0f, Position: null),
            Detailes: new ColonistDetailsDto(
                WorkInfo: new PawnWorkInfoDto(
                    Skills: [new SkillDto("Plants", 12, 2)],
                    CurrentJob: "Sowing",
                    Traits: [new TraitDto("Industrious", "industrious")]),
                MedicalInfo: new PawnMedicalInfoDto(
                    IsDead: false, IsDowned: false, Hediffs: [])));

        return StandardRouterWithoutColonists()
            .Add("api/v2/colonists/detailed", Envelope(new List<ColonistDetailedDto> { pawn }));
    }

    private static PathRouter StandardRouterWithoutColonists()
    {
        var map = new MapInfoDto(0, 0, true, false, "10", 1, "(250,1,250)");
        var state = new GameStateDto(
            Tick: 12345, Wealth: 7500f, ColonistCount: 1,
            Storyteller: "Cassandra", Paused: false, ProgramState: "Playing", MapCount: 1);
        var date = new DateTimeDto("5th of Aprimay, 5500, 14h");
        var farm = new FarmSummaryDto(50, 0.6f, 5, [new CropBreakdownDto("Rice", 30, 0.7f)]);
        var zones = new List<ZoneDto>
        {
            new("z1", "StockpileZone", "main", [], null)
        };
        var buildings = new List<BuildingDto>
        {
            new("b1", "Bed", null, 1.0f, true, true)
        };
        var power = new PowerInfoDto(2000f, 1500f, 100f, 500f);
        var weather = new WeatherDto("Clear", 18f, 0f);
        var lords = new List<LordDto>
        {
            new("l1", "Raid", "Pirates", ["p1","p2"], 350f)
        };
        var incidents = new List<IncidentDto>
        {
            new("RaidEnemy", 0.5f, "Raider attack")
        };

        var resources = new ResourcesSummaryDto(
            TotalItems: 200, TotalMarketValue: 4500f,
            CriticalResources: new CriticalResourcesDto(
                FoodSummary: new FoodSummaryDto(
                    FoodTotal: 80, TotalNutrition: 45.0f, MealsCount: 20, RawFoodCount: 40),
                MedicineTotal: 15, WeaponCount: 4, WeaponValue: 800f));
        var research = new ResearchProgressDto(
            Name: "Microelectronics", Label: "Microelectronics",
            Progress: 1500f, ResearchPoints: 3000f,
            IsFinished: false, CanStartNow: true,
            ProgressPercent: 50f);

        return new PathRouter()
            .Add("api/v1/maps",                    Envelope(new List<MapInfoDto> { map }))
            .Add("api/v1/game/state",              Envelope(state))
            .Add("api/v1/datetime",                Envelope(date))
            .Add("api/v1/map/farm/summary",        Envelope(farm))
            .Add("api/v1/map/zones",               Envelope(zones))
            .Add("api/v1/map/buildings",           Envelope(buildings))
            .Add("api/v1/map/power/info",          Envelope(power))
            .Add("api/v1/map/weather",             Envelope(weather))
            .Add("api/v1/lords",                   Envelope(lords))
            .Add("api/v1/incidents",               Envelope(incidents))
            .Add("api/v1/resources/summary",       Envelope(resources))
            .Add("api/v1/research/progress",       Envelope(research));
    }

    private static HttpClient MakeClient(PathRouter router) =>
        new(router) { BaseAddress = new Uri("http://localhost:8765/") };

    private static HttpContent Envelope<T>(T data) =>
        JsonContent.Create(new RimApiEnvelope<T>(
            Success: true, Data: data, Errors: null, Warnings: null, Timestamp: null));

    private sealed class PathRouter : HttpMessageHandler
    {
        private readonly List<(string segment, HttpContent content)> _routes = [];

        public PathRouter Add(string pathSegment, HttpContent content)
        {
            _routes.Add((pathSegment, content));
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri?.PathAndQuery ?? "";
            // Longest match wins so "api/v1/map/farm/summary" beats "api/v1/map".
            foreach (var (segment, content) in _routes.OrderByDescending(r => r.segment.Length))
            {
                if (path.Contains(segment, StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
