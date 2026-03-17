using KillTeam.DataSlate.Domain.Engine.Input;
using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;
using Spectre.Console;

namespace KillTeam.DataSlate.Console.InputProviders;

public class ConsoleShootInputProvider(IAnsiConsole console) : IShootInputProvider
{
    public async Task<GameOperativeState> SelectTargetAsync(
        IList<GameOperativeState> candidates,
        IReadOnlyDictionary<Guid, Operative> allOperatives)
    {
        return await Task.FromResult(console.Prompt(
            new SelectionPrompt<GameOperativeState>()
                .Title("Select a target to shoot:")
                .UseConverter(s => allOperatives.TryGetValue(s.OperativeId, out var o)
                    ? $"{Markup.Escape(o.Name)} (Wounds: {s.CurrentWounds}/{o.Wounds})"
                    : s.OperativeId.ToString())
                .AddChoices(candidates)));
    }

    public async Task<Weapon> SelectWeaponAsync(IList<Weapon> weapons, bool hasMovedNonDash)
    {
        return await Task.FromResult(console.Prompt(
            new SelectionPrompt<Weapon>()
                .Title("Select a ranged weapon:")
                .UseConverter(w =>
                {
                    var rulesText = w.ParsedRules.Count > 0
                        ? $" | {string.Join(", ", w.ParsedRules.Select(r => r.RawText))}"
                        : "";
                    var saturate = w.ParsedRules.Any(r => r.Kind == SpecialRuleKind.Saturate)
                        ? " [yellow]Saturate[/]"
                        : "";
                    return $"{Markup.Escape(w.Name)} (Attack: [green]{w.Atk}[/] | Hit: [green]{w.Hit}+[/] | Normal: [green]{w.NormalDmg}[/] | Crit: [green]{w.CriticalDmg}[/]{Markup.Escape(rulesText)}){saturate}";
                })
                .AddChoices(weapons)));
    }

    public Task<string> GetCoverStatusAsync(string targetName)
    {
        return Task.FromResult(console.Prompt(
            new SelectionPrompt<string>()
                .Title($"Is {Markup.Escape(targetName)} in cover or obscured?")
                .AddChoices("In cover", "Obscured", "Neither")));
    }

    public Task<int> GetFriendlyAllyCountAsync()
    {
        return Task.FromResult(console.Prompt(
            new TextPrompt<int>("How many non-engaged friendly allies within 6\" of target? (0-2):")
                .DefaultValue(0)
                .Validate(v => v is >= 0 and <= 2
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Enter 0, 1, or 2.[/]"))));
    }

    public Task<int> GetDefenceDiceCountAsync()
    {
        return Task.FromResult(console.Prompt(
            new TextPrompt<int>("How many defence dice to roll? (0 or more):")
                .Validate(v => v >= 0)));
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
                else { valid = false; break; }
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
}
