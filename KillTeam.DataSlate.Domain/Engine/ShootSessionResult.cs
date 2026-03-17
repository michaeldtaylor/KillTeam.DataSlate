namespace KillTeam.DataSlate.Domain.Engine;

public record ShootSessionResult(
    bool CausedIncapacitation,
    int DamageDealt,
    Guid? TargetOperativeId);
