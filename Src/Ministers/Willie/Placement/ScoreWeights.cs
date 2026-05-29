namespace RimBob.Ministers.Willie;

public sealed record ScoreWeights(
    double WalkablePathCost,
    double GeneratorConfidence,
    double DiversityBonus,
    double MaterialCost)
{
    public static ScoreWeights Default { get; } = new(
        WalkablePathCost: 16,
        GeneratorConfidence: 2,
        DiversityBonus: 3,
        MaterialCost: 2);
}
