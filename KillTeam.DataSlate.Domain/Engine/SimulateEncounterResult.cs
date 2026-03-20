namespace KillTeam.DataSlate.Domain.Engine;

public record SimulateEncounterResult(
    int AttackerDamageDealt,
    int DefenderDamageDealt,
    bool AttackerIncapacitated,
    bool DefenderIncapacitated,
    int AttackerCurrentWounds,
    int DefenderCurrentWounds);
