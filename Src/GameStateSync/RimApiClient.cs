using System.Net.Http.Json;
using System.Text.Json;
using RimAI.Ingestion.Dtos;

namespace RimAI.Ingestion;

/// <summary>
/// Typed HTTP wrapper for RIMAPI (https://github.com/IlyaChichkov/RIMAPI).
/// Full endpoint reference: design/rimapi.md
/// Called over HTTP only — no linking. GPL-3.0 license posture resolved.
///
/// Rules:
///   - Add a method only when a minister actually needs it. No speculative coverage.
///   - Ministers read the state store, never this client directly.
///   - Only Labor issues pawn-allocation writes (work priorities, schedules, zones).
///   - All responses are wrapped in RimApiEnvelope<T>; use Unwrap() to extract data.
///
/// Base URL: http://localhost:8765/ (configurable via RIMAPI_BASE_URL env var)
/// v1 paths:  api/v1/...
/// v2 paths:  api/v2/...   (pawn detailed controller)
/// </summary>
public sealed class RimApiClient(HttpClient http)
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<T> GetEnvelopedAsync<T>(string path, CancellationToken ct)
    {
        RimApiEnvelope<T>? envelope = await http.GetFromJsonAsync<RimApiEnvelope<T>>(path, ct)
            ?? throw new InvalidOperationException($"RIMAPI returned null for {path}");

        if (!envelope.Success)
        {
            string errors = string.Join(", ", envelope.Errors ?? []);
            throw new RimApiException($"RIMAPI error at {path}: {errors}");
        }

        return envelope.Data
            ?? throw new InvalidOperationException($"RIMAPI envelope.data was null for {path}");
    }

    /// <summary>
    /// Like GetEnvelopedAsync but for collection endpoints. Returns an empty list when
    /// RIMAPI sends {} or null for data (observed when a map has no zones/buildings/etc).
    /// </summary>
    private async Task<IReadOnlyList<T>> GetEnvelopedListAsync<T>(string path, CancellationToken ct)
    {
        try
        {
            RimApiEnvelope<List<T>>? envelope =
                await http.GetFromJsonAsync<RimApiEnvelope<List<T>>>(path, ct);

            if (envelope?.Success == true)
                return envelope.Data ?? [];

            string errors = string.Join(", ", envelope?.Errors ?? []);
            throw new RimApiException($"RIMAPI error at {path}: {errors}");
        }
        catch (JsonException)
        {
            // RIMAPI returns {} instead of [] for empty collections on some endpoints.
            return [];
        }
    }

    // ── Handshake ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Smoke-test: confirms RIMAPI is reachable and a colony is loaded.
    /// Returns (true, firstColonistName) or (false, errorMessage).
    /// </summary>
    public async Task<(bool Ok, string Message)> HandshakeAsync(CancellationToken ct = default)
    {
        try
        {
            var state = await GetGameStateAsync(ct);
            if (state.ColonistCount == 0)
                return (false, "RIMAPI reachable but no colonists found — is a colony loaded?");

            var maps = await GetMapsAsync(ct);
            var home = maps.FirstOrDefault(m => m.IsPlayerHome) ?? maps.FirstOrDefault();
            if (home is null)
                return (false, "RIMAPI reachable but no maps loaded.");

            var pawns = await GetMapPawnsAsync(home.Id, ct);
            var firstName = pawns.FirstOrDefault()?.Name ?? "unknown";
            return (true, firstName);
        }
        catch (HttpRequestException ex)
        {
            return (false, $"RIMAPI unreachable: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"Unexpected error: {ex.Message}");
        }
    }

    // ── Game ──────────────────────────────────────────────────────────────────

    /// <summary>GET api/v1/game/state — tick, wealth, colonist count, storyteller, paused.</summary>
    public Task<GameStateDto> GetGameStateAsync(CancellationToken ct = default) =>
        GetEnvelopedAsync<GameStateDto>("api/v1/game/state", ct);

    /// <summary>GET api/v1/datetime — single string ("Nth of Quadrum, Year, Hh"). Parse client-side.</summary>
    public Task<DateTimeDto> GetDateTimeAsync(CancellationToken ct = default) =>
        GetEnvelopedAsync<DateTimeDto>("api/v1/datetime", ct);

    /// <summary>GET api/v1/maps — list of loaded maps with id, faction, player-home flag.</summary>
    public Task<IReadOnlyList<MapInfoDto>> GetMapsAsync(CancellationToken ct = default) =>
        GetEnvelopedListAsync<MapInfoDto>("api/v1/maps", ct);

    // ── Pawns ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// GET api/v1/map/pawns?mapId — basic pawn list (name, health, mood, hunger, position).
    /// Use GetColonistsDetailedAsync for skills and traits.
    /// </summary>
    public Task<IReadOnlyList<MapPawnDto>> GetMapPawnsAsync(
        int mapId, CancellationToken ct = default) =>
        GetEnvelopedListAsync<MapPawnDto>($"api/v1/map/pawns?map_id={mapId}", ct);

    /// <summary>
    /// GET api/v2/colonists/detailed?map_id — full bio + needs + skills + health.
    /// Primary source for ColonistRegistry and LaborBriefing skill data.
    /// TODO: confirm response is a list; the v2 controller shape is not fully cached in rimapi.md.
    /// </summary>
    public Task<IReadOnlyList<ColonistDetailedDto>> GetColonistsDetailedAsync(
        int mapId, CancellationToken ct = default) =>
        GetEnvelopedListAsync<ColonistDetailedDto>(
            $"api/v2/colonists/detailed?map_id={mapId}", ct);

    // ── Agriculture ───────────────────────────────────────────────────────────

    /// <summary>GET api/v1/map/farm/summary?map_id — crop totals + avg growth per crop type.</summary>
    public Task<FarmSummaryDto> GetFarmSummaryAsync(int mapId, CancellationToken ct = default) =>
        GetEnvelopedAsync<FarmSummaryDto>($"api/v1/map/farm/summary?map_id={mapId}", ct);

    /// <summary>GET api/v1/map/plants?map_id — all plants with growth %, position, zone.</summary>
    public Task<IReadOnlyList<PlantDto>> GetPlantsAsync(
        int mapId, CancellationToken ct = default) =>
        GetEnvelopedListAsync<PlantDto>($"api/v1/map/plants?map_id={mapId}", ct);

    /// <summary>GET api/v1/map/animals?map_id — wild and tame animals (for hunting assessment).</summary>
    public Task<IReadOnlyList<AnimalDto>> GetAnimalsAsync(
        int mapId, CancellationToken ct = default) =>
        GetEnvelopedListAsync<AnimalDto>($"api/v1/map/animals?map_id={mapId}", ct);

    /// <summary>GET api/v1/map/weather?map_id — current weather def, temperature, rain rate.</summary>
    public Task<WeatherDto> GetWeatherAsync(int mapId, CancellationToken ct = default) =>
        GetEnvelopedAsync<WeatherDto>($"api/v1/map/weather?map_id={mapId}", ct);

    // ── Map / Colony state ────────────────────────────────────────────────────

    /// <summary>GET api/v1/map/zones?map_id — grow zones and stockpile zones with cell lists.</summary>
    public Task<IReadOnlyList<ZoneDto>> GetZonesAsync(
        int mapId, CancellationToken ct = default) =>
        GetEnvelopedListAsync<ZoneDto>($"api/v1/map/zones?map_id={mapId}", ct);

    /// <summary>GET api/v1/map/buildings?map_id — all buildings (hp, power state, working).</summary>
    public Task<IReadOnlyList<BuildingDto>> GetBuildingsAsync(
        int mapId, CancellationToken ct = default) =>
        GetEnvelopedListAsync<BuildingDto>($"api/v1/map/buildings?map_id={mapId}", ct);

    /// <summary>GET api/v1/map/power/info?map_id — colony-wide power production / consumption / storage.</summary>
    public Task<PowerInfoDto> GetPowerInfoAsync(int mapId, CancellationToken ct = default) =>
        GetEnvelopedAsync<PowerInfoDto>($"api/v1/map/power/info?map_id={mapId}", ct);

    /// <summary>GET api/v1/map/creatures/summary?map_id — headcounts (colonists, enemies, animals, prisoners).</summary>
    public Task<CreaturesSummaryDto> GetCreaturesSummaryAsync(
        int mapId, CancellationToken ct = default) =>
        GetEnvelopedAsync<CreaturesSummaryDto>(
            $"api/v1/map/creatures/summary?map_id={mapId}", ct);

    /// <summary>GET api/v1/map/rooms?map_id — rooms with role, temperature, bed count, impressiveness.</summary>
    public Task<IReadOnlyList<RoomDto>> GetRoomsAsync(
        int mapId, CancellationToken ct = default) =>
        GetEnvelopedListAsync<RoomDto>($"api/v1/map/rooms?map_id={mapId}", ct);

    // ── Threats / Events ──────────────────────────────────────────────────────

    /// <summary>
    /// GET api/v1/lords?map_id — active AI lords (raids, sieges, caravans).
    /// Presence of a hostile lord is the primary raid-detection signal for Defense.
    /// </summary>
    public Task<IReadOnlyList<LordDto>> GetLordsAsync(
        int mapId, CancellationToken ct = default) =>
        GetEnvelopedListAsync<LordDto>($"api/v1/lords?map_id={mapId}", ct);

    /// <summary>GET api/v1/incidents?map_id — recent incidents with days_since.</summary>
    public Task<IReadOnlyList<IncidentDto>> GetIncidentsAsync(
        int mapId, CancellationToken ct = default) =>
        GetEnvelopedListAsync<IncidentDto>($"api/v1/incidents?map_id={mapId}", ct);

    // ── Write endpoints (Labor-owned) ─────────────────────────────────────────
    // TODO: Pawn Edit Controller and Pawn Job Controller field shapes are not cached
    //       in rimapi.md. Fetch live docs when M3 (Labor/assignment solver) begins
    //       and implement: SetWorkPriority, SetSchedule, ForceJob, SetZoneRestriction.
    //       Only MinisterOfLabor calls these writes.

    // ── Grow zone write (Agriculture-owned) ──────────────────────────────────
    /// <summary>
    /// POST api/v1/map/zone/growing — create a grow zone over a rect with a crop def.
    /// Owned by Agriculture minister. Only call via the HTN planner primitive.
    /// TODO: confirm request body shape { "map_id", "plant_def", "rect": {x1,y1,x2,y2} }.
    /// </summary>
    public async Task CreateGrowZoneAsync(
        int mapId, string plantDef, int x1, int z1, int x2, int z2,
        CancellationToken ct = default)
    {
        var body = new { map_id = mapId, plant_def = plantDef,
                         rect = new { x1, z1, x2, z2 } };
        var response = await http.PostAsJsonAsync("api/v1/map/zone/growing", body, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// POST api/v1/order/designate/area — designate Hunt / Harvest / Mine / Deconstruct
    /// over a rect. Used by Agriculture (harvest, hunt) and Construction (mine, decon).
    /// TODO: confirm request body shape { "map_id", "designation", "rect" }.
    /// </summary>
    public async Task DesignateAreaAsync(
        int mapId, string designation, int x1, int z1, int x2, int z2,
        CancellationToken ct = default)
    {
        var body = new { map_id = mapId, designation,
                         rect = new { x1, z1, x2, z2 } };
        var response = await http.PostAsJsonAsync("api/v1/order/designate/area", body, ct);
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>Thrown when RIMAPI returns success=false with errors.</summary>
public sealed class RimApiException(string message) : Exception(message);
