using System.Text.Json.Serialization;

namespace RimBob.Core.Advice;

[JsonConverter(typeof(SnakeCaseLowerEnumConverter<WillieConcern>))]
public enum WillieConcern
{
    PowerStability,
    ThermalControl,
    FunctionalRooms,
    StoragePlacement,
    MaterialBottleneck,
    FireRisk,
    StalledBuilds,
    BaseLayout
}
