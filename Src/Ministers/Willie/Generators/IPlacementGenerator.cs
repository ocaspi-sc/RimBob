namespace RimBob.Ministers.Willie;

public interface IPlacementGenerator
{
    string Id { get; }

    /// <summary>
    /// Deterministic contract: same spec, evidence, and budget must produce the same ordered drafts with no ambient RNG.
    /// </summary>
    IReadOnlyList<PlacementDraft> Generate(
        PlacementSpec spec,
        PlacementEvidence evidence,
        GenerationBudget budget);
}
