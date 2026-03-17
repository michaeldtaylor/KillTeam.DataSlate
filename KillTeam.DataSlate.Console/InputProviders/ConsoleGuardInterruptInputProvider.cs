using KillTeam.DataSlate.Domain.Engine.Input;
using Spectre.Console;

namespace KillTeam.DataSlate.Console.InputProviders;

public class ConsoleGuardInterruptInputProvider(IAnsiConsole console) : IGuardInterruptInputProvider
{
    public Task<bool> ConfirmInControlRangeAsync(string enemyName, string guardOpName)
    {
        var result = console.Confirm(
            $"Is [bold]{Markup.Escape(enemyName)}[/] within 6\" (control range) of [bold]{Markup.Escape(guardOpName)}[/]?",
            defaultValue: false);

        return Task.FromResult(result);
    }

    public Task<bool> ConfirmVisibleAsync(string enemyName, string guardOpName)
    {
        var result = console.Confirm(
            $"Is [bold]{Markup.Escape(enemyName)}[/] visible to [bold]{Markup.Escape(guardOpName)}[/]?",
            defaultValue: true);

        return Task.FromResult(result);
    }

    public Task<string> SelectGuardActionAsync(string guardOpName)
    {
        var result = console.Prompt(
            new SelectionPrompt<string>()
                .Title($"[bold yellow]Guard interrupt available![/] Use {Markup.Escape(guardOpName)}'s Guard?")
                .AddChoices("Shoot", "Fight", "Skip"));

        return Task.FromResult(result);
    }
}
