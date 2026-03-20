using FluentAssertions;
using KillTeam.DataSlate.Domain.Engine;
using KillTeam.DataSlate.Domain.Engine.Input;
using KillTeam.DataSlate.Domain.Engine.WeaponRules;
using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using KillTeam.DataSlate.Domain.Repositories.InMemory;
using Xunit;

namespace KillTeam.DataSlate.Tests.DomainTests;

public class FightEngineTests
{
    private static Weapon MakeMeleeWeapon(string name = "Blade", int atk = 1, int hit = 3, int normalDmg = 3, int critDmg = 4, WeaponRuleKind[]? rules = null) =>
        new()
        {
            Name = name,
            Type = WeaponType.Melee,
            Atk = atk,
            Hit = hit,
            NormalDmg = normalDmg,
            CriticalDmg = critDmg,
            Rules = rules?.Select(k => new WeaponRule(k, null)).ToList() ?? [],
        };

    private static Operative MakeOperative(string name, string teamId, int wounds = 12, List<Weapon>? weapons = null) =>
        new()
        {
            TeamId = teamId,
            Name = name,
            OperativeType = "Test",
            Wounds = wounds,
            Weapons = weapons ?? [],
        };

    private static Game MakeGame(string teamId1, string teamId2) =>
        new()
        {
            Participant1 = new GameParticipant { Team = new TeamSummary(teamId1, "Team 1", "", ""), PlayerId = Guid.NewGuid(), CommandPoints = 0 },
            Participant2 = new GameParticipant { Team = new TeamSummary(teamId2, "Team 2", "", ""), PlayerId = Guid.NewGuid(), CommandPoints = 0 },
        };

    private static IReadOnlyList<GameOperativeState> SeedStates(
        Game game, Operative attacker, Operative target, int attackerWounds, int targetWounds)
    {
        var stateRepo = new InMemoryGameOperativeStateRepository();
        var attackerState = new GameOperativeState
        {
            GameId = game.Id,
            OperativeId = attacker.Id,
            CurrentWounds = attackerWounds,
        };
        var targetState = new GameOperativeState
        {
            GameId = game.Id,
            OperativeId = target.Id,
            CurrentWounds = targetWounds,
        };

        stateRepo.Seed([attackerState, targetState]);

        return stateRepo.GetAll();
    }

    private static FightEngine MakeEngine(
        IFightInputProvider fightInputProvider,
        IRerollInputProvider rerollInputProvider,
        IGameRepository gameRepo)
    {
        var rerollEngine = new RerollEngine(rerollInputProvider, gameRepo);
        var actionRepo = new InMemoryActionRepository();

        return new FightEngine(fightInputProvider, rerollEngine, actionRepo, new FightWeaponRulePipeline());
    }

