using System.Text.Json.Serialization;

namespace RimBob.Core.Advice;

[JsonConverter(typeof(SnakeCaseLowerEnumConverter<WelfareConcern>))]
public enum WelfareConcern
{
    BreakRisk,
    ShelterFloor,
    RecreationGap,
    ComfortBeauty
}
