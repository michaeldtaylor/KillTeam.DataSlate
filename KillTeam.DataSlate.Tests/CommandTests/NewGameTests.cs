using FluentAssertions;
using KillTeam.DataSlate.Infrastructure.Repositories;
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
        const string team1Name = "Angels of Death";
        const string team2Name = "Plague Marines";
        var operative1Id = Guid.NewGuid();
        var operative2Id = Guid.NewGuid();
        var operative3Id = Guid.NewGuid();
        var operative4Id = Guid.NewGuid();

        using var db = TestDbBuilder.Create()
            .WithPlayer(playerId1, "Michael")
            .WithPlayer(playerId2, "Solomon")
            .WithTeam("angels_of_death", team1Name, "Adeptus Astartes")
            .WithTeam("plague_marines", team2Name, "Heretic Astartes")
            .WithOperative(operative1Id, "angels_of_death", "Sergeant", wounds: 13, save: 3, apl: 3, move: 3)
            .WithOperative(operative2Id, "angels_of_death", "Intercessor", wounds: 13, save: 3, apl: 2, move: 3)
            .WithOperative(operative3Id, "plague_marines", "Champion", wounds: 14, save: 3, apl: 3, move: 3)
            .WithOperative(operative4Id, "plague_marines", "Warrior", wounds: 14, save: 3, apl: 2, move: 3);

        var gameRepo = new SqliteGameRepository(db.Connection);
        var stateRepo = new SqliteGameOperativeStateRepository(db.Connection);
        var teamRepo = new SqliteTeamRepository(db.Connection);

        // Act — simulate what NewGameCommand does
        var game = new Game
        {
            Id = Guid.NewGuid(),
            PlayedAt = DateTime.UtcNow,
            Participant1 = new GameParticipant
            {
                TeamId = "angels_of_death",
                TeamName = team1Name,
                PlayerId = playerId1,
                CommandPoints = 2
            },
            Participant2 = new GameParticipant
            {
                TeamId = "plague_marines",
                TeamName = team2Name,
                PlayerId = playerId2,
                CommandPoints = 2
            },
            Status = GameStatus.InProgress
        };
        await gameRepo.CreateAsync(game);

        var fullTeam1 = await teamRepo.GetWithOperativesAsync(team1Name);
        var fullTeam2 = await teamRepo.GetWithOperativesAsync(team2Name);
        var allOperatives = (fullTeam1?.Operatives ?? []).Concat(fullTeam2?.Operatives ?? []).ToList();

        foreach (var operative in allOperatives)
        {
            await stateRepo.CreateAsync(new GameOperativeState
            {
                Id = Guid.NewGuid(),
                GameId = game.Id,
                OperativeId = operative.Id,
                CurrentWounds = operative.Wounds,
                Order = Order.Conceal,
                IsReady = true
            });
        }

        // Assert
        var foundGame = await gameRepo.GetByIdAsync(game.Id);
        foundGame.Should().NotBeNull();
        foundGame!.Status.Should().Be(GameStatus.InProgress);
        foundGame.Participant1.CommandPoints.Should().Be(2);
        foundGame.Participant2.CommandPoints.Should().Be(2);

        var states = (await stateRepo.GetByGameAsync(game.Id)).ToList();
        states.Should().HaveCount(4);
        states.All(s => s.IsReady).Should().BeTrue();
        states.All(s => s.Order == Order.Conceal).Should().BeTrue();
        states.All(s => !s.IsOnGuard).Should().BeTrue();
        states.All(s => s.AplModifier == 0).Should().BeTrue();
    }

    [Fact]
    public async Task SingleKillTeamImported_ReturnsLessThanTwoTeams()
    {
        var playerId = Guid.NewGuid();

        using var db = TestDbBuilder.Create()
            .WithPlayer(playerId, "Michael")
            .WithTeam("angels_of_death", "Angels of Death", "Adeptus Astartes");

        var teamRepo = new SqliteTeamRepository(db.Connection);
        var teams = (await teamRepo.GetAllAsync()).ToList();

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

