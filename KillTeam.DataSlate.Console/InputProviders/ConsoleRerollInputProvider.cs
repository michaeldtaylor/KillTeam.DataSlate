using KillTeam.DataSlate.Domain.Engine.Input;
using Spectre.Console;

namespace KillTeam.DataSlate.Console.InputProviders;

public class ConsoleRerollInputProvider(IAnsiConsole console) : IRerollInputProvider
{
    public async Task<RollableDie> SelectBalancedRerollDieAsync(IList<RollableDie> pool, string label)
    {
        return await Task.FromResult(console.Prompt(
            new SelectionPrompt<RollableDie>()
                .Title($"[yellow]{Markup.Escape(label)}[/] [dim](Balanced)[/] Pick 1 die to re-roll:")
                .UseConverter(d => $"Die {d.Index + 1}: [bold]{d.Value}[/]")
                .AddChoices(pool)));
    }

    public Task<int> GetCeaselessRerollValueAsync(string label)
    {
        return Task.FromResult(console.Prompt(
            new TextPrompt<int>($"[yellow]{Markup.Escape(label)}[/] [dim](Ceaseless)[/] Re-roll all dice showing which value? (1-6):")
                .Validate(v => v is >= 1 and <= 6
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Enter a value from 1 to 6.[/]"))));
    }

    public async Task<IList<RollableDie>> SelectRelentlessRerollDiceAsync(IList<RollableDie> pool, string label)
    {
        return await Task.FromResult<IList<RollableDie>>(console.Prompt(
            new MultiSelectionPrompt<RollableDie>()
                .Title($"[yellow]{Markup.Escape(label)}[/] [dim](Relentless)[/] Select dice to re-roll (space to toggle):")
                .UseConverter(d => $"Die {d.Index + 1}: [bold]{d.Value}[/]")
                .AddChoices(pool)
                .NotRequired()));
    }

    public Task<bool> ConfirmCpRerollAsync(string label, int currentCp)
    {
        return Task.FromResult(console.Confirm($"[yellow]{Markup.Escape(label)}[/] Spend 1CP (have {currentCp}CP) to re-roll one die?", defaultValue: false));
    }

    public async Task<RollableDie> SelectCpRerollDieAsync(IList<RollableDie> pool)
    {
        return await Task.FromResult(console.Prompt(
            new SelectionPrompt<RollableDie>()
                .Title("Select die to re-roll:")
                .UseConverter(d => $"Die {d.Index + 1}: [bold]{d.Value}[/]")
                .AddChoices(pool)));
    }
}
