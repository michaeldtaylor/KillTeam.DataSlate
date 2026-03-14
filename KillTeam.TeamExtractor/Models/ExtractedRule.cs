namespace KillTeam.TeamExtractor.Models;

/// <summary>A named rule — faction rule, strategy ploy, or firefight ploy.</summary>
public class ExtractedRule
{
    /// <summary>The rule name in title case.</summary>
    public required string Name { get; init; }

    /// <summary>The full rule description text.</summary>
    public required string Text { get; init; }
}
