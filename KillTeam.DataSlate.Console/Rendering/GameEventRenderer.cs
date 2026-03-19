using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;
using Spectre.Console;

namespace KillTeam.DataSlate.Console.Rendering;

/// <summary>
/// Renders game events to the console using a two-column layout.
/// Subscribe to GameEventStream.OnEventEmitted before combat starts.
/// </summary>
public class GameEventRenderer(
    IAnsiConsole console,
    TwoColumnRenderer columns)
{
    public void Render(GameEvent gameEvent)
    {
        switch (gameEvent)
        {
            case FightTargetSelectedEvent e:
                columns.PrintLine(e.Participant, $"Target: [bold]{Markup.Escape(e.TargetName)}[/] (Wounds: [green]{e.CurrentWounds}/{e.MaxWounds}[/])");
                break;

            case ShootTargetSelectedEvent e:
                columns.PrintLine(e.Participant, $"Target: [bold]{Markup.Escape(e.TargetName)}[/] (Wounds: [green]{e.CurrentWounds}/{e.MaxWounds}[/])");
                break;

            case WeaponSelectedEvent e:
                var injuredNote = e.IsInjured ? $" [yellow](Injured: effective Hit {e.EffectiveHit}+)[/]" : string.Empty;
                var autoNote = e.WasAutoSelected ? "Auto-selected" : "Selected";

                columns.PrintLine(e.Participant, $"{autoNote} {e.Role.ToLower()} weapon: [bold]{Markup.Escape(e.WeaponName)}[/] (Attack: [green]{e.Attack}[/] | Hit: [green]{e.Hit}+[/] | Normal: [green]{e.NormalDmg}[/] | Crit: [green]{e.CritDmg}[/]){injuredNote}");
                break;

            case TargetNoMeleeWeaponsEvent e:
                columns.PrintLine($"[dim]{Markup.Escape(e.TargetName)} has no melee weapons — rolls 0 attack dice.[/]");
                break;

            case CombatWarningEvent e when e.Kind == CombatWarningKind.NoValidTargets:
                columns.PrintLine($"[yellow]{Markup.Escape(e.Message)}[/]");
                break;

            case CombatWarningEvent e when e.Kind == CombatWarningKind.NoWeaponsAvailable:
                columns.PrintLine($"[yellow]{Markup.Escape(e.Message)}[/]");
                break;

            case CombatWarningEvent e:
                columns.PrintLine($"[red]{Markup.Escape(e.Message)}[/]");
                break;

            case ShockAppliedEvent e:
                columns.PrintLine(e.Participant, $"[yellow]SHOCK:[/] {Markup.Escape(e.TargetName)} discards die (rolled {e.DiscardedDieValue})");
                break;

            case FightPoolsDisplayedEvent e:
                RenderFightPools(e);
                break;

            case FightStrikeResolvedEvent e:
                columns.PrintSubLine(e.Participant, $"[red]STRIKE[/] with die ({e.DieValue}) — {e.DamageDealt} damage to {Markup.Escape(e.TargetOperativeName)} [{FormatDieResult(e.StrikeResult)}]");
                break;

            case FightBlockResolvedEvent e:
                columns.PrintSubLine(e.Participant, $"[cyan]BLOCK[/]: die ({e.ActiveDieValue}) cancels ({e.CancelledDieValue})");
                break;

            case IncapacitationEvent e:
                columns.PrintLine($"[red]INCAPACITATED! {Markup.Escape(e.OperativeName)} is out of action![/]");
                break;

            case FightResolvedEvent:
                // Summary is handled by SimulateSessionOrchestrator's DisplayEncounterSummary
                break;

            case DiceRolledEvent e:
                columns.PrintSubLine(e.Participant, $"Rolled: [green]{string.Join(", ", e.Values)}[/]");
                break;

            case BalancedRerollAppliedEvent e:
                columns.PrintSubLine(e.Participant, $"[dim](Balanced)[/] Re-rolled die {e.DieIndex + 1}: {e.OldValue} → [bold]{e.NewValue}[/]");
                break;

            case CeaselessRerollAppliedEvent e:
                columns.PrintSubLine(e.Participant, $"[dim](Ceaseless)[/] Re-rolled die {e.DieIndex + 1}: {e.OldValue} → [bold]{e.NewValue}[/]");
                break;

            case RelentlessRerollAppliedEvent e:
                columns.PrintSubLine(e.Participant, $"[dim](Relentless)[/] Re-rolled die {e.DieIndex + 1}: {e.OldValue} → [bold]{e.NewValue}[/]");
                break;

            case CpRerollAppliedEvent e:
                columns.PrintSubLine(e.Participant, $"[dim](CP re-roll)[/] Re-rolled die {e.DieIndex + 1}: {e.OldValue} → [bold]{e.NewValue}[/] ({e.RemainingCp}CP remaining)");
                break;

            case CoverSaveNotifiedEvent:
                columns.PrintLine("[green]+1 cover save will be added automatically.[/]");
                break;

            case ShootPoolsDisplayedEvent e:
                RenderShootPools(e);
                break;

            case ShootResultDisplayedEvent e:
                RenderShootResult(e);
                break;

            case StunAppliedEvent e:
                columns.PrintLine($"[yellow]STUN applied to {Markup.Escape(e.TargetName)} (-{e.AplReduction} APL)[/]");
                break;

            case SelfDamageDealtEvent e:
                columns.PrintLine($"[red]HOT! {Markup.Escape(e.AttackerName)} takes {e.SelfDamageAmount} self-damage! (Wounds: {e.RemainingWounds})[/]");
                break;

            case ShootResolvedEvent:
                // Summary handled by SimulateSessionOrchestrator
                break;
        }
    }

    private void RenderShootPools(ShootPoolsDisplayedEvent e)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn($"[bold]{Markup.Escape(e.AttackerName)}[/] (Attacker)")
            .AddColumn($"[bold]{Markup.Escape(e.TargetName)}[/] (Target)");

        table.AddRow(
            $"Wounds: {e.AttackerWounds}/{e.AttackerMaxWounds}",
            $"Wounds: {e.TargetWounds}/{e.TargetMaxWounds}");

        var maxRows = Math.Max(e.AttackerDice.Count, e.TargetDice.Count);

        for (var i = 0; i < maxRows; i++)
        {
            var attackerCell = i < e.AttackerDice.Count ? FormatAttackDieSnapshot(i + 1, e.AttackerDice[i]) : string.Empty;
            var targetCell = i < e.TargetDice.Count ? FormatDefenceDieSnapshot(i + 1, e.TargetDice[i]) : string.Empty;

            table.AddRow(attackerCell, targetCell);
        }

        console.Write(table);
    }

    private void RenderShootResult(ShootResultDisplayedEvent e)
    {
        var attackerWoundsColour = e.AttackerWoundsAfter > 0 ? "green" : "red";
        var targetWoundsColour = e.TargetWoundsAfter > 0 ? "green" : "red";

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn($"[bold]{Markup.Escape(e.AttackerName)}[/] (Attacker)")
            .AddColumn($"[bold]{Markup.Escape(e.TargetName)}[/] (Target)");

        table.AddRow(
            $"Wounds: [{attackerWoundsColour}]{e.AttackerWoundsAfter}/{e.AttackerMaxWounds}[/]",
            $"Wounds: [{targetWoundsColour}]{e.TargetWoundsAfter}/{e.TargetMaxWounds}[/]");

        var targetDetail = $"Damage dealt: [bold red]{e.TotalDamage}[/]" +
                           $"\nUnblocked Crits: [bold]{e.UnblockedCrits}[/]" +
                           $"\nUnblocked Normals: [bold]{e.UnblockedNormals}[/]";

        if (e.InCover)
        {
            targetDetail += "\nCover Save: [green]Applied[/]";
        }

        if (e.IsObscured)
        {
            targetDetail += "\nObscured: [green]Crits converted[/]";
        }

        table.AddRow(string.Empty, targetDetail);

        console.Write(table);
    }

    private void RenderFightPools(FightPoolsDisplayedEvent e)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn($"[bold]{Markup.Escape(e.AttackerName)}[/] (Attacker)")
            .AddColumn($"[bold]{Markup.Escape(e.TargetName)}[/] (Defender)");

        table.AddRow(
            $"Wounds: {e.AttackerWounds}/{e.AttackerMaxWounds}",
            $"Wounds: {e.TargetWounds}/{e.TargetMaxWounds}");

        var maxRows = Math.Max(e.AttackerDice.Count, e.TargetDice.Count);

        for (var i = 0; i < maxRows; i++)
        {
            var attackerCell = i < e.AttackerDice.Count ? FormatDieSnapshot("A", i + 1, e.AttackerDice[i]) : string.Empty;
            var defenderCell = i < e.TargetDice.Count ? FormatDieSnapshot("D", i + 1, e.TargetDice[i]) : string.Empty;

            table.AddRow(attackerCell, defenderCell);
        }

        console.Write(table);
    }

    private static string FormatDieResult(DieResult result) => result switch
    {
        DieResult.Crit => "[bold yellow]CRIT[/]",
        DieResult.Hit  => "[green]HIT [/]",
        DieResult.Miss => "[dim]MISS[/]",
        DieResult.Save => "[green]SAVE[/]",
        DieResult.Fail => "[red]FAIL[/]",
        _              => result.ToString(),
    };

    private static string FormatDieSnapshot(string prefix, int num, FightDieSnapshot die)
    {
        return $"{prefix}{num}: {FormatDieResult(die.Result)} (rolled [green]{die.RolledValue}[/])";
    }

    private static string FormatAttackDieSnapshot(int num, FightDieSnapshot die)
    {
        return $"A{num}: {FormatDieResult(die.Result)} (rolled [green]{die.RolledValue}[/])";
    }

    private static string FormatDefenceDieSnapshot(int num, FightDieSnapshot die)
    {
        return $"D{num}: {FormatDieResult(die.Result)} (rolled [green]{die.RolledValue}[/])";
    }
}

