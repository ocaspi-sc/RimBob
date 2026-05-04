namespace RimAI.Core.Ministers;

/// <summary>
/// Mayor posture propagated downward to all ministers as shared context.
/// This is NOT a message — it's a read-only view ministers consult when evaluating rules.
/// </summary>
public record ColonyContext(
    string WealthPolicy,      // freeze | grow_slow | grow | trim
    string ExpansionPolicy,   // halt | maintain | expand
    string DefensePriority,   // low | normal | elevated | critical
    string? ColonyObjective,  // survive_year_one | establish_engine | etc.
    IReadOnlyDictionary<string, int> MinisterBiases // minister name → priority bias [-3, +3]
)
{
    public static ColonyContext Default { get; } = new(
        WealthPolicy: "grow_slow",
        ExpansionPolicy: "maintain",
        DefensePriority: "normal",
        ColonyObjective: "survive_year_one",
        MinisterBiases: new Dictionary<string, int>()
    );

    public int GetBias(string ministerName) =>
        MinisterBiases.TryGetValue(ministerName, out var bias) ? bias : 0;
}
