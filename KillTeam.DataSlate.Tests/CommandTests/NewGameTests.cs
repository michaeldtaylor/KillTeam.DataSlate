using FluentAssertions;
using KillTeam.DataSlate.Console.Infrastructure.Repositories;
using KillTeam.DataSlate.Domain.Models;
using Xunit;

namespace KillTeam.DataSlate.Tests.CommandTests;

public class NewGameTests
{
    [Fact]
    public async Task HappyPath_CreatesGameWithOperativeStates()
    {
        // Arrange
        var playerId1 = Guid.NewGuid();
        var playerId2 = Guid.NewGuid();
        var teamId1 = Guid.NewGuid();
        var teamId2 = Guid.NewGuid();
        var op1Id = Guid.NewGuid();
        var op2Id = Guid.NewGuid();
        var op3Id = Guid.NewGuid();
        var op4Id = Guid.NewGuid();

        using var db = TestDbBuilder.Create()
            .WithPlayer(playerId1, "Michael")
            .WithPlayer(playerId2, "Solomon")
            .WithKillTeam(teamId1, "Angels of Death", "Adeptus Astartes")
            .WithKillTeam(teamId2, "Plague Marines", "Heretic Astartes")
            .WithOperative(op1Id, teamId1, "Sergeant", wounds: 13, save: 3, apl: 3, move: 3)
            .WithOperative(op2Id, teamId1, "Intercessor", wounds: 13, save: 3, apl: 2, move: 3)
            .WithOperative(op3Id, teamId2, "Champion", wounds: 14, save: 3, apl: 3, move: 3)
            .WithOperative(op4Id, teamId2, "Warrior", wounds: 14, save: 3, apl: 2, move: 3);

        var gameRepo = new SqliteGameRepository(db.Connection);
        var stateRepo = new SqliteGameOperativeStateRepository(db.Connection);
        var killTeamRepo = new SqliteKillTeamRepository(db.Connection);

        // Act — simulate what NewGameCommand does
        var game = new Game
        {
            Id = Guid.NewGuid(),
            PlayedAt = DateTime.UtcNow,
            TeamAId = teamId1,
            TeamBId = teamId2,
            PlayerAId = playerId1,
            PlayerBId = playerId2,
            Status = GameStatus.InProgress,
            CpTeamA = 2,
            CpTeamB = 2
        };
        var created = await gameRepo.CreateAsync(game);

        var fullTeamA = await killTeamRepo.GetWithOperativesAsync(teamId1);
        var fullTeamB = await killTeamRepo.GetWithOperativesAsync(teamId2);
        var allOps = (fullTeamA?.Operatives ?? []).Concat(fullTeamB?.Operatives ?? []).ToList();

        foreach (var op in allOps)
        {
            await stateRepo.CreateAsync(new GameOperativeState
            {
                Id = Guid.NewGuid(),
                GameId = created.Id,
                OperativeId = op.Id,
                CurrentWounds = op.Wounds,
                Order = Order.Conceal,
                IsReady = true
            });
        }

        // Assert
        var foundGame = await gameRepo.GetByIdAsync(created.Id);
        foundGame.Should().NotBeNull();
        foundGame!.Status.Should().Be(GameStatus.InProgress);
        foundGame.CpTeamA.Should().Be(2);
        foundGame.CpTeamB.Should().Be(2);

        var states = (await stateRepo.GetByGameAsync(created.Id)).ToList();
        states.Should().HaveCount(4);
        states.All(s => s.IsReady).Should().BeTrue();
        states.All(s => s.Order == Order.Conceal).Should().BeTrue();
        states.All(s => !s.IsOnGuard).Should().BeTrue();
        states.All(s => s.AplModifier == 0).Should().BeTrue();
    }

    [Fact]
    public async Task SingleRosterImported_ReturnsLessThanTwoTeams()
    {
        var playerId = Guid.NewGuid();
        var teamId = Guid.NewGuid();

        using var db = TestDbBuilder.Create()
            .WithPlayer(playerId, "Michael")
            .WithKillTeam(teamId, "Angels of Death", "Adeptus Astartes");

        var killTeamRepo = new SqliteKillTeamRepository(db.Connection);
        var teams = (await killTeamRepo.GetAllAsync()).ToList();

        teams.Should().HaveCount(1, "NewGameCommand would reject this with < 2 teams");
    }

    [Fact]
    public async Task NoPlayersRegistered_ReturnsZeroPlayers()
    {
        using var db = TestDbBuilder.Create();
        var playerRepo = new SqlitePlayerRepository(db.Connection);
        var players = (await playerRepo.GetAllAsync()).ToList();

        players.Should().BeEmpty("NewGameCommand would reject this with no players");
    }
}
