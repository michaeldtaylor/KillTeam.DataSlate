using KillTeam.DataSlate.Domain.Engine.Input;
using Spectre.Console;

namespace KillTeam.DataSlate.Console.InputProviders;

public class ConsoleStrategyPhaseInputProvider(IAnsiConsole console) : IStrategyPhaseInputProvider
{
    public Task<string> SelectInitiativeWinnerAsync(string team1Name, string team2Name)
    {
        while (true)
        {
            var winner = console.Prompt(
                new SelectionPrompt<string>()
                    .Title("Who won initiative? (or T for a tie roll)")
                    .AddChoices(team1Name, team2Name, "Tie — re-roll"));

            if (winner == "Tie — re-roll")
            {
                console.MarkupLine("[dim]Tie! Both players re-roll...[/]");
                continue;
            }

            return Task.FromResult(winner);
        }
    }

    public Task<PloyEntry?> GetPloyDetailsAsync(string teamName, int currentCp)
    {
        if (!console.Confirm(
                $"[bold]{Markup.Escape(teamName)}[/] — record a ploy? (current CP: {currentCp})",
                defaultValue: false))
        {
            return Task.FromResult<PloyEntry?>(null);
        }

        var ployName = console.Prompt(
            new TextPrompt<string>("Ploy name:").Validate(s => !string.IsNullOrWhiteSpace(s)));

        var description = console.Prompt(
            new TextPrompt<string>("Description [dim](optional)[/]:").AllowEmpty());

        var cpCost = console.Prompt(
            new TextPrompt<int>("CP cost:").Validate(c => c >= 0 && c <= 10));

        if (cpCost > currentCp)
        {
            console.MarkupLine($"[red]Not enough CP (have {currentCp}, need {cpCost}).[/]");
            return Task.FromResult<PloyEntry?>(new PloyEntry(ployName, null, cpCost));
        }

        return Task.FromResult<PloyEntry?>(new PloyEntry(
            ployName,
            string.IsNullOrWhiteSpace(description) ? null : description,
            cpCost));
    }
}
