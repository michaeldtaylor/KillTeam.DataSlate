using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Services;

public static class WeaponRuleRegistry
{
    private static readonly WeaponRuleDefinition[] All =
    [
        new() { Kind = WeaponRuleKind.Accurate,      Phase = WeaponRulePhase.Both,  Description = "Retain x normal hits without rolling (stacks, max 2)." },
        new() { Kind = WeaponRuleKind.Balanced,      Phase = WeaponRulePhase.Fight, Description = "Re-roll one attack die." },
        new() { Kind = WeaponRuleKind.Blast,         Phase = WeaponRulePhase.Shoot, Description = "Make attacks against all operatives within x\" of target visible to target." },
        new() { Kind = WeaponRuleKind.Brutal,        Phase = WeaponRulePhase.Fight, Description = "Opponent can only block with Critical successes." },
        new() { Kind = WeaponRuleKind.Ceaseless,     Phase = WeaponRulePhase.Both,  Description = "Re-roll all dice showing one specific value." },
        new() { Kind = WeaponRuleKind.Devastating,   Phase = WeaponRulePhase.Both,  Description = "Critical hits inflict x damage." },
        new() { Kind = WeaponRuleKind.Heavy,         Phase = WeaponRulePhase.Shoot, Description = "Cannot shoot in the same activation as moving." },
        new() { Kind = WeaponRuleKind.HeavyDashOnly, Phase = WeaponRulePhase.Shoot, Description = "Can only move with a Dash in the same activation as shooting." },
        new() { Kind = WeaponRuleKind.Hot,           Phase = WeaponRulePhase.Shoot, Description = "After shooting, roll 1D6; if result is lower than the Hit stat, suffer damage equal to 2× the result." },
        new() { Kind = WeaponRuleKind.Lethal,        Phase = WeaponRulePhase.Both,  Description = "Inflict Critical hits on a roll of x+ instead of 6+." },
        new() { Kind = WeaponRuleKind.Limited,       Phase = WeaponRulePhase.Both,  Description = "This weapon has x uses per battle." },
        new() { Kind = WeaponRuleKind.Piercing,      Phase = WeaponRulePhase.Both,  Description = "Remove x of the defender's dice before they roll." },
        new() { Kind = WeaponRuleKind.PiercingCrits, Phase = WeaponRulePhase.Both,  Description = "Remove x of the defender's dice before they roll, only if a Critical success was rolled." },
        new() { Kind = WeaponRuleKind.Punishing,     Phase = WeaponRulePhase.Both,  Description = "Retain a failed die as a normal success if any Critical successes are retained." },
        new() { Kind = WeaponRuleKind.Range,         Phase = WeaponRulePhase.Shoot, Description = "Target must be within x\" of the shooter." },
        new() { Kind = WeaponRuleKind.Relentless,    Phase = WeaponRulePhase.Both,  Description = "Re-roll any or all attack dice." },
        new() { Kind = WeaponRuleKind.Rending,       Phase = WeaponRulePhase.Both,  Description = "Convert one normal hit to a Critical hit if any Critical successes are retained." },
        new() { Kind = WeaponRuleKind.Saturate,      Phase = WeaponRulePhase.Shoot, Description = "The defender cannot retain any cover saves." },
        new() { Kind = WeaponRuleKind.Seek,          Phase = WeaponRulePhase.Shoot, Description = "Targets cannot use any terrain for cover." },
        new() { Kind = WeaponRuleKind.SeekLight,     Phase = WeaponRulePhase.Shoot, Description = "Targets cannot use light terrain for cover." },
        new() { Kind = WeaponRuleKind.Severe,        Phase = WeaponRulePhase.Both,  Description = "Convert one normal hit to a Critical hit if no Critical successes are retained." },
        new() { Kind = WeaponRuleKind.Shock,         Phase = WeaponRulePhase.Fight, Description = "The first Critical strike discards the opponent's worst success." },
        new() { Kind = WeaponRuleKind.Silent,        Phase = WeaponRulePhase.Shoot, Description = "Can perform a Shoot action whilst on a Conceal order." },
        new() { Kind = WeaponRuleKind.Stun,          Phase = WeaponRulePhase.Both,  Description = "Remove 1 APL from the target if any Critical successes are retained." },
        new() { Kind = WeaponRuleKind.Torrent,       Phase = WeaponRulePhase.Shoot, Description = "Make attacks against all operatives within x\" of target visible to the shooter." },
    ];

    public static IReadOnlyDictionary<WeaponRuleKind, WeaponRuleDefinition> ByKind => All.ToDictionary(d => d.Kind);
}
