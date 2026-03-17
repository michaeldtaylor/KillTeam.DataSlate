namespace KillTeam.DataSlate.Domain.Models;

/// <summary>A read-only projection of a game's header — status, players, teams, and score.</summary>
public record GameHeader(
    GameStatus Status,
    string? MissionName,
    string PlayerAName,
    string TeamAName,
    string PlayerBName,
    string TeamBName,
    string? WinnerTeamName,
    int VictoryPointsA,
    int VictoryPointsB);
