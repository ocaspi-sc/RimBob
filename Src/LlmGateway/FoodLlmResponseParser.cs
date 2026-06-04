using System.Text.Json;
using System.Text.Json.Nodes;
using RimBob.Core.Advice;
using RimBob.Core.Briefings;

using GuideCitation = RimBob.Core.Advice.GuideCitation;

namespace RimBob.LLM;

public sealed record FoodLlmParseResult(
    FoodLlmResponse Response,
    string ParseMode,
    bool Normalized,
    int DroppedFlagCount = 0);

public static class FoodLlmResponseParser
{
    private static readonly JsonSerializerOptions ResponseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static FoodLlmParseResult Parse(
        string text,
        FoodBriefing briefing,
        IReadOnlyList<GuideCitation> guideContext)
    {
        FoodLlmResponse? strict = TryParseStrict(text);
        if (strict is not null)
        {
            return new FoodLlmParseResult(
                EnsureStateSummary(
                    StampRuntimeMetadata(
                        AdviceResponseNormalizer.NormalizeStrictResponse(strict),
                        briefing),
                    briefing),
                "strict_json",
                Normalized: false,
                DroppedFlagCount: 0);
        }

        LlmAdviceNormalizationContext normalizeContext = new(
            Minister: "Chef",
            Domain: "food",
            BriefingVersion: briefing.BriefingVersion,
            GameTick: briefing.GameTick,
            Date: briefing.Date,
            DefaultRationale: "Chef LLM escalation selected this recommendation.",
            GuideContext: guideContext);

        JsonNode root = JsonNode.Parse(text) ??
            throw new JsonException("LLM response JSON parsed to null.");
        NormalizedAdviceResponse normalizedResponse =
            AdviceResponseNormalizer.Normalize(root, normalizeContext, ResponseJson);
        FoodLlmResponse parsed = new(
            normalizedResponse.StateSummary,
            normalizedResponse.Advice,
            normalizedResponse.Flags,
            normalizedResponse.Notes);

        parsed = EnsureStateSummary(AdviceResponseNormalizer.NormalizeStrictResponse(parsed), briefing);
        return new FoodLlmParseResult(
            parsed,
            "tolerant_normalization",
            Normalized: true,
            DroppedFlagCount: normalizedResponse.DroppedFlagCount);
    }

