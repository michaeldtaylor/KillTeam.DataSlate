using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Services;

public static class SpecialRuleRegistry
{
    private static readonly SpecialRuleDefinition[] All =
    [
        new() { Kind = SpecialRuleKind.Accurate,      Phase = SpecialRulePhase.Both,  Description = "Retain x normal hits without rolling (stacks, max 2)." },
        new() { Kind = SpecialRuleKind.Balanced,      Phase = SpecialRulePhase.Fight, Description = "Re-roll one attack die." },
        new() { Kind = SpecialRuleKind.Blast,         Phase = SpecialRulePhase.Shoot, Description = "Make attacks against all operatives within x\" of target visible to target." },
        new() { Kind = SpecialRuleKind.Brutal,        Phase = SpecialRulePhase.Fight, Description = "Opponent can only block with Critical successes." },
        new() { Kind = SpecialRuleKind.Ceaseless,     Phase = SpecialRulePhase.Both,  Description = "Re-roll all dice showing one specific value." },
        new() { Kind = SpecialRuleKind.Devastating,   Phase = SpecialRulePhase.Both,  Description = "Critical hits inflict x damage." },
        new() { Kind = SpecialRuleKind.Heavy,         Phase = SpecialRulePhase.Shoot, Description = "Cannot shoot in the same activation as moving." },
        new() { Kind = SpecialRuleKind.HeavyDashOnly, Phase = SpecialRulePhase.Shoot, Description = "Can only move with a Dash in the same activation as shooting." },
        new() { Kind = SpecialRuleKind.Hot,           Phase = SpecialRulePhase.Shoot, Description = "After shooting, roll 1D6; if result is lower than the Hit stat, suffer damage equal to 2× the result." },
        new() { Kind = SpecialRuleKind.Lethal,        Phase = SpecialRulePhase.Both,  Description = "Inflict Critical hits on a roll of x+ instead of 6+." },
        new() { Kind = SpecialRuleKind.Limited,       Phase = SpecialRulePhase.Both,  Description = "This weapon has x uses per battle." },
        new() { Kind = SpecialRuleKind.Piercing,      Phase = SpecialRulePhase.Both,  Description = "Remove x of the defender's dice before they roll." },
        new() { Kind = SpecialRuleKind.PiercingCrits, Phase = SpecialRulePhase.Both,  Description = "Remove x of the defender's dice before they roll, only if a Critical success was rolled." },
        new() { Kind = SpecialRuleKind.Punishing,     Phase = SpecialRulePhase.Both,  Description = "Retain a failed die as a normal success if any Critical successes are retained." },
        new() { Kind = SpecialRuleKind.Range,         Phase = SpecialRulePhase.Shoot, Description = "Target must be within x\" of the shooter." },
        new() { Kind = SpecialRuleKind.Relentless,    Phase = SpecialRulePhase.Both,  Description = "Re-roll any or all attack dice." },
        new() { Kind = SpecialRuleKind.Rending,       Phase = SpecialRulePhase.Both,  Description = "Convert one normal hit to a Critical hit if any Critical successes are retained." },
        new() { Kind = SpecialRuleKind.Saturate,      Phase = SpecialRulePhase.Shoot, Description = "The defender cannot retain any cover saves." },
        new() { Kind = SpecialRuleKind.Seek,          Phase = SpecialRulePhase.Shoot, Description = "Targets cannot use any terrain for cover." },
        new() { Kind = SpecialRuleKind.SeekLight,     Phase = SpecialRulePhase.Shoot, Description = "Targets cannot use light terrain for cover." },
        new() { Kind = SpecialRuleKind.Severe,        Phase = SpecialRulePhase.Both,  Description = "Convert one normal hit to a Critical hit if no Critical successes are retained." },
        new() { Kind = SpecialRuleKind.Shock,         Phase = SpecialRulePhase.Fight, Description = "The first Critical strike discards the opponent's worst success." },
        new() { Kind = SpecialRuleKind.Silent,        Phase = SpecialRulePhase.Shoot, Description = "Can perform a Shoot action whilst on a Conceal order." },
        new() { Kind = SpecialRuleKind.Stun,          Phase = SpecialRulePhase.Both,  Description = "Remove 1 APL from the target if any Critical successes are retained." },
        new() { Kind = SpecialRuleKind.Torrent,       Phase = SpecialRulePhase.Shoot, Description = "Make attacks against all operatives within x\" of target visible to the shooter." },
    ];

    public static IReadOnlyDictionary<SpecialRuleKind, SpecialRuleDefinition> ByKind { get; } =
        All.ToDictionary(d => d.Kind);
}
