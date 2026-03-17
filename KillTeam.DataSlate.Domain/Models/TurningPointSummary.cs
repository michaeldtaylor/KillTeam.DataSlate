namespace KillTeam.DataSlate.Domain.Models;

/// <summary>A lightweight projection of a turning point, including the initiative team name if set.</summary>
public record TurningPointSummary(Guid Id, int Number, string? InitiativeTeamName);
