using System.Text.Json.Serialization;

namespace RimBob.Core.Advice;

/// <summary>
/// Per-minister autonomy dial. MVP honours only <see cref="Suggest"/>;
/// <see cref="Auto"/> graduations are post-MVP (M7+).
/// </summary>
[JsonConverter(typeof(SnakeCaseLowerEnumConverter<AutonomyMode>))]
public enum AutonomyMode
{
    Off,
    Suggest,
    Auto
}
