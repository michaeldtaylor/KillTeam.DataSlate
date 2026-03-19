using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Services;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules;

public class WeaponEffectContext
{
    public required Operative Attacker { get; init; }

    public required GameOperativeState AttackerState { get; init; }

    public required ShootResult ResolutionResult { get; init; }

    public GameEventStream? EventStream { get; init; }

    public int SelfDamageApplied { get; set; }

    public bool AttackerBecameIncapacitated { get; set; }
}