    private static FoodLlmResponse? TryParseStrict(string text)
    {
        try
        {
            JsonNode? root = JsonNode.Parse(text);
            if (root is null || !HasStrictResponseShape(root)) return null;

            FoodLlmResponse? parsed = root.Deserialize<FoodLlmResponse>(ResponseJson);
            return parsed is not null && IsStrictFoodResponse(parsed) ? parsed : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static FoodLlmResponse EnsureStateSummary(FoodLlmResponse response, FoodBriefing briefing) =>
        string.IsNullOrWhiteSpace(response.StateSummary)
            ? response with { StateSummary = FoodStateSummary.Build(briefing) }
            : response with { StateSummary = response.StateSummary.Trim() };

    private static FoodLlmResponse StampRuntimeMetadata(FoodLlmResponse response, FoodBriefing briefing)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IReadOnlyList<AdviceItem> advice = response.Advice
            .Select(item =>
            {
                DateTimeOffset issuedAt = IsRuntimeTimestamp(item.IssuedAt, now)
                    ? item.IssuedAt
                    : now;
                DateTimeOffset expiresAt = IsUsefulExpiry(item.ExpiresAt, now)
                    ? item.ExpiresAt
                    : now.AddHours(item.Priority >= Priority.High ? 4 : 24);

                return item with
                {
                    Minister = "Chef",
                    Stamp = new AdviceStamp(
                        issuedAt,
                        expiresAt,
                        briefing.Date,
                        item.IssuedGameTick is > 0
                            ? item.IssuedGameTick
                            : briefing.GameTick,
                        IsUsefulGameExpiry(item.ExpiresGameTick, briefing.GameTick)
                            ? item.ExpiresGameTick
                            : AdviceFreshness.ExpiresGameTick(briefing.GameTick, item.Priority)),
                    BriefingRef = item.BriefingRef is null
                        ? new BriefingRef("Chef", briefing.BriefingVersion, $"food:{briefing.BriefingVersion}")
                        : item.BriefingRef with { Minister = "Chef" }
                };
            })
            .ToArray();

        IReadOnlyList<RimBob.Core.Ministers.AgentFlag> flags = response.Flags
            .Select(flag => flag with
            {
                SourceMinister = "Chef",
                ExpiresAt = flag.ExpiresAt is { } expiresAt && IsUsefulExpiry(expiresAt, now)
                    ? expiresAt
                    : now.AddHours(24)
            })
            .ToArray();

        return response with { Advice = advice, Flags = flags };
    }

    private static bool IsRuntimeTimestamp(DateTimeOffset value, DateTimeOffset now) =>
        value != default && value <= now.AddDays(1);

    private static bool IsUsefulExpiry(DateTimeOffset value, DateTimeOffset now) =>
        value > now && value <= now.AddDays(7);

    private static bool IsUsefulGameExpiry(long? value, long gameTick) =>
        value is { } expiresGameTick &&
        expiresGameTick > gameTick &&
        expiresGameTick <= gameTick + AdviceFreshness.TicksPerGameDay * 7;

    private static bool HasStrictResponseShape(JsonNode root) =>
        HasStrictAdviceShape(root) && HasStrictFlagShape(root);

    private static bool HasStrictAdviceShape(JsonNode root)
    {
        JsonArray? advice = root["advice"]?.AsArray();
        return advice is not null && advice.All(item => item?["priority"] is not null && item?["actions"] is not null);
    }

    private static bool HasStrictFlagShape(JsonNode root)
    {
        JsonArray? flags = root["flags"]?.AsArray();
        return flags is null || flags.All(item =>
            item is null ||
            item is not JsonObject obj ||
            obj.Count == 0 ||
            item["id"] is not null &&
            item["priority"] is not null &&
            item["domain"] is not null &&
            item["summary"] is not null);
    }

    private static bool IsStrictFoodResponse(FoodLlmResponse response) =>
        !string.IsNullOrWhiteSpace(response.StateSummary) &&
        response.Advice is not null &&
            response.Flags is not null &&
            response.Advice.All(advice =>
                !string.IsNullOrWhiteSpace(advice.Id) &&
                !string.IsNullOrWhiteSpace(advice.Minister) &&
                !string.IsNullOrWhiteSpace(advice.Title) &&
                !string.IsNullOrWhiteSpace(advice.Body) &&
            !string.IsNullOrWhiteSpace(advice.Rationale) &&
            advice.Stamp is not null &&
            advice.Actions.All(action =>
                !string.IsNullOrWhiteSpace(action.Instruction)) &&
            response.Flags.All(flag =>
                IsStrictFlag(flag) &&
                RequestsComplete(flag)));

    private static bool IsStrictFlag(RimBob.Core.Ministers.AgentFlag flag) =>
        !string.IsNullOrWhiteSpace(flag.Id) &&
        !string.IsNullOrWhiteSpace(flag.Domain) &&
        !string.IsNullOrWhiteSpace(flag.Summary);

    private static bool RequestsComplete(RimBob.Core.Ministers.AgentFlag flag) =>
        Complete(flag.BuildingRequests, request => request.Request, request => request.Reason) &&
        Complete(flag.LaborRequests, request => request.Request, request => request.Reason) &&
        Complete(flag.ItemRequests, request => request.Request, request => request.Reason) &&
        Complete(flag.Attention, request => request.Request, request => request.Reason);

    private static bool Complete<T>(
        IReadOnlyList<T>? requests,
        Func<T, string> request,
        Func<T, string> reason) =>
        requests is null ||
        requests.All(item =>
            !string.IsNullOrWhiteSpace(request(item)) &&
            !string.IsNullOrWhiteSpace(reason(item)));
}
