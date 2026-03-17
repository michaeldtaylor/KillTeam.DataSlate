namespace KillTeam.DataSlate.Domain.Models;

/// <summary>A read-only projection of a completed game for history listing.</summary>
public record GameHistoryEntry(
    Guid Id,
    string PlayedAt,
    string? MissionName,
    string PlayerAName,
    string TeamAName,
    string PlayerBName,
    string TeamBName,
    int VictoryPointsA,
    int VictoryPointsB,
    string? WinnerTeamName);
