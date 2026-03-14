using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KillTeam.DataSlate.Tests.CommandTests;

public class HistoryTests
{
    [Fact]
    public async Task Query_ReturnsCompletedGamesInOrder()
    {
        var pid1 = Guid.NewGuid(); var pid2 = Guid.NewGuid();
        var tid1 = Guid.NewGuid(); var tid2 = Guid.NewGuid();
        var gid1 = Guid.NewGuid(); var gid2 = Guid.NewGuid();

        using var db = TestDbBuilder.Create()
            .WithPlayer(pid1, "Alpha").WithPlayer(pid2, "Beta")
            .WithTeam("Team A", "Faction A")
            .WithTeam("Team B", "Faction B")
            .WithGame(gid1, "Team A", "Team B", pid1, pid2, "Completed")
            .WithGame(gid2, "Team B", "Team A", pid2, pid1, "Completed");

        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM games WHERE status='Completed'";
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        count.Should().Be(2);
    }

    [Fact]
    public async Task Query_WithPlayerFilter_ReturnsOnlyMatchingGames()
    {
        var pid1 = Guid.NewGuid(); var pid2 = Guid.NewGuid(); var pid3 = Guid.NewGuid();
        var tid1 = Guid.NewGuid(); var tid2 = Guid.NewGuid();
        var gid1 = Guid.NewGuid(); var gid2 = Guid.NewGuid();

        using var db = TestDbBuilder.Create()
            .WithPlayer(pid1, "Michael").WithPlayer(pid2, "Solomon").WithPlayer(pid3, "David")
            .WithTeam("Angels", "AS").WithTeam("Plague", "HA")
            .WithGame(gid1, "Angels", "Plague", pid1, pid2, "Completed") // Michael + Solomon
            .WithGame(gid2, "Angels", "Plague", pid2, pid3, "Completed"); // Solomon + David

        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM games g
            JOIN players pa ON pa.id=g.player_a_id
            JOIN players pb ON pb.id=g.player_b_id
            WHERE g.status='Completed' AND (pa.name LIKE '%Michael%' OR pb.name LIKE '%Michael%')
            """;
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        count.Should().Be(1, "only Michael's game should be returned");
    }

    [Fact]
    public void Query_NoGames_ReturnsEmptyResult()
    {
        using var db = TestDbBuilder.Create();
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM games WHERE status='Completed'";
        Convert.ToInt32(cmd.ExecuteScalar()).Should().Be(0);
    }
}

public class StatsTests
{
    [Fact]
    public async Task PerPlayerStats_CalculatesWinsAndGamesPlayed()
    {
        var pid1 = Guid.NewGuid(); var pid2 = Guid.NewGuid();
        var tid1 = Guid.NewGuid(); var tid2 = Guid.NewGuid();

        using var db = TestDbBuilder.Create()
            .WithPlayer(pid1, "Alpha").WithPlayer(pid2, "Beta")
            .WithTeam("Team A", "FA").WithTeam("Team B", "FB");

        // 2 completed games, both won by Team A (player Alpha is player_a)
        for (int i = 0; i < 2; i++)
        {
            var gid = Guid.NewGuid();
            using var insertCmd = db.Connection.CreateCommand();
            insertCmd.CommandText = """
                INSERT INTO games (id, played_at, team_a_name, team_b_name, player_a_id, player_b_id,
                    status, winner_team_name, victory_points_team_a, victory_points_team_b)
                VALUES (@id, @at, @ta, @tb, @pa, @pb, 'Completed', @winner, 5, 3)
                """;
            insertCmd.Parameters.AddWithValue("@id", gid.ToString());
            insertCmd.Parameters.AddWithValue("@at", DateTime.UtcNow.ToString("o"));
            insertCmd.Parameters.AddWithValue("@ta", "Team A");
            insertCmd.Parameters.AddWithValue("@tb", "Team B");
            insertCmd.Parameters.AddWithValue("@pa", pid1.ToString());
            insertCmd.Parameters.AddWithValue("@pb", pid2.ToString());
            insertCmd.Parameters.AddWithValue("@winner", "Team A");
            insertCmd.ExecuteNonQuery();
        }

        // Verify aggregation
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) as games,
                   SUM(CASE WHEN player_a_id=@pid AND winner_team_name=team_a_name THEN 1 ELSE 0 END) as wins
            FROM games WHERE (player_a_id=@pid OR player_b_id=@pid) AND status='Completed'
            """;
        cmd.Parameters.AddWithValue("@pid", pid1.ToString());
        using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        r.GetInt32(0).Should().Be(2, "Alpha played 2 games");
        r.GetInt32(1).Should().Be(2, "Alpha won 2 games");
    }

    [Fact]
    public void PerTeamStats_KillsCount_IncludesBlastTargetIncapacitations()
    {
        var pid = Guid.NewGuid();
        var tid = Guid.NewGuid(); var tid2 = Guid.NewGuid();
        var opId = Guid.NewGuid(); var targetId = Guid.NewGuid();
        var gameId = Guid.NewGuid(); var tpId = Guid.NewGuid();
        var actId = Guid.NewGuid(); var actionId = Guid.NewGuid();

        using var db = TestDbBuilder.Create()
            .WithPlayer(pid, "Alpha")
            .WithTeam("Team A", "FA").WithTeam("Team B", "FB")
            .WithOperative(opId, "Team A", "Shooter", wounds: 13, save: 3, apl: 3, move: 3)
            .WithOperative(targetId, "Team B", "Target", wounds: 13, save: 3, apl: 2, move: 3)
            .WithGame(gameId, "Team A", "Team B", pid, pid)
            .WithTurningPoint(tpId, gameId, 1)
            .WithActivation(actId, tpId, 1, opId, "Team A");

        // Insert action
        using var aCmd = db.Connection.CreateCommand();
        aCmd.CommandText = "INSERT INTO actions (id, activation_id, type, ap_cost) VALUES (@id, @act, 'Shoot', 1)";
        aCmd.Parameters.AddWithValue("@id", actionId.ToString());
        aCmd.Parameters.AddWithValue("@act", actId.ToString());
        aCmd.ExecuteNonQuery();

        // Insert blast target with incapacitation
        var btId = Guid.NewGuid();
        using var btCmd = db.Connection.CreateCommand();
        btCmd.CommandText = """
            INSERT INTO action_blast_targets
            (id, action_id, target_operative_id, operative_name, caused_incapacitation)
            VALUES (@id, @aid, @tgt, 'Target', 1)
            """;
        btCmd.Parameters.AddWithValue("@id", btId.ToString());
        btCmd.Parameters.AddWithValue("@aid", actionId.ToString());
        btCmd.Parameters.AddWithValue("@tgt", targetId.ToString());
        btCmd.ExecuteNonQuery();

        // Verify kill count query
        using var killCmd = db.Connection.CreateCommand();
        killCmd.CommandText = """
            SELECT COUNT(*) FROM action_blast_targets abt
            JOIN actions a ON a.id = abt.action_id
            JOIN activations act ON act.id = a.activation_id
            WHERE abt.caused_incapacitation = 1 AND act.team_name = @tname
            """;
        killCmd.Parameters.AddWithValue("@tname", "Team A");
        Convert.ToInt32(killCmd.ExecuteScalar()).Should().Be(1);
    }
}
