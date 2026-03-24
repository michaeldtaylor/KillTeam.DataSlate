using FluentAssertions;
using Microsoft.Data.Sqlite;
using KillTeam.DataSlate.Infrastructure;
using KillTeam.DataSlate.Infrastructure.Repositories;
using KillTeam.DataSlate.Domain.Models;
using Xunit;

namespace KillTeam.DataSlate.Tests;

public class SchemaTests
{
    [Fact]
    public void DatabaseInitialiser_Initialise_CreatesAllTables()
    {
        using var db = TestDbBuilder.Create();

        string[] expectedTables =
        [
            "schema_version", "players", "teams", "operatives", "weapons",
            "games", "turning_points", "activations", "actions", "game_operative_states",
            "ploy_uses",
            "faction_rules", "strategy_ploys", "firefight_ploys",
            "faction_equipment", "universal_equipment",
            "operative_abilities", "operative_special_actions", "operative_special_rules"
        ];

        foreach (var table in expectedTables)
        {
            using var cmd = db.Connection.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=@name";
            cmd.Parameters.AddWithValue("@name", table);
            var result = cmd.ExecuteScalar();
            result.Should().NotBeNull($"table '{table}' should exist");
        }
    }

    [Fact]
    public void DatabaseInitialiser_Initialise_IsIdempotent()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON";
        pragma.ExecuteNonQuery();

        // First apply
        DatabaseInitialiser.ApplyAllMigrations(conn);
        // Second apply — must not throw
        DatabaseInitialiser.ApplyAllMigrations(conn);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT version FROM schema_version";
        var version = Convert.ToInt32(cmd.ExecuteScalar());
        version.Should().Be(1);
    }

    [Fact]
    public async Task PlayerRepository_Add_PersistsPlayer()
    {
        using var db = TestDbBuilder.Create();
        var repo = new SqlitePlayerRepository(db.Connection);
        var player = new Player { Id = Guid.NewGuid(), Username = "michael", FirstName = "Michael", LastName = "Smith" };

        await repo.CreateAsync(player);

        var found = await repo.FindByUsernameAsync("michael");
        found.Should().NotBeNull();
        found!.Id.Should().Be(player.Id);
        found.Username.Should().Be("michael");
    }

    [Fact]
    public async Task PlayerRepository_Add_DuplicateName_Throws()
    {
        using var db = TestDbBuilder.Create();
        var repo = new SqlitePlayerRepository(db.Connection);
        await repo.CreateAsync(new Player { Id = Guid.NewGuid(), Username = "solomon", FirstName = "Solomon", LastName = "Jones" });

        await Assert.ThrowsAsync<SqliteException>(
            () => repo.CreateAsync(new Player { Id = Guid.NewGuid(), Username = "solomon", FirstName = "Solomon", LastName = "Jones" }));
    }

    [Fact]
    public async Task PlayerRepository_FindByUsername_IsCaseInsensitive()
    {
        using var db = TestDbBuilder.Create();
        var repo = new SqlitePlayerRepository(db.Connection);
        await repo.CreateAsync(new Player { Id = Guid.NewGuid(), Username = "michael", FirstName = "Michael", LastName = "Smith" });

        var found = await repo.FindByUsernameAsync("MICHAEL");
        found.Should().NotBeNull();
        found!.Username.Should().Be("michael");
    }

    [Fact]
    public async Task PlayerRepository_Delete_RemovesPlayer()
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
    public async Task GameRepository_Create_PersistsGame()
    {
        var playerId1 = Guid.NewGuid();
        var playerId2 = Guid.NewGuid();

        using var db = TestDbBuilder.Create()
            .WithPlayer(playerId1, "michael", "Michael", "Smith")
            .WithPlayer(playerId2, "solomon", "Solomon", "Jones")
            .WithTeam("angels_of_death", "Angels of Death", "Adeptus Astartes")
            .WithTeam("plague_marines", "Plague Marines", "Heretic Astartes");

        var repo = new SqliteGameRepository(db.Connection);
        var game = new Game
        {
            Id = Guid.NewGuid(),
            StartedAt = DateTime.UtcNow,
            Participant1 = new GameParticipant
            {
                Team = new TeamSummary("angels_of_death", "Angels of Death", "", ""),
                PlayerId = playerId1,
                CommandPoints = 2
            },
            Participant2 = new GameParticipant
            {
                Team = new TeamSummary("plague_marines", "Plague Marines", "", ""),
                PlayerId = playerId2,
                CommandPoints = 2
            },
            Status = GameStatus.InProgress
        };

        await repo.CreateAsync(game);

        var found = await repo.GetByIdAsync(game.Id);
        found.Should().NotBeNull();
        found!.Status.Should().Be(GameStatus.InProgress);
        found.Participant1.CommandPoints.Should().Be(2);
    }

    [Fact]
    public void TestDbBuilder_WithPlayer_SeedsCorrectly()
    {
        var playerId = Guid.NewGuid();

        using var db = TestDbBuilder.Create()
            .WithPlayer(playerId, "michael", "Michael", "Smith");

        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT username FROM players WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", playerId.ToString());
        var username = cmd.ExecuteScalar() as string;
        username.Should().Be("michael");
    }

    [Fact]
    public void TestDbBuilder_WithTurningPoint_SeedsCorrectly()
    {
        var playerId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        var tpId = Guid.NewGuid();

        using var db = TestDbBuilder.Create()
            .WithPlayer(playerId, "michael", "Michael", "Smith")
            .WithTeam("angels_of_death", "Angels of Death", "Adeptus Astartes")
            .WithGame(gameId, "angels_of_death", "Angels of Death", "angels_of_death", "Angels of Death", playerId, playerId)
            .WithTurningPoint(tpId, gameId, 1, false);

        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT number, is_strategy_phase_complete FROM turning_points WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", tpId.ToString());
        using var reader = cmd.ExecuteReader();
        reader.Read().Should().BeTrue();
        reader.GetInt32(0).Should().Be(1);
        reader.GetInt32(1).Should().Be(0);
    }
}
