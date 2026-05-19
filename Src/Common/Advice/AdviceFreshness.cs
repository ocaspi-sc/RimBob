namespace RimBob.Core.Advice;

public static class AdviceFreshness
{
    public const long TicksPerGameDay = 60_000;

    public static long LifetimeTicks(AdvicePriority priority) =>
        priority >= AdvicePriority.High
            ? TicksPerGameDay
            : TicksPerGameDay * 4;

    public static long ExpiresGameTick(long issuedGameTick, AdvicePriority priority) =>
        Math.Max(issuedGameTick, issuedGameTick + LifetimeTicks(priority));

    public static bool IsExpiredForGameTick(AdviceItem advice, long currentGameTick) =>
        advice.ExpiresGameTick is { } expiresGameTick &&
        currentGameTick >= expiresGameTick;
}
