# RimBob - Knowledge Base and RAG

> **Living document.** See `AGENTS.md` for update rules.
> This doc records retrieval policy and guide-corpus decisions. Exact classes,
> config keys, chunking implementation, embedding model names, cache files, and
> API payloads live in source and tests.

---

## Purpose

Ground minister reasoning in actual RimWorld community knowledge rather than
relying on LLM training data alone. Game mechanics and community-optimal
strategies are ingested from curated guide docs and retrieved when an LLM
escalation needs them.

RAG supports judgment. It must not replace live state, deterministic rules, or
briefing-derived facts.

---

## Two-Tier Content Strategy

### Tier 1: System Prompt

Evergreen strategic knowledge can live in minister system prompts when it is
stable, compact, and broadly useful.

Examples:

- Food: crop choice principles, seasonal priorities, food math.
- Defense: raid benchmarks, killbox principles, weapon guidance.
- Construction: build order, materials, power heuristics.
- Welfare: Mood & Needs principles, including mood modifiers, thoughts,
  recreation, schedules, and comfort.
- Mayor: early colony strategic frame.

Tier 1 should stay concise. Do not paste whole guides into prompts.

### Tier 2: Retrieval

Specific, situational, or long-tail knowledge is retrieved at LLM call time.

Examples:

- Infestations, mechanoid clusters, toxic fallout.
- DLC-specific mechanics.
- Edge-case crop decisions.
- Unusual raid compositions.
- Detailed guide snippets that would bloat a system prompt.

---

## Retrieval Flow

At design level:

1. State store derives a minister briefing.
2. Rules decide whether they can handle the situation.
3. If escalation needs guide knowledge, the retriever builds a query from live
   facts, rule directives, and minister context.
4. Relevant guide chunks are passed into the LLM prompt.
5. Returned advice may cite guide ids.
6. The server stamps retriever-provided citation metadata onto the final output
   so the dashboard can render evidence.

Exact retriever classes, chunk metadata, embedding calls, and cache behavior are
implementation details.

---

## Store And Cache Policy

The current guide corpus is small enough for an in-process cosine store. Avoid
external vector infrastructure until corpus size, latency, or tooling demands
it.

Embedding cache behavior must be deterministic and debuggable: unchanged guide
text should not be re-embedded on every run, and stale cache handling should be
visible in diagnostics.

The embedding cache is durable Host runtime data, not a branch artifact. By
default it lives under the stable machine-local data root at `embeddings/`;
`RimBob:DataRoot` may move all persistent runtime data, and
`RimBob:Rag:CacheRoot` may override only the embedding cache. Relative
`RimBob:Rag:CacheRoot` values resolve under the stable data root. SYSTEM should
show the resolved cache root alongside RAG health so operators can verify it is
not tied to the active checkout or worktree.

If the corpus grows large enough that local retrieval becomes slow or awkward,
revisit Qdrant, sqlite-vss, or another vector backend behind the same design
contract.

---

## Guide Corpus

The guide corpus lives under `Docs/guides/`. Source code should treat guides as
repo docs/content, not as C# infrastructure.

Each minister session should identify which guides matter for that minister and
add them deliberately. Suggested areas:

| Minister | Guide areas |
|---|---|
| Food | crop tables, food math, freezer/cooking policy |
| Defense | killboxes, raids, mechanoids, weapons |
| Construction | power math, room stats, biome-specific build concerns |
| Welfare | Mood & Needs: mood modifiers, thoughts, recreation, schedules, comfort |
| Mayor | general strategy, wealth pressure, early-game pacing |

Guide provenance and freshness matter. When a guide is copied or summarized,
record enough source context that a future agent can refresh or challenge it.

---

## Retrieval Per Minister

Each minister may have a retrieval profile: topic filters, a query strategy,
result count, and whether retrieval is required for a particular escalation.

Rules evaluations do not need RAG. Retrieval belongs on the LLM escalation path
unless a specific rule is intentionally asking for guide-backed judgment.

---

## Guide-Knowledge Escalation

Rules can decide that a situation needs guide knowledge rather than firing a
local heuristic. The escalation reason should make the missing judgment clear,
for example crop timing, unusual threat response, or biome-specific recovery.

The LLM may use guide context to explain or choose among options, but it must
not invent live facts that the briefing did not provide.

---

## M2 Resolved Design

- Mayor RAG is live through Tier 2 retrieval.
- Retrieved guide context is passed to the Mayor prompt.
- Server-stamped guide citations give the dashboard enough evidence to render
  source snippets even when the LLM omits citations.
- Missing or disabled retrieval should degrade cleanly: the Mayor can still run
  without guide context.
- Tier 1 prompt distillation remains a follow-up if prompt traces show the Mayor
  underusing retrieved passages.

---

## Open Questions

- [ ] How do we flag stale guide content after RimWorld updates?
- [ ] Who approves minister-specific guide additions?
- [ ] When should evergreen knowledge move from retrieval into a system prompt?
- [ ] What corpus size or latency threshold justifies a vector-store upgrade?
