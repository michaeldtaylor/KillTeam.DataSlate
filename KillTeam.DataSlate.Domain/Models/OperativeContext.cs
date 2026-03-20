namespace KillTeam.DataSlate.Domain.Models;

/// <summary>
/// Pairs an operative's static definition with its mutable game state.
/// </summary>
public record OperativeContext(Operative Operative, GameOperativeState State);
