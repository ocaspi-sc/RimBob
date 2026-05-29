namespace RimBob.Ministers.Willie;

public sealed record ScoreWeights(double WalkablePathCost)
{
    public static ScoreWeights Default { get; } = new(WalkablePathCost: 16);
}
