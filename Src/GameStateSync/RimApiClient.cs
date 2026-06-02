using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RimBob.Ingestion.Dtos;

namespace RimBob.Ingestion;

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
public sealed class RimApiClient(HttpClient http, ILogger<RimApiClient>? log = null)
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

    private async Task<TResponse> PostEnvelopedAsync<TRequest, TResponse>(
        string path,
        TRequest body,
        CancellationToken ct)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync(path, body, ct);
        try
        {
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            throw new RimApiHttpException($"RIMAPI HTTP error at {path}: {(int?)response.StatusCode} {response.ReasonPhrase}", response.StatusCode, ex);
        }

        RimApiEnvelope<TResponse>? envelope = await response.Content.ReadFromJsonAsync<RimApiEnvelope<TResponse>>(cancellationToken: ct)
            ?? throw new InvalidOperationException($"RIMAPI returned null for {path}");

        if (!envelope.Success)
        {
            string errors = string.Join(", ", envelope.Errors ?? []);
            throw new RimApiException($"RIMAPI error at {path}: {errors}");
        }

        return envelope.Data
            ?? throw new InvalidOperationException($"RIMAPI envelope.data was null for {path}");
    }

    private static string Query(string value) => Uri.EscapeDataString(value);

    private static async Task EnsureWriteAcceptedAsync(HttpResponseMessage response, string path, CancellationToken ct)
    {
        try
        {
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            throw new RimApiHttpException($"RIMAPI HTTP error at {path}: {(int?)response.StatusCode} {response.ReasonPhrase}", response.StatusCode, ex);
        }

        if (response.Content.Headers.ContentLength == 0)
            return;

        string text = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(text))
            return;

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("success", out JsonElement success) ||
                success.ValueKind != JsonValueKind.False)
                return;

            string errors = ReadStringArray(root, "errors");
            throw new RimApiException($"RIMAPI rejected write at {path}: {errors}");
        }
        catch (JsonException)
        {
            return;
        }
    }

    /// <summary>
    /// Like GetEnvelopedAsync but for collection endpoints. Returns an empty list
    /// only when RIMAPI sends null, [], or {} for data. Non-empty schema drift
    /// fails loudly instead of pretending the collection is empty.
    /// </summary>
    private async Task<IReadOnlyList<T>> GetEnvelopedListAsync<T>(
        string path,
        CancellationToken ct,
        params string[] nestedArrayProperties)
    {
        using HttpResponseMessage response = await http.GetAsync(path, ct);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        JsonElement root = document.RootElement;

        bool success = root.TryGetProperty("success", out JsonElement successElement) &&
                       successElement.ValueKind == JsonValueKind.True;
        if (!success)
        {
            string errors = ReadStringArray(root, "errors");
            throw new RimApiException($"RIMAPI error at {path}: {errors}");
        }

        if (!root.TryGetProperty("data", out JsonElement data) ||
            data.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return [];

        if (data.ValueKind == JsonValueKind.Array)
            return DeserializeListData<T>(path, data);

        if (data.ValueKind == JsonValueKind.Object)
        {
            if (!data.EnumerateObject().Any())
                return [];

            if (nestedArrayProperties.Length > 0)
            {
                List<T> merged = [];
                bool foundNestedArrayProperty = false;
                foreach (string nestedArrayProperty in nestedArrayProperties.Where(property => !string.IsNullOrWhiteSpace(property)))
                {
                    if (!data.TryGetProperty(nestedArrayProperty, out JsonElement nested))
                        continue;

                    foundNestedArrayProperty = true;
                    if (nested.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                        continue;

                    if (nested.ValueKind == JsonValueKind.Array)
                    {
                        merged.AddRange(DeserializeListData<T>($"{path}.data.{nestedArrayProperty}", nested));
                        continue;
                    }

                    string nestedMessage = $"RIMAPI schema drift at {path}: expected data.{nestedArrayProperty} to be an array, null, or missing for List<{typeof(T).Name}>, got {nested.ValueKind}.";
                    log?.LogError(nestedMessage);
                    throw new RimApiException(nestedMessage);
                }

                if (foundNestedArrayProperty)
                    return merged;
            }
        }

        string message = $"RIMAPI schema drift at {path}: expected data to be an array, null, or empty object for List<{typeof(T).Name}>, got {data.ValueKind}.";
        log?.LogError(message);
        throw new RimApiException(message);
    }

    private IReadOnlyList<T> DeserializeListData<T>(string path, JsonElement data)
    {
        try
        {
            return data.Deserialize<List<T>>() ?? [];
        }
        catch (JsonException ex)
        {
            string message =
                $"RIMAPI schema drift at {path}: could not deserialize data as List<{typeof(T).Name}> at {ex.Path ?? "unknown path"}. {ex.Message}";
            log?.LogError(ex, message);
            throw new RimApiException(message, ex);
        }
    }

    private static string ReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
            return string.Empty;

        if (value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? string.Empty;

        if (value.ValueKind != JsonValueKind.Array)
            return value.ToString();

        return string.Join(", ", value.EnumerateArray().Select(item => item.ToString()));
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
    /// </summary>
    public Task<IReadOnlyList<ColonistDetailedDto>> GetColonistsDetailedAsync(
        int mapId, CancellationToken ct = default) =>
        GetEnvelopedListAsync<ColonistDetailedDto>(
            $"api/v2/colonists/detailed?map_id={mapId}", ct);

    // ── Food-chain reads ──────────────────────────────────────────────────────

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

    /// <summary>GET api/v1/map/things?map_id — broad map thing list, including forbidden items.</summary>
    public Task<IReadOnlyList<ThingDto>> GetThingsAsync(
        int mapId, CancellationToken ct = default) =>
        GetEnvelopedListAsync<ThingDto>($"api/v1/map/things?map_id={mapId}", ct);

    /// <summary>GET api/v1/map/weather?map_id — current weather def, temperature, rain rate.</summary>
    public Task<WeatherDto> GetWeatherAsync(int mapId, CancellationToken ct = default) =>
        GetEnvelopedAsync<WeatherDto>($"api/v1/map/weather?map_id={mapId}", ct);

    // ── Map / Colony state ────────────────────────────────────────────────────

    /// <summary>GET api/v1/map/zones?map_id — zones plus area rows such as Home.</summary>
    public Task<IReadOnlyList<ZoneDto>> GetZonesAsync(
        int mapId, CancellationToken ct = default) =>
        GetEnvelopedListAsync<ZoneDto>($"api/v1/map/zones?map_id={mapId}", ct, "zones", "areas");

    /// <summary>GET api/v1/map/terrain?map_id — RLE terrain grid plus palette.</summary>
    public Task<TerrainGridDto> GetTerrainAsync(int mapId, CancellationToken ct = default) =>
        GetEnvelopedAsync<TerrainGridDto>($"api/v1/map/terrain?map_id={mapId}", ct);

    /// <summary>GET api/v1/map/buildings?map_id — all buildings (hp, power state, working).</summary>
    public Task<IReadOnlyList<BuildingDto>> GetBuildingsAsync(
        int mapId, CancellationToken ct = default) =>
        GetEnvelopedListAsync<BuildingDto>($"api/v1/map/buildings?map_id={mapId}", ct);

    /// <summary>GET api/v1/map/work-tables?map_id — work benches omitted from the general buildings endpoint.</summary>
    public Task<IReadOnlyList<WorkTableDto>> GetWorkTablesAsync(
        int mapId, CancellationToken ct = default) =>
        GetEnvelopedListAsync<WorkTableDto>($"api/v1/map/work-tables?map_id={mapId}", ct);

    /// <summary>GET api/v1/map/power/info?map_id — colony-wide power production / consumption / storage.</summary>
    public Task<PowerInfoDto> GetPowerInfoAsync(int mapId, CancellationToken ct = default) =>
        GetEnvelopedAsync<PowerInfoDto>($"api/v1/map/power/info?map_id={mapId}", ct);

    /// <summary>GET api/v1/map/creatures/summary?map_id — headcounts (colonists, enemies, animals, prisoners).</summary>
    public Task<CreaturesSummaryDto> GetCreaturesSummaryAsync(
        int mapId, CancellationToken ct = default) =>
        GetEnvelopedAsync<CreaturesSummaryDto>(
            $"api/v1/map/creatures/summary?map_id={mapId}", ct);

    /// <summary>
    /// GET api/v1/map/rooms?map_id — rooms with role, temperature, quality stats, and optional Willie placement detail fields.
    /// The fork omits detail fields for oversized rooms, bounding worst-case payloads.
    /// </summary>
    /// <param name="includeFootprint">When true, requests bounded cells, entries, contained buildings, and region ids.</param>
    public Task<IReadOnlyList<RoomDto>> GetRoomsAsync(
        int mapId,
        bool includeFootprint = true,
        CancellationToken ct = default)
    {
        string detailQuery = includeFootprint
            ? "&include_cells=true&include_entry_cells=true&include_contained_buildings=true&include_region=true"
            : string.Empty;

        return GetEnvelopedListAsync<RoomDto>(
            $"api/v1/map/rooms?map_id={mapId}{detailQuery}",
            ct,
            "rooms");
    }

    /// <summary>GET api/v1/map/construction/backlog?map_id - pending blueprint/frame backlog groups.</summary>
    public Task<IReadOnlyList<ConstructionBacklogGroupDto>> GetConstructionBacklogAsync(
        int mapId,
        CancellationToken ct = default) =>
        GetEnvelopedListAsync<ConstructionBacklogGroupDto>(
            $"api/v1/map/construction/backlog?map_id={mapId}", ct);

    /// <summary>POST api/v1/builder/blueprint-group/validate - dry-run group blueprint placement for Willie solver options.</summary>
    public Task<BlueprintGroupValidateResponseDto> PostBlueprintGroupValidateAsync(
        BlueprintGroupValidateRequestDto request,
        CancellationToken ct = default) =>
        PostEnvelopedAsync<BlueprintGroupValidateRequestDto, BlueprintGroupValidateResponseDto>(
            "api/v1/builder/blueprint-group/validate",
            request,
            ct);

    /// <summary>POST api/v1/builder/blueprint-group/place - player-click group blueprint placement after server-side validation.</summary>
    public Task<BlueprintGroupPlaceResultDto> PostBlueprintGroupPlaceAsync(
        BlueprintGroupPlaceRequestDto request,
        CancellationToken ct = default) =>
        PostEnvelopedAsync<BlueprintGroupPlaceRequestDto, BlueprintGroupPlaceResultDto>(
            "api/v1/builder/blueprint-group/place",
            request,
            ct);

    /// <summary>GET api/v1/map/reach - default in-map cell reachability for Willie placement scoring.</summary>
    public Task<MapReachResponseDto> GetReachAsync(
        int mapId,
        int fromX,
        int fromZ,
        int toX,
        int toZ,
        string mode = "pass_doors",
        string peMode = "on_cell",
        CancellationToken ct = default) =>
        GetEnvelopedAsync<MapReachResponseDto>(
            $"api/v1/map/reach?map_id={mapId}&from_x={fromX}&from_z={fromZ}&to_x={toX}&to_z={toZ}&mode={Query(mode)}&pe_mode={Query(peMode)}",
            ct);

    /// <summary>GET api/v1/map/region-at - region id for one in-map cell.</summary>
    public Task<MapRegionAtResponseDto> GetRegionAtAsync(
        int mapId,
        int x,
        int z,
        CancellationToken ct = default) =>
        GetEnvelopedAsync<MapRegionAtResponseDto>(
            $"api/v1/map/region-at?map_id={mapId}&x={x}&z={z}",
            ct);

    /// <summary>POST api/v1/map/path-cost - single-pair path-cost primitive for future Willie solver scoring.</summary>
    public Task<MapPathCostResponseDto> PostPathCostAsync(
        MapPathCostRequestDto request,
        CancellationToken ct = default) =>
        PostEnvelopedAsync<MapPathCostRequestDto, MapPathCostResponseDto>(
            "api/v1/map/path-cost",
            request,
            ct);

    /// <summary>POST api/v1/map/path-cost/batch - bounded batch path-cost primitive for future Willie solver scoring.</summary>
    public Task<MapPathCostBatchResponseDto> PostPathCostBatchAsync(
        MapPathCostBatchRequestDto request,
        CancellationToken ct = default)
    {
        // TODO: source the 4096 cap from a shared constant once a second consumer needs it; today only PS1 reads.
        const int maxPathCostBatchPairs = 4096;
        if (request.Pairs.Count > maxPathCostBatchPairs)
            throw new ArgumentException(
                $"Path-cost batch pairs length {request.Pairs.Count} exceeds limit {maxPathCostBatchPairs}.",
                nameof(request));

        // TODO: first consumer is PS1 (placement-solver.md); methods unused until then.
        return PostEnvelopedAsync<MapPathCostBatchRequestDto, MapPathCostBatchResponseDto>(
            "api/v1/map/path-cost/batch",
            request,
            ct);
    }

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
        GetEnvelopedListAsync<IncidentDto>($"api/v1/incidents?map_id={mapId}", ct, "incidents");

    // ── Resources / inventory ─────────────────────────────────────────────────

    /// <summary>
    /// GET api/v1/resources/summary?map_id — colony-wide stockpile rollup
    /// (food/medicine/weapon counts + total nutrition + market value).
    /// Replaces reliance on per-zone item lists, which RIMAPI's /map/zones
    /// does not expose.
    /// </summary>
    public Task<ResourcesSummaryDto> GetResourcesSummaryAsync(
        int mapId, CancellationToken ct = default) =>
        GetEnvelopedAsync<ResourcesSummaryDto>($"api/v1/resources/summary?map_id={mapId}", ct);

    /// <summary>GET api/v1/resources/stored?map_id — stored item stacks grouped by resource category.</summary>
    public Task<StoredResourcesDto> GetStoredResourcesAsync(
        int mapId, CancellationToken ct = default) =>
        GetEnvelopedAsync<StoredResourcesDto>($"api/v1/resources/stored?map_id={mapId}", ct);

    /// <summary>GET api/v1/def/all — thing defs nested at data.things_defs.</summary>
    public Task<IReadOnlyList<ThingDefDto>> GetThingDefsAsync(CancellationToken ct = default) =>
        GetEnvelopedListAsync<ThingDefDto>("api/v1/def/all", ct, "things_defs");

    /// <summary>GET api/v1/def/all - full definition catalog used by icon warming.</summary>
    public Task<DefCatalogDto> GetDefCatalogAsync(CancellationToken ct = default) =>
        GetEnvelopedAsync<DefCatalogDto>("api/v1/def/all", ct);

    /// <summary>GET api/v1/item/image?name - base64 PNG for a ThingDef.</summary>
    public Task<RimApiImageDto> GetItemImageAsync(string defName, CancellationToken ct = default) =>
        GetEnvelopedAsync<RimApiImageDto>($"api/v1/item/image?name={Query(defName)}", ct);

    /// <summary>GET api/v1/terrain/image?name - base64 PNG for a TerrainDef.</summary>
    public Task<RimApiImageDto> GetTerrainImageAsync(string defName, CancellationToken ct = default) =>
        GetEnvelopedAsync<RimApiImageDto>($"api/v1/terrain/image?name={Query(defName)}", ct);

    /// <summary>GET api/v1/factions - current-world factions with load ids.</summary>
    public Task<IReadOnlyList<FactionDto>> GetFactionsAsync(CancellationToken ct = default) =>
        GetEnvelopedListAsync<FactionDto>("api/v1/factions", ct);

    /// <summary>GET api/v1/faction/icon?id - base64 PNG plus faction color.</summary>
    public Task<FactionIconDto> GetFactionIconAsync(int loadId, CancellationToken ct = default) =>
        GetEnvelopedAsync<FactionIconDto>($"api/v1/faction/icon?id={loadId}", ct);

    /// <summary>GET api/v1/pawn/portrait/image - base64 PNG portrait for a pawn.</summary>
    public Task<RimApiImageDto> GetPawnPortraitImageAsync(
        int pawnId,
        int width = 128,
        int height = 128,
        string direction = "South",
        CancellationToken ct = default) =>
        GetEnvelopedAsync<RimApiImageDto>(
            $"api/v1/pawn/portrait/image?pawn_id={pawnId}&width={width}&height={height}&direction={Query(direction)}",
            ct);

    /// <summary>GET api/v1/colonist/body/image?id - base64 body/head images for a colonist.</summary>
    public Task<ColonistBodyImageDto> GetColonistBodyImageAsync(int pawnId, CancellationToken ct = default) =>
        GetEnvelopedAsync<ColonistBodyImageDto>($"api/v1/colonist/body/image?id={pawnId}", ct);

    // ── Research ──────────────────────────────────────────────────────────────

    /// <summary>
    /// GET api/v1/research/progress — current research project + progress.
    /// Returns name="none", label="None", progress_percent=0 when nothing is
    /// selected; IngestionDispatcher maps that to ResearchInfo.CurrentProject = null.
    /// </summary>
    public Task<ResearchProgressDto> GetResearchProgressAsync(CancellationToken ct = default) =>
        GetEnvelopedAsync<ResearchProgressDto>("api/v1/research/progress", ct);

    // ── Bill reads ───────────────────────────────────────────────────────────────

    /// <summary>GET api/v1/buildings/recipes?building_id — available recipes for a work table.</summary>
    public Task<IReadOnlyList<WorkTableRecipeDto>> GetWorkTableRecipesAsync(
        int buildingId,
        CancellationToken ct = default) =>
        GetEnvelopedListAsync<WorkTableRecipeDto>($"api/v1/buildings/recipes?building_id={buildingId}", ct);

    /// <summary>GET api/v1/buildings/bills?building_id — current bills for a work table.</summary>
    public Task<IReadOnlyList<WorkTableBillDto>> GetWorkTableBillsAsync(
        int buildingId,
        CancellationToken ct = default) =>
        GetEnvelopedListAsync<WorkTableBillDto>($"api/v1/buildings/bills?building_id={buildingId}", ct);

    // ── Write endpoints (Labor-owned) ─────────────────────────────────────────
    // TODO: Pawn Edit Controller and Pawn Job Controller field shapes are not cached
    //       in rimapi.md. Fetch live docs when M3 (Labor/assignment solver) begins
    //       and implement: SetWorkPriority, SetSchedule, ForceJob, SetZoneRestriction.
    //       Only MinisterOfLabor calls these writes.

    // ── Grow zone write (Chef-owned, deferred Auto path) ─────────────────────
    /// <summary>
    /// POST api/v1/map/zone/growing — create a grow zone over a rect with a crop def.
    /// Owned by Chef. Only call via the HTN planner primitive.
    /// </summary>
    public async Task CreateGrowZoneAsync(
        int mapId, string plantDef, int x1, int z1, int x2, int z2,
        CancellationToken ct = default)
    {
        object body = new
        {
            map_id = mapId,
            plant_def = plantDef,
            point_a = new { x = x1, y = 0, z = z1 },
            point_b = new { x = x2, y = 0, z = z2 }
        };
        HttpResponseMessage response = await http.PostAsJsonAsync("api/v1/map/zone/growing", body, ct);
        await EnsureWriteAcceptedAsync(response, "api/v1/map/zone/growing", ct);
    }

    /// <summary>
    /// POST api/v1/order/designate/area — designate Hunt / Harvest / Mine / Deconstruct
    /// over a rect. Used by Chef (harvest, hunt) and Willie (mine, decon).
    /// RIMAPI accepts designation/type and either point_a/point_b or rect.
    /// </summary>
    public async Task DesignateAreaAsync(
        int mapId, string designation, int x1, int z1, int x2, int z2,
        CancellationToken ct = default)
    {
        var body = new { map_id = mapId, designation,
                         rect = new { x1, z1, x2, z2 } };
        var response = await http.PostAsJsonAsync("api/v1/order/designate/area", body, ct);
        await EnsureWriteAcceptedAsync(response, "api/v1/order/designate/area", ct);
    }

    /// <summary>
    /// POST api/v1/order/unforbid — safe item allow-list endpoint expected from
    /// the RIMAPI companion change. This must not call destructive forbidden
    /// endpoints.
    /// </summary>
    public async Task UnforbidThingsAsync(
        int mapId,
        IReadOnlyList<string> thingIds,
        CancellationToken ct = default)
    {
        var body = new { map_id = mapId, thing_ids = thingIds };
        var response = await http.PostAsJsonAsync("api/v1/order/unforbid", body, ct);
        await EnsureWriteAcceptedAsync(response, "api/v1/order/unforbid", ct);
    }

    /// <summary>POST api/v1/buildings/bills/add — add a work-table bill.</summary>
    public async Task AddBillAsync(
        int buildingId,
        string recipeDefName,
        string repeatMode,
        int targetCount,
        CancellationToken ct = default)
    {
        string path = $"api/v1/buildings/bills/add?building_id={buildingId}";
        var body = new
        {
            recipe_def_name = recipeDefName,
            repeat_mode = repeatMode,
            target_count = targetCount,
            suspended = false
        };
        var response = await http.PostAsJsonAsync(path, body, ct);
        await EnsureWriteAcceptedAsync(response, path, ct);
    }

    /// <summary>PUT api/v1/buildings/bill/update — update a work-table bill.</summary>
    public async Task UpdateBillAsync(
        int buildingId,
        int billId,
        string repeatMode,
        int targetCount,
        CancellationToken ct = default)
    {
        string path = $"api/v1/buildings/bill/update?building_id={buildingId}&bill_id={billId}";
        var body = new
        {
            repeat_mode = repeatMode,
            target_count = targetCount
        };
        var response = await http.PutAsJsonAsync(path, body, ct);
        await EnsureWriteAcceptedAsync(response, path, ct);
    }
}

/// <summary>Thrown when RIMAPI returns success=false or an incompatible wire shape.</summary>
public class RimApiException : Exception
{
    public RimApiException(string message) : base(message) { }

    public RimApiException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class RimApiHttpException : RimApiException
{
    public System.Net.HttpStatusCode? StatusCode { get; }

    public RimApiHttpException(string message, System.Net.HttpStatusCode? statusCode, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
