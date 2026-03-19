using FluentAssertions;
using KillTeam.DataSlate.Domain.Engine;
using KillTeam.DataSlate.Domain.Engine.Input;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using Xunit;

namespace KillTeam.DataSlate.Tests.DomainTests;

public class RerollEngineTests
{
    private static Game MakeGame(int cp1 = 0, int cp2 = 0) =>
        new()
        {
            Participant1 = new GameParticipant { TeamId = "team-a", TeamName = "Team A", PlayerId = Guid.NewGuid(), CommandPoints = cp1 },
            Participant2 = new GameParticipant { TeamId = "team-b", TeamName = "Team B", PlayerId = Guid.NewGuid(), CommandPoints = cp2 },
        };

    private static Weapon MakeWeaponWith(params WeaponRuleKind[] ruleKinds) =>
        new()
        {
            Name = "Test Weapon",
            Type = WeaponType.Melee,
            Atk = 4,
            Hit = 3,
            NormalDmg = 3,
            CriticalDmg = 4,
            Rules = ruleKinds.Select(k => new WeaponRule(k, null, k.ToString())).ToList(),
        };

    [Fact]
    public async Task ApplyAttackerRerolls_BalancedRule_RerollsOnlySelectedDie()
    {
        var game = MakeGame();
        var gameRepo = new StubGameRepository(game);
        var inputProvider = new StubRerollInputProvider(
            balancedSelectIndex: 0,
            confirmCpReroll: false);
        var engine = new RerollEngine(inputProvider, gameRepo);
        var weapon = MakeWeaponWith(WeaponRuleKind.Balanced);

        var result = await engine.ApplyAttackerRerollsAsync([3, 4, 5], weapon.Rules.ToList(), game.Id, isTeam1: true, "Attacker");

        result.Should().HaveCount(3);
        result[1].Should().Be(4, "index 1 was not selected for reroll");
        result[2].Should().Be(5, "index 2 was not selected for reroll");
        result[0].Should().BeInRange(1, 6, "rerolled die should be a valid d6");
    }

    [Fact]
    public async Task ApplyAttackerRerolls_CeaselessRule_RerollsDiceMatchingFace()
    {
        var game = MakeGame();
        var gameRepo = new StubGameRepository(game);
        var inputProvider = new StubRerollInputProvider(
            ceaselessFace: 3,
            confirmCpReroll: false);
        var engine = new RerollEngine(inputProvider, gameRepo);
        var weapon = MakeWeaponWith(WeaponRuleKind.Ceaseless);

        var result = await engine.ApplyAttackerRerollsAsync([3, 3, 5], weapon.Rules.ToList(), game.Id, isTeam1: true, "Attacker");

        result.Should().HaveCount(3);
        result[2].Should().Be(5, "die with face 5 does not match Ceaseless face 3");
        result[0].Should().BeInRange(1, 6, "die at index 0 was re-rolled (matched face 3)");
        result[1].Should().BeInRange(1, 6, "die at index 1 was re-rolled (matched face 3)");
    }

    [Fact]
    public async Task ApplyAttackerRerolls_RelentlessRule_RerollsSelectedDice()
    {
        var game = MakeGame();
        var gameRepo = new StubGameRepository(game);
        var inputProvider = new StubRerollInputProvider(
            relentlessSelectIndices: [0, 2],
            confirmCpReroll: false);
        var engine = new RerollEngine(inputProvider, gameRepo);
        var weapon = MakeWeaponWith(WeaponRuleKind.Relentless);

        var result = await engine.ApplyAttackerRerollsAsync([3, 4, 5], weapon.Rules.ToList(), game.Id, isTeam1: true, "Attacker");

        result.Should().HaveCount(3);
        result[1].Should().Be(4, "index 1 was not selected for Relentless reroll");
        result[0].Should().BeInRange(1, 6, "index 0 was selected for Relentless reroll");
        result[2].Should().BeInRange(1, 6, "index 2 was selected for Relentless reroll");
    }

    [Fact]
    public async Task ApplyAttackerRerolls_ZeroCommandPoints_SkipsCpReroll()
    {
        var game = MakeGame(cp1: 0);
        var gameRepo = new StubGameRepository(game);
        var inputProvider = new StubRerollInputProvider(confirmCpReroll: false);
        var engine = new RerollEngine(inputProvider, gameRepo);

        var result = await engine.ApplyAttackerRerollsAsync([3, 4, 5], [], game.Id, isTeam1: true, "Attacker");

        result.Should().Equal([3, 4, 5], "no CP available — dice unchanged");
    }

    [Fact]
    public async Task ApplyTargetReroll_DeclinedCpReroll_ReturnsDiceUnchanged()
    {
        var game = MakeGame(cp1: 2);
        var gameRepo = new StubGameRepository(game);
        var inputProvider = new StubRerollInputProvider(confirmCpReroll: false);
        var engine = new RerollEngine(inputProvider, gameRepo);

        var result = await engine.ApplyTargetRerollAsync([2, 4, 6], game.Id, isTeam1: true, "Target");

        result.Should().Equal([2, 4, 6], "CP reroll was declined — dice unchanged");
    }

    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class StubGameRepository(Game game) : IGameRepository
    {
        public Task CreateAsync(Game g) => Task.CompletedTask;

        public Task<Game?> GetByIdAsync(Guid id)
        {
            return Task.FromResult<Game?>(game);
        }

        public Task<GameHeader?> GetHeaderAsync(Guid gameId) => Task.FromResult<GameHeader?>(null);

        public Task<IReadOnlyList<GameHistoryEntry>> GetHistoryAsync(string? playerNameFilter = null) =>
            Task.FromResult<IReadOnlyList<GameHistoryEntry>>([]);

        public Task UpdateStatusAsync(Guid id, GameStatus status, string? winnerTeamId, int vp1, int vp2) =>
            Task.CompletedTask;

        public Task UpdateCommandPointsAsync(Guid id, int cp1, int cp2) => Task.CompletedTask;
    }

    private sealed class StubRerollInputProvider(
        int balancedSelectIndex = 0,
        int ceaselessFace = 1,
        int[]? relentlessSelectIndices = null,
        bool confirmCpReroll = false) : IRerollInputProvider
    {
        public Task<RollableDie> SelectBalancedRerollDieAsync(IList<RollableDie> pool, string label)
        {
            var die = pool.FirstOrDefault(d => d.Index == balancedSelectIndex) ?? pool.First();

            return Task.FromResult(die);
        }

        public Task<int> GetCeaselessRerollValueAsync(string label)
        {
            return Task.FromResult(ceaselessFace);
        }

        public Task<IList<RollableDie>> SelectRelentlessRerollDiceAsync(IList<RollableDie> pool, string label)
        {
            var indices = relentlessSelectIndices ?? [];
            IList<RollableDie> selected = pool.Where(d => indices.Contains(d.Index)).ToList();

            return Task.FromResult(selected);
        }

        public Task<bool> ConfirmCpRerollAsync(string label, int currentCp)
        {
            return Task.FromResult(confirmCpReroll);
        }

        public Task<RollableDie> SelectCpRerollDieAsync(IList<RollableDie> pool)
        {
            return Task.FromResult(pool.First());
        }
    }
}
