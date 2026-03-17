using KillTeam.DataSlate.Domain.Engine.Input;
using KillTeam.DataSlate.Domain.Models;
using Spectre.Console;

namespace KillTeam.DataSlate.Console.InputProviders;

public class ConsoleBlastInputProvider(IAnsiConsole console) : IBlastInputProvider
{
    public async Task<List<GameOperativeState>> SelectAdditionalTargetsAsync(
        IList<GameOperativeState> candidates,
        IReadOnlyDictionary<Guid, Operative> allOperatives,
        string attackerTeamId)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        return await Task.FromResult<List<GameOperativeState>>(console.Prompt(
            new MultiSelectionPrompt<GameOperativeState>()
                .Title("Select additional targets (space to toggle, enter to confirm):")
                .UseConverter(s =>
                {
                    if (!allOperatives.TryGetValue(s.OperativeId, out var o))
                    {
                        return s.OperativeId.ToString();
                    }
                    var isFriendly = o.TeamId == attackerTeamId;
                    var friendly = isFriendly ? " [red][FRIENDLY FIRE!][/]" : string.Empty;

                    return $"{Markup.Escape(o.Name)} (Wounds: {s.CurrentWounds}/{o.Wounds}){friendly}";
                })
                .AddChoices(candidates)
                .NotRequired()));
    }

    public Task<bool> ConfirmFriendlyFireAsync(int friendlyCount)
    {
        console.MarkupLine($"[red]WARNING: This will affect {friendlyCount} friendly operative(s).[/]");
        return Task.FromResult(console.Confirm("Confirm?", defaultValue: false));
    }

    public Task<string> GetCoverStatusAsync(string targetName)
    {
        return Task.FromResult(console.Prompt(
            new SelectionPrompt<string>()
                .Title($"Is {Markup.Escape(targetName)} in cover or obscured?")
                .AddChoices("In cover", "Obscured", "Neither")));
    }

    public Task<string> GetNarrativeNoteAsync()
    {
        return Task.FromResult(console.Prompt(
            new TextPrompt<string>("Narrative note [dim](optional, press enter to skip)[/]:")
                .AllowEmpty()));
    }

    public async Task<int[]> RollOrEnterDiceAsync(int count, string label)
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

            console.MarkupLine($"  Rolled: [green]{string.Join(", ", rolled)}[/]");
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
                if (int.TryParse(p, out var v) && v is >= 1 and <= 6)
                {
                    values.Add(v);
                }
                else
                {
                    valid = false;
                    break;
                }
            }

            if (valid && values.Count == count)
            {
                return values.ToArray();
            }

            console.MarkupLine("[red]Invalid input. Enter integers 1-6 separated by spaces or commas.[/]");
        }
    }
}
