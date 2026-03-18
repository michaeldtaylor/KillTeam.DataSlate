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
public class PlayCommand(
    IAnsiConsole console,
    IGameRepository gameRepository,
    ITurningPointRepository turningPointRepository,
    ITeamRepository teamRepository,
    StrategyPhaseOrchestrator strategyPhaseOrchestrator,
    FirefightPhaseOrchestrator firefightPhaseOrchestrator,
    ILogger<PlayCommand> logger) : AsyncCommand<PlayCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [Description("The ID of the game to play or resume.")]
        [CommandArgument(0, "<game-id>")]
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

        var team1 = await teamRepository.GetByIdAsync(game.Participant1.TeamId);
        var team2 = await teamRepository.GetByIdAsync(game.Participant2.TeamId);
        var team1Name = team1?.Name ?? game.Participant1.TeamName;
        var team2Name = team2?.Name ?? game.Participant2.TeamName;

        console.Write(new Rule($"[bold]Team Game[/]  {Markup.Escape(team1Name)} vs {Markup.Escape(team2Name)}"));

        // Determine starting TP
        var currentTp = await turningPointRepository.GetCurrentAsync(game.Id);
        var startTpNumber = currentTp?.Number ?? 1;

        for (var tpNumber = startTpNumber; tpNumber <= 4; tpNumber++)
        {
            logger.LogDebug("Starting TP {TpNumber} for game {GameId}", tpNumber, gameId);
            TurningPoint activeTp;

            if (currentTp is not null && currentTp.Number == tpNumber)
            {
                if (!currentTp.IsStrategyPhaseComplete)
                {
                    // Strategy phase was interrupted — mark complete and proceed
                    console.MarkupLine($"[yellow]Resuming TP {tpNumber}: marking strategy phase complete.[/]");
                    await turningPointRepository.CompleteStrategyPhaseAsync(currentTp.Id);
                    currentTp.IsStrategyPhaseComplete = true;
                }
                activeTp = currentTp;
            }
            else
            {
                // Run strategy phase — creates a new TP
                activeTp = await strategyPhaseOrchestrator.RunAsync(game, tpNumber, team1Name, team2Name);
                game = (await gameRepository.GetByIdAsync(game.Id))!;
            }

            // Run firefight phase
            await firefightPhaseOrchestrator.RunAsync(game, activeTp);
            logger.LogDebug("Completed TP {TpNumber} for game {GameId}", tpNumber, gameId);

            // Check if game ended
            game = (await gameRepository.GetByIdAsync(game.Id))!;
        if (game.Status == GameStatus.Completed)
        {
            break;
        }

            // Prepare for next TP
            currentTp = await turningPointRepository.GetCurrentAsync(game.Id);
        }

        if (game.Status == GameStatus.Completed)
        {
            console.Write(new Rule("[bold green]Game Complete![/]"));
            var winner = game.WinnerTeamId is null
                ? "Draw"
                : game.WinnerTeamId == game.Participant1.TeamId
                    ? $"{team1Name} wins"
                    : $"{team2Name} wins";
            logger.LogInformation("Game {GameId} completed. Winner: {Winner}", gameId, winner);
            console.MarkupLine($"Result: [bold]{Markup.Escape(winner)}[/]  |  {Markup.Escape(team1Name)}: {game.Participant1.VictoryPoints} VP  |  {Markup.Escape(team2Name)}: {game.Participant2.VictoryPoints} VP");
        }

        return 0;
    }
}
