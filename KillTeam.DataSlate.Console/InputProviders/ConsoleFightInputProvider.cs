using KillTeam.DataSlate.Domain.Engine.Input;
using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Services;
using Spectre.Console;

namespace KillTeam.DataSlate.Console.InputProviders;

public class ConsoleFightInputProvider(IAnsiConsole console) : IFightInputProvider
{
    public async Task<GameOperativeState> SelectTargetAsync(
        IList<GameOperativeState> candidates,
        IReadOnlyDictionary<Guid, Operative> allOperatives)
    {
        return await Task.FromResult(console.Prompt(
            new SelectionPrompt<GameOperativeState>()
                .Title("Select an enemy to fight (must be within control range):")
                .UseConverter(s => allOperatives.TryGetValue(s.OperativeId, out var o)
                    ? $"{Markup.Escape(o.Name)} (Wounds: {s.CurrentWounds}/{o.Wounds})"
                    : s.OperativeId.ToString())
                .AddChoices(candidates)));
    }

    public async Task<Weapon> SelectAttackerWeaponAsync(IList<Weapon> weapons, bool isInjured)
    {
        return await Task.FromResult(console.Prompt(
            new SelectionPrompt<Weapon>()
                .Title("Select attacker''s melee weapon:")
                .UseConverter(w =>
                {
                    var injuredNote = isInjured ? $" [yellow](Injured: effective Hit {w.Hit + 1}+)[/]" : "";
                    return $"{Markup.Escape(w.Name)} (Attack: [green]{w.Atk}[/] | Hit: [green]{w.Hit}+[/] | Normal: [green]{w.NormalDmg}[/] | Crit: [green]{w.CriticalDmg}[/]){injuredNote}";
                })
                .AddChoices(weapons)));
    }

    public async Task<Weapon> SelectDefenderWeaponAsync(IList<Weapon> weapons)
    {
        return await Task.FromResult(console.Prompt(
            new SelectionPrompt<Weapon>()
                .Title("Select defender''s melee weapon:")
                .UseConverter(w => $"{Markup.Escape(w.Name)} (Attack: [green]{w.Atk}[/] | Hit: [green]{w.Hit}+[/] | Normal: [green]{w.NormalDmg}[/] | Crit: [green]{w.CriticalDmg}[/])")
                .AddChoices(weapons)));
    }

    public Task<int> GetFightAssistCountAsync()
    {
        return Task.FromResult(console.Prompt(
            new TextPrompt<int>("How many non-engaged friendly allies within 6\" of target? (0-2):")
                .DefaultValue(0)
                .Validate(v => v is >= 0 and <= 2
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Enter 0, 1, or 2.[/]"))));
    }

    public async Task<FightAction> SelectActionAsync(IList<FightAction> actions, string operativeName)
    {
        return await Task.FromResult(console.Prompt(
            new SelectionPrompt<FightAction>()
                .Title($"[bold]{Markup.Escape(operativeName)}[/] — select an action:")
                .UseConverter(FormatFightAction)
                .AddChoices(actions)));
    }

    public Task<string> GetNarrativeNoteAsync()
    {
        return Task.FromResult(console.Prompt(
            new TextPrompt<string>("Narrative note [dim](optional, press enter to skip)[/]:")
                .AllowEmpty()));
    }

    public async Task<int[]> RollOrEnterDiceAsync(
        int count, string label,
        string operativeName, string role, string phase,
        string participant, GameEventStream? eventStream)
    {
        if (count == 0)
        {
            return [];
        }

        var choice = console.Prompt(
            new SelectionPrompt<string>()
                .Title($"[bold]{Markup.Escape(label)}[/] ({count} dice):")
                .AddChoices("Roll for me", "Enter manually"));

        if (choice == "Roll for me")
        {
            var rolled = Enumerable.Range(0, count).Select(_ => Random.Shared.Next(1, 7)).ToArray();
            eventStream?.Emit((seq, ts) => new DiceRolledEvent(eventStream.GameSessionId, seq, ts, participant, operativeName, role, phase, rolled));
            return rolled;
        }

        while (true)
        {
            var input = console.Prompt(
                new TextPrompt<string>($"Enter {count} dice values (space or comma separated):")
                    .AllowEmpty());
            var parts = input.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
            var values = new List<int>();
            var valid = true;
            foreach (var p in parts)
            {
                if (int.TryParse(p, out int v) && v is >= 1 and <= 6)
                    values.Add(v);
                else
                {
                    valid = false;
                    break;
                }
            }
            if (valid && values.Count == count)
            {
                var rolled = values.ToArray();
                eventStream?.Emit((seq, ts) => new DiceRolledEvent(eventStream.GameSessionId, seq, ts, participant, operativeName, role, phase, rolled));
                return rolled;
            }
            console.MarkupLine("[red]Invalid input. Enter integers 1-6 separated by spaces or commas.[/]");
        }
    }

    private static string FormatFightAction(FightAction a)
    {
        var resultLabel = a.ActiveDie.Result == DieResult.Crit ? "CRIT" : "HIT";
        var dieInfo = $"rolled [green]{a.ActiveDie.RolledValue}[/] ({resultLabel})";
        if (a.Type == FightActionType.Strike)
        {
            return $"[red]STRIKE[/] — {dieInfo}";
        }
        var targetLabel = a.TargetDie!.Result == DieResult.Crit ? "CRIT" : "HIT";
        return $"[cyan]BLOCK[/] — {dieInfo} cancels rolled [green]{a.TargetDie.RolledValue}[/] ({targetLabel})";
    }
}
