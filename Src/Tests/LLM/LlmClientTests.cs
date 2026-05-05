using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RimAI.LLM;

namespace RimAI.Tests.LLM;

[Collection(nameof(LlmClientTestCollection))]
public sealed class LlmClientTests
{
    [Fact]
    public async Task PingAsync_ReturnsTrue_WhenExecutorSucceeds()
    {
        var sut = new LlmClient(
            NullLogger<LlmClient>.Instance,
            _ => Task.FromResult<string?>("pong"));

        var result = await sut.PingAsync(CancellationToken.None);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PingAsync_ReturnsFalse_WhenReplyMissingOrWhitespace(string? reply)
    {
        var sut = new LlmClient(
            NullLogger<LlmClient>.Instance,
            _ => Task.FromResult<string?>(reply));

        var result = await sut.PingAsync(CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task PingAsync_ReturnsFalse_WhenExecutorThrows()
    {
        var sut = new LlmClient(
            NullLogger<LlmClient>.Instance,
            _ => Task.FromException<string?>(new InvalidOperationException("boom")));

        var result = await sut.PingAsync(CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Constructor_DoesNotThrow_AndPingReturnsFalse_WhenGeminiKeyMissing()
    {
        const string keyName = "GEMINI_API_KEY";
        var previous = Environment.GetEnvironmentVariable(keyName);
        try
        {
            Environment.SetEnvironmentVariable(keyName, null);

            var sut = new LlmClient(NullLogger<LlmClient>.Instance);

            sut.IsConfigured.Should().BeFalse();
            (await sut.PingAsync(CancellationToken.None)).Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable(keyName, previous);
        }
    }
}
