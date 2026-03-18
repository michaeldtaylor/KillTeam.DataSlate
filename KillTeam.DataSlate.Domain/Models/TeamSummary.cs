namespace KillTeam.DataSlate.Domain.Models;

/// <summary>Lightweight team projection — basic identity fields only, no operatives loaded.</summary>
public record TeamSummary(string Id, string Name, string Faction, string GrandFaction);
