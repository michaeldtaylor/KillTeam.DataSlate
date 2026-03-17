namespace KillTeam.TeamExtractor.Models;

/// <summary>Operative selection rules extracted from the Operative Selection PDF.</summary>
public class ExtractedOperativeSelection
{
    /// <summary>The archetype string extracted from the ARCHETYPE: line.</summary>
    public string Archetype { get; init; } = string.Empty;

    /// <summary>The full selection rules text following the archetype declaration.</summary>
    public string Text { get; init; } = string.Empty;
}
