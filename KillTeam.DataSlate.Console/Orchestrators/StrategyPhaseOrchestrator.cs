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
        string initiativeTeamName;
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

            initiativeTeamName = winner == teamAName ? game.TeamAName : game.TeamBName;
            break;
        }

        // ─── 2. Create TurningPoint record ────────────────────────────────────
        var tp = new TurningPoint
        {
            Id = Guid.NewGuid(),
            GameId = game.Id,
            Number = tpNumber,
            TeamWithInitiativeName = initiativeTeamName
        };
        tp = await turningPointRepository.CreateAsync(tp);

        // ─── 3. CP gains ──────────────────────────────────────────────────────
        var cpA = game.CpTeamA;
        var cpB = game.CpTeamB;

        if (tpNumber == 1)
        {
            cpA += 1;
            cpB += 1;
        }
        else
        {
            // Initiative team +1CP, other team +2CP
            if (initiativeTeamName == game.TeamAName)
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
        game.CpTeamA = cpA;
        game.CpTeamB = cpB;

        AnsiConsole.MarkupLine(FormatCp(teamAName, cpA) + "  " + FormatCp(teamBName, cpB));

        // ─── 5. Ploy recording — non-initiative player first ─────────────────
        var nonInitTeamName = initiativeTeamName == game.TeamAName ? teamBName : teamAName;
        var initTeamName = initiativeTeamName == game.TeamAName ? teamAName : teamBName;

        (cpA, cpB) = await RunPloyLoopAsync(tp, game.TeamAName, game.TeamBName,
            nonInitTeamName, cpA, cpB, game.Id);

        (cpA, cpB) = await RunPloyLoopAsync(tp, game.TeamAName, game.TeamBName,
            initTeamName, cpA, cpB, game.Id);

        // ─── 6. Mark strategy phase complete ──────────────────────────────────
        await turningPointRepository.CompleteStrategyPhaseAsync(tp.Id);

        AnsiConsole.MarkupLine("[dim]Strategy Phase complete.[/]");
        return tp;
    }

    private async Task<(int cpA, int cpB)> RunPloyLoopAsync(
        TurningPoint tp, string teamAName, string teamBName,
        string activeTeamName,
        int cpA, int cpB, Guid gameId)
    {
        while (true)
        {
            if (!AnsiConsole.Confirm($"[bold]{Markup.Escape(activeTeamName)}[/] — record a ploy? (current CP: {(activeTeamName == teamAName ? cpA : cpB)})",
                defaultValue: false))
                break;

            var ployName = AnsiConsole.Prompt(
                new TextPrompt<string>("Ploy name:").Validate(s => !string.IsNullOrWhiteSpace(s)));

            var description = AnsiConsole.Prompt(
                new TextPrompt<string>("Description [dim](optional)[/]:").AllowEmpty());

            var cpCost = AnsiConsole.Prompt(
                new TextPrompt<int>("CP cost:")
                    .Validate(c => c >= 0 && c <= 10));

            var currentCp = activeTeamName == teamAName ? cpA : cpB;
            if (cpCost > currentCp)
            {
                AnsiConsole.MarkupLine($"[red]Not enough CP (have {currentCp}, need {cpCost}).[/]");
                continue;
            }

            await ployRepository.RecordPloyUseAsync(new PloyUse
            {
                Id = Guid.NewGuid(),
                TurningPointId = tp.Id,
                TeamName = activeTeamName,
                PloyName = ployName,
                Description = string.IsNullOrWhiteSpace(description) ? null : description,
                CpCost = cpCost
            });

            if (activeTeamName == teamAName)
            {
                cpA -= cpCost;
            }
            else
            {
                cpB -= cpCost;
            }

            await gameRepository.UpdateCpAsync(gameId, cpA, cpB);
            AnsiConsole.MarkupLine(FormatCp(activeTeamName, activeTeamName == teamAName ? cpA : cpB));
        }

        return (cpA, cpB);
    }

    private static string FormatCp(string teamName, int cp)
    {
        var color = cp switch { >= 3 => "white", 1 or 2 => "yellow", _ => "red" };
        return $"{Markup.Escape(teamName)}: [{color}][{cp}CP][/{color}]";
    }
}
