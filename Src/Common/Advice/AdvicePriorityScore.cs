namespace RimAI.Core.Advice;

public static class AdvicePriorityScore
{
    public const int Min = 1;
    public const int Max = 10;
    public const int Default = 5;

    public static int Normalize(int value, AdviceSeverity severity) =>
        value <= 0 ? DefaultForSeverity(severity) : Math.Clamp(value, Min, Max);

    public static int DefaultForSeverity(AdviceSeverity severity) => severity switch
    {
        AdviceSeverity.Critical => 10,
        AdviceSeverity.High => 8,
        AdviceSeverity.Medium => 5,
        AdviceSeverity.Low => 3,
        _ => Default
    };
}
