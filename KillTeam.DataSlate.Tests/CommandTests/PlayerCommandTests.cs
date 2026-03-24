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

        await repo.CreateAsync(new Player { Id = Guid.NewGuid(), Username = "michael", FirstName = "Michael", LastName = "Smith" });

        var all = (await repo.GetAllAsync()).ToList();
        all.Should().ContainSingle(p => p.Username == "michael");
    }

    [Fact]
    public async Task AddPlayer_DuplicateName_ThrowsConstraint()
    {
        using var db = TestDbBuilder.Create();
        var repo = new SqlitePlayerRepository(db.Connection);
        await repo.CreateAsync(new Player { Id = Guid.NewGuid(), Username = "solomon", FirstName = "Solomon", LastName = "Jones" });

        var act = () => repo.CreateAsync(new Player { Id = Guid.NewGuid(), Username = "solomon", FirstName = "Solomon", LastName = "Jones" });

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task GetAllPlayers_ReturnsSortedByName()
    {
        using var db = TestDbBuilder.Create();
        var repo = new SqlitePlayerRepository(db.Connection);
        await repo.CreateAsync(new Player { Id = Guid.NewGuid(), Username = "zara", FirstName = "Zara", LastName = "Ali" });
        await repo.CreateAsync(new Player { Id = Guid.NewGuid(), Username = "aaron", FirstName = "Aaron", LastName = "Brown" });

        var all = (await repo.GetAllAsync()).ToList();

        all.Should().HaveCount(2);
        all[0].Username.Should().Be("aaron");
        all[1].Username.Should().Be("zara");
    }

    [Fact]
    public async Task DeletePlayer_NoGameHistory_Deletes()
    {
        using var db = TestDbBuilder.Create();
        var repo = new SqlitePlayerRepository(db.Connection);
        var player = new Player { Id = Guid.NewGuid(), Username = "doomed", FirstName = "Doomed", LastName = "Player" };
        await repo.CreateAsync(player);

        await repo.DeleteAsync(player.Id);

        var found = await repo.FindByUsernameAsync("doomed");
        found.Should().BeNull();
    }

    [Fact]
    public void DeletePlayer_WithGameHistory_IsBlockedByQuery()
    {
        // This test verifies the guard query used by DeletePlayerCommand returns > 0 when
        // the player has games — the command reads this value before calling DeleteAsync.
        var playerId = Guid.NewGuid();
        var gameId = Guid.NewGuid();

        using var db = TestDbBuilder.Create()
            .WithPlayer(playerId, "veteran", "Vet", "Player")
            .WithTeam("angels_of_death", "Angels of Death", "Adeptus Astartes")
            .WithGame(gameId, "angels_of_death", "Angels of Death", "angels_of_death", "Angels of Death", playerId, playerId);

        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM games WHERE participant1_player_id=@id OR participant2_player_id=@id";
        cmd.Parameters.AddWithValue("@id", playerId.ToString());
        var count = Convert.ToInt32(cmd.ExecuteScalar());

        count.Should().BeGreaterThan(0, "player with a game should be blocked from deletion");
    }
}
