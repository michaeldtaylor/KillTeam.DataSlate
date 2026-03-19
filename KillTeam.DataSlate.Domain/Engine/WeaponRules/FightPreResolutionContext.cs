using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Services;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules;

public class FightPreResolutionContext
{
    public required Operative Attacker { get; init; }

    public required Operative Target { get; init; }

    public required GameEventStream? EventStream { get; init; }

    public required FightDicePool AttackerPool { get; set; }

    public required FightDicePool TargetPool { get; set; }

    public bool IsBrutal { get; set; }
}
