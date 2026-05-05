# RimAI — Knowledge Base and RAG

> **Living document.** See `CLAUDE.md` for update rules.

---

## Purpose

Ground minister reasoning in actual RimWorld community knowledge rather than relying on LLM training data alone. Game mechanics and community-optimal strategies are ingested once at startup and retrieved at LLM call time.

---

## Two-tier content strategy

**Tier 1 — System prompt (evergreen content)**
Distilled, stable strategic knowledge baked into each minister's system prompt. Cached at the LLM API layer (prompt caching). Does not hit the vector store.

- Agriculture: crop choice rules, seasonal priorities, food math
- Defense: raid tier benchmarks, killbox principles, weapon recommendations
- Construction: build order heuristics, material choices, power math
- Welfare: mood modifier sources, recreation building efficiency
- Mayor: two-year strategic plan (Y1-Y2 guide)

**Tier 2 — RAG retrieval (long-tail)**
Specific, situational, or detailed content retrieved at call time. Used when the situation likely needs guide knowledge that isn't in the system prompt.

- Specific event handling (infestations, mechanoid clusters, toxic fallout)
- DLC-specific mechanics (Royalty quests, Biotech genes, Ideology rituals)
- Edge-case farm decisions (devilstrand timing, drug crop yield math)
- Defense against unusual raid compositions

---

## Implementation

### In-process cosine store

No vector DB framework. Small corpus; simple store is sufficient and debuggable.

```csharp
public class KnowledgeBase
{
    private List<Chunk> _chunks;

    record Chunk(float[] Embedding, string Text, ChunkMetadata Meta);

    public IEnumerable<string> Retrieve(float[] queryEmbedding, int topK = 5)
        => _chunks
            .OrderByDescending(c => CosineSimilarity(c.Embedding, queryEmbedding))
            .Take(topK)
            .Select(c => c.Text);

    static float CosineSimilarity(float[] a, float[] b) { ... }
}
```

On-disk JSON cache of embeddings keyed by chunk hash (SHA-256 of text content). Re-runs don't re-embed unless content changes.

### Upgrade path

If corpus exceeds ~5,000 chunks or query latency becomes noticeable, swap `KnowledgeBase` backend to Qdrant or sqlite-vss. Interface stays the same; only the implementation changes.

---

## Guide corpus

```
Docs/guides/
├── strategic-plan-y1-y2.md          // Y1-Y2 colony strategy, distilled
└── beginner/
    ├── beginner-survival-tips.md     // Steam guide (1.4, 2023) — colonist setup, research path
    ├── survival-tactics.md           // rimworldhub.com — first 15 min, base layout, mood breaks
    ├── wealth-management.md          // gamepadsquire.com — raid-point math, trade beacons, 60/140 rule
    ├── killbox-design.md             // thegamer.com — 10 killbox design principles
    ├── first-steps.md                // bisecthosting.com — scenario selection, 8-step build order
    ├── tips-and-tricks.md            // blogs.plitch.com — 11 tips
    └── failed-fetches.md             // wiki URLs blocked by 403 — copy manually
```

`Ingest.cs` points to this path via config — `Src/KnowledgeBase/` stays pure C# infrastructure.

Each minister session should identify which guides are most relevant and add them. Suggested additions:

| Minister | Guides to add |
|---|---|
| Agriculture | RimWorld wiki crop tables, food math guide |
| Defense | Killbox guide, raid composition wiki, mechanoid guide |
| Construction | Power math, room stats (beauty, cleanliness), biome-specific tips |
| Welfare | Mood modifier reference, recreation building guide |
| Mayor | General strategy tier list, wealth management wiki page |

---

## Retrieval per minister

Each minister has a retrieval profile — a query template and topic filter:

```csharp
public class RetrievalProfile
{
    public string[] TopicFilters  { get; }  // e.g. ["food", "farming", "hunting"]
    public string   QueryTemplate { get; }  // "{situation} in {season}, what should I do?"
    public int      TopK          { get; }  // default 3
    public bool     RequiredForEscalation { get; }  // skip retrieval on rules path
}
```

Retrieval only runs on the LLM escalation path. Rules evaluations don't use RAG.

---

## When a rule needs guide knowledge

A rule can flag that it needs guide knowledge before it can fire:

```csharp
// In Agriculture Rules.cs
if (briefing.Season == Season.Fall && briefing.DaysToWinter < 15)
{
    // This decision benefits from guide knowledge — escalate with context
    return new Escalate(
        reason: "First devilstrand harvest decision — guide knowledge needed",
        context: new { briefing.DevilstrandGrowth, briefing.DaysToWinter }
    );
}
```

The escalation reason flags that RAG retrieval for "devilstrand harvest timing" should be included in the LLM prompt.

---

## Open questions

- [ ] Embedding model: Gemini embeddings (`text-embedding-004` / `gemini-embedding-001`) or a local model (e.g., `sentence-transformers` via Python sidecar)? Local is cheaper at ingestion; Gemini is simpler to wire.
- [ ] Chunking strategy: fixed-size (512 tokens) vs. semantic (by heading/paragraph)? Semantic is better quality; fixed is simpler.
- [ ] Guide freshness: RimWorld updates change mechanics. How do we flag stale guide content?
- [ ] Per-minister guide curation: who decides which guides are relevant? Add to each minister's session scope.
