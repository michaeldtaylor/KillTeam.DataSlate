using System.ComponentModel;
using KillTeam.DataSlate.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace KillTeam.DataSlate.Console.Commands;

/// <summary>Lists completed games, optionally filtered by player name.</summary>
[Description("List completed games, optionally filtered by player.")]
public class HistoryCommand(IAnsiConsole console, IGameRepository games, ILogger<HistoryCommand> logger) : AsyncCommand<HistoryCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [Description("Filter results to games involving this player (case-insensitive).")]
        [CommandOption("--player <name>")]
        public string? PlayerName { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        logger.LogDebug("Listing game history. PlayerFilter={Player}", settings.PlayerName ?? "(all)");
        var rows = await games.GetHistoryAsync(settings.PlayerName);

        if (rows.Count == 0)
        {
            console.MarkupLine("[dim]No games recorded yet.[/]");

            return 0;
        }

        var table = new Table()
            .AddColumn("Date")
            .AddColumn("Mission")
            .AddColumn("Player A (Team)")
            .AddColumn("Player B (Team)")
            .AddColumn(new TableColumn("Score A").Centered())
            .AddColumn(new TableColumn("Score B").Centered())
            .AddColumn("Winner");

        foreach (var row in rows)
        {
            var date = DateTime.TryParse(row.PlayedAt, out var parsed)
                ? parsed.ToString("yyyy-MM-dd") : row.PlayedAt;

            table.AddRow(
                date,
                Markup.Escape(row.MissionName ?? "—"),
                $"{Markup.Escape(row.Player1Name)} ({Markup.Escape(row.Team1Name)})",
                $"{Markup.Escape(row.Player2Name)} ({Markup.Escape(row.Team2Name)})",
                row.VictoryPoints1.ToString(),
                row.VictoryPoints2.ToString(),
                Markup.Escape(row.WinnerTeamName ?? "—"));
        }

        console.Write(table);

        return 0;
    }
}
