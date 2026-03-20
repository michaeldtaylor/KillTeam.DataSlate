using KillTeam.DataSlate.Domain.Engine;
using KillTeam.DataSlate.Domain.Models;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace KillTeam.DataSlate.Console.Orchestrators;

public class StrategyPhaseOrchestrator(IAnsiConsole console, StrategyPhaseEngine engine, ILogger<StrategyPhaseOrchestrator> logger)
{
    /// <summary>
    /// Runs the Strategy Phase for the given turning point number.
    /// Creates the TurningPoint record, handles initiative, CP gains, and ploy recording.
    /// Returns the created TurningPoint.
    /// </summary>
    public async Task<TurningPoint> RunAsync(
        Game game,
        int turningPointNumber,
        string team1Name,
        string team2Name)
    {
        logger.LogDebug("Strategy phase TP{TpNumber} started for game {GameId}", turningPointNumber, game.Id);

        console.Write(new Rule($"[bold]Turning Point {turningPointNumber} — Strategy Phase[/]"));

        var turningPoint = await engine.RunAsync(game, turningPointNumber, team1Name, team2Name);

        var commandPoints1 = game.Participant1.CommandPoints;
        var commandPoints2 = game.Participant2.CommandPoints;

        console.MarkupLine(FormatCommandPoint(team1Name, commandPoints1) + "  " + FormatCommandPoint(team2Name, commandPoints2));
        console.MarkupLine("[dim]Strategy Phase complete.[/]");

        logger.LogDebug("Strategy phase TP{TpNumber} complete", turningPointNumber);

        return turningPoint;
    }

    private static string FormatCommandPoint(string teamName, int commandPoint)
    {
        var color = commandPoint switch
        {
            >= 3 => "white",
            1 or 2 => "yellow",
            _ => "red",
        };

        return $"{Markup.Escape(teamName)}: [{color}][{commandPoint}CP][/{color}]";
    }
}
