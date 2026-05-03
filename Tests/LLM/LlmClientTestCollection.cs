using Xunit;

namespace RimAI.Tests.LLM;

/// <summary>
/// Serializes LLM-related tests that mutate process environment variables.
/// </summary>
[CollectionDefinition(nameof(LlmClientTestCollection), DisableParallelization = true)]
public sealed class LlmClientTestCollection;
