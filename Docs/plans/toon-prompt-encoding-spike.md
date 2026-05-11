# TOON Prompt Encoding Spike

Evaluate TOON as an optional LLM prompt-input encoding, not as a replacement for RimAI's JSON contracts.

## Scope

- Keep JSON as the canonical format for APIs, SSE, fixtures, JSONL logs, embedding cache, and LLM output parsing.
- Add or prototype a `PromptEncoding = Json | Toon` path around `MayorPromptPayload`.
- Benchmark the current compact JSON prompt against TOON on existing Mayor fixtures.
- Measure token count, prompt readability, output JSON validity, and agenda quality.
- Adopt TOON only if it provides meaningful token savings without weakening schema adherence.

## Notes

- TOON is strongest for uniform arrays of primitive-object rows.
- The current `MayorBriefing` is mostly nested objects with some arrays, so savings may be modest.
- Avoid taking a production dependency on `toon-dotnet` until the .NET implementation is stable enough for our use.
