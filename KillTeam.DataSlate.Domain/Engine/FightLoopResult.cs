namespace KillTeam.DataSlate.Domain.Engine;

public record FightLoopResult(
    int AttackerCurrentWounds,
    int TargetCurrentWounds,
    int AttackerDamageDealt,
    int TargetDamageDealt
);
