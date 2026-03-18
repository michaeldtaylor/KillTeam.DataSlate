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
    public async Task<TurningPoint> RunAsync(Game game, int tpNumber,
        string team1Name, string team2Name)
    {
        logger.LogDebug("Strategy phase TP{TpNumber} started for game {GameId}", tpNumber, game.Id);
        console.Write(new Rule($"[bold]Turning Point {tpNumber} — Strategy Phase[/]"));

        var turningPoint = await engine.RunAsync(game, tpNumber, team1Name, team2Name);

        var cp1 = game.Participant1.CommandPoints;
        var cp2 = game.Participant2.CommandPoints;
        console.MarkupLine(FormatCp(team1Name, cp1) + "  " + FormatCp(team2Name, cp2));
        console.MarkupLine("[dim]Strategy Phase complete.[/]");
        logger.LogDebug("Strategy phase TP{TpNumber} complete", tpNumber);

        return turningPoint;
    }

    private static string FormatCp(string teamName, int cp)
    {
        var color = cp switch { >= 3 => "white", 1 or 2 => "yellow", _ => "red" };
        return $"{Markup.Escape(teamName)}: [{color}][{cp}CP][/{color}]";
    }
}