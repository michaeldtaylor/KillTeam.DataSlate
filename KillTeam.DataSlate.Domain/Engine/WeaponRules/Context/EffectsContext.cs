using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;

public class EffectsContext
{
    public required OperativeContext Attacker { get; init; }

    public required ShootResolution ResolutionResult { get; init; }

    public GameEventStream? EventStream { get; init; }

    public int SelfDamageApplied { get; set; }

    public bool AttackerBecameIncapacitated { get; set; }
}
