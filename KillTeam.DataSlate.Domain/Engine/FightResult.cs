namespace KillTeam.DataSlate.Domain.Engine;

public record FightResult(
    bool AttackerCausedIncapacitation,
    bool TargetCausedIncapacitation,
    int AttackerDamageDealt,
    int TargetDamageDealt,
    Guid TargetOperativeId);
