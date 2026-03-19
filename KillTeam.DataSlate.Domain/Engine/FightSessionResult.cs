namespace KillTeam.DataSlate.Domain.Engine;

public record FightSessionResult(
    bool AttackerCausedIncapacitation,
    bool TargetCausedIncapacitation,
    int AttackerDamageDealt,
    int TargetDamageDealt,
    Guid TargetOperativeId);
