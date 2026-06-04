using FluentAssertions;
using RimBob.Coordination;
using RimBob.Core.Advice;
using RimBob.Core.Ministers;

namespace RimBob.Tests.Coordination;

public sealed class FlagChannelTests
{
    [Fact]
    public void Publish_DedupesByIdAndFiltersSeverity()
    {
        FlagChannel channel = new();
        channel.Publish(new AgentFlag("food:low", "Chef", Priority.Low, "food", "low"));
        channel.Publish(new AgentFlag("food:high", "Chef", Priority.High, "food", "high"));
        channel.Publish(new AgentFlag("food:high", "Chef", Priority.Medium, "food", "updated"));

        channel.Active(Priority.Medium).Should().ContainSingle()
            .Which.Summary.Should().Be("updated");
    }
}
