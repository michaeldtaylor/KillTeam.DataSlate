using KillTeam.DataSlate.Domain.Events;
using Spectre.Console;

namespace KillTeam.DataSlate.Console.Rendering;

/// <summary>
/// Renders game events to the console with participant labels ([You] / [AI] / player name).
/// Subscribe to GameEventStream.OnEventEmitted before combat starts.
/// </summary>
public class GameEventRenderer(
    IAnsiConsole console,
    IReadOnlyDictionary<string, string> participantLabels)
{
    public void Render(GameEvent evt)
    {
        var label = Label(evt.Participant);
        switch (evt)
        {
            case FightTargetSelectedEvent e:
                console.MarkupLine($"{label} Target: [bold]{Markup.Escape(e.TargetName)}[/] (Wounds: [green]{e.CurrentWounds}/{e.MaxWounds}[/])");
                break;

            case ShootTargetSelectedEvent e:
                console.MarkupLine($"{label} Target: [bold]{Markup.Escape(e.TargetName)}[/] (Wounds: [green]{e.CurrentWounds}/{e.MaxWounds}[/])");
                break;

            case WeaponSelectedEvent e:
                var injuredNote = e.IsInjured ? $" [yellow](Injured: effective Hit {e.EffectiveHit}+)[/]" : "";
                var autoNote = e.WasAutoSelected ? "Auto-selected" : "Selected";
                console.MarkupLine($"{label} {autoNote} {e.Role.ToLower()} weapon: [bold]{Markup.Escape(e.WeaponName)}[/] (Attack: [green]{e.Attack}[/] | Hit: [green]{e.Hit}+[/] | Normal: [green]{e.NormalDmg}[/] | Crit: [green]{e.CritDmg}[/]){injuredNote}");
                break;

            case DefenderNoMeleeWeaponsEvent e:
                console.MarkupLine($"[dim]{Markup.Escape(e.DefenderName)} has no melee weapons — rolls 0 attack dice.[/]");
                break;

            case CombatWarningEvent e when e.Kind == CombatWarningKind.NoValidTargets:
                console.MarkupLine($"[yellow]{Markup.Escape(e.Message)}[/]");
                break;

            case CombatWarningEvent e when e.Kind == CombatWarningKind.NoWeaponsAvailable:
                console.MarkupLine($"[yellow]{Markup.Escape(e.Message)}[/]");
                break;

            case CombatWarningEvent e:
                console.MarkupLine($"[red]{Markup.Escape(e.Message)}[/]");
                break;

            case ShockAppliedEvent e:
                console.MarkupLine($"{label} [yellow]SHOCK:[/] {Markup.Escape(e.TargetName)} discards die (rolled {e.DiscardedDieValue})");
                break;

            case FightPoolsDisplayedEvent e:
                RenderFightPools(e);
                break;

            case FightStrikeResolvedEvent e:
                console.MarkupLine($"  {label} [red]STRIKE[/] with die ({e.DieValue}) — {e.DamageDealt} damage to {Markup.Escape(e.TargetOperativeName)}");
                break;

            case FightBlockResolvedEvent e:
                console.MarkupLine($"  {label} [cyan]BLOCK[/]: die ({e.ActiveDieValue}) cancels ({e.CancelledDieValue})");
                break;

            case IncapacitationEvent e:
                console.MarkupLine($"[red]INCAPACITATED! {Markup.Escape(e.OperativeName)} is out of action![/]");
                break;

            case FightResolvedEvent:
                // Summary is handled by SimulateSessionOrchestrator's DisplayEncounterSummary
                break;

            case DiceRolledEvent e:
                console.MarkupLine($"  {label} Rolled: [green]{string.Join(", ", e.Values)}[/]");
                break;

            case BalancedRerollAppliedEvent e:
                console.MarkupLine($"  {label} [dim](Balanced)[/] Re-rolled die {e.DieIndex + 1}: {e.OldValue} → [bold]{e.NewValue}[/]");
                break;

            case CeaselessRerollAppliedEvent e:
                console.MarkupLine($"  {label} [dim](Ceaseless)[/] Re-rolled die {e.DieIndex + 1}: {e.OldValue} → [bold]{e.NewValue}[/]");
                break;

            case RelentlessRerollAppliedEvent e:
                console.MarkupLine($"  {label} [dim](Relentless)[/] Re-rolled die {e.DieIndex + 1}: {e.OldValue} → [bold]{e.NewValue}[/]");
                break;

            case CpRerollAppliedEvent e:
                console.MarkupLine($"  {label} [dim](CP re-roll)[/] Re-rolled die {e.DieIndex + 1}: {e.OldValue} → [bold]{e.NewValue}[/] ({e.RemainingCp}CP remaining)");
                break;

            case CoverSaveNotifiedEvent:
                console.MarkupLine("[green]+1 cover save will be added automatically.[/]");
                break;

            case ShootResultDisplayedEvent e:
                RenderShootResult(e);
                break;

            case StunAppliedEvent e:
                console.MarkupLine($"[yellow]STUN applied to {Markup.Escape(e.TargetName)} (-{e.AplReduction} APL)[/]");
                break;

            case SelfDamageDealtEvent e:
                console.MarkupLine($"[red]HOT! {Markup.Escape(e.AttackerName)} takes {e.SelfDamageAmount} self-damage! (Wounds: {e.RemainingWounds})[/]");
                break;

            case ShootResolvedEvent:
                // Summary handled by SimulateSessionOrchestrator
                break;
        }
    }

    private void RenderFightPools(FightPoolsDisplayedEvent e)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn($"[bold]{Markup.Escape(e.AttackerName)}[/] (Atk)")
            .AddColumn($"[bold]{Markup.Escape(e.DefenderName)}[/] (Def)");

        table.AddRow(
            $"Wounds: {e.AttackerWounds}/{e.AttackerMaxWounds}",
            $"Wounds: {e.DefenderWounds}/{e.DefenderMaxWounds}");

        var maxRows = Math.Max(e.AttackerDice.Count, e.DefenderDice.Count);
        for (int i = 0; i < maxRows; i++)
        {
            var atkCell = i < e.AttackerDice.Count ? FormatDieSnapshot("A", i + 1, e.AttackerDice[i]) : "";
            var defCell = i < e.DefenderDice.Count ? FormatDieSnapshot("D", i + 1, e.DefenderDice[i]) : "";
            table.AddRow(atkCell, defCell);
        }
        console.Write(table);
    }

    private void RenderShootResult(ShootResultDisplayedEvent e)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Result")
            .AddColumn("Value");
        table.AddRow("Unblocked Crits", $"[bold]{e.UnblockedCrits}[/]");
        table.AddRow("Unblocked Normals", $"[bold]{e.UnblockedNormals}[/]");
        table.AddRow("Total Damage", $"[bold red]{e.TotalDamage}[/]");
        if (e.InCover) table.AddRow("Cover Save", "[green]Applied[/]");
        if (e.IsObscured) table.AddRow("Obscured", "[green]Crits converted[/]");
        console.Write(table);
    }

    private static string FormatDieSnapshot(string prefix, int num, FightDieSnapshot die)
    {
        var result = die.Result == "CRIT" ? "[bold yellow]CRIT[/]" : "[green]HIT [/]";
        return $"{prefix}{num}: {result} (rolled [green]{die.RolledValue}[/])";
    }

    private string Label(string participantId)
    {
        if (!participantLabels.TryGetValue(participantId, out var label))
            return "";
        return label switch
        {
            "You" => "[bold cyan]\\[You][/]",
            "AI" => "[bold red]\\[AI][/]",
            _ => $"[bold]{Markup.Escape(label)}[/]"
        };
    }
}
