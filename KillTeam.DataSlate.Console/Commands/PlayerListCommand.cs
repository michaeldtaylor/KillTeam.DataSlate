using System.ComponentModel;
using KillTeam.DataSlate.Domain.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace KillTeam.DataSlate.Console.Commands;

/// <summary>Lists all registered players with their win/loss statistics.</summary>
[Description("List all registered players with win/loss stats.")]
public class PlayerListCommand(IPlayerRepository players, IConfiguration config) : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        var all = (await players.GetAllAsync()).ToList();
        if (all.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No players registered yet.[/]");
            return 0;
        }

        var dbPath = config["DataSlate:DatabasePath"] ?? "./data/kill-team.db";
        var connStr = $"Data Source={dbPath}";

        var table = new Table()
            .AddColumn("Name")
            .AddColumn(new TableColumn("Games Played").Centered())
            .AddColumn(new TableColumn("Wins").Centered())
            .AddColumn(new TableColumn("Win %").Centered());

        foreach (var p in all)
        {
            await using var conn = new SqliteConnection(connStr);
            await conn.OpenAsync();

            using var gCmd = conn.CreateCommand();
            gCmd.CommandText = "SELECT COUNT(*) FROM games WHERE player_a_id=@id OR player_b_id=@id";
            gCmd.Parameters.AddWithValue("@id", p.Id.ToString());
            var games = Convert.ToInt32(await gCmd.ExecuteScalarAsync());

            using var wCmd = conn.CreateCommand();
            wCmd.CommandText = """
                SELECT COUNT(*) FROM games
                WHERE (player_a_id=@id AND winner_team_id=team_a_id)
                   OR (player_b_id=@id AND winner_team_id=team_b_id)
                """;
            wCmd.Parameters.AddWithValue("@id", p.Id.ToString());
            var wins = Convert.ToInt32(await wCmd.ExecuteScalarAsync());

            var pct = games == 0 ? "—" : $"{wins * 100 / games}%";
            table.AddRow(Markup.Escape(p.Name), games.ToString(), wins.ToString(), pct);
        }

        AnsiConsole.Write(table);
        return 0;
    }
}
