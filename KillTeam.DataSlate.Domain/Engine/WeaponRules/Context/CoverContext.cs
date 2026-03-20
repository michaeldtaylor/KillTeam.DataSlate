using KillTeam.DataSlate.Domain.Engine.Input;
using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;

public class CoverContext
{
    public required Operative Attacker { get; init; }

    public required Operative Target { get; init; }

    public required IShootInputProvider InputProvider { get; init; }

    public GameEventStream? EventStream { get; init; }

    public bool CoverPromptSuppressed { get; set; }

    public bool LightCoverBlocked { get; set; }

    public bool InCover { get; set; }

    public bool IsObscured { get; set; }
}
