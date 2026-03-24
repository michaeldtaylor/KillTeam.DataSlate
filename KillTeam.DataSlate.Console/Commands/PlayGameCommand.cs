using System.ComponentModel;
using KillTeam.DataSlate.Console.Orchestrators;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace KillTeam.DataSlate.Console.Commands;

/// <summary>Plays (or resumes) a game session, running all four Turning Points interactively.</summary>
[Description("Play or resume a game session by ID.")]
public class PlayGameCommand(
    IAnsiConsole console,
    IGameRepository gameRepository,
    ITurningPointRepository turningPointRepository,
    StrategyPhaseOrchestrator strategyPhaseOrchestrator,
    FirefightPhaseOrchestrator firefightPhaseOrchestrator,
    ILogger<PlayGameCommand> logger) : AsyncCommand<PlayGameCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [Description("The ID of the game to play or resume.")]
        [CommandArgument(0, "<game-id>")]
        // Spectre.Console CommandSettings — required omitted intentionally
        public string GameId { get; set; } = string.Empty;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (!Guid.TryParse(settings.GameId, out var gameId))
        {
            console.MarkupLine("[red]Error: Invalid game ID format.[/]");
            return 1;
        }

        logger.LogInformation("Playing game {GameId}", gameId);

        var game = await gameRepository.GetByIdAsync(gameId);

        if (game is null)
        {
            logger.LogWarning("Game {GameId} not found", gameId);
            console.MarkupLine($"[red]Game {settings.GameId} not found.[/]");
            return 1;
        }
        if (game.Status == GameStatus.Completed)
        {
            logger.LogInformation("Game {GameId} already completed", gameId);

            console.MarkupLine("[yellow]Game is already completed.[/]");
            return 0;
        }

        var team1Name = game.Participant1.Team.Name;
        var team2Name = game.Participant2.Team.Name;

        console.Write(new Rule($"[bold]Team Game[/]  {Markup.Escape(team1Name)} vs {Markup.Escape(team2Name)}"));

        // Determine starting TP
        var currentTurningPoint = await turningPointRepository.GetCurrentAsync(game.Id);
        var startTurningPointNumber = currentTurningPoint?.Number ?? 1;

        for (var turningPointNumber = startTurningPointNumber; turningPointNumber <= 4; turningPointNumber++)
        {
            logger.LogDebug("Starting TP {TpNumber} for game {GameId}", turningPointNumber, gameId);

            TurningPoint activeTurningPoint;

            if (currentTurningPoint is not null && currentTurningPoint.Number == turningPointNumber)
            {
                if (!currentTurningPoint.IsStrategyPhaseComplete)
                {
                    // Strategy phase was interrupted — mark complete and proceed
                    console.MarkupLine($"[yellow]Resuming TP {turningPointNumber}: marking strategy phase complete.[/]");

                    await turningPointRepository.CompleteStrategyPhaseAsync(currentTurningPoint.Id);

                    currentTurningPoint.IsStrategyPhaseComplete = true;
                }

                activeTurningPoint = currentTurningPoint;
            }
            else
            {
                // Run strategy phase — creates a new TP
                activeTurningPoint = await strategyPhaseOrchestrator.RunAsync(game, turningPointNumber);
                game = (await gameRepository.GetByIdAsync(game.Id))!;
            }

            // Run firefight phase
            await firefightPhaseOrchestrator.RunAsync(game, activeTurningPoint);

            logger.LogDebug("Completed TP {TpNumber} for game {GameId}", turningPointNumber, gameId);

            // Check if game ended
            game = (await gameRepository.GetByIdAsync(game.Id))!;

            if (game.Status == GameStatus.Completed)
            {
                break;
            }

            // Prepare for next TP
            currentTurningPoint = await turningPointRepository.GetCurrentAsync(game.Id);
        }

        if (game.Status != GameStatus.Completed)
        {
            return 0;
        }

        console.Write(new Rule("[bold green]Game Complete![/]"));
        var winner = game.WinnerTeamId is null
            ? "Draw"
            : game.WinnerTeamId == game.Participant1.Team.Id
                ? $"{team1Name} wins"
                : $"{team2Name} wins";

        logger.LogInformation("Game {GameId} completed. Winner: {Winner}", gameId, winner);
        console.MarkupLine($"Result: [bold]{Markup.Escape(winner)}[/]  |  {Markup.Escape(team1Name)}: {game.Participant1.VictoryPoints} VP  |  {Markup.Escape(team2Name)}: {game.Participant2.VictoryPoints} VP");

        return 0;
    }
}
