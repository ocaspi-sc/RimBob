using RimBob.Core.Briefings;

namespace RimBob.Core.Advice;

public static class AdviceFreshness
{
    public const long TicksPerGameDay = GameTime.TicksPerGameDay;

    public static long LifetimeTicks(Priority priority) =>
        priority >= Priority.High
            ? TicksPerGameDay
            : TicksPerGameDay * 4;

    public static long ExpiresGameTick(long issuedGameTick, Priority priority) =>
        Math.Max(issuedGameTick, issuedGameTick + LifetimeTicks(priority));

    public static bool IsExpiredForGameTick(AdviceItem advice, long currentGameTick) =>
        advice.ExpiresGameTick is { } expiresGameTick &&
        currentGameTick >= expiresGameTick;
}
