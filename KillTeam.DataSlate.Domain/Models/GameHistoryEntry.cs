namespace KillTeam.DataSlate.Domain.Models;

/// <summary>A read-only projection of a completed game for history listing.</summary>
public record GameHistoryEntry(
    Guid Id,
    string PlayedAt,
    string? MissionName,
    string Player1Name,
    string Team1Name,
    string Player2Name,
    string Team2Name,
    int VictoryPoints1,
    int VictoryPoints2,
    string? WinnerTeamName);
