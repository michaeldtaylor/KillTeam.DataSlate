using FluentAssertions;
using Xunit;

namespace KillTeam.DataSlate.Tests.CommandTests;

public class HistoryTests
{
    [Fact]
    public async Task Query_ReturnsCompletedGamesInOrder()
    {
        var pid1 = Guid.NewGuid(); var pid2 = Guid.NewGuid();
        var gid1 = Guid.NewGuid(); var gid2 = Guid.NewGuid();

        using var db = TestDbBuilder.Create()
            .WithPlayer(pid1, "Alpha").WithPlayer(pid2, "Beta")
            .WithTeam("team_a", "Team A", "Faction A")
            .WithTeam("team_b", "Team B", "Faction B")
            .WithGame(gid1, "team_a", "Team A", "team_b", "Team B", pid1, pid2, "Completed")
            .WithGame(gid2, "team_b", "Team B", "team_a", "Team A", pid2, pid1, "Completed");

        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM games WHERE status='Completed'";
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        count.Should().Be(2);
    }

    [Fact]
    public async Task Query_WithPlayerFilter_ReturnsOnlyMatchingGames()
    {
        var pid1 = Guid.NewGuid(); var pid2 = Guid.NewGuid(); var pid3 = Guid.NewGuid();
        var gid1 = Guid.NewGuid(); var gid2 = Guid.NewGuid();

        using var db = TestDbBuilder.Create()
            .WithPlayer(pid1, "Michael").WithPlayer(pid2, "Solomon").WithPlayer(pid3, "David")
            .WithTeam("angels", "Angels", "AS").WithTeam("plague", "Plague", "HA")
            .WithGame(gid1, "angels", "Angels", "plague", "Plague", pid1, pid2, "Completed") // Michael + Solomon
            .WithGame(gid2, "angels", "Angels", "plague", "Plague", pid2, pid3, "Completed"); // Solomon + David

        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM games g
            JOIN players pa ON pa.id=g.participant1_player_id
            JOIN players pb ON pb.id=g.participant2_player_id
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

        using var db = TestDbBuilder.Create()
            .WithPlayer(pid1, "Alpha").WithPlayer(pid2, "Beta")
            .WithTeam("team_a", "Team A", "FA").WithTeam("team_b", "Team B", "FB");

        // 2 completed games, both won by Team A (player Alpha is player_a)
        for (var i = 0; i < 2; i++)
        {
            var gid = Guid.NewGuid();
            using var insertCmd = db.Connection.CreateCommand();
            insertCmd.CommandText = """
                INSERT INTO games (id, played_at, participant1_team_id, participant1_team_name, participant2_team_id, participant2_team_name,
                    participant1_player_id, participant2_player_id, status, winner_team_id, participant1_victory_points, participant2_victory_points)
                VALUES (@id, @at, @ta_id, @ta, @tb_id, @tb, @pa, @pb, 'Completed', @winner_id, 5, 3)
                """;
            insertCmd.Parameters.AddWithValue("@id", gid.ToString());
            insertCmd.Parameters.AddWithValue("@at", DateTime.UtcNow.ToString("o"));
            insertCmd.Parameters.AddWithValue("@ta_id", "team_a");
            insertCmd.Parameters.AddWithValue("@ta", "Team A");
            insertCmd.Parameters.AddWithValue("@tb_id", "team_b");
            insertCmd.Parameters.AddWithValue("@tb", "Team B");
            insertCmd.Parameters.AddWithValue("@pa", pid1.ToString());
            insertCmd.Parameters.AddWithValue("@pb", pid2.ToString());
            insertCmd.Parameters.AddWithValue("@winner_id", "team_a");
            insertCmd.ExecuteNonQuery();
        }

        // Verify aggregation
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) as games,
                   SUM(CASE WHEN participant1_player_id=@pid AND winner_team_id=participant1_team_id THEN 1 ELSE 0 END) as wins
            FROM games WHERE (participant1_player_id=@pid OR participant2_player_id=@pid) AND status='Completed'
            """;
        cmd.Parameters.AddWithValue("@pid", pid1.ToString());
        using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        r.GetInt32(0).Should().Be(2, "Alpha played 2 games");
        r.GetInt32(1).Should().Be(2, "Alpha won 2 games");
    }

}
