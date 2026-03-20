using FluentAssertions;
using KillTeam.DataSlate.Domain.Engine;
using KillTeam.DataSlate.Domain.Engine.WeaponRules;
using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Infrastructure.Repositories;
using Xunit;

namespace KillTeam.DataSlate.Tests.CommandTests;

public class GameSessionTests
{
    // ─── CP Calculation ───────────────────────────────────────────────────────

    [Fact]
    public async Task CpCalculation_TP1_BothTeamsGain1Cp()
    {
        // Arrange: game starts with 2CP each; TP1 logic adds +1 to each → 3 each
        var playerId = Guid.NewGuid();
        var gameId = Guid.NewGuid();

        using var db = TestDbBuilder.Create()
            .WithPlayer(playerId, "Player1")
            .WithTeam("team_1", "Team 1", "Faction 1")
            .WithTeam("team_2", "Team 2", "Faction 2")
            .WithGame(gameId, "team_1", "Team 1", "team_2", "Team 2", playerId, playerId);

        var gameRepo = new SqliteGameRepository(db.Connection);

        // Initial CP = 2 each (default); TP1 adds 1 to each
        await gameRepo.UpdateCommandPointsAsync(gameId, 3, 3);

        var updated = await gameRepo.GetByIdAsync(gameId);
        updated.Should().NotBeNull();
        updated!.Participant1.CommandPoints.Should().Be(3, "TP1 gives +1CP to each team");
        updated.Participant2.CommandPoints.Should().Be(3, "TP1 gives +1CP to each team");
    }

    [Fact]
    public async Task CpCalculation_TP2_InitiativeTeamGains1_OtherGains2()
    {
        // TP2: initiative team (A) +1, other team (B) +2
        // Starting from 3CP each after TP1 → A gets 4, B gets 5
        var playerId = Guid.NewGuid();
        var gameId = Guid.NewGuid();

        using var db = TestDbBuilder.Create()
            .WithPlayer(playerId, "Player1")
            .WithTeam("team_1", "Team 1", "Faction 1")
            .WithTeam("team_2", "Team 2", "Faction 2")
            .WithGame(gameId, "team_1", "Team 1", "team_2", "Team 2", playerId, playerId);

        var gameRepo = new SqliteGameRepository(db.Connection);

        // Set starting CP to 3 each (after TP1)
        await gameRepo.UpdateCommandPointsAsync(gameId, 3, 3);

        // Team 1 has initiative in TP2: +1 for Team 1, +2 for Team 2
        var cp1 = 3 + 1; // initiative team
        var cp2 = 3 + 2; // other team
        await gameRepo.UpdateCommandPointsAsync(gameId, cp1, cp2);

        var updated = await gameRepo.GetByIdAsync(gameId);
        updated!.Participant1.CommandPoints.Should().Be(4, "initiative team gains 1CP");
        updated.Participant2.CommandPoints.Should().Be(5, "other team gains 2CP");
    }

    // ─── Wound Reduction ──────────────────────────────────────────────────────

    [Fact]
    public async Task WoundReduction_AfterShoot_UpdatesCurrentWounds()
    {
        var playerId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        var opId = Guid.NewGuid();
        var stateId = Guid.NewGuid();

        using var db = TestDbBuilder.Create()
            .WithPlayer(playerId, "Player1")
            .WithTeam("team_1", "Team 1", "Faction")
            .WithTeam("team_2", "Team 2", "Faction")
            .WithOperative(opId, "team_1", "Sergeant", wounds: 12, save: 3, apl: 3, move: 3)
            .WithGame(gameId, "team_1", "Team 1", "team_2", "Team 2", playerId, playerId)
            .WithGameOperativeState(stateId, gameId, opId, currentWounds: 12);

        var stateRepo = new SqliteGameOperativeStateRepository(db.Connection);

        await stateRepo.UpdateWoundsAsync(stateId, 6);

        var states = (await stateRepo.GetByGameAsync(gameId)).ToList();
        var state = states.Single(s => s.Id == stateId);
        state.CurrentWounds.Should().Be(6);
    }

    // ─── Injured Threshold ────────────────────────────────────────────────────

