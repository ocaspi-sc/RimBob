using System.Text.Json;
using System.Text.Json.Nodes;
using RimAI.Core.Advice;
using RimAI.Core.Briefings;

using GuideCitation = RimAI.Core.Advice.GuideCitation;

namespace RimAI.LLM;

public sealed record FoodLlmParseResult(
    FoodLlmResponse Response,
    string ParseMode,
    bool Normalized);

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
                StampRuntimeMetadata(
                    LlmAdviceResponseNormalizer.NormalizeStrictResponse(strict),
                    briefing),
                "strict_json",
                Normalized: false);
        }

        LlmAdviceNormalizationContext normalizeContext = new(
            Minister: "Food",
            Domain: "food",
            BriefingVersion: briefing.BriefingVersion,
            GameTick: briefing.GameTick,
            Date: briefing.Date,
            DefaultAdviceType: nameof(FoodAdviceType.FoodSecurity),
            DefaultRationale: "Food LLM escalation selected this recommendation.",
            GuideContext: guideContext);

        JsonNode root = JsonNode.Parse(text) ??
            throw new JsonException("LLM response JSON parsed to null.");
        NormalizedAdviceResponse normalizedResponse =
            LlmAdviceResponseNormalizer.Normalize(root, normalizeContext, ResponseJson);
        FoodLlmResponse parsed = new(
            normalizedResponse.Advice,
            normalizedResponse.Flags,
            normalizedResponse.Notes);

        parsed = LlmAdviceResponseNormalizer.NormalizeStrictResponse(parsed);
        return new FoodLlmParseResult(
            parsed,
            "tolerant_normalization",
            Normalized: true);
    }

    private static FoodLlmResponse? TryParseStrict(string text)
    {
        try
        {
            JsonNode? root = JsonNode.Parse(text);
            if (root is null || !HasStrictAdvicePriority(root)) return null;

            FoodLlmResponse? parsed = root.Deserialize<FoodLlmResponse>(ResponseJson);
            return parsed is not null && IsStrictFoodResponse(parsed) ? parsed : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

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
                    : now.AddHours(item.Priority >= AdvicePriority.High ? 4 : 24);

                return item with
                {
                    IssuedAt = issuedAt,
                    ExpiresAt = expiresAt,
                    IssuedInGameTick = string.IsNullOrWhiteSpace(item.IssuedInGameTick)
                        ? FormatTick(briefing.Date)
                        : item.IssuedInGameTick,
                    BriefingRef = item.BriefingRef ??
                        new BriefingRef("Food", briefing.BriefingVersion, $"food:{briefing.BriefingVersion}")
                };
            })
            .ToArray();

        IReadOnlyList<RimAI.Core.Ministers.AgentFlag> flags = response.Flags
            .Select(flag => flag with
            {
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

    private static string FormatTick(DateStamp date) =>
        $"Y{date.Year ?? 0}{date.Quadrum ?? "?"}D{date.Day ?? 0}";

    private static bool HasStrictAdvicePriority(JsonNode root)
    {
        JsonArray? advice = root["advice"]?.AsArray();
        return advice is not null && advice.All(item => item?["priority"] is not null);
    }

    private static bool IsStrictFoodResponse(FoodLlmResponse response) =>
        response.Advice.All(advice =>
            !string.IsNullOrWhiteSpace(advice.Id) &&
            !string.IsNullOrWhiteSpace(advice.Minister) &&
            !string.IsNullOrWhiteSpace(advice.AdviceType) &&
            !string.IsNullOrWhiteSpace(advice.Title) &&
            !string.IsNullOrWhiteSpace(advice.Body) &&
            !string.IsNullOrWhiteSpace(advice.Rationale) &&
            advice.ResourceRequests.All(request =>
                !string.IsNullOrWhiteSpace(request.What) &&
                !string.IsNullOrWhiteSpace(request.Why)) &&
            advice.SuggestedActions.All(action =>
                !string.IsNullOrWhiteSpace(action.What)) &&
            response.Flags.All(flag =>
                flag.Requests is null || flag.Requests.All(request =>
                    !string.IsNullOrWhiteSpace(request.What) &&
                    !string.IsNullOrWhiteSpace(request.Why))));
}
