using System.ComponentModel;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;
using KillTeam.DataSlate.Domain.Repositories;

namespace KillTeam.DataSlate.Console.Commands;

/// <summary>Displays the full detail of a game: turning points, activations, actions, dice, and narrative notes.</summary>
[Description("View full details of a game — turning points, activations, actions, and notes.")]
public class ViewGameCommand(
    IActivationRepository activations,
    IActionRepository actions,
    IPloyRepository ploys,
    IConfiguration config) : AsyncCommand<ViewGameCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [Description("The ID of the game to view.")]
        [CommandArgument(0, "<game-id>")]
        public string GameId { get; set; } = string.Empty;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (!Guid.TryParse(settings.GameId, out var gameId))
        {
            AnsiConsole.MarkupLine("[red]Invalid game ID format.[/]");
            return 1;
        }

        var dbPath = config["DataSlate:DatabasePath"] ?? "./data/kill-team.db";
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        // Load game header
        using var gameCmd = conn.CreateCommand();
        gameCmd.CommandText = """
            SELECT g.status, g.mission_name, g.victory_points_team_a, g.victory_points_team_b,
                   pa.name, ta.name, pb.name, tb.name,
                   CASE WHEN g.winner_team_name = g.team_a_name THEN ta.name
                        WHEN g.winner_team_name = g.team_b_name THEN tb.name
                        ELSE NULL END
            FROM games g
            JOIN players pa ON pa.id = g.player_a_id
            JOIN players pb ON pb.id = g.player_b_id
            JOIN kill_teams ta ON ta.name = g.team_a_name
            JOIN kill_teams tb ON tb.name = g.team_b_name
            WHERE g.id = @id
            """;
        gameCmd.Parameters.AddWithValue("@id", gameId.ToString());

        string? status, missionName, playerA, teamA, playerB, teamB, winner;
        int vpA, vpB;

        using (var r = await gameCmd.ExecuteReaderAsync())
        {
            if (!await r.ReadAsync())
            {
                AnsiConsole.MarkupLine($"[red]Game {Markup.Escape(settings.GameId)} not found.[/]");
                return 1;
            }
            status = r.GetString(0);
            missionName = r.IsDBNull(1) ? null : r.GetString(1);
            vpA = r.GetInt32(2); vpB = r.GetInt32(3);
            playerA = r.GetString(4); teamA = r.GetString(5);
            playerB = r.GetString(6); teamB = r.GetString(7);
            winner = r.IsDBNull(8) ? null : r.GetString(8);
        }

        AnsiConsole.MarkupLine($"[bold]=== {Markup.Escape(playerA)} ({Markup.Escape(teamA)}) vs {Markup.Escape(playerB)} ({Markup.Escape(teamB)}) ===[/]");
        if (missionName is not null)
            AnsiConsole.MarkupLine($"Mission: {Markup.Escape(missionName)}");

        // Load turning points
        using var tpCmd = conn.CreateCommand();
        tpCmd.CommandText = """
            SELECT tp.id, tp.number, kt.name
            FROM turning_points tp
            LEFT JOIN kill_teams kt ON kt.name = tp.team_with_initiative_name
            WHERE tp.game_id = @gid ORDER BY tp.number
            """;
        tpCmd.Parameters.AddWithValue("@gid", gameId.ToString());

        var tpList = new List<(Guid Id, int Number, string? InitTeam)>();
        using (var r = await tpCmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                tpList.Add((Guid.Parse(r.GetString(0)), r.GetInt32(1), r.IsDBNull(2) ? null : r.GetString(2)));

        foreach (var (tpId, tpNum, initTeam) in tpList)
        {
            AnsiConsole.MarkupLine($"\n[bold]=== Turning Point {tpNum} ===[/]");
            if (initTeam is not null)
                AnsiConsole.MarkupLine($"  Initiative: {Markup.Escape(initTeam)}");

            // Show ploys
            var ployList = (await ploys.GetByTurningPointAsync(tpId)).ToList();
            foreach (var p in ployList)
            {
                var teamLabel = p.TeamName;
                AnsiConsole.MarkupLine($"  [dim]Ploy:[/] {Markup.Escape(p.PloyName)} ({Markup.Escape(teamLabel)}, {p.CpCost}CP)" +
                    (p.Description is not null ? $" — {Markup.Escape(p.Description)}" : ""));
            }

            // Show activations
            var actList = (await activations.GetByTurningPointAsync(tpId)).ToList();
            foreach (var act in actList)
            {
                var opName = await GetOperativeNameAsync(conn, act.OperativeId);
                var flags = new List<string>();
                if (act.IsCounteract) flags.Add("Counteract");
                if (act.IsGuardInterrupt) flags.Add("Guard Interrupt");
                var flagStr = flags.Count > 0 ? $" [dim]({string.Join(", ", flags)})[/]" : "";
                AnsiConsole.MarkupLine($"  [Act {act.SequenceNumber}] {Markup.Escape(opName)} ({act.OrderSelected}){flagStr}");
                if (act.NarrativeNote is not null)
                    AnsiConsole.MarkupLine($"    [dim]🖊 {Markup.Escape(act.NarrativeNote)}[/]");

                // Show actions
                var actionList = (await actions.GetByActivationAsync(act.Id)).ToList();
                foreach (var a in actionList)
                {
                    var targetName = a.TargetOperativeId.HasValue
                        ? await GetOperativeNameAsync(conn, a.TargetOperativeId.Value) : null;
                    var dmg = a.NormalDamageDealt + a.CriticalDamageDealt;
                    var coverStr = a.TargetInCover == true ? " [dim](cover)[/]" : "";
                    var obscStr = a.IsObscured == true ? " [dim](obscured)[/]" : "";
                    var incapStr = a.CausedIncapacitation ? " [red](Incapacitated!)[/]" : "";
                    var targetStr = targetName is not null ? $" → {Markup.Escape(targetName)}" : "";
                    AnsiConsole.MarkupLine($"    {a.Type}{targetStr}: {dmg} dmg{coverStr}{obscStr}{incapStr}");
                    if (a.NarrativeNote is not null)
                        AnsiConsole.MarkupLine($"      [dim]🖊 {Markup.Escape(a.NarrativeNote)}[/]");
                }
            }
        }

        // Final score
        AnsiConsole.WriteLine();
        if (status == "Completed" && winner is not null)
            AnsiConsole.MarkupLine($"[bold]Final Score:[/] {teamA} {vpA} — {vpB} {teamB}  |  Winner: [green]{Markup.Escape(winner)}[/]");
        else
            AnsiConsole.MarkupLine($"[dim](In Progress — TP{tpList.LastOrDefault().Number})[/]");

        return 0;
    }

    private static async Task<string> GetOperativeNameAsync(SqliteConnection conn, Guid id)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM operatives WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        return await cmd.ExecuteScalarAsync() as string ?? id.ToString()[..8];
    }
}
