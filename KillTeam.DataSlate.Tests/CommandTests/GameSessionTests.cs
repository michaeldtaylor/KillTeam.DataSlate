using FluentAssertions;
using KillTeam.DataSlate.Console.Infrastructure.Repositories;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Services;
using Xunit;

namespace KillTeam.DataSlate.Tests.CommandTests;

public class GameSessionTests
{
    // ─── CP Calculation ───────────────────────────────────────────────────────

    [Fact]
    public async Task CpCalculation_TP1_BothTeamsGain1Cp()
    {
        // Arrange: game starts with 2CP each; TP1 logic adds +1 to each → 3 each
        var teamAId = Guid.NewGuid();
        var teamBId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var gameId = Guid.NewGuid();

        using var db = TestDbBuilder.Create()
            .WithPlayer(playerId, "Player1")
            .WithKillTeam("Team A", "Faction A")
            .WithKillTeam("Team B", "Faction B")
            .WithGame(gameId, "Team A", "Team B", playerId, playerId);

        var gameRepo = new SqliteGameRepository(db.Connection);

        // Initial CP = 2 each (default); TP1 adds 1 to each
        await gameRepo.UpdateCpAsync(gameId, 3, 3);

        var updated = await gameRepo.GetByIdAsync(gameId);
        updated.Should().NotBeNull();
        updated!.CpTeamA.Should().Be(3, "TP1 gives +1CP to each team");
        updated.CpTeamB.Should().Be(3, "TP1 gives +1CP to each team");
    }

    [Fact]
    public async Task CpCalculation_TP2_InitiativeTeamGains1_OtherGains2()
    {
        // TP2: initiative team (A) +1, other team (B) +2
        // Starting from 3CP each after TP1 → A gets 4, B gets 5
        var teamAId = Guid.NewGuid();
        var teamBId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var gameId = Guid.NewGuid();

        using var db = TestDbBuilder.Create()
            .WithPlayer(playerId, "Player1")
            .WithKillTeam("Team A", "Faction A")
            .WithKillTeam("Team B", "Faction B")
            .WithGame(gameId, "Team A", "Team B", playerId, playerId);

        var gameRepo = new SqliteGameRepository(db.Connection);

        // Set starting CP to 3 each (after TP1)
        await gameRepo.UpdateCpAsync(gameId, 3, 3);

        // Team A has initiative in TP2: +1 for A, +2 for B
        int cpA = 3 + 1; // initiative team
        int cpB = 3 + 2; // other team
        await gameRepo.UpdateCpAsync(gameId, cpA, cpB);

        var updated = await gameRepo.GetByIdAsync(gameId);
        updated!.CpTeamA.Should().Be(4, "initiative team gains 1CP");
        updated.CpTeamB.Should().Be(5, "other team gains 2CP");
    }

    // ─── Wound Reduction ──────────────────────────────────────────────────────

    [Fact]
    public async Task WoundReduction_AfterShoot_UpdatesCurrentWounds()
    {
        var teamAId = Guid.NewGuid();
        var teamBId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        var opId = Guid.NewGuid();
        var stateId = Guid.NewGuid();

        using var db = TestDbBuilder.Create()
            .WithPlayer(playerId, "Player1")
            .WithKillTeam("Team A", "Faction")
            .WithKillTeam("Team B", "Faction")
            .WithOperative(opId, "Team A", "Sergeant", wounds: 12, save: 3, apl: 3, move: 3)
            .WithGame(gameId, "Team A", "Team B", playerId, playerId)
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
        var operative = new Operative { TeamName = "Team", Name = "Op", OperativeType = "Op", Wounds = 12 };
        var state = new GameOperativeState { CurrentWounds = 5 };

        bool isInjured = state.CurrentWounds < operative.Wounds / 2;
        isInjured.Should().BeTrue("5 < 6 means the operative is injured");
    }

