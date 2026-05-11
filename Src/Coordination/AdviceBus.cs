using RimAI.Core.Advice;

namespace RimAI.Coordination;

/// <summary>
/// In-process pub/sub for advice events. Host bridges this onto the SSE feed.
/// Synchronous invocation; SSE handlers internally queue via a bounded channel.
/// </summary>
public sealed class AdviceBus
{
    public event Action<AgendaUpdated>? AgendaUpdated;
    public event Action<AdviceItem>?    AdvicePublished;

    public void Publish(AgendaUpdated e) => AgendaUpdated?.Invoke(e);
    public void Publish(AdviceItem item) => AdvicePublished?.Invoke(item);
}

public sealed record AgendaUpdated(MayorAgenda Agenda);
