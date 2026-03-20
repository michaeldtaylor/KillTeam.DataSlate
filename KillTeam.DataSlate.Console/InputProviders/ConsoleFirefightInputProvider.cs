using KillTeam.DataSlate.Domain.Engine.Input;
using KillTeam.DataSlate.Domain.Models;
using Spectre.Console;

namespace KillTeam.DataSlate.Console.InputProviders;

public class ConsoleFirefightInputProvider(IAnsiConsole console) : IFirefightInputProvider
{
    public Task DisplayTurningPointHeaderAsync(int tpNumber)
    {
        console.Write(new Rule($"[bold]Turning Point {tpNumber} — Firefight Phase[/]"));

        return Task.CompletedTask;
    }

    public Task DisplayBoardStateAsync(
        Game game,
        TurningPoint turningPoint,
        IReadOnlyList<GameOperativeState> allStates,
        IReadOnlyDictionary<Guid, Operative> allOperatives)
    {
        var table = new Table()
            .Title($"[bold]TP {turningPoint.Number} — Board State[/]")
            .Border(TableBorder.Rounded)
            .AddColumn("Name")
            .AddColumn("W")
            .AddColumn("Order")
            .AddColumn("Status")
            .AddColumn("Guard");

        foreach (var state in allStates)
        {
            if (!allOperatives.TryGetValue(state.OperativeId, out var operative))
            {
                continue;
            }

            var teamTag = operative.TeamId == game.Participant1.TeamId ? "[blue]A[/]" : "[red]B[/]";
            var name = $"{teamTag} {Markup.Escape(operative.Name)}";

            var injured = state.CurrentWounds < operative.Wounds / 2;
            var wounds = injured
                ? $"{state.CurrentWounds}/{operative.Wounds} [yellow](Injured)[/]"
                : $"{state.CurrentWounds}/{operative.Wounds}";

            var status = state.IsIncapacitated
                ? "[red]Incapacitated[/]"
                : state.IsReady ? "[green]Ready[/]" : "[dim]Expended[/]";

            var guard = state.IsOnGuard ? "[yellow]⚑[/]" : string.Empty;

            table.AddRow(name, wounds, state.Order.ToString(), status, guard);
        }

        console.Write(table);
        console.MarkupLine($"  CP → A:[yellow]{game.Participant1.CommandPoints}[/]  B:[yellow]{game.Participant2.CommandPoints}[/]");

        return Task.CompletedTask;
    }

    public Task DisplayActivationHeaderAsync(string operativeName)
    {
        console.Write(new Rule($"[cyan]{Markup.Escape(operativeName)}[/] activation"));

        return Task.CompletedTask;
    }

    public Task DisplayGuardSetAsync(string operativeName)
    {
        console.MarkupLine($"[green]{Markup.Escape(operativeName)} is now On Guard.[/]");

        return Task.CompletedTask;
    }

    public Task DisplayCounteractAvailableAsync(string operativeName)
    {
        console.MarkupLine($"[yellow]Counteract! {Markup.Escape(operativeName)} gets 1 AP (max 2\" movement).[/]");

        return Task.CompletedTask;
    }

    public Task DisplayTurningPointCompleteAsync(int tpNumber)
    {
        console.MarkupLine($"[dim]Turning Point {tpNumber} complete. Resetting for next turningPoint...[/]");

        return Task.CompletedTask;
    }

    public Task DisplayGameOverAsync()
    {
        console.Write(new Rule("[bold red]Game Over![/]"));

        return Task.CompletedTask;
    }

    public Task DisplayWinnerAsync(string? winnerLabel, int vp1, int vp2)
    {
        console.MarkupLine(winnerLabel is not null
            ? $"[bold green]Winner: {winnerLabel} — {(winnerLabel == "Team A" ? vp1 : vp2)} VP[/]"
            : $"[yellow]Draw! Team A: {vp1} VP  |  Team B: {vp2} VP[/]");

        return Task.CompletedTask;
    }

    public Task<(Operative operative, GameOperativeState state)> SelectActivatingOperativeAsync(
        IReadOnlyList<(Operative operative, GameOperativeState state)> candidates)
    {
        if (candidates.Count == 1)
        {
            return Task.FromResult(candidates[0]);
        }

        var selected = console.Prompt(
            new SelectionPrompt<(Operative operative, GameOperativeState state)>()
                .Title("Select an operative to activate:")
                .UseConverter(pair =>
                    $"{Markup.Escape(pair.operative.Name)} (Wounds: {pair.state.CurrentWounds}/{pair.operative.Wounds}, {pair.state.Order})")
                .AddChoices(candidates));

        return Task.FromResult(selected);
    }

    public Task<Order> SelectOrderAsync(string operativeName)
    {
        var orderChoice = console.Prompt(
            new SelectionPrompt<string>()
                .Title($"Set order for {Markup.Escape(operativeName)}:")
                .AddChoices("Engage", "Conceal"));

        var order = orderChoice == "Engage" ? Order.Engage : Order.Conceal;

        return Task.FromResult(order);
    }

    public Task<string> SelectActionAsync(
        string operativeName,
        int remainingAp,
        IReadOnlyList<string> availableActions)
    {
        var selected = console.Prompt(
            new SelectionPrompt<string>()
                .Title($"[bold]{Markup.Escape(operativeName)}[/] — {remainingAp} AP remaining. Choose an action:")
                .AddChoices(availableActions));

        return Task.FromResult(selected);
    }

    public Task<string?> GetMoveDistanceAsync(string operativeName)
    {
        var distance = console.Prompt(
            new TextPrompt<string>($"How far did {Markup.Escape(operativeName)} move? (e.g. '3\"', leave blank to skip):")
                .AllowEmpty());

        return Task.FromResult<string?>(string.IsNullOrWhiteSpace(distance) ? null : distance);
    }

    public Task<string?> SelectCounteractOperativeAsync(IReadOnlyList<string> candidateNames)
    {
        var choices = candidateNames.Append("Skip counteract").ToList();
        var selected = console.Prompt(
            new SelectionPrompt<string>()
                .Title("[yellow]Counteract available![/] Select operative to counteract, or skip:")
                .AddChoices(choices));

        return Task.FromResult<string?>(selected == "Skip counteract" ? null : selected);
    }

    public Task<string> SelectCounteractActionAsync(string operativeName)
    {
        var selected = console.Prompt(
            new SelectionPrompt<string>()
                .Title($"{Markup.Escape(operativeName)} counteract action:")
                .AddChoices("Move (max 2\")", "Shoot", "Fight", "Skip"));

        return Task.FromResult(selected);
    }

    public Task<int> GetFinalVpAsync(string teamLabel)
    {
        var vp = console.Prompt(
            new TextPrompt<int>($"Enter final VP for {teamLabel}:").Validate(v => v >= 0));

        return Task.FromResult(vp);
    }
}
