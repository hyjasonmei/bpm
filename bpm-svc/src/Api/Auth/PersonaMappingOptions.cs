namespace Bpm.Api.Auth;

/// Maps a persona_code to either a seed user's email or User.Id (Guid).
/// Loaded from the "Personas" section of appsettings.Development.json.
public sealed class PersonaMappingOptions
{
    public Dictionary<string, string> Map { get; set; } = new();
}
