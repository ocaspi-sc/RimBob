namespace RimBob.State;

public static class ColonyStateFreeze
{
    public static ColonyState Capture(ColonyState live)
    {
        ColonyState frozen = new()
        {
            LastRefreshSource = live.LastRefreshSource,
            LastLiveRefreshAt = live.LastLiveRefreshAt
        };

        // WHY: aggregate values are immutable records, so a reference-copy freeze
        // gives background readers a stable view without serializing the map.
        frozen.Map.Update(live.Map.Value);
        frozen.Economy.Update(live.Economy.Value);
        frozen.Colonists.Update(live.Colonists.Value);
        frozen.Rooms.Update(live.Rooms.Value);
        frozen.Stockpiles.Update(live.Stockpiles.Value);
        frozen.Zones.Update(live.Zones.Value);
        frozen.Areas.Update(live.Areas.Value);
        frozen.Buildings.Update(live.Buildings.Value);
        frozen.WorkTables.Update(live.WorkTables.Value);
        frozen.Power.Update(live.Power.Value);
        frozen.Threats.Update(live.Threats.Value);
        frozen.Weather.Update(live.Weather.Value);
        frozen.Farm.Update(live.Farm.Value);
        frozen.Plants.Update(live.Plants.Value);
        frozen.Things.Update(live.Things.Value);
        frozen.ThingDefs.Update(live.ThingDefs.Value);
        frozen.AnimalDefs.Update(live.AnimalDefs.Value);
        frozen.Terrain.Update(live.Terrain.Value);
        frozen.StoredResources.Update(live.StoredResources.Value);
        frozen.Animals.Update(live.Animals.Value);
        frozen.Resources.Update(live.Resources.Value);
        frozen.Research.Update(live.Research.Value);
        frozen.WillieBacklog.Update(live.WillieBacklog.Value);
        return frozen;
    }
}
