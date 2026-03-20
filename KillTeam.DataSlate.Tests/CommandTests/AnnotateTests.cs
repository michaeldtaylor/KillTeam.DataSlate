using FluentAssertions;
using KillTeam.DataSlate.Infrastructure.Repositories;
using Xunit;

namespace KillTeam.DataSlate.Tests.CommandTests;

public class AnnotateTests
{
    [Fact]
    public async Task SaveAnnotation_OnActivation_PersistsNarrativeNote()
    {
        var playerId = Guid.NewGuid();
        var opId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        var tpId = Guid.NewGuid();
        var actId = Guid.NewGuid();

        using var db = TestDbBuilder.Create()
            .WithPlayer(playerId, "Michael")
            .WithTeam("angels_of_death", "Angels of Death", "Adeptus Astartes")
            .WithOperative(opId, "angels_of_death", "Sergeant", wounds: 13, save: 3, apl: 3, move: 3)
            .WithGame(gameId, "angels_of_death", "Angels of Death", "angels_of_death", "Angels of Death", playerId, playerId)
            .WithTurningPoint(tpId, gameId, 1)
            .WithActivation(actId, tpId, 1, opId, "angels_of_death");

        var repo = new SqliteActivationRepository(db.Connection);

        await repo.UpdateNarrativeAsync(actId, "Charged into the objective room.");

        var acts = (await repo.GetByTurningPointAsync(tpId)).ToList();
        acts.Should().ContainSingle();
        acts[0].NarrativeNote.Should().Be("Charged into the objective room.");
    }

    [Fact]
    public async Task OverwriteAnnotation_ReplacesExistingNote()
    {
        var playerId = Guid.NewGuid();
        var opId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        var tpId = Guid.NewGuid();
        var actId = Guid.NewGuid();

        using var db = TestDbBuilder.Create()
            .WithPlayer(playerId, "Michael")
            .WithTeam("angels_of_death", "Angels of Death", "Adeptus Astartes")
            .WithOperative(opId, "angels_of_death", "Sergeant", wounds: 13, save: 3, apl: 3, move: 3)
            .WithGame(gameId, "angels_of_death", "Angels of Death", "angels_of_death", "Angels of Death", playerId, playerId)
            .WithTurningPoint(tpId, gameId, 1)
            .WithActivation(actId, tpId, 1, opId, "angels_of_death");

        var repo = new SqliteActivationRepository(db.Connection);
        await repo.UpdateNarrativeAsync(actId, "First note");
        await repo.UpdateNarrativeAsync(actId, "Updated note");

        var acts = (await repo.GetByTurningPointAsync(tpId)).ToList();
        acts[0].NarrativeNote.Should().Be("Updated note");
    }

    [Fact]
    public async Task SaveAnnotation_OnAction_PersistsActionNarrativeNote()
    {
        var playerId = Guid.NewGuid();
        var opId = Guid.NewGuid();
        var targetOpId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        var tpId = Guid.NewGuid();
        var actId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        using var db = TestDbBuilder.Create()
            .WithPlayer(playerId, "Michael")
            .WithTeam("angels_of_death", "Angels of Death", "Adeptus Astartes")
            .WithOperative(opId, "angels_of_death", "Sergeant", wounds: 13, save: 3, apl: 3, move: 3)
            .WithOperative(targetOpId, "angels_of_death", "Intercessor", wounds: 13, save: 3, apl: 2, move: 3)
            .WithGame(gameId, "angels_of_death", "Angels of Death", "angels_of_death", "Angels of Death", playerId, playerId)
            .WithTurningPoint(tpId, gameId, 1)
            .WithActivation(actId, tpId, 1, opId, "angels_of_death");

        // Insert action directly
        using var insertCmd = db.Connection.CreateCommand();
        insertCmd.CommandText = """
            INSERT INTO actions (id, activation_id, type, ap_cost)
            VALUES (@id, @actId, 'Shoot', 1)
            """;
        insertCmd.Parameters.AddWithValue("@id", actionId.ToString());
        insertCmd.Parameters.AddWithValue("@actId", actId.ToString());
        insertCmd.ExecuteNonQuery();

        var repo = new SqliteActionRepository(db.Connection);
        await repo.UpdateNarrativeAsync(actionId, "Epic headshot.");

        var foundActions = (await repo.GetByActivationAsync(actId)).ToList();
        foundActions.Should().ContainSingle();
        foundActions[0].NarrativeNote.Should().Be("Epic headshot.");
    }
}
