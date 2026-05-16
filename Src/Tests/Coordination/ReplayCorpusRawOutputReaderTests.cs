using FluentAssertions;
using RimAI.Coordination;
using RimAI.LLM;

namespace RimAI.Tests.Coordination;

public sealed class ReplayCorpusRawOutputReaderTests
{
    [Fact]
    public async Task Latest_ReturnsNewestRawLlmOutputForMinister()
    {
        string directory = NewTempRoot();
        try
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "food-20260517.jsonl");
            await File.WriteAllLinesAsync(path,
            [
                """
                {"schema_version":2,"captured_at":"2026-05-17T01:00:00+00:00","minister":"Food","path":"rules","llm":null}
                """,
                """
                {"schema_version":2,"captured_at":"2026-05-17T01:01:00+00:00","minister":"Food","path":"llm","llm":{"provider":"Gemini","model":"gemini-2.5-flash","captured_at":"2026-05-17T01:01:01+00:00","system_prompt_chars":10,"user_prompt_chars":20,"status":"parsed","parse_mode":"strict_json","latency_ms":42,"raw_output":"{\"advice\":[\"old\"]}","api_key_index":1,"api_key_label":"primary"}}
                """,
                """
                {"schema_version":2,"captured_at":"2026-05-17T01:02:00+00:00","minister":"Food","path":"llm","llm":{"provider":"Codex","model":"codex-subagent","captured_at":"2026-05-17T01:02:01+00:00","system_prompt_chars":11,"user_prompt_chars":21,"status":"manual_parsed","parse_mode":"strict_json","latency_ms":0,"raw_output":"{\"advice\":[\"new\"]}","api_key_index":null,"api_key_label":null}}
                """
            ]);

            ReplayCorpusRawOutputReader reader = new(directory);

            RawLlmOutputSnapshot? snapshot = reader.Latest("Food");

            snapshot.Should().NotBeNull();
            snapshot!.Provider.Should().Be("Codex");
            snapshot.Model.Should().Be("codex-subagent");
            snapshot.Status.Should().Be("manual_parsed");
            snapshot.Text.Should().Be("{\"advice\":[\"new\"]}");
            snapshot.SystemPromptChars.Should().Be(11);
            snapshot.UserPromptChars.Should().Be(21);
        }
        finally
        {
            DeleteTempRoot(directory);
        }
    }

    [Fact]
    public async Task Latest_IgnoresOtherMinistersAndRecordsWithoutRawOutput()
    {
        string directory = NewTempRoot();
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllLinesAsync(Path.Combine(directory, "food-20260517.jsonl"),
            [
                """
                {"schema_version":2,"captured_at":"2026-05-17T01:00:00+00:00","minister":"Mayor","path":"llm","llm":{"raw_output":"{\"agenda\":true}"}}
                """,
                """
                {"schema_version":2,"captured_at":"2026-05-17T01:01:00+00:00","minister":"Food","path":"llm_failed","llm":{"raw_output":null}}
                """
            ]);

            ReplayCorpusRawOutputReader reader = new(directory);

            reader.Latest("Food").Should().BeNull();
        }
        finally
        {
            DeleteTempRoot(directory);
        }
    }

    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), "rimai-replay-raw-tests", Guid.NewGuid().ToString("N"));

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
