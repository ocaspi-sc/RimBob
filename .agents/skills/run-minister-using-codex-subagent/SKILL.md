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
   - Read `Docs/DESIGN.md`, `Docs/TODO.md`, `Docs/design/dashboard.md`, `Docs/design/ministers.md`, and the target minister doc under `Docs/design/ministers/`.
   - Check current endpoints in `Src/ApiHost/Endpoints/MinisterEndpoints.cs`.
   - Verify Host health:
     ```powershell
     Invoke-RestMethod -Uri http://127.0.0.1:5000/api/health
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
   - Check the current raw output so you know what is being replaced:
     ```powershell
     Invoke-RestMethod -Uri http://127.0.0.1:5000/api/ministers/food/llm-output/latest
     ```

3. Ask the Codex subagent for JSON only.
   - Pass the exact system prompt, exact user prompt/briefing JSON, allowed advice types, and any current user quality constraints.
   - Require the current strict shape:
     - Top level: `{ "advice": [AdviceItem], "flags": [AgentFlag], "notes": "short trace label" }`
     - `AdviceItem`: `id`, `minister`, `advice_type`, `priority`, `title`, `body`, `rationale`, `resource_requests`, `suggested_actions`, `guide_citations`, `issued_at`, `expires_at`. Do not use old advice `severity` or `priority_score` fields in new raw output.
     - `ResourceRequest`: `kind`, `request`, `reason`, optional `quantity`, `priority`, `requested_from`, `work_type`, `skill`. Do not use old `what` / `why` keys in new raw output.
     - `SuggestedAction`: `kind`, `instruction`. Do not use old `what` keys in new raw output.
     - `AgentFlag`: `id`, `source_minister`, `severity`, `domain`, `summary`, `requests`, `detail`, `expires_at`.
   - Reject and correct loose shapes such as `type`/`summary` advice items or `flags` as an object. Run one correction pass with the actual schema before ingestion.
   - Preserve prompt constraints exactly. For Food, avoid vague wording such as `audit`, `identification`, `unclassified edible items`, and generic `note` actions when structured action kinds fit.

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
   - A good response is `accepted=true`, `status=manual_parsed` or `manual_normalized`, and nonzero `advice_count`.

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
   - Check `GET /api/status` and the dashboard at `http://localhost:5000`.

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

After starting this way, always verify with a separate `GET /api/health` before posting.

## Reporting

Report the result concisely:

- Whether the subagent output was ingested.
- Advice count, flag count, notes.
- Latest raw output provider/model/status.
- Whether the Host is still reachable and which dashboard URL to refresh.
- Any verification that stale wording is absent.
