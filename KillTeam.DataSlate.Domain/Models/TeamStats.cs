namespace KillTeam.DataSlate.Domain.Models;

/// <summary>Aggregated statistics for a team across all completed games.</summary>
public record TeamStats(int GamesPlayed, int Wins, int Kills, string? MostUsedWeapon);
