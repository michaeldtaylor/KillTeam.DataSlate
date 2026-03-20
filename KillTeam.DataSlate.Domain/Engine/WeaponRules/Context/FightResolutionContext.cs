using KillTeam.DataSlate.Domain.Engine.Input;
using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;

public class FightResolutionContext
{
    public required Operative Attacker { get; init; }

    public required Operative Target { get; init; }

    public required Weapon AttackerWeapon { get; init; }

    public Weapon? TargetWeapon { get; init; }

    public required FightDicePool AttackerPool { get; init; }

    public required FightDicePool TargetPool { get; init; }

    public required bool BlockRestrictedToCrits { get; init; }

    public required int AttackerCurrentWounds { get; init; }

    public required int TargetCurrentWounds { get; init; }

    public required IFightInputProvider InputProvider { get; init; }

    public required GameEventStream? EventStream { get; init; }
}
