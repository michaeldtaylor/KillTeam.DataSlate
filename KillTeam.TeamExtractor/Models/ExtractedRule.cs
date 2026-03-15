namespace KillTeam.TeamExtractor.Models;

/// <summary>A named rule — faction rule, strategy ploy, or firefight ploy.</summary>
public class ExtractedRule
{
    /// <summary>
    /// Optional category heading from the PDF (e.g. "Howling Banshee", "Dire Avenger").
    /// Populated when one or more ALL-CAPS headers appear before the rule name with no body text.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>The rule name in title case.</summary>
    public required string Name { get; init; }

    /// <summary>The full rule description text.</summary>
    public required string Text { get; init; }
}
