using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using Spectre.Console;

namespace KillTeam.DataSlate.Console.Orchestrators;

public class StrategyPhaseOrchestrator(
    IGameRepository gameRepository,
    ITurningPointRepository turningPointRepository,
    IPloyRepository ployRepository)
{
    /// <summary>
    /// Runs the Strategy Phase for the given turning point number.
    /// Creates the TurningPoint record, handles initiative, CP gains, and ploy recording.
    /// Returns the created TurningPoint.
    /// </summary>
    public async Task<TurningPoint> RunAsync(Game game, int tpNumber,
        string teamAName, string teamBName)
    {
        AnsiConsole.Write(new Rule($"[bold]Turning Point {tpNumber} — Strategy Phase[/]"));

        // ─── 1. Initiative prompt ─────────────────────────────────────────────
        string initiativeTeamId;
        while (true)
        {
            var winner = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Who won initiative? (or T for a tie roll)")
                    .AddChoices(teamAName, teamBName, "Tie — re-roll"));

            if (winner == "Tie — re-roll")
            {
                AnsiConsole.MarkupLine("[dim]Tie! Both players re-roll...[/]");
                continue;
            }

            initiativeTeamId = winner == teamAName ? game.TeamA.TeamId : game.TeamB.TeamId;
            break;
        }

        // ─── 2. Create TurningPoint record ────────────────────────────────────
        var tp = new TurningPoint
        {
            Id = Guid.NewGuid(),
            GameId = game.Id,
            Number = tpNumber,
            TeamWithInitiativeId = initiativeTeamId
        };
        tp = await turningPointRepository.CreateAsync(tp);

        // ─── 3. CP gains ──────────────────────────────────────────────────────
        var cpA = game.TeamA.CommandPoints;
        var cpB = game.TeamB.CommandPoints;

        if (tpNumber == 1)
        {
            cpA += 1;
            cpB += 1;
        }
        else
        {
            // Initiative team +1CP, other team +2CP
            if (initiativeTeamId == game.TeamA.TeamId)
            {
                cpA += 1;
                cpB += 2;
            }
            else
            {
                cpA += 2;
                cpB += 1;
            }
        }

        await gameRepository.UpdateCpAsync(game.Id, cpA, cpB);
        game.TeamA.CommandPoints = cpA;
        game.TeamB.CommandPoints = cpB;

        AnsiConsole.MarkupLine(FormatCp(teamAName, cpA) + "  " + FormatCp(teamBName, cpB));

        // ─── 5. Ploy recording — non-initiative player first ─────────────────
        var (nonInitId, nonInitName) = initiativeTeamId == game.TeamA.TeamId
            ? (game.TeamB.TeamId, teamBName)
            : (game.TeamA.TeamId, teamAName);
        var (initId, initName) = initiativeTeamId == game.TeamA.TeamId
            ? (game.TeamA.TeamId, teamAName)
            : (game.TeamB.TeamId, teamBName);

        (cpA, cpB) = await RunPloyLoopAsync(tp, game.TeamA.TeamId, game.TeamB.TeamId,
            nonInitId, nonInitName, cpA, cpB, game.Id);

        (cpA, cpB) = await RunPloyLoopAsync(tp, game.TeamA.TeamId, game.TeamB.TeamId,
            initId, initName, cpA, cpB, game.Id);

        // ─── 6. Mark strategy phase complete ──────────────────────────────────
        await turningPointRepository.CompleteStrategyPhaseAsync(tp.Id);

        AnsiConsole.MarkupLine("[dim]Strategy Phase complete.[/]");
        return tp;
    }

    private async Task<(int cpA, int cpB)> RunPloyLoopAsync(
        TurningPoint tp, string teamAId, string teamBId,
        string activeTeamId, string activeTeamDisplayName,
        int cpA, int cpB, Guid gameId)
    {
        while (true)
        {
            if (!AnsiConsole.Confirm($"[bold]{Markup.Escape(activeTeamDisplayName)}[/] — record a ploy? (current CP: {(activeTeamId == teamAId ? cpA : cpB)})",
                defaultValue: false))
                break;

            var ployName = AnsiConsole.Prompt(
                new TextPrompt<string>("Ploy name:").Validate(s => !string.IsNullOrWhiteSpace(s)));

            var description = AnsiConsole.Prompt(
                new TextPrompt<string>("Description [dim](optional)[/]:").AllowEmpty());

            var cpCost = AnsiConsole.Prompt(
                new TextPrompt<int>("CP cost:")
                    .Validate(c => c >= 0 && c <= 10));

            var currentCp = activeTeamId == teamAId ? cpA : cpB;
            if (cpCost > currentCp)
            {
                AnsiConsole.MarkupLine($"[red]Not enough CP (have {currentCp}, need {cpCost}).[/]");
                continue;
            }

            await ployRepository.RecordPloyUseAsync(new PloyUse
            {
                Id = Guid.NewGuid(),
                TurningPointId = tp.Id,
                TeamId = activeTeamId,
                PloyName = ployName,
                Description = string.IsNullOrWhiteSpace(description) ? null : description,
                CpCost = cpCost
            });

            if (activeTeamId == teamAId)
            {
                cpA -= cpCost;
            }
            else
            {
                cpB -= cpCost;
            }

            await gameRepository.UpdateCpAsync(gameId, cpA, cpB);
            AnsiConsole.MarkupLine(FormatCp(activeTeamDisplayName, activeTeamId == teamAId ? cpA : cpB));
        }

        return (cpA, cpB);
    }

    private static string FormatCp(string teamName, int cp)
    {
        var color = cp switch { >= 3 => "white", 1 or 2 => "yellow", _ => "red" };
        return $"{Markup.Escape(teamName)}: [{color}][{cp}CP][/{color}]";
    }
}
