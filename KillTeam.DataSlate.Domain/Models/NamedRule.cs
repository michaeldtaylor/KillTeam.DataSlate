namespace KillTeam.DataSlate.Domain.Models;

/// <summary>A named rule with optional category (used for faction rules, ploys).</summary>
public class NamedRule
{
    public required string Name { get; init; }

    public string? Category { get; init; }

    public required string Text { get; init; }
}