    [Fact]
    public async Task RunAsync_AttackerCrit_IncapacitatesLowHealthTarget()
    {
        const string attackerTeamId = "team-1";
        const string targetTeamId = "team-2";

        var meleeWeapon = MakeMeleeWeapon(atk: 1, normalDmg: 3, critDmg: 12);
        var attacker = MakeOperative("Veteran", attackerTeamId, wounds: 12, weapons: [meleeWeapon]);
        var target = MakeOperative("Scout", targetTeamId, wounds: 12, weapons: []);

        var game = MakeGame(attackerTeamId, targetTeamId);
        var allStates = SeedStates(game, attacker, target, attackerWounds: 12, targetWounds: 12);

        var targetState = allStates.Single(s => s.OperativeId == target.Id);
        var allOperatives = new Dictionary<Guid, Operative> { [attacker.Id] = attacker, [target.Id] = target };
        var activation = new Activation { TurningPointId = Guid.NewGuid(), TeamId = attackerTeamId, SequenceNumber = 1 };

        var fightInput = new StubFightInputProvider(attackerDice: [6], targetDice: []);
        var engine = MakeEngine(fightInput, new NoCpRerollInputProvider(), new StubGameRepository(game));

        var result = await engine.RunAsync(new GameContext(game, allStates, allOperatives), activation, attacker, allStates.Single(s => s.OperativeId == attacker.Id));

        result.AttackerCausedIncapacitation.Should().BeTrue("crit dealt 12 damage to a 12-wound target");
        result.TargetCausedIncapacitation.Should().BeFalse("target had no melee weapon");
        result.AttackerDamageDealt.Should().Be(12);
        result.TargetDamageDealt.Should().Be(0);
        result.TargetOperativeId.Should().Be(target.Id);
        targetState.IsIncapacitated.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_TargetHasNoMeleeWeapons_EmitsTargetNoMeleeWeaponsEvent()
    {
        const string attackerTeamId = "team-1";
        const string targetTeamId = "team-2";

        var meleeWeapon = MakeMeleeWeapon(atk: 1, normalDmg: 3, critDmg: 4);
        var attacker = MakeOperative("Veteran", attackerTeamId, wounds: 12, weapons: [meleeWeapon]);
        var target = MakeOperative("Scout", targetTeamId, wounds: 12, weapons: []);

        var game = MakeGame(attackerTeamId, targetTeamId);
        var allStates = SeedStates(game, attacker, target, attackerWounds: 12, targetWounds: 12);

        var allOperatives = new Dictionary<Guid, Operative> { [attacker.Id] = attacker, [target.Id] = target };
        var activation = new Activation { TurningPointId = Guid.NewGuid(), TeamId = attackerTeamId, SequenceNumber = 1 };
        var stream = new GameEventStream(game.Id);
        var emittedEvents = new List<GameEvent>();
        stream.OnEventEmitted += emittedEvents.Add;

        var fightInput = new StubFightInputProvider(attackerDice: [4], targetDice: []);
        var engine = MakeEngine(fightInput, new NoCpRerollInputProvider(), new StubGameRepository(game));

        await engine.RunAsync(new GameContext(game, allStates, allOperatives, stream), activation, attacker, allStates.Single(s => s.OperativeId == attacker.Id));

        emittedEvents.OfType<TargetNoMeleeWeaponsEvent>().Should().ContainSingle(
            "target has no melee weapons — event must be emitted");
    }

    [Fact]
    public async Task RunAsync_ShockRule_RemovesTargetSuccessBeforeResolution()
    {
        const string attackerTeamId = "team-1";
        const string targetTeamId = "team-2";

        var shockWeapon = MakeMeleeWeapon(atk: 1, normalDmg: 3, critDmg: 12, rules: [WeaponRuleKind.Shock]);
        var targetWeapon = MakeMeleeWeapon(name: "Claws", atk: 1, normalDmg: 3, critDmg: 3);
        var attacker = MakeOperative("Striker", attackerTeamId, wounds: 12, weapons: [shockWeapon]);
        var target = MakeOperative("Guard", targetTeamId, wounds: 12, weapons: [targetWeapon]);

        var game = MakeGame(attackerTeamId, targetTeamId);
        var allStates = SeedStates(game, attacker, target, attackerWounds: 12, targetWounds: 12);

        var allOperatives = new Dictionary<Guid, Operative> { [attacker.Id] = attacker, [target.Id] = target };
        var activation = new Activation { TurningPointId = Guid.NewGuid(), TeamId = attackerTeamId, SequenceNumber = 1 };
        var stream = new GameEventStream(game.Id);
        var emittedEvents = new List<GameEvent>();
        stream.OnEventEmitted += emittedEvents.Add;

        // Attacker rolls 1 crit → triggers Shock. Target rolls 1 hit (value 5) → removed by Shock.
        var fightInput = new StubFightInputProvider(attackerDice: [6], targetDice: [5]);
        var engine = MakeEngine(fightInput, new NoCpRerollInputProvider(), new StubGameRepository(game));

        await engine.RunAsync(new GameContext(game, allStates, allOperatives, stream), activation, attacker, allStates.Single(s => s.OperativeId == attacker.Id));

        emittedEvents.OfType<ShockAppliedEvent>().Should().ContainSingle(
            "Shock fires when attacker has a crit and target has a success die");
    }

    [Fact]
    public async Task RunAsync_NoEnemyOperatives_ReturnsEmptyResult()
    {
        const string attackerTeamId = "team-1";
        const string targetTeamId = "team-2";

        var meleeWeapon = MakeMeleeWeapon();
        var attacker = MakeOperative("Veteran", attackerTeamId, wounds: 12, weapons: [meleeWeapon]);

        var game = MakeGame(attackerTeamId, targetTeamId);
        var attackerState = new GameOperativeState { GameId = game.Id, OperativeId = attacker.Id, CurrentWounds = 12 };
        IReadOnlyList<GameOperativeState> allStates = [attackerState];

        var allOperatives = new Dictionary<Guid, Operative> { [attacker.Id] = attacker };
        var activation = new Activation { TurningPointId = Guid.NewGuid(), TeamId = attackerTeamId, SequenceNumber = 1 };

        var fightInput = new StubFightInputProvider(attackerDice: [], targetDice: []);
        var engine = MakeEngine(fightInput, new NoCpRerollInputProvider(), new StubGameRepository(game));

        var result = await engine.RunAsync(new GameContext(game, allStates, allOperatives), activation, attacker, attackerState);

        result.AttackerCausedIncapacitation.Should().BeFalse();
        result.TargetCausedIncapacitation.Should().BeFalse();
        result.AttackerDamageDealt.Should().Be(0);
        result.TargetOperativeId.Should().BeNull();
    }

    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class StubFightInputProvider(int[] attackerDice, int[] targetDice) : IFightInputProvider
    {
        public Task<GameOperativeState> SelectTargetAsync(
            IList<GameOperativeState> candidates,
            IReadOnlyDictionary<Guid, Operative> allOperatives)
        {
            return Task.FromResult(candidates.First());
        }

        public Task<Weapon> SelectAttackerWeaponAsync(IList<Weapon> weapons, bool isInjured)
        {
            return Task.FromResult(weapons.First());
        }

        public Task<Weapon> SelectTargetWeaponAsync(IList<Weapon> weapons)
        {
            return Task.FromResult(weapons.First());
        }

        public Task<int> GetFightAssistCountAsync()
        {
            return Task.FromResult(0);
        }

        public Task<FightAction> SelectActionAsync(IList<FightAction> actions, string operativeName)
        {
            var strike = actions.FirstOrDefault(a => a.Type == FightActionType.Strike) ?? actions.First();

            return Task.FromResult(strike);
        }

        public Task<string> GetNarrativeNoteAsync()
        {
            return Task.FromResult(string.Empty);
        }

        public Task<int[]> RollOrEnterDiceAsync(
            int count, string label,
            string operativeName, string role, string phase,
            string participant, GameEventStream? eventStream)
        {
            var dice = role == "Target" ? targetDice : attackerDice;

            return Task.FromResult(dice);
        }
    }

    private sealed class NoCpRerollInputProvider : IRerollInputProvider
    {
        public Task<RollableDie> SelectBalancedRerollDieAsync(IList<RollableDie> pool, string label)
        {
            return Task.FromResult(pool.First());
        }

        public Task<int> GetCeaselessRerollValueAsync(string label)
        {
            return Task.FromResult(0);
        }

        public Task<IList<RollableDie>> SelectRelentlessRerollDiceAsync(IList<RollableDie> pool, string label)
        {
            return Task.FromResult<IList<RollableDie>>([]);
        }

        public Task<bool> ConfirmCpRerollAsync(string label, int currentCp)
        {
            return Task.FromResult(false);
        }

        public Task<RollableDie> SelectCpRerollDieAsync(IList<RollableDie> pool)
        {
            return Task.FromResult(pool.First());
        }
    }

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
}
