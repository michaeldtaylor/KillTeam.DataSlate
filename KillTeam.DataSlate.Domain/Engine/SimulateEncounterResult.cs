namespace KillTeam.DataSlate.Domain.Engine;

public record SimulateEncounterResult(
    int AttackerDamageDealt,
    int TargetDamageDealt,
    bool AttackerIncapacitated,
    bool TargetIncapacitated,
    int AttackerCurrentWounds,
    int TargetCurrentWounds);
