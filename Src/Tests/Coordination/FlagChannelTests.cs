using FluentAssertions;
using RimAI.Coordination;
using RimAI.Core.Ministers;

namespace RimAI.Tests.Coordination;

public sealed class FlagChannelTests
{
    [Fact]
    public void Publish_DedupesByIdAndFiltersSeverity()
    {
        FlagChannel channel = new();
        channel.Publish(new AgentFlag("food:low", "Food", FlagSeverity.Low, "food", "low"));
        channel.Publish(new AgentFlag("food:high", "Food", FlagSeverity.High, "food", "high"));
        channel.Publish(new AgentFlag("food:high", "Food", FlagSeverity.Medium, "food", "updated"));

        channel.Active(FlagSeverity.Medium).Should().ContainSingle()
            .Which.Summary.Should().Be("updated");
    }
}
