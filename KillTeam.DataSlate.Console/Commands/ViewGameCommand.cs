using System.ComponentModel;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace KillTeam.DataSlate.Console.Commands;

/// <summary>Displays the full detail of a game: turning points, activations, actions, dice, and narrative notes.</summary>
[Description("View full details of a game — turning points, activations, actions, and notes.")]
public class ViewGameCommand(
    IAnsiConsole console,
    IGameRepository games,
    IActivationRepository activations,
    IActionRepository actions,
    IPloyRepository ploys,
    ITurningPointRepository turningPoints,
    IOperativeRepository operatives,
    ILogger<ViewGameCommand> logger) : AsyncCommand<ViewGameCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [Description("The ID of the game to view.")]
        [CommandArgument(0, "<game-id>")]
        public string GameId { get; set; } = string.Empty;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (!Guid.TryParse(settings.GameId, out var gameId))
        {
            console.MarkupLine("[red]Invalid game ID format.[/]");

            return 1;
        }

        logger.LogDebug("Viewing game {GameId}", gameId);

        var header = await games.GetHeaderAsync(gameId);

        if (header is null)
        {
            logger.LogWarning("Game {GameId} not found for view", gameId);
            console.MarkupLine($"[red]Game {Markup.Escape(settings.GameId)} not found.[/]");

            return 1;
        }

        console.MarkupLine($"[bold]=== {Markup.Escape(header.PlayerAName)} ({Markup.Escape(header.TeamAName)}) vs {Markup.Escape(header.PlayerBName)} ({Markup.Escape(header.TeamBName)}) ===[/]");

        if (header.MissionName is not null)
        {
            console.MarkupLine($"Mission: {Markup.Escape(header.MissionName)}");
        }

        var turningPointSummaries = await turningPoints.GetSummariesByGameAsync(gameId);

        foreach (var turningPoint in turningPointSummaries)
        {
            console.MarkupLine($"\n[bold]=== Turning Point {turningPoint.Number} ===[/]");

            if (turningPoint.InitiativeTeamName is not null)
            {
                console.MarkupLine($"  Initiative: {Markup.Escape(turningPoint.InitiativeTeamName)}");
            }

            var ployList = (await ploys.GetByTurningPointAsync(turningPoint.Id)).ToList();

            foreach (var ploy in ployList)
            {
                console.MarkupLine($"  [dim]Ploy:[/] {Markup.Escape(ploy.PloyName)} ({Markup.Escape(ploy.TeamId)}, {ploy.CpCost}CP)" +
                    (ploy.Description is not null ? $" — {Markup.Escape(ploy.Description)}" : string.Empty));
            }

            var activationList = (await activations.GetByTurningPointAsync(turningPoint.Id)).ToList();

            foreach (var activation in activationList)
            {
                var operativeName = await operatives.GetNameByIdAsync(activation.OperativeId) ?? activation.OperativeId.ToString()[..8];
                var flags = new List<string>();

                if (activation.IsCounteract)
                {
                    flags.Add("Counteract");
                }

                if (activation.IsGuardInterrupt)
                {
                    flags.Add("Guard Interrupt");
                }

                var flagStr = flags.Count > 0 ? $" [dim]({string.Join(", ", flags)})[/]" : string.Empty;

                console.MarkupLine($"  [Act {activation.SequenceNumber}] {Markup.Escape(operativeName)} ({activation.OrderSelected}){flagStr}");

                if (activation.NarrativeNote is not null)
                {
                    console.MarkupLine($"    [dim]🖊 {Markup.Escape(activation.NarrativeNote)}[/]");
                }

                var actionList = (await actions.GetByActivationAsync(activation.Id)).ToList();

                foreach (var action in actionList)
                {
                    var targetName = action.TargetOperativeId.HasValue
                        ? await operatives.GetNameByIdAsync(action.TargetOperativeId.Value) : null;
                    var damage = action.NormalDamageDealt + action.CriticalDamageDealt;
                    var coverStr = action.TargetInCover == true ? " [dim](cover)[/]" : string.Empty;
                    var obscStr = action.IsObscured == true ? " [dim](obscured)[/]" : string.Empty;
                    var incapStr = action.CausedIncapacitation ? " [red](Incapacitated!)[/]" : string.Empty;
                    var targetStr = targetName is not null ? $" → {Markup.Escape(targetName)}" : string.Empty;

                    console.MarkupLine($"    {action.Type}{targetStr}: {damage} dmg{coverStr}{obscStr}{incapStr}");

                    if (action.NarrativeNote is not null)
                    {
                        console.MarkupLine($"      [dim]🖊 {Markup.Escape(action.NarrativeNote)}[/]");
                    }
                }
            }
        }

        console.WriteLine();

        if (header.Status == GameStatus.Completed && header.WinnerTeamName is not null)
        {
            console.MarkupLine($"[bold]Final Score:[/] {header.TeamAName} {header.VictoryPointsA} — {header.VictoryPointsB} {header.TeamBName}  |  Winner: [green]{Markup.Escape(header.WinnerTeamName)}[/]");
        }
        else
        {
            console.MarkupLine($"[dim](In Progress — TP{turningPointSummaries.LastOrDefault()?.Number})[/]");
        }

        return 0;
    }
}
