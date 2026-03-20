using KillTeam.DataSlate.Domain.Engine.Input;
using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;

public class FightResolutionContext
{
    public required OperativeContext Attacker { get; init; }

    public required OperativeContext Target { get; init; }

    public required Weapon AttackerWeapon { get; init; }

    public Weapon? TargetWeapon { get; init; }

    public required FightDicePool AttackerPool { get; init; }

    public required FightDicePool TargetPool { get; init; }

    public required bool BlockRestrictedToCrits { get; init; }

    public required IFightInputProvider InputProvider { get; init; }

    public required GameEventStream? EventStream { get; init; }
}
