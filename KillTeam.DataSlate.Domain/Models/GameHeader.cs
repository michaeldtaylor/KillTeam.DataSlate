namespace KillTeam.DataSlate.Domain.Models;

/// <summary>A read-only projection of a game's header — status, players, teams, and score.</summary>
public record GameHeader(
    GameStatus Status,
    string? MissionName,
    string Player1Name,
    string Team1Name,
    string Player2Name,
    string Team2Name,
    string? WinnerTeamName,
    int VictoryPoints1,
    int VictoryPoints2);
