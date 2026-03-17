using System.ComponentModel;
using KillTeam.DataSlate.Domain.Repositories;
using Spectre.Console;
using Spectre.Console.Cli;

namespace KillTeam.DataSlate.Console.Commands;

/// <summary>Lists completed games, optionally filtered by player name.</summary>
[Description("List completed games, optionally filtered by player.")]
public class HistoryCommand(IGameRepository games) : AsyncCommand<HistoryCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [Description("Filter results to games involving this player (case-insensitive).")]
        [CommandOption("--player <name>")]
        public string? PlayerName { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var rows = await games.GetHistoryAsync(settings.PlayerName);

        if (rows.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No games recorded yet.[/]");

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
                $"{Markup.Escape(row.PlayerAName)} ({Markup.Escape(row.TeamAName)})",
                $"{Markup.Escape(row.PlayerBName)} ({Markup.Escape(row.TeamBName)})",
                row.VictoryPointsA.ToString(),
                row.VictoryPointsB.ToString(),
                Markup.Escape(row.WinnerTeamName ?? "—"));
        }

        AnsiConsole.Write(table);

        return 0;
    }
}
