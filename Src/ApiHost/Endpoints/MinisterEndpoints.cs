using System.Text.Json;
using RimAI.Coordination;
using RimAI.Core.Advice;
using RimAI.Core.Briefings;
using RimAI.Core.Ministers;
using RimAI.Knowledge;
using RimAI.LLM;
using RimAI.Ministers.Food;
using RimAI.State;

namespace RimAI.Host.Endpoints;

public static class MinisterEndpoints
{
    public static IEndpointRouteBuilder MapMinisterEndpoints(this IEndpointRouteBuilder app)
    {
        EndpointCoverageCatalog coverage = app.ServiceProvider.GetRequiredService<EndpointCoverageCatalog>();
        coverage.Register(
            "/api/ministers/{minister}/prompt",
            _ => "partial",
            context => $"Generalized prompt inspector for {context.CapabilityNames(descriptor => descriptor.HasPrompt)}.");
        coverage.Register(
            "/api/ministers/{minister}/llm-output/latest",
            _ => "partial",
            context => $"Latest raw Gemini response for {context.CapabilityNames(descriptor => descriptor.HasRawLlmOutput)} after an LLM call occurs.");
        coverage.Register(
            "/api/ministers/{minister}/llm-output/manual",
            _ => "partial",
            context => $"Developer manual raw LLM ingestion for {context.CapabilityNames(descriptor => descriptor.HasManualLlmOutput)}.");
        coverage.Register("/api/ministers/{minister}/trace/latest", "partial", "Wake trigger visible; rule/LLM path details not exposed yet.");
        coverage.Register("/api/ministers/{minister}/rag/latest", "not_exposed_yet", "Planned RAG retrieval inspector.");

        app.MapGet("/api/ministers", (MinisterRegistry registry) =>
            Results.Ok(registry.Scopes.Select(MinisterScopeInfo.FromDescriptor)));

        app.MapGet("/api/ministers/{minister}/prompt", async (
            string minister,
            MinisterRegistry registry,
            BriefingCache briefings,
            AgendaStore agendaStore,
            PromptBuilder prompts,
            MayorRagRetriever mayorRetriever,
            FoodRagRetriever foodRetriever,
            FlagChannel flags,
            CancellationToken ct) =>
        {
            MinisterDescriptor? scope = registry.FindMinister(minister);
            if (scope is null)
                return Results.NotFound(new { error = $"Unknown minister scope '{minister}'." });

            if (!scope.Ready)
            {
                return Results.Problem(
                    title: "Prompt not wired",
                    detail: $"{scope.Label} is a planned minister scope; prompt introspection is not wired yet.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            if (!scope.HasPrompt)
            {
                return Results.Problem(
                    title: "Prompt not wired",
                    detail: $"{scope.Label} prompt introspection is not wired yet.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            if (scope.Key == "mayor")
            {
                MayorBriefing briefing = briefings.GetMayorBriefing();
                IReadOnlyList<GuideCitation> retrieved = await mayorRetriever.RetrieveAsync(briefing, [], ct);
                IReadOnlyList<AgentFlag> activeFlags = flags.Active(FlagSeverity.Medium);
                string user = prompts.BuildMayorUserMessage(briefing, agendaStore.Current, [], retrieved, activeFlags);
                return Results.Ok(new { system = ReadPromptOrPlaceholder(() => prompts.MayorSystemPrompt), user });
            }

            if (scope.Key == "food")
            {
                FoodBriefing briefing = briefings.GetFoodBriefing();
                MinisterBriefingContext context = MinisterOfFood.BuildContext(agendaStore.Current);
                IReadOnlyList<GuideCitation> retrieved = await foodRetriever.RetrieveAsync(briefing, ct);
                string user = prompts.BuildFoodUserMessage(briefing, context, retrieved);
                return Results.Ok(new { system = ReadPromptOrPlaceholder(() => prompts.FoodSystemPrompt), user });
            }

            return Results.Problem(
                title: "Prompt not wired",
                detail: $"{scope.Label} prompt introspection is not wired yet.",
                statusCode: StatusCodes.Status501NotImplemented);
        });

        app.MapGet("/api/ministers/{minister}/llm-output/latest", (
            string minister,
            MinisterRegistry registry,
            RawLlmOutputStore outputs) =>
        {
            MinisterDescriptor? scope = registry.FindMinister(minister);
            if (scope is null)
                return Results.NotFound(new { error = $"Unknown minister scope '{minister}'." });

            if (!scope.Ready || !scope.HasRawLlmOutput)
            {
                return Results.Problem(
                    title: "Raw LLM output not wired",
                    detail: $"{scope.Label} is a planned minister scope; raw LLM output is not wired yet.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            RawLlmOutputSnapshot? snapshot = outputs.Latest(scope.Label);
            if (snapshot is null)
            {
                return Results.Ok(new RawLlmOutputSnapshot(
                    Minister: scope.Label,
                    Provider: "Gemini",
                    Model: LlmClient.DefaultModel,
                    ApiKeyIndex: null,
                    ApiKeyLabel: null,
                    CapturedAt: DateTimeOffset.UtcNow,
                    LatencyMs: 0,
                    Status: "not_seen_yet",
                    ParseMode: "not_applicable",
                    SystemPromptChars: 0,
                    UserPromptChars: 0,
                    Text: ""));
            }

            return Results.Ok(snapshot);
        });

        app.MapPost("/api/ministers/{minister}/llm-output/manual", async (
            string minister,
            JsonElement body,
            MinisterRegistry registry,
            BriefingCache briefings,
            AgendaStore agendaStore,
            PromptBuilder prompts,
            FoodRagRetriever foodRetriever,
            RawLlmOutputStore outputs,
            AdviceBus bus,
            FlagChannel flags,
            CancellationToken ct) =>
        {
            MinisterDescriptor? scope = registry.FindMinister(minister);
            if (scope is null)
                return Results.NotFound(new { error = $"Unknown minister scope '{minister}'." });

            if (!scope.HasManualLlmOutput)
            {
                return Results.Problem(
                    title: "Manual LLM output not wired",
                    detail: $"{scope.Label} does not have manual LLM output ingestion yet.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            string text = ReadManualLlmOutputText(body);
            if (string.IsNullOrWhiteSpace(text))
                return Results.BadRequest(new { error = "Manual LLM output text is required." });

            FoodBriefing briefing = briefings.GetFoodBriefing();
            MinisterBriefingContext context = MinisterOfFood.BuildContext(agendaStore.Current);
            IReadOnlyList<GuideCitation> retrieved = await foodRetriever.RetrieveAsync(briefing, ct);
            string user = prompts.BuildFoodUserMessage(briefing, context, retrieved);
            string system = ReadPromptOrPlaceholder(() => prompts.FoodSystemPrompt);
            DateTimeOffset capturedAt = DateTimeOffset.UtcNow;

            try
            {
                FoodLlmParseResult parseResult = FoodLlmResponseParser.Parse(text, briefing, retrieved);
                outputs.Record(new RawLlmOutputSnapshot(
                    Minister: scope.Label,
                    Provider: "Codex",
                    Model: "codex-subagent",
                    ApiKeyIndex: null,
                    ApiKeyLabel: null,
                    CapturedAt: capturedAt,
                    LatencyMs: 0,
                    Status: parseResult.Normalized ? "manual_normalized" : "manual_parsed",
                    ParseMode: parseResult.ParseMode,
                    SystemPromptChars: system.Length,
                    UserPromptChars: user.Length,
                    Text: text));

                bus.ReplaceMinisterAdvice(scope.Label, parseResult.Response.Advice);
                foreach (AgentFlag flag in parseResult.Response.Flags)
                    flags.Publish(flag);

                return Results.Ok(new
                {
                    accepted = true,
                    minister = scope.Label,
                    status = parseResult.Normalized ? "manual_normalized" : "manual_parsed",
                    parse_mode = parseResult.ParseMode,
                    advice_count = parseResult.Response.Advice.Count,
                    flag_count = parseResult.Response.Flags.Count,
                    notes = parseResult.Response.Notes
                });
            }
            catch (JsonException ex)
            {
                outputs.Record(new RawLlmOutputSnapshot(
                    Minister: scope.Label,
                    Provider: "Codex",
                    Model: "codex-subagent",
                    ApiKeyIndex: null,
                    ApiKeyLabel: null,
                    CapturedAt: capturedAt,
                    LatencyMs: 0,
                    Status: "manual_parse_failed",
                    ParseMode: "strict_json",
                    SystemPromptChars: system.Length,
                    UserPromptChars: user.Length,
                    Text: text));
                return Results.BadRequest(new
                {
                    error = "Manual LLM output could not be parsed as Food advice JSON.",
                    detail = ex.Message
                });
            }
        });

        app.MapGet("/api/ministers/{minister}/trace/latest", (
            string minister,
            MinisterRegistry registry,
            MinisterTraceStore traces) =>
        {
            MinisterDescriptor? scope = registry.FindMinister(minister);

            if (scope is null)
                return Results.NotFound(new { error = $"Unknown minister scope '{minister}'." });

            MinisterTraceSnapshot? snapshot = traces.Latest(scope.Label);
            if (snapshot is null)
            {
                return Results.Ok(new MinisterTraceSnapshot(
                    Minister: scope.Label,
                    Trigger: "not_seen_yet",
                    Status: scope.Ready ? "waiting" : "not_wired",
                    StartedAt: DateTimeOffset.UtcNow,
                    CompletedAt: null,
                    Path: "not_exposed_yet",
                    RuleFired: null,
                    EscalationReason: null,
                    WakeupPayload: null,
                    Flag: null,
                    Note: scope.Ready
                        ? "No live trace recorded since Host startup."
                        : "Minister scope is planned but not wired yet."));
            }

            return Results.Ok(snapshot);
        });

        return app;
    }

    private static string ReadPromptOrPlaceholder(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (FileNotFoundException ex)
        {
            return $"(prompt file not found: {ex.FileName})";
        }
    }

    private static string ReadManualLlmOutputText(JsonElement body)
    {
        if (body.ValueKind == JsonValueKind.Object)
        {
            if (body.TryGetProperty("text", out JsonElement textProperty) &&
                textProperty.ValueKind == JsonValueKind.String)
            {
                return textProperty.GetString() ?? "";
            }

            if (body.TryGetProperty("raw", out JsonElement rawProperty) &&
                rawProperty.ValueKind == JsonValueKind.String)
            {
                return rawProperty.GetString() ?? "";
            }
        }

        if (body.ValueKind == JsonValueKind.String)
            return body.GetString() ?? "";

        return body.GetRawText();
    }

    private sealed record MinisterScopeInfo(
        string Key,
        string Label,
        string Kind,
        bool Ready,
        IReadOnlyList<string> EnabledViews)
    {
        public static MinisterScopeInfo FromDescriptor(MinisterDescriptor descriptor) =>
            new(
                descriptor.Key,
                descriptor.Label,
                descriptor.Kind,
                descriptor.Ready,
                descriptor.EnabledViews);
    }
}
