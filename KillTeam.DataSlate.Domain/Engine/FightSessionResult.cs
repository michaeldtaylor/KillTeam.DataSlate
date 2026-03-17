namespace KillTeam.DataSlate.Domain.Engine;

public record FightSessionResult(
    bool AttackerCausedIncapacitation,
    bool DefenderCausedIncapacitation,
    int AttackerDamageDealt,
    int DefenderDamageDealt,
    Guid TargetOperativeId);
