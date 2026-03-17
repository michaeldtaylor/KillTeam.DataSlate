namespace KillTeam.DataSlate.Domain.Models;

/// <summary>Aggregated statistics for a player across all completed games.</summary>
public record PlayerStats(Guid Id, string Name, int GamesPlayed, int Wins);
