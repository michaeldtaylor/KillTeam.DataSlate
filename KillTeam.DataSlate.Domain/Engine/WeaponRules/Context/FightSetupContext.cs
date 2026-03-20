using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;

public class FightSetupContext
{
    public required Operative Attacker { get; init; }

    public required Operative Target { get; init; }

    public required GameEventStream? EventStream { get; init; }

    public required FightDicePool AttackerPool { get; set; }

    public required FightDicePool TargetPool { get; set; }

    public bool BlockRestrictedToCrits { get; set; }
}