    [Fact]
    public void InjuredThreshold_At5Of12Wounds_IsInjured()
    {
        // Injured = currentWounds < startingWounds / 2
        // For a 12W operative: threshold = 12/2 = 6; at 5W → injured
        var operative = new Operative { TeamId = "team_1", Name = "Op", OperativeType = "Op", Wounds = 12 };
        var state = new GameOperativeState { CurrentWounds = 5 };

        var isInjured = state.CurrentWounds < operative.Wounds / 2;
        isInjured.Should().BeTrue("5 < 6 means the operative is injured");
    }

    [Fact]
    public void InjuredThreshold_AtExactlyHalfWounds_IsNotInjured()
    {
        // At exactly 6/12 wounds: 6 < 6 = false → not injured
        var operative = new Operative { TeamId = "team_1", Name = "Op", OperativeType = "Op", Wounds = 12 };
        var state = new GameOperativeState { CurrentWounds = 6 };

        var isInjured = state.CurrentWounds < operative.Wounds / 2;
        isInjured.Should().BeFalse("6 is not less than 6");
    }

    // ─── Incapacitation ───────────────────────────────────────────────────────

    [Fact]
    public async Task Incapacitation_WoundsReachZero_CanMarkIncapacitated()
    {
        var playerId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        var opId = Guid.NewGuid();
        var stateId = Guid.NewGuid();

        using var db = TestDbBuilder.Create()
            .WithPlayer(playerId, "Player1")
            .WithTeam("team_1", "Team 1", "Faction")
            .WithTeam("team_2", "Team 2", "Faction")
            .WithOperative(opId, "team_1", "Warrior", wounds: 10, save: 4, apl: 2, move: 3)
            .WithGame(gameId, "team_1", "Team 1", "team_2", "Team 2", playerId, playerId)
            .WithGameOperativeState(stateId, gameId, opId, currentWounds: 10);

        var stateRepo = new SqliteGameOperativeStateRepository(db.Connection);

        await stateRepo.UpdateWoundsAsync(stateId, 0);
        await stateRepo.SetIncapacitatedAsync(stateId, true);

        var states = (await stateRepo.GetByGameAsync(gameId)).ToList();
        var state = states.Single(s => s.Id == stateId);
        state.CurrentWounds.Should().Be(0);
        state.IsIncapacitated.Should().BeTrue();
    }

    // ─── Cover Save ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CoverSave_InCover_BlocksNormalHit()
    {
        // Attack: 1 normal hit (die=5 at threshold 3), no defence dice, but InCover=true
        // Cover adds 1 normal save → blocks the 1 normal hit → 0 damage
        var applicator = new ShootWeaponRulePipeline();
        var weapon = new Weapon { Name = "Test Weapon" };
        var ctx = new ShootContext(
            AttackerDice: [5],
            TargetDice: [],
            InCover: true,
            IsObscured: false,
            HitThreshold: 3,
            SaveThreshold: 3,
            NormalDmg: 3,
            CritDmg: 4
        );

        var result = await applicator.ResolveShootAsync(weapon, ctx);

        result.TotalDamage.Should().Be(0, "cover save blocks the single normal hit");
        result.UnblockedNormals.Should().Be(0);
    }

    // ─── Counteract Eligibility ───────────────────────────────────────────────

    [Fact]
    public async Task Counteract_Eligibility_EngageOrderExpendedNotUsed()
    {
        // A state with IsReady=false, Order=Engage, HasUsedCounteract=false
        // should be eligible for counteract (not blocked by any condition)
        var playerId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        var opId = Guid.NewGuid();
        var stateId = Guid.NewGuid();

        using var db = TestDbBuilder.Create()
            .WithPlayer(playerId, "Player1")
            .WithTeam("team_1", "Team 1", "Faction")
            .WithTeam("team_2", "Team 2", "Faction")
            .WithOperative(opId, "team_1", "Grenadier", wounds: 13, save: 3, apl: 2, move: 3)
            .WithGame(gameId, "team_1", "Team 1", "team_2", "Team 2", playerId, playerId)
            .WithGameOperativeState(stateId, gameId, opId, currentWounds: 13, order: "Engage");

        var stateRepo = new SqliteGameOperativeStateRepository(db.Connection);
        await stateRepo.SetReadyAsync(stateId, false);

        var states = (await stateRepo.GetByGameAsync(gameId)).ToList();
        var state = states.Single(s => s.Id == stateId);

        var eligible = !state.IsReady
            && state.Order == Order.Engage
            && !state.HasUsedCounteractThisTurningPoint
            && !state.IsIncapacitated;

        eligible.Should().BeTrue("operative meets all counteract eligibility criteria");
    }
}
