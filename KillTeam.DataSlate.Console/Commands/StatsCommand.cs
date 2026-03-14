using System.ComponentModel;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace KillTeam.DataSlate.Console.Commands;

/// <summary>Displays win/loss statistics per player or per kill team.</summary>
[Description("Show win/loss statistics per player or per kill team.")]
public class StatsCommand(IConfiguration config) : AsyncCommand<StatsCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [Description("Show statistics for a specific kill team (case-insensitive).")]
        [CommandOption("--team <name>")]
        public string? TeamName { get; set; }

        [Description("Filter statistics to a specific player (case-insensitive).")]
        [CommandOption("--player <name>")]
        public string? PlayerName { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var dbPath = config["DataSlate:DatabasePath"] ?? "./data/kill-team.db";
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        if (!string.IsNullOrWhiteSpace(settings.TeamName))
            return await ShowTeamStatsAsync(conn, settings.TeamName);

        return await ShowPlayerStatsAsync(conn, settings.PlayerName);
    }

    private static async Task<int> ShowTeamStatsAsync(SqliteConnection conn, string teamName)
    {
        using var teamCmd = conn.CreateCommand();
        teamCmd.CommandText = "SELECT id, name, faction FROM kill_teams WHERE name LIKE @name COLLATE NOCASE LIMIT 1";
        teamCmd.Parameters.AddWithValue("@name", teamName);
        string? teamId = null, resolvedName = null, faction = null;
        using (var r = await teamCmd.ExecuteReaderAsync())
        {
            if (await r.ReadAsync())
            {
                teamId = r.GetString(0);
                resolvedName = r.GetString(1);
                faction = r.GetString(2);
            }
        }

        if (teamId is null)
        {
            AnsiConsole.MarkupLine($"[red]Team '{Markup.Escape(teamName)}' not found.[/]");
            return 1;
        }

        // Games played + wins
        using var statsCmd = conn.CreateCommand();
        statsCmd.CommandText = """
            SELECT 
                COUNT(*) as games,
                SUM(CASE WHEN winner_team_id = @tid THEN 1 ELSE 0 END) as wins
            FROM games
            WHERE (team_a_id = @tid OR team_b_id = @tid) AND status = 'Completed'
            """;
        statsCmd.Parameters.AddWithValue("@tid", teamId);
        int games = 0, wins = 0;
        using (var r = await statsCmd.ExecuteReaderAsync())
        {
            if (await r.ReadAsync()) { games = r.GetInt32(0); wins = r.GetInt32(1); }
        }

        // Total kills
        using var killCmd = conn.CreateCommand();
        killCmd.CommandText = """
            SELECT COUNT(*) FROM actions a
            JOIN activations act ON act.id = a.activation_id
            JOIN turning_points tp ON tp.id = act.turning_point_id
            JOIN games g ON g.id = tp.game_id
            WHERE a.caused_incapacitation = 1 AND act.team_id = @tid
            UNION ALL
            SELECT COUNT(*) FROM action_blast_targets abt
            JOIN actions a2 ON a2.id = abt.action_id
            JOIN activations act2 ON act2.id = a2.activation_id
            JOIN turning_points tp2 ON tp2.id = act2.turning_point_id
            JOIN games g2 ON g2.id = tp2.game_id
            WHERE abt.caused_incapacitation = 1 AND act2.team_id = @tid
            """;
        killCmd.Parameters.AddWithValue("@tid", teamId);
        int kills = 0;
        using (var r = await killCmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) kills += r.GetInt32(0);
        }

        // Most used weapon
        using var weaponCmd = conn.CreateCommand();
        weaponCmd.CommandText = """
            SELECT w.name, COUNT(*) as uses
            FROM actions a
            JOIN activations act ON act.id = a.activation_id
            JOIN weapons w ON w.id = a.weapon_id
            WHERE a.type IN ('Shoot', 'Fight') AND act.team_id = @tid AND a.weapon_id IS NOT NULL
            GROUP BY a.weapon_id
            ORDER BY uses DESC
            LIMIT 1
            """;
        weaponCmd.Parameters.AddWithValue("@tid", teamId);
        string mostUsedWeapon = "—";
        using (var r = await weaponCmd.ExecuteReaderAsync())
        {
            if (await r.ReadAsync()) mostUsedWeapon = r.GetString(0);
        }

        int losses = games - wins;
        var winPct = games > 0 ? $"{wins * 100 / games}%" : "—";

        AnsiConsole.MarkupLine($"[bold]{Markup.Escape(resolvedName!)}[/] ({Markup.Escape(faction!)})");
        var t = new Table()
            .AddColumn("Games").AddColumn("Wins").AddColumn("Losses")
            .AddColumn("Win %").AddColumn("Kills").AddColumn("Most Used Weapon");
        t.AddRow(games.ToString(), wins.ToString(), losses.ToString(),
            winPct, kills.ToString(), Markup.Escape(mostUsedWeapon));
        AnsiConsole.Write(t);
        return 0;
    }

    private static async Task<int> ShowPlayerStatsAsync(SqliteConnection conn, string? playerFilter)
    {
        using var playersCmd = conn.CreateCommand();
        playersCmd.CommandText = "SELECT id, name FROM players";
        if (!string.IsNullOrWhiteSpace(playerFilter))
        {
            playersCmd.CommandText += " WHERE name LIKE @filter COLLATE NOCASE";
            playersCmd.Parameters.AddWithValue("@filter", $"%{playerFilter}%");
        }
        playersCmd.CommandText += " ORDER BY name";

        var players = new List<(string Id, string Name)>();
        using (var r = await playersCmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                players.Add((r.GetString(0), r.GetString(1)));

        if (players.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No players registered yet.[/]");
            return 0;
        }

        var table = new Table()
            .AddColumn("Player")
            .AddColumn(new TableColumn("Games").Centered())
            .AddColumn(new TableColumn("Wins").Centered())
            .AddColumn(new TableColumn("Win %").Centered());

        foreach (var (id, name) in players)
        {
            using var gCmd = conn.CreateCommand();
            gCmd.CommandText = """
                SELECT 
                    COUNT(*) as games,
                    SUM(CASE 
                        WHEN player_a_id = @id AND winner_team_id = team_a_id THEN 1
                        WHEN player_b_id = @id AND winner_team_id = team_b_id THEN 1
                        ELSE 0 END) as wins
                FROM games WHERE (player_a_id = @id OR player_b_id = @id) AND status = 'Completed'
                """;
            gCmd.Parameters.AddWithValue("@id", id);
            int g = 0, w = 0;
            using (var r = await gCmd.ExecuteReaderAsync())
                if (await r.ReadAsync()) { g = r.GetInt32(0); w = r.GetInt32(1); }

            var pct = g > 0 ? $"{w * 100 / g}%" : "—";
            table.AddRow(Markup.Escape(name), g.ToString(), w.ToString(), pct);
        }

        AnsiConsole.Write(table);
        return 0;
    }
}
