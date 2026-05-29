using RimBob.Core.Advice;
using RimBob.Core.Placement;

namespace RimBob.Ministers.Willie;

public sealed class PathCostLookup
{
    private readonly IReadOnlyDictionary<int, IReadOnlyList<PathCostLookupEntry>> entriesByDraftIndex;

    private PathCostLookup(
        bool probeAvailable,
        IReadOnlyDictionary<int, IReadOnlyList<PathCostLookupEntry>> entriesByDraftIndex,
        IReadOnlyDictionary<int, string> draftKeysByPairIndex)
    {
        ProbeAvailable = probeAvailable;
        this.entriesByDraftIndex = entriesByDraftIndex;
        DraftKeysByPairIndex = draftKeysByPairIndex;
    }

    public bool ProbeAvailable { get; }

    public IReadOnlyDictionary<int, string> DraftKeysByPairIndex { get; }

    public static IReadOnlyList<PathCostPair> RequestPairsFor(IReadOnlyList<PlacementDraft> drafts) =>
        drafts
            .SelectMany(draft => draft.AccessCells
                .Select(cell => new PathCostPair(cell, draft.SourceAnchor.TargetCell.ToMapCell())))
            .ToList();

    public static PathCostLookup FromProbeResults(
        IReadOnlyList<PlacementDraft> drafts,
        IReadOnlyList<PathCostResult> results)
    {
        Dictionary<int, List<PathCostLookupEntry>> entriesByDraft = [];
        Dictionary<int, string> draftKeysByPairIndex = [];
        int pairIndex = 0;

        for (int draftIndex = 0; draftIndex < drafts.Count; draftIndex++)
        {
            PlacementDraft draft = drafts[draftIndex];
            string draftKey = DraftKey(draft);
            MapCell target = draft.SourceAnchor.TargetCell.ToMapCell();

            foreach (MapCell accessCell in draft.AccessCells)
            {
                PathCostResult? result = pairIndex < results.Count
                    ? results[pairIndex]
                    : null;
                draftKeysByPairIndex[pairIndex] = draftKey;

                if (!entriesByDraft.TryGetValue(draftIndex, out List<PathCostLookupEntry>? entries))
                {
                    entries = [];
                    entriesByDraft[draftIndex] = entries;
                }

                entries.Add(new PathCostLookupEntry(
                    PairIndex: pairIndex,
                    DraftIndex: draftIndex,
                    DraftKey: draftKey,
                    From: accessCell,
                    To: target,
                    Result: result));
                pairIndex++;
            }
        }

        return new PathCostLookup(
            probeAvailable: true,
            entriesByDraftIndex: entriesByDraft.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<PathCostLookupEntry>)pair.Value),
            draftKeysByPairIndex: draftKeysByPairIndex);
    }

    public static PathCostLookup ProbeUnavailable(IReadOnlyList<PlacementDraft> drafts)
    {
        Dictionary<int, string> draftKeysByPairIndex = [];
        int pairIndex = 0;
        foreach (PlacementDraft draft in drafts)
        {
            string draftKey = DraftKey(draft);
            foreach (MapCell _ in draft.AccessCells)
            {
                draftKeysByPairIndex[pairIndex] = draftKey;
                pairIndex++;
            }
        }

        return new PathCostLookup(
            probeAvailable: false,
            entriesByDraftIndex: new Dictionary<int, IReadOnlyList<PathCostLookupEntry>>(),
            draftKeysByPairIndex: draftKeysByPairIndex);
    }

    public IReadOnlyList<PathCostLookupEntry> EntriesForDraft(int draftIndex) =>
        entriesByDraftIndex.TryGetValue(draftIndex, out IReadOnlyList<PathCostLookupEntry>? entries)
            ? entries
            : [];

    private static string DraftKey(PlacementDraft draft)
    {
        int minX = draft.Group.Assets.Count == 0
            ? 0
            : draft.Group.Assets.Min(asset => asset.Cell.X);
        int minZ = draft.Group.Assets.Count == 0
            ? 0
            : draft.Group.Assets.Min(asset => asset.Cell.Z);
        return $"{draft.GeneratorId}|{draft.SourceAnchor.Anchor.RoomId}|{minX},{minZ}|{draft.Group.Assets.Count}";
    }
}

public sealed record PathCostLookupEntry(
    int PairIndex,
    int DraftIndex,
    string DraftKey,
    MapCell From,
    MapCell To,
    PathCostResult? Result);
