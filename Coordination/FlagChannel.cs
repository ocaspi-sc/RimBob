namespace RimAI.Coordination;

/// <summary>
/// Flag emission, routing, severity tiers, and expiry.
/// Ministers emit flags here; the Chief of Staff reads and routes them.
/// Ministers never communicate directly — only through this channel.
///
/// Full spec: design/communication.md
/// Full implementation: M3 slice.
/// </summary>
public sealed class FlagChannel
{
    // Placeholder — implemented in M3.
}
