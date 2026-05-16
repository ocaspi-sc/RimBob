using System.Text.Json.Serialization;

namespace RimBob.Core.Advice;

[JsonConverter(typeof(SnakeCaseLowerEnumConverter<WorkType>))]
public enum WorkType
{
    Firefight,
    Patient,
    Doctor,
    BedRest,
    Basic,
    Warden,
    Handle,
    Cook,
    Hunt,
    Construct,
    Grow,
    Mine,
    PlantCut,
    Smith,
    Tailor,
    Art,
    Craft,
    Haul,
    Clean,
    Research
}
