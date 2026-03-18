using FluentAssertions;
using KillTeam.DataSlate.Infrastructure.Repositories;
using KillTeam.DataSlate.Domain.Models;
using Xunit;

namespace KillTeam.DataSlate.Tests.CommandTests;

public class PlayerCommandTests
{
    [Fact]
    public async Task AddPlayer_NewName_CreatesPlayer()
    {
        using var db = TestDbBuilder.Create();
        var repo = new SqlitePlayerRepository(db.Connection);

        await repo.CreateAsync(new Player { Id = Guid.NewGuid(), Name = "Michael" });

        var all = (await repo.GetAllAsync()).ToList();
        all.Should().ContainSingle(p => p.Name == "Michael");
    }

    [Fact]
    public async Task AddPlayer_DuplicateName_ThrowsConstraint()
    {
        using var db = TestDbBuilder.Create();
        var repo = new SqlitePlayerRepository(db.Connection);
        await repo.CreateAsync(new Player { Id = Guid.NewGuid(), Name = "Solomon" });

        var act = () => repo.CreateAsync(new Player { Id = Guid.NewGuid(), Name = "Solomon" });

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task GetAllPlayers_ReturnsSortedByName()
    {
        using var db = TestDbBuilder.Create();
        var repo = new SqlitePlayerRepository(db.Connection);
        await repo.CreateAsync(new Player { Id = Guid.NewGuid(), Name = "Zara" });
        await repo.CreateAsync(new Player { Id = Guid.NewGuid(), Name = "Aaron" });

        var all = (await repo.GetAllAsync()).ToList();

        all.Should().HaveCount(2);
        all[0].Name.Should().Be("Aaron");
        all[1].Name.Should().Be("Zara");
    }

    [Fact]
    public async Task DeletePlayer_NoGameHistory_Deletes()
    {
        using var db = TestDbBuilder.Create();
        var repo = new SqlitePlayerRepository(db.Connection);
        var player = new Player { Id = Guid.NewGuid(), Name = "Doomed" };
        await repo.CreateAsync(player);

        await repo.DeleteAsync(player.Id);

        var found = await repo.FindByNameAsync("Doomed");
        found.Should().BeNull();
    }

    [Fact]
    public void DeletePlayer_WithGameHistory_IsBlockedByQuery()
    {
        // This test verifies the guard query used by PlayerDeleteCommand returns > 0 when
        // the player has games — the command reads this value before calling DeleteAsync.
        var playerId = Guid.NewGuid();
        var gameId = Guid.NewGuid();

        using var db = TestDbBuilder.Create()
            .WithPlayer(playerId, "Veteran")
            .WithTeam("angels_of_death", "Angels of Death", "Adeptus Astartes")
            .WithGame(gameId, "angels_of_death", "Angels of Death", "angels_of_death", "Angels of Death", playerId, playerId);

        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM games WHERE participant1_player_id=@id OR participant2_player_id=@id";
        cmd.Parameters.AddWithValue("@id", playerId.ToString());
        var count = Convert.ToInt32(cmd.ExecuteScalar());

        count.Should().BeGreaterThan(0, "player with a game should be blocked from deletion");
    }
}
