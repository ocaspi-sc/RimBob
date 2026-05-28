namespace RimBob.Coordination;

public sealed record MinisterDescriptor(
    string Key,
    string Label,
    string Kind,
    bool Ready,
    IReadOnlyList<string> EnabledViews,
    int? CabinetOrder = null,
    bool CanManualTrigger = false,
    bool HasPrompt = false,
    bool HasRawLlmOutput = false,
    bool HasManualLlmOutput = false,
    bool HasRag = false)
{
    public bool IsMinister => Kind.Equals("minister", StringComparison.OrdinalIgnoreCase);
}

public sealed class MinisterRegistry
{
    private static readonly IReadOnlyList<string> MinisterViews =
        ["prompt", "briefing", "rag", "rules", "raw_llm", "advice"];

    private static readonly IReadOnlyList<string> RulesOnlyMinisterViews =
        ["briefing", "rules", "advice"];

    private readonly IReadOnlyList<MinisterDescriptor> scopes =
    [
        new("system", "SYSTEM", "system", true, ["overview"]),
        new(
            Key: "mayor",
            Label: "Mayor",
            Kind: "minister",
            Ready: true,
            EnabledViews: MinisterViews,
            CabinetOrder: 20,
            CanManualTrigger: true,
            HasPrompt: true,
            HasRawLlmOutput: true,
            HasRag: true),
        new(
            Key: "food",
            Label: "Chef",
            Kind: "minister",
            Ready: true,
            EnabledViews: MinisterViews,
            CabinetOrder: 10,
            CanManualTrigger: true,
            HasPrompt: true,
            HasRawLlmOutput: true,
            HasManualLlmOutput: true,
            HasRag: true),
        new(
            Key: "willie",
            Label: "Willie",
            Kind: "minister",
            Ready: true,
            EnabledViews: RulesOnlyMinisterViews,
            CabinetOrder: 15,
            CanManualTrigger: true),
        new("defense", "Defense", "minister", false, MinisterViews),
        new("welfare", "Welfare", "minister", false, MinisterViews),
        new("medical", "Medical", "minister", false, MinisterViews),
        new("research", "Research", "minister", false, MinisterViews),
        new("industry", "Industry", "minister", false, MinisterViews),
        new("economy", "Economy", "minister", false, MinisterViews),
        new("chief_of_staff", "Chief of Staff", "minister", false, MinisterViews),
    ];

    public IReadOnlyList<MinisterDescriptor> Scopes => scopes;

    public IReadOnlyList<MinisterDescriptor> Ministers =>
        scopes.Where(scope => scope.IsMinister).ToArray();

    public IReadOnlyList<MinisterDescriptor> CabinetMinisters =>
        scopes
            .Where(scope => scope.CabinetOrder.HasValue)
            .OrderBy(scope => scope.CabinetOrder)
            .ToArray();

    public IReadOnlyList<MinisterDescriptor> ReadyMinisters =>
        scopes.Where(scope => scope.IsMinister && scope.Ready).ToArray();

    public MinisterDescriptor? Find(string keyOrLabel)
    {
        string normalized = NormalizeKey(keyOrLabel);
        return scopes.FirstOrDefault(scope =>
            scope.Key.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
            NormalizeKey(scope.Label).Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    public MinisterDescriptor? FindMinister(string keyOrLabel)
    {
        MinisterDescriptor? descriptor = Find(keyOrLabel);
        return descriptor is { IsMinister: true } ? descriptor : null;
    }

    public static string NormalizeKey(string value) =>
        value.Trim()
            .Replace(" ", "_", StringComparison.Ordinal)
            .Replace("-", "_", StringComparison.Ordinal)
            .ToLowerInvariant();
}
