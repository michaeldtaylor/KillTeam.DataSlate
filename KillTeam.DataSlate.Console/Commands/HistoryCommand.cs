using System.ComponentModel;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace KillTeam.DataSlate.Console.Commands;

/// <summary>Lists completed games, optionally filtered by player name.</summary>
[Description("List completed games, optionally filtered by player.")]
public class HistoryCommand(IConfiguration config) : AsyncCommand<HistoryCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [Description("Filter results to games involving this player (case-insensitive).")]
        [CommandOption("--player <name>")]
        public string? PlayerName { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var dbPath = config["DataSlate:DatabasePath"] ?? "./data/kill-team.db";
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT g.id, g.played_at, g.mission_name,
                   pa.name as player_a_name, g.team_a_name,
                   pb.name as player_b_name, g.team_b_name,
                   g.victory_points_team_a, g.victory_points_team_b,
                   CASE WHEN g.winner_team_id = g.team_a_id THEN g.team_a_name
                        WHEN g.winner_team_id = g.team_b_id THEN g.team_b_name
                        ELSE '—' END as winner
            FROM games g
            JOIN players pa ON pa.id = g.player_a_id
            JOIN players pb ON pb.id = g.player_b_id
            WHERE g.status = 'Completed'
            """;

        if (!string.IsNullOrWhiteSpace(settings.PlayerName))
        {
            cmd.CommandText += " AND (pa.name LIKE @pname OR pb.name LIKE @pname)";
            cmd.Parameters.AddWithValue("@pname", $"%{settings.PlayerName}%");
        }

        cmd.CommandText += " ORDER BY g.played_at DESC";

        var rows = new List<(string Date, string Mission, string PlayerA, string TeamA,
            string PlayerB, string TeamB, int VpA, int VpB, string Winner)>();

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var date = DateTime.TryParse(reader.GetString(1), out var dt)
                ? dt.ToString("yyyy-MM-dd") : reader.GetString(1);
            rows.Add((date, reader.IsDBNull(2) ? "—" : reader.GetString(2),
                reader.GetString(3), reader.GetString(4),
                reader.GetString(5), reader.GetString(6),
                reader.GetInt32(7), reader.GetInt32(8),
                reader.GetString(9)));
        }

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

        foreach (var r in rows)
        {
            table.AddRow(r.Date, Markup.Escape(r.Mission),
                $"{Markup.Escape(r.PlayerA)} ({Markup.Escape(r.TeamA)})",
                $"{Markup.Escape(r.PlayerB)} ({Markup.Escape(r.TeamB)})",
                r.VpA.ToString(), r.VpB.ToString(), Markup.Escape(r.Winner));
        }

        AnsiConsole.Write(table);
        return 0;
    }
}