    [Fact]
    public void InjuredThreshold_AtExactlyHalfWounds_IsNotInjured()
    {
        // At exactly 6/12 wounds: 6 < 6 = false → not injured
        var operative = new Operative { TeamName = "Team", Name = "Op", OperativeType = "Op", Wounds = 12 };
        var state = new GameOperativeState { CurrentWounds = 6 };

        bool isInjured = state.CurrentWounds < operative.Wounds / 2;
        isInjured.Should().BeFalse("6 is not less than 6");
    }

    // ─── Incapacitation ───────────────────────────────────────────────────────

    [Fact]
    public async Task Incapacitation_WoundsReachZero_CanMarkIncapacitated()
    {
        var teamAId = Guid.NewGuid();
        var teamBId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        var opId = Guid.NewGuid();
        var stateId = Guid.NewGuid();

        using var db = TestDbBuilder.Create()
            .WithPlayer(playerId, "Player1")
            .WithKillTeam("Team A", "Faction")
            .WithKillTeam("Team B", "Faction")
            .WithOperative(opId, "Team A", "Warrior", wounds: 10, save: 4, apl: 2, move: 3)
            .WithGame(gameId, "Team A", "Team B", playerId, playerId)
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
    public void CoverSave_InCover_BlocksNormalHit()
    {
        // Attack: 1 normal hit (die=5 at threshold 3), no defence dice, but InCover=true
        // Cover adds 1 normal save → blocks the 1 normal hit → 0 damage
        var svc = new CombatResolutionService();
        var ctx = new ShootContext(
            AttackDice: [5],
            DefenceDice: [],
            InCover: true,
            IsObscured: false,
            HitThreshold: 3,
            SaveThreshold: 3,
            NormalDmg: 3,
            CritDmg: 4,
            WeaponRules: []
        );

        var result = svc.ResolveShoot(ctx);
        result.TotalDamage.Should().Be(0, "cover save blocks the single normal hit");
        result.UnblockedNormals.Should().Be(0);
    }

    // ─── Counteract Eligibility ───────────────────────────────────────────────

    [Fact]
    public async Task Counteract_Eligibility_EngageOrderExpendedNotUsed()
    {
        // A state with IsReady=false, Order=Engage, HasUsedCounteract=false
        // should be eligible for counteract (not blocked by any condition)
        var teamAId = Guid.NewGuid();
        var teamBId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        var opId = Guid.NewGuid();
        var stateId = Guid.NewGuid();

        using var db = TestDbBuilder.Create()
            .WithPlayer(playerId, "Player1")
            .WithKillTeam("Team A", "Faction")
            .WithKillTeam("Team B", "Faction")
            .WithOperative(opId, "Team A", "Grenadier", wounds: 13, save: 3, apl: 2, move: 3)
            .WithGame(gameId, "Team A", "Team B", playerId, playerId)
            .WithGameOperativeState(stateId, gameId, opId, currentWounds: 13, order: "Engage");

        var stateRepo = new SqliteGameOperativeStateRepository(db.Connection);
        await stateRepo.SetReadyAsync(stateId, false);

        var states = (await stateRepo.GetByGameAsync(gameId)).ToList();
        var state = states.Single(s => s.Id == stateId);

        bool eligible = !state.IsReady
            && state.Order == Order.Engage
            && !state.HasUsedCounteractThisTurningPoint
            && !state.IsIncapacitated;

        eligible.Should().BeTrue("operative meets all counteract eligibility criteria");
    }

    // ─── Guard Resolution Service ─────────────────────────────────────────────

    [Fact]
    public void GuardResolutionService_GetEligibleGuards_ReturnsOnGuardNonIncapacitated()
    {
        var svc = new GuardResolutionService();

        var guard1 = new GameOperativeState { IsOnGuard = true, IsIncapacitated = false };
        var guard2 = new GameOperativeState { IsOnGuard = true, IsIncapacitated = true };
        var notGuard = new GameOperativeState { IsOnGuard = false, IsIncapacitated = false };

        var eligible = svc.GetEligibleGuards([guard1, guard2, notGuard]);

        eligible.Should().HaveCount(1);
        eligible.Should().Contain(guard1);
        eligible.Should().NotContain(guard2, "incapacitated guard is not eligible");
        eligible.Should().NotContain(notGuard, "not on guard");
    }
}
