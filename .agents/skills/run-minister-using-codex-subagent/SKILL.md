---
name: run-minister-using-codex-subagent
description: Run a RimAI minister's LLM/manual fallback using a Codex subagent instead of Gemini. Use when the user explicitly asks to generate minister output with a Codex subagent, bypass Gemini quota/network failures, run the manual LLM step, or paste/ingest manually generated minister advice through `/api/ministers/{minister}/llm-output/manual`.
---

# Run Minister Using Codex Subagent

Generate a minister LLM response with a Codex subagent, ingest it through RimAI's manual raw-output endpoint, and verify the dashboard sees it. This is a developer fallback for provider failures; it remains suggest-only and must not call RIMAPI write endpoints.

## Preconditions

- Only spawn a subagent when the current user request explicitly asks for a Codex subagent, delegated agent, or manual subagent generation.
- Default target is `Food`. For other ministers, first inspect whether `/api/ministers/{minister}/llm-output/manual` exists. If not wired, report that rather than pretending the manual path exists.
- Read the target minister prompt and briefing from the running Host; do not reconstruct them from memory.

## Workflow

1. Ground in repo and runtime truth.
   - Read `Docs/DESIGN.md`, `HumanTodo.md`, `Docs/design/dashboard.md`, `Docs/design/ministers.md`, and the target minister doc under `Docs/design/ministers/`.
   - Check current endpoints in `Src/ApiHost/Endpoints/MinisterEndpoints.cs`.
   - Verify Host health:
     ```powershell
     Invoke-RestMethod -Uri http://127.0.0.1:5000/api/system/health
     ```
   - If Host is not running, prefer:
     ```powershell
     powershell.exe -ExecutionPolicy Bypass -File .\run-rimai.ps1 -SkipDashboardBuild -NoRestore
     ```

2. Capture the exact minister inputs.
   - Get the prompt:
     ```powershell
     Invoke-RestMethod -Uri http://127.0.0.1:5000/api/ministers/food/prompt
     ```
   - Get the latest briefing:
     ```powershell
     Invoke-RestMethod -Uri http://127.0.0.1:5000/api/briefings/food/latest
     ```
   - Capture the current raw output before posting so you can compare before/after:
     ```powershell
     Invoke-RestMethod -Uri http://127.0.0.1:5000/api/ministers/food/llm-output/latest
     ```
   - Keep a compact previous-output summary for reporting:
     - provider, model, status, parseMode, capturedAt, text length.
     - If the raw text parses as JSON, note advice ids/titles/types/priorities, flag ids/severities/summaries, and notes.
     - If it does not parse as JSON, note only provider/model/status and a short reason.

3. Ask the Codex subagent for JSON only.
   - Pass the exact live system prompt, exact user prompt/briefing JSON, allowed advice types, and any current user quality constraints.
   - Treat the live system prompt as the source of truth for target-minister JSON shape, field names, allowed values, and writing style. Do not restate or override those requirements from memory.
   - Require JSON only, with no Markdown fences or explanatory prose.
   - Reject and correct obvious stale schema before ingestion, especially old `what`/`why` action keys, old advice `severity`/`priority_score` fields, `type`/`summary` advice items, or `flags` as an object.
   - Run one correction pass with the exact live prompt if the output conflicts with the prompt, uses loose schema, or contains stale Food wording.
   - Treat endpoint `style_warnings` as a failed quality pass unless the wording is intentionally longer for safety.

4. Ingest the raw JSON.
   - Post the subagent's raw JSON object, not a normalized rewrite, unless you had to correct schema validity.
   - Use:
     ```powershell
     $raw = '<json from subagent>'
     Invoke-RestMethod `
       -Method Post `
       -Uri http://127.0.0.1:5000/api/ministers/food/llm-output/manual `
       -ContentType 'application/json' `
       -Body $raw
     ```
   - A good response is `accepted=true`, `status=manual_parsed` or `manual_normalized`, nonzero `advice_count`, and empty or intentionally accepted `style_warnings`.

5. Verify from a separate read.
   - Check raw output:
     ```powershell
     Invoke-RestMethod -Uri http://127.0.0.1:5000/api/ministers/food/llm-output/latest
     ```
   - Confirm:
     - `provider` is `Codex`.
     - `model` is `codex-subagent`.
     - `status` is `manual_parsed` or `manual_normalized`.
     - `parseMode` matches the ingestion response.
     - The raw text contains the intended corrected wording and does not contain known-bad stale wording.
   - Compare the verified latest output to the previous-output summary captured before ingestion:
     - provider/model/status/parseMode changes.
     - advice count, ids, titles, advice_type, and priority changes.
     - flag count, ids, severity, and summary changes.
     - notes changes.
     - wording changes that matter to the user request, especially removed stale wording or newly introduced concrete actions.
   - Treat the comparison as part of the deliverable; do not only report that ingestion succeeded.
   - Check `GET /api/system/health` and the dashboard at `http://localhost:5000`.

## Host Persistence Notes

`RawLlmOutputStore` and active advice are in-memory. If the Host exits after the POST, the dashboard loses the manual output.

If a sandboxed `Start-Process` child exits with the shell job, start the Host through a persistent Windows process after requesting approval if needed:

```powershell
$cmd = '"C:\dev\RimAI\Src\ApiHost\bin\Debug\net9.0\RimAI.Host.exe"'
Invoke-CimMethod `
  -ClassName Win32_Process `
  -MethodName Create `
  -Arguments @{ CommandLine = $cmd; CurrentDirectory = 'C:\dev\RimAI\Src\ApiHost' }
```

After starting this way, always verify with a separate `GET /api/system/health` before posting.

## Reporting

Report the result concisely:

- Whether the subagent output was ingested.
- Advice count, flag count, notes.
- Any `style_warnings`; if present, report whether you corrected them or why they were accepted.
- Latest raw output provider/model/status.
- What changed compared with the previous raw output: advice items, flags, priority/type shifts, notes, and relevant wording changes.
- Whether the Host is still reachable and which dashboard URL to refresh.
- Any verification that stale wording is absent.
