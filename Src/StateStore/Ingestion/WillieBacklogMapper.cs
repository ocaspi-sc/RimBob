using System.Globalization;
using RimBob.Core.Aggregates;
using RimBob.Ingestion.Dtos;

namespace RimBob.State;

public static class WillieBacklogMapper
{
    public static WillieConstructionBacklog FromConstructionBacklog(
        IReadOnlyList<ConstructionBacklogGroupDto> groups) =>
        new(groups.Select(MapGroup).ToList())
        {
            SourceAvailable = true
        };

    private static WillieBacklogGroup MapGroup(ConstructionBacklogGroupDto group) =>
        new(
            Kind: group.Kind ?? "",
            DefName: group.DefName ?? "",
            StuffDefName: group.StuffDefName,
            Allowed: group.Allowed,
            Count: group.Count,
            ThingIds: group.ThingIds
                .Select(id => id.ToString(CultureInfo.InvariantCulture))
                .ToList(),
            SampleCells: group.SampleCells
                .Select(cell => new MapPosition(cell.X, 0, cell.Z))
                .ToList(),
            TotalWorkLeft: group.TotalWorkLeft,
            Cost: group.Cost.Select(ToMaterialCount).ToList(),
            MaterialsAvailable: group.MaterialsAvailable
                .Select(item => ToAvailabilityCount(item, item.Available))
                .ToList(),
            MaterialsMissing: group.MaterialsMissing
                .Select(item => ToAvailabilityCount(item, item.Missing))
                .ToList(),
            BlockedCount: group.BlockedCount,
            DisallowedCount: group.DisallowedCount);

    private static MaterialCount ToMaterialCount(ConstructionMaterialCountDto dto) =>
        new(dto.DefName ?? "", dto.Count);

    private static MaterialCount ToAvailabilityCount(
        ConstructionMaterialAvailabilityDto dto,
        int count) =>
        new(dto.DefName ?? "", count)
        {
            Required = dto.Required,
            Available = dto.Available,
            Missing = dto.Missing
        };
}
