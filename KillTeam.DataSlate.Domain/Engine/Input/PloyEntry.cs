namespace KillTeam.DataSlate.Domain.Engine.Input;

/// <summary>Details of a ploy a team wishes to record during the Strategy Phase.</summary>
public record PloyEntry(string Name, string? Description, int CpCost);
