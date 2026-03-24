namespace KillTeam.DataSlate.Domain.Models;

/// <summary>Aggregated statistics for a player across all completed games.</summary>
public record PlayerStats(Guid Id, string Username, string FirstName, string LastName, int GamesPlayed, int Wins);
