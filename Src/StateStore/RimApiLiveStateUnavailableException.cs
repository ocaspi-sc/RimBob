namespace RimBob.State;

public enum RimApiLiveStateUnavailableReason
{
    NoLoadedMap
}

public sealed class RimApiLiveStateUnavailableException(
    RimApiLiveStateUnavailableReason reason,
    string message) : InvalidOperationException(message)
{
    public RimApiLiveStateUnavailableReason Reason { get; } = reason;
}
