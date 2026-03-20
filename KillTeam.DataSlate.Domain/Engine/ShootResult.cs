namespace KillTeam.DataSlate.Domain.Engine;

public record ShootResult(
    bool CausedIncapacitation,
    int DamageDealt,
    Guid? TargetOperativeId);
