using FluentAssertions;
using RimBob.Host;

namespace RimBob.Tests.ApiHost;

public sealed class PromptInspectorCacheTests
{
    [Fact]
    public async Task GetOrCreateAsync_ReusesCachedPromptForSameKey()
    {
        PromptInspectorCache cache = new(TimeSpan.FromMinutes(1));
        int calls = 0;

        PromptInspectorPayload first = await cache.GetOrCreateAsync(
            "mayor:1",
            refresh: false,
            _ =>
            {
                calls++;
                return Task.FromResult(new PromptInspectorPayload("system", $"user-{calls}"));
            },
            CancellationToken.None);
        PromptInspectorPayload second = await cache.GetOrCreateAsync(
            "mayor:1",
            refresh: false,
            _ =>
            {
                calls++;
                return Task.FromResult(new PromptInspectorPayload("system", $"user-{calls}"));
            },
            CancellationToken.None);

        first.User.Should().Be("user-1");
        second.User.Should().Be("user-1");
        calls.Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateAsync_RefreshBypassesCachedPrompt()
    {
        PromptInspectorCache cache = new(TimeSpan.FromMinutes(1));
        int calls = 0;

        await cache.GetOrCreateAsync(
            "food:1",
            refresh: false,
            _ =>
            {
                calls++;
                return Task.FromResult(new PromptInspectorPayload("system", $"user-{calls}"));
            },
            CancellationToken.None);
        PromptInspectorPayload refreshed = await cache.GetOrCreateAsync(
            "food:1",
            refresh: true,
            _ =>
            {
                calls++;
                return Task.FromResult(new PromptInspectorPayload("system", $"user-{calls}"));
            },
            CancellationToken.None);

        refreshed.User.Should().Be("user-2");
        calls.Should().Be(2);
    }
}
