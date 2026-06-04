using System.Text.Json;
using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Core.Aggregates;
using RimBob.Core.Briefings;
using RimBob.Core.Ministers;
using RimBob.Knowledge;
using RimBob.LLM;
using RimBob.Ministers.Food;
using RimBob.Ministers.Welfare;
using RimBob.Ministers.Willie;
using RimBob.State;

namespace RimBob.Host.Endpoints;

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
        coverage.Register(
            "/api/ministers/{minister}/snapshot",
            _ => "available",
            _ => "Latest persisted typed minister output snapshot: MayorAgenda for mayor, AdviceSnapshot for feeders.");
        coverage.Register(
            "/api/ministers/mayor/snapshot/manual",
            "available",
            "Developer manual Mayor snapshot fallback ingestion.");
        coverage.Register("/api/ministers/{minister}/trace/latest", "available", "Latest minister trigger, status, rules/LLM path, selected/suppressed rule diagnostics, escalation reason, counts, and error detail.");
        coverage.Register("/api/ministers/{minister}/rag/latest", "not_exposed_yet", "Planned RAG retrieval inspector.");
        coverage.Register(
            "/api/ministers/food/crop-math/latest",
            "available",
            "Read-only Chef crop candidate diagnostics computed from the latest food briefing.");
        coverage.Register(
            "/api/ministers/food/hunt-risk/latest",
            "available",
            "Read-only Chef hunt risk diagnostics computed from current animal state and animal-def metadata.");
        coverage.Register(
            "/api/ministers/willie/solver/latest",
            "available",
            "Latest Willie Placement Solver outcome: selected rule, per-generator draft trace and scores, no-fit reason, and draftable/placement/materials/apply readiness.");
        coverage.Register(
            "/api/ministers/willie/solver/requests",
            "available",
            "Current Willie building-request board joined with each request's latest live Placement Solver outcome and options.");

        app.MapGet("/api/ministers", (MinisterRegistry registry) =>
            Results.Ok(registry.Scopes.Select(MinisterScopeInfo.FromDescriptor)));

        app.MapGet("/api/ministers/{minister}/prompt", async (
            string minister,
            MinisterRegistry registry,
            BriefingCache briefings,
            MinisterOutputStore outputStore,
            PromptBuilder prompts,
            PromptInspectorCache promptCache,
            MayorRagRetriever mayorRetriever,
            FoodRagRetriever foodRetriever,
            WelfareRagRetriever welfareRetriever,
            FlagChannel flags,
            bool? refresh,
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
                IReadOnlyList<AgentFlag> activeFlags = flags.Active(Priority.Medium);
                string key = PromptInspectorCache.BuildKey(
                    scope.Key,
                    briefing.BriefingVersion,
                    outputStore.CurrentMayorAgenda?.Version,
                    PromptInspectorCache.HashJson(activeFlags));
                PromptInspectorPayload payload = await promptCache.GetOrCreateAsync(
                    key,
                    refresh == true,
                    async cancellationToken =>
                    {
                        IReadOnlyList<GuideCitation> retrieved = await mayorRetriever.RetrieveAsync(briefing, [], cancellationToken);
                        string user = prompts.BuildMayorUserMessage(briefing, [], retrieved, activeFlags);
                        return new PromptInspectorPayload(
                            ReadPromptOrPlaceholder(() => prompts.MayorSystemPrompt),
                            user);
                    },
                    ct);
                return Results.Ok(payload);
            }

            if (scope.Key == "food")
            {
                FoodBriefing briefing = briefings.GetFoodBriefing();
                string key = PromptInspectorCache.BuildKey(
                    scope.Key,
                    briefing.BriefingVersion,
                    outputStore.CurrentMayorAgenda?.Version);
                PromptInspectorPayload payload = await promptCache.GetOrCreateAsync(
                    key,
                    refresh == true,
                    async cancellationToken =>
                    {
                        MinisterBriefingContext context = Chef.BuildContext(outputStore.CurrentMayorAgenda);
                        IReadOnlyList<GuideCitation> retrieved = await foodRetriever.RetrieveAsync(briefing, cancellationToken);
                        IReadOnlyList<FoodPromptCropCandidate> cropCandidates = Chef.BuildCropCandidates(briefing);
                        string user = prompts.BuildFoodUserMessage(briefing, context, retrieved, cropCandidates);
                        return new PromptInspectorPayload(
                            ReadPromptOrPlaceholder(() => prompts.FoodSystemPrompt),
                            user);
                    },
                    ct);
                return Results.Ok(payload);
            }

            if (scope.Key == "welfare")
            {
                WelfareSourceBriefing briefing = briefings.GetWelfareBriefing();
                string key = PromptInspectorCache.BuildKey(
                    scope.Key,
                    briefing.BriefingVersion,
                    outputStore.CurrentMayorAgenda?.Version);
                PromptInspectorPayload payload = await promptCache.GetOrCreateAsync(
                    key,
                    refresh == true,
                    async cancellationToken =>
                    {
                        MinisterBriefingContext context = MinisterOfWelfare.BuildContext(outputStore.CurrentMayorAgenda);
                        IReadOnlyList<GuideCitation> retrieved = await welfareRetriever.RetrieveAsync(briefing, cancellationToken);
                        string user = prompts.BuildWelfareUserMessage(briefing, context, retrieved);
                        return new PromptInspectorPayload(
                            ReadPromptOrPlaceholder(() => prompts.WelfareSystemPrompt),
                            user);
                    },
                    ct);
                return Results.Ok(payload);
            }

            return Results.Problem(
                title: "Prompt not wired",
                detail: $"{scope.Label} prompt introspection is not wired yet.",
                statusCode: StatusCodes.Status501NotImplemented);
        });

        app.MapGet("/api/ministers/food/crop-math/latest", (BriefingCache briefings) =>
        {
            FoodBriefing briefing = briefings.GetFoodBriefing();
            FoodCropRecommendation recommendation = FoodCropMath.Recommend(briefing);
            return Results.Ok(new FoodCropMathSnapshot(
                BriefingVersion: briefing.BriefingVersion,
                GameTick: briefing.GameTick,
                Date: briefing.Date,
                Season: briefing.Season,
                ColonistCount: briefing.ColonistCount,
                EstimatedDaysOfFood: briefing.EstimatedDaysOfFood,
                NutritionSource: briefing.NutritionSource,
                GrowingTerrain: briefing.GrowingTerrain,
                BestCandidate: recommendation.BestCandidate,
                Candidates: recommendation.Candidates));
        });

        app.MapGet("/api/ministers/food/hunt-risk/latest", (BriefingCache briefings, ColonyState state) =>
        {
            FoodBriefing briefing = briefings.GetFoodBriefing();
            IReadOnlyList<HuntRiskDiagnostic> animalDiagnostics = state.Animals.Value.Animals
                .Select(animal => FoodHuntSafety.Diagnose(animal, state.AnimalDefs.Value))
                .ToList();
            IReadOnlyList<FoodHuntRiskSpeciesDiagnostic> species = animalDiagnostics
                .GroupBy(diagnostic => diagnostic.Def, StringComparer.OrdinalIgnoreCase)
                .Select(group => BuildHuntRiskSpeciesDiagnostic(group, state.AnimalDefs.Value))
                .OrderBy(speciesDiagnostic => RiskSort(speciesDiagnostic.Risk))
                .ThenByDescending(speciesDiagnostic => speciesDiagnostic.SortScore)
                .ThenByDescending(speciesDiagnostic => speciesDiagnostic.EstimatedNutritionTotal ?? 0f)
                .ThenBy(speciesDiagnostic => speciesDiagnostic.Def, StringComparer.OrdinalIgnoreCase)
                .ToList();
            IReadOnlyList<FoodHuntRiskCandidateDiagnostic> candidates = species
                .Where(speciesDiagnostic => speciesDiagnostic.LowRiskCount > 0)
                .OrderBy(speciesDiagnostic => speciesDiagnostic.RiskRank)
                .ThenByDescending(speciesDiagnostic => speciesDiagnostic.EstimatedNutritionTotal ?? 0f)
                .ThenByDescending(speciesDiagnostic => speciesDiagnostic.LowRiskCount)
                .ThenBy(speciesDiagnostic => speciesDiagnostic.Def, StringComparer.OrdinalIgnoreCase)
                .Select((speciesDiagnostic, index) => new FoodHuntRiskCandidateDiagnostic(
                    Rank: index + 1,
                    Def: speciesDiagnostic.Def,
                    Label: speciesDiagnostic.Label,
                    Count: speciesDiagnostic.LowRiskCount,
                    RiskRank: speciesDiagnostic.RiskRank,
                    EstimatedNutritionEach: speciesDiagnostic.EstimatedNutritionEach,
                    EstimatedNutritionTotal: speciesDiagnostic.EstimatedNutritionTotal,
                    Reason: speciesDiagnostic.Reason))
                .Take(8)
                .ToList();

            return Results.Ok(new FoodHuntRiskDiagnosticsSnapshot(
                BriefingVersion: briefing.BriefingVersion,
                GameTick: briefing.GameTick,
                Date: briefing.Date,
                HasLiveState: state.LastRefreshSource == ColonyStateOrigin.Live,
                StateSource: state.LastRefreshSource.ToString(),
                AnimalDefCount: state.AnimalDefs.Value.DefsByName.Count,
                TotalAnimals: animalDiagnostics.Count,
                HealthyWildAnimals: animalDiagnostics.Count(diagnostic => diagnostic.IsHealthyWild),
                LowRiskAnimals: animalDiagnostics.Count(diagnostic => diagnostic.Profile.Risk == "low"),
                CautionAnimals: animalDiagnostics.Count(diagnostic => diagnostic.Profile.Risk == "caution"),
                DangerousAnimals: animalDiagnostics.Count(diagnostic => diagnostic.Profile.Risk == "dangerous"),
                BlockedAnimals: animalDiagnostics.Count(diagnostic => diagnostic.Profile.Risk == "blocked"),
                MetadataBackedAnimals: animalDiagnostics.Count(diagnostic => diagnostic.MetadataAvailable),
                FallbackAnimals: animalDiagnostics.Count(diagnostic => diagnostic.FallbackUsed),
                Thresholds: FoodHuntRiskThresholds.Current,
                Species: species,
                Candidates: candidates));
        });

        app.MapGet("/api/ministers/willie/solver/latest", (
            MinisterRegistry registry,
            WillieSolverStore solverStore) =>
        {
            MinisterDescriptor? scope = registry.FindMinister("willie");
            if (scope is null)
                return Results.NotFound(new { error = "Willie minister scope is not registered." });

            return Results.Ok(solverStore.Latest(scope.Label) ?? WillieSolverSnapshot.NotSeen(scope.Label));
        });

        app.MapGet("/api/ministers/willie/solver/requests", (
            MinisterRegistry registry,
            WillieSolverStore solverStore) =>
        {
            MinisterDescriptor? scope = registry.FindMinister("willie");
            if (scope is null)
                return Results.NotFound(new { error = "Willie minister scope is not registered." });

            IReadOnlyList<WillieRequestBoardRow> board = solverStore.RequestBoard(scope.Label);
            return Results.Ok(new WillieRequestBoardPayload(
                Minister: scope.Label,
                Requests: board.Select(WillieRequestRowPayload.FromRow).ToList()));
        });

        app.MapGet("/api/ministers/{minister}/snapshot", (
            string minister,
            MinisterRegistry registry,
            MinisterOutputStore outputStore) =>
        {
            MinisterDescriptor? scope = registry.FindMinister(minister);
            if (scope is null)
                return Results.NotFound(new { error = $"Unknown minister scope '{minister}'." });

            if (scope.Key == "mayor")
                return outputStore.CurrentMayorAgenda is { } agenda ? Results.Ok(agenda) : Results.NoContent();

            AdviceSnapshot? snapshot = outputStore.GetAdviceSnapshot(scope.Label)
                                       ?? outputStore.GetAdviceSnapshot(scope.Key);
            return snapshot is not null ? Results.Ok(snapshot) : Results.NoContent();
        });

        app.MapPost("/api/ministers/mayor/snapshot/manual", async (
            MayorAgendaInput input,
            BriefingCache briefings,
            MinisterOutputStore outputStore,
            AdviceBus bus,
            CancellationToken ct) =>
        {
            MayorAgendaInput capped = input.ShortTerm.Count > 5
                ? input with { ShortTerm = input.ShortTerm.Take(5).ToList() }
                : input;

            MayorBriefing briefing = briefings.GetMayorBriefing();
            MayorAgenda stamped = await outputStore.UpdateMayorAsync(capped, briefing.Date, ct);
            bus.Publish(new AgendaUpdated(stamped));
            return Results.Ok(stamped);
        });

        app.MapGet("/api/ministers/{minister}/llm-output/latest", (
            string minister,
            MinisterRegistry registry,
            RawLlmOutputStore outputs,
            ReplayCorpusRawOutputReader replayOutputs) =>
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

            RawLlmOutputSnapshot? snapshot = LatestRawOutput(
                outputs.Latest(scope.Label),
                replayOutputs.Latest(scope.Label));
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
            MinisterOutputStore outputStore,
            PromptBuilder prompts,
            FoodRagRetriever foodRetriever,
            WelfareRagRetriever welfareRetriever,
            RawLlmOutputStore outputs,
            MinisterReplayRecorder replay,
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

            if (scope.Key == "welfare")
            {
                WelfareSourceBriefing welfareBriefing = briefings.GetWelfareBriefing();
                MinisterBriefingContext welfareContext = MinisterOfWelfare.BuildContext(outputStore.CurrentMayorAgenda);
                IReadOnlyList<GuideCitation> welfareRetrieved = await welfareRetriever.RetrieveAsync(welfareBriefing, ct);
                string welfareUser = prompts.BuildWelfareUserMessage(welfareBriefing, welfareContext, welfareRetrieved);
                string welfareSystem = ReadPromptOrPlaceholder(() => prompts.WelfareSystemPrompt);
                DateTimeOffset welfareCapturedAt = DateTimeOffset.UtcNow;

                try
                {
                    WelfareLlmParseResult parseResult = WelfareLlmResponseParser.Parse(text, welfareBriefing, welfareRetrieved);
                    IReadOnlyList<string> styleWarnings = ManualLlmStyleWarnings(parseResult);
                    string stateSummary = WelfareStateSummary.Build(welfareBriefing);
                    outputs.Record(new RawLlmOutputSnapshot(
                        Minister: scope.Label,
                        Provider: "Codex",
                        Model: "codex-subagent",
                        ApiKeyIndex: null,
                        ApiKeyLabel: null,
                        CapturedAt: welfareCapturedAt,
                        LatencyMs: 0,
                        Status: parseResult.Normalized ? "manual_normalized" : "manual_parsed",
                        ParseMode: parseResult.ParseMode,
                        SystemPromptChars: welfareSystem.Length,
                        UserPromptChars: welfareUser.Length,
                        Text: text));
                    await replay.RecordAsync(new MinisterReplayEntry(
                        Minister: scope.Label,
                        Cycle: PlayCycleContext.ManualTrigger,
                        Path: "llm",
                        Briefing: welfareBriefing,
                        Context: welfareContext,
                        EscalationReason: "manual_llm_output_endpoint",
                        EscalationContext: new { endpoint = "/api/ministers/{minister}/llm-output/manual" },
                        GuideCitations: welfareRetrieved,
                        Advice: parseResult.Response.Advice,
                        Flags: parseResult.Response.Flags,
                        StateSummary: stateSummary,
                        LlmAttemptStarted: welfareCapturedAt,
                        OutputKind: "advice_flags",
                        Output: new { advice = parseResult.Response.Advice, flags = parseResult.Response.Flags, notes = parseResult.Response.Notes }), ct);

                    bus.ReplaceMinisterAdvice(scope.Label, parseResult.Response.Advice, stateSummary, flags: parseResult.Response.Flags);
                    foreach (AgentFlag flag in parseResult.Response.Flags)
                        flags.Publish(flag);

                    return Results.Ok(new
                    {
                        accepted = true,
                        minister = scope.Label,
                        status = parseResult.Normalized ? "manual_normalized" : "manual_parsed",
                        parse_mode = parseResult.ParseMode,
                        state_summary = stateSummary,
                        llm_state_summary = parseResult.Response.StateSummary,
                        advice_count = parseResult.Response.Advice.Count,
                        flag_count = parseResult.Response.Flags.Count,
                        dropped_flag_count = parseResult.DroppedFlagCount,
                        style_warnings = styleWarnings,
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
                        CapturedAt: welfareCapturedAt,
                        LatencyMs: 0,
                        Status: "manual_parse_failed",
                        ParseMode: "strict_json",
                        SystemPromptChars: welfareSystem.Length,
                        UserPromptChars: welfareUser.Length,
                        Text: text));
                    await replay.RecordAsync(new MinisterReplayEntry(
                        Minister: scope.Label,
                        Cycle: PlayCycleContext.ManualTrigger,
                        Path: "llm_failed",
                        Briefing: welfareBriefing,
                        Context: welfareContext,
                        EscalationReason: "manual_llm_output_endpoint",
                        EscalationContext: new { endpoint = "/api/ministers/{minister}/llm-output/manual" },
                        GuideCitations: welfareRetrieved,
                        Error: new ReplayErrorSummary(ex.GetType().Name, ex.Message),
                        LlmAttemptStarted: welfareCapturedAt), ct);
                    return Results.BadRequest(new
                    {
                        error = "Manual LLM output could not be parsed as Welfare advice JSON.",
                        detail = ex.Message
                    });
                }
            }

            FoodBriefing briefing = briefings.GetFoodBriefing();
            MinisterBriefingContext context = Chef.BuildContext(outputStore.CurrentMayorAgenda);
            IReadOnlyList<GuideCitation> retrieved = await foodRetriever.RetrieveAsync(briefing, ct);
            IReadOnlyList<FoodPromptCropCandidate> cropCandidates = Chef.BuildCropCandidates(briefing);
            string user = prompts.BuildFoodUserMessage(briefing, context, retrieved, cropCandidates);
            string system = ReadPromptOrPlaceholder(() => prompts.FoodSystemPrompt);
            DateTimeOffset capturedAt = DateTimeOffset.UtcNow;

            try
            {
                FoodLlmParseResult parseResult = FoodLlmResponseParser.Parse(text, briefing, retrieved);
                IReadOnlyList<string> styleWarnings = ManualLlmStyleWarnings(parseResult);
                string stateSummary = FoodStateSummary.Build(briefing);
                AdviceChainModel chain = FoodChainModelBuilder.Build(briefing, parseResult.Response.Advice);
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
                await replay.RecordAsync(new MinisterReplayEntry(
                    Minister: scope.Label,
                    Cycle: PlayCycleContext.ManualTrigger,
                    Path: "llm",
                    Briefing: briefing,
                    Context: context,
                    EscalationReason: "manual_llm_output_endpoint",
                    EscalationContext: new { endpoint = "/api/ministers/{minister}/llm-output/manual" },
                    GuideCitations: retrieved,
                    Advice: parseResult.Response.Advice,
                    Flags: parseResult.Response.Flags,
                    StateSummary: stateSummary,
                    Chain: chain,
                    LlmAttemptStarted: capturedAt,
                    OutputKind: "advice_flags",
                    Output: new { advice = parseResult.Response.Advice, flags = parseResult.Response.Flags }), ct);

                bus.ReplaceMinisterAdvice(scope.Label, parseResult.Response.Advice, stateSummary, chain, parseResult.Response.Flags);
                foreach (AgentFlag flag in parseResult.Response.Flags)
                    flags.Publish(flag);

                return Results.Ok(new
                {
                    accepted = true,
                    minister = scope.Label,
                    status = parseResult.Normalized ? "manual_normalized" : "manual_parsed",
                    parse_mode = parseResult.ParseMode,
                    state_summary = stateSummary,
                    llm_state_summary = parseResult.Response.StateSummary,
                    advice_count = parseResult.Response.Advice.Count,
                    flag_count = parseResult.Response.Flags.Count,
                    dropped_flag_count = parseResult.DroppedFlagCount,
                    style_warnings = styleWarnings,
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
                await replay.RecordAsync(new MinisterReplayEntry(
                    Minister: scope.Label,
                    Cycle: PlayCycleContext.ManualTrigger,
                    Path: "llm_failed",
                    Briefing: briefing,
                    Context: context,
                    EscalationReason: "manual_llm_output_endpoint",
                    EscalationContext: new { endpoint = "/api/ministers/{minister}/llm-output/manual" },
                    GuideCitations: retrieved,
                    Error: new ReplayErrorSummary(ex.GetType().Name, ex.Message),
                    LlmAttemptStarted: capturedAt), ct);
                return Results.BadRequest(new
                {
                    error = "Manual LLM output could not be parsed as Chef advice JSON.",
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
                    RuleDiagnostics: null,
                    EscalationReason: null,
                    ErrorType: null,
                    ErrorMessage: null,
                    AdviceCount: null,
                    FlagCount: null,
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

    private static FoodHuntRiskSpeciesDiagnostic BuildHuntRiskSpeciesDiagnostic(
        IGrouping<string, HuntRiskDiagnostic> group,
        AnimalDefRegistry animalDefs)
    {
        IReadOnlyList<HuntRiskDiagnostic> diagnostics = group.ToList();
        HuntRiskDiagnostic representative = diagnostics
            .OrderBy(diagnostic => RiskSort(diagnostic.Profile.Risk))
            .ThenByDescending(diagnostic => diagnostic.Profile.EstimatedNutrition ?? 0f)
            .First();
        int lowRiskCount = diagnostics.Count(diagnostic => diagnostic.Profile.Risk == "low");
        float? nutritionEach = representative.Profile.EstimatedNutrition;
        float? nutritionTotal = nutritionEach is null || lowRiskCount == 0
            ? null
            : nutritionEach.Value * lowRiskCount;
        animalDefs.DefsByName.TryGetValue(group.Key, out AnimalDefRecord? animalDef);

        return new FoodHuntRiskSpeciesDiagnostic(
            Def: group.Key,
            Label: animalDef?.Label,
            Count: diagnostics.Count,
            HealthyWildCount: diagnostics.Count(diagnostic => diagnostic.IsHealthyWild),
            LowRiskCount: lowRiskCount,
            CautionCount: diagnostics.Count(diagnostic => diagnostic.Profile.Risk == "caution"),
            DangerousCount: diagnostics.Count(diagnostic => diagnostic.Profile.Risk == "dangerous"),
            BlockedCount: diagnostics.Count(diagnostic => diagnostic.Profile.Risk == "blocked"),
            MetadataAvailable: diagnostics.Any(diagnostic => diagnostic.MetadataAvailable),
            FallbackUsed: diagnostics.Any(diagnostic => diagnostic.FallbackUsed),
            Risk: representative.Profile.Risk,
            RiskRank: representative.Profile.RiskRank,
            SortScore: HuntTypeSortScore(representative.Profile, lowRiskCount, nutritionTotal),
            IsLowRisk: representative.Profile.IsLowRisk,
            EstimatedNutritionEach: nutritionEach,
            EstimatedNutritionTotal: nutritionTotal,
            Reason: representative.Profile.Reason,
            Signals: diagnostics
                .SelectMany(diagnostic => diagnostic.Signals)
                .GroupBy(signal => signal.Key, StringComparer.OrdinalIgnoreCase)
                .Select(signalGroup => signalGroup.First())
                .ToList(),
            SampleAnimalIds: diagnostics
                .Select(diagnostic => diagnostic.AnimalId)
                .Take(8)
                .ToList());
    }

    private static int RiskSort(string risk) =>
        risk switch
        {
            "low" => 0,
            "caution" => 1,
            "dangerous" => 2,
            "blocked" => 3,
            _ => 4
        };

    private static float HuntSortScore(HuntRiskProfile profile)
    {
        int safetyScore = profile.Risk switch
        {
            "low" => 3000,
            "caution" => 2000,
            "dangerous" => 1000,
            "blocked" => 0,
            _ => 0
        };
        int riskRankScore = Math.Max(0, 4 - profile.RiskRank) * 50;
        float nutritionScore = profile.EstimatedNutrition ?? 0f;
        return safetyScore + riskRankScore + nutritionScore;
    }

    private static float HuntTypeSortScore(HuntRiskProfile profile, int lowRiskCount, float? nutritionTotal)
    {
        return HuntSortScore(profile) + (lowRiskCount * 10) + (nutritionTotal ?? 0f);
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

    private static RawLlmOutputSnapshot? LatestRawOutput(
        RawLlmOutputSnapshot? inMemory,
        RawLlmOutputSnapshot? replay) =>
        (inMemory, replay) switch
        {
            (null, null) => null,
            (RawLlmOutputSnapshot current, null) => current,
            (null, RawLlmOutputSnapshot persisted) => persisted,
            (RawLlmOutputSnapshot current, RawLlmOutputSnapshot persisted) =>
                current.CapturedAt >= persisted.CapturedAt ? current : persisted,
        };

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

    private static IReadOnlyList<string> ManualLlmStyleWarnings(FoodLlmParseResult parseResult)
    {
        List<string> warnings = [.. AdviceTextStyleWarnings.ForFood(parseResult.Response)];
        warnings.AddRange(DroppedFlagWarnings(parseResult.DroppedFlagCount));
        return warnings;
    }

    private static IReadOnlyList<string> ManualLlmStyleWarnings(WelfareLlmParseResult parseResult) =>
        DroppedFlagWarnings(parseResult.DroppedFlagCount);

    private static IReadOnlyList<string> DroppedFlagWarnings(int droppedFlagCount)
    {
        List<string> warnings = [];
        if (droppedFlagCount > 0)
        {
            string plural = droppedFlagCount == 1 ? "" : "s";
            warnings.Add($"{droppedFlagCount} flag object{plural} dropped during strict AgentFlag parsing; include the envelope and known enum tokens.");
        }

        return warnings;
    }

    private sealed record MinisterScopeInfo(
        string Key,
        string Label,
        string Kind,
        bool Ready,
        IReadOnlyList<string> EnabledViews,
        bool CanManualTrigger,
        bool CanRunRules,
        bool CanRunLlm,
        bool HasPrompt,
        bool HasRawLlmOutput,
        bool HasManualLlmOutput,
        bool HasRag)
    {
        public static MinisterScopeInfo FromDescriptor(MinisterDescriptor descriptor) =>
            new(
                descriptor.Key,
                descriptor.Label,
                descriptor.Kind,
                descriptor.Ready,
                descriptor.EnabledViews,
                descriptor.CanManualTrigger,
                descriptor.CanRunRules,
                descriptor.CanRunLlm,
                descriptor.HasPrompt,
                descriptor.HasRawLlmOutput,
                descriptor.HasManualLlmOutput,
                descriptor.HasRag);
    }

    private sealed record FoodCropMathSnapshot(
        long BriefingVersion,
        long GameTick,
        GameDate Date,
        SeasonContext Season,
        int ColonistCount,
        float? EstimatedDaysOfFood,
        string NutritionSource,
        FoodGrowingTerrainSummary GrowingTerrain,
        FoodCropCandidate? BestCandidate,
        IReadOnlyList<FoodCropCandidate> Candidates);

    private sealed record FoodHuntRiskDiagnosticsSnapshot(
        long BriefingVersion,
        long GameTick,
        GameDate Date,
        bool HasLiveState,
        string StateSource,
        int AnimalDefCount,
        int TotalAnimals,
        int HealthyWildAnimals,
        int LowRiskAnimals,
        int CautionAnimals,
        int DangerousAnimals,
        int BlockedAnimals,
        int MetadataBackedAnimals,
        int FallbackAnimals,
        FoodHuntRiskThresholds Thresholds,
        IReadOnlyList<FoodHuntRiskSpeciesDiagnostic> Species,
        IReadOnlyList<FoodHuntRiskCandidateDiagnostic> Candidates);

    private sealed record WillieRequestBoardPayload(
        string Minister,
        IReadOnlyList<WillieRequestRowPayload> Requests);

    private sealed record WillieRequestRowPayload(
        BuildingRequest Request,
        string? SourceMinister,
        long? GameTick,
        DateTimeOffset? CapturedAt,
        PlacementSolverReplayOutput? Output,
        IReadOnlyList<AdviceOption> Options)
    {
        public static WillieRequestRowPayload FromRow(WillieRequestBoardRow row)
        {
            IReadOnlyList<AdviceOption> options = row.Outcome is not null &&
                string.Equals(row.Outcome.Status, "options", StringComparison.OrdinalIgnoreCase)
                    ? row.Outcome.Options
                    : [];

            return new WillieRequestRowPayload(
                Request: row.Inbound.Request,
                SourceMinister: row.Inbound.SourceMinister,
                GameTick: row.Outcome?.GameTick,
                CapturedAt: row.Outcome?.CapturedAt,
                Output: row.Outcome?.Output,
                Options: options);
        }
    }

    private sealed record FoodHuntRiskThresholds(
        float MinimumHealthyWildHealth,
        float DangerousManhunterChance,
        float CautionManhunterChance,
        float HerdPackCautionManhunterChance,
        float DangerousBodySize,
        float CautionBodySize)
    {
        public static FoodHuntRiskThresholds Current { get; } = new(
            FoodHuntSafety.MinimumHealthyWildHealth,
            FoodHuntSafety.DangerousManhunterChance,
            FoodHuntSafety.CautionManhunterChance,
            FoodHuntSafety.HerdPackCautionManhunterChance,
            FoodHuntSafety.DangerousBodySize,
            FoodHuntSafety.CautionBodySize);
    }

    private sealed record FoodHuntRiskSpeciesDiagnostic(
        string Def,
        string? Label,
        int Count,
        int HealthyWildCount,
        int LowRiskCount,
        int CautionCount,
        int DangerousCount,
        int BlockedCount,
        bool MetadataAvailable,
        bool FallbackUsed,
        string Risk,
        int RiskRank,
        float SortScore,
        bool IsLowRisk,
        float? EstimatedNutritionEach,
        float? EstimatedNutritionTotal,
        string? Reason,
        IReadOnlyList<HuntRiskSignal> Signals,
        IReadOnlyList<string> SampleAnimalIds);

    private sealed record FoodHuntRiskCandidateDiagnostic(
        int Rank,
        string Def,
        string? Label,
        int Count,
        int RiskRank,
        float? EstimatedNutritionEach,
        float? EstimatedNutritionTotal,
        string? Reason);
}
