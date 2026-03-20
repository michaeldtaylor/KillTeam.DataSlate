using KillTeam.DataSlate.Domain.Engine.Input;
using KillTeam.DataSlate.Domain.Engine.WeaponRules;
using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using KillTeam.DataSlate.Domain.Repositories.InMemory;

namespace KillTeam.DataSlate.Domain.Engine;

/// <summary>
/// Encapsulates a single simulated fight or shoot encounter:
/// synthetic domain-object setup, in-memory state isolation,
/// order selection, engine dispatch, and result aggregation.
/// Nothing is persisted — all state is discarded after each run.
/// </summary>
public class SimulateEncounterEngine(
    IFightInputProvider fightInputProvider,
    IShootInputProvider shootInputProvider,
    IRerollInputProvider rerollInputProvider,
    IAoEInputProvider aoeInputProvider,
    ShootWeaponRulePipeline shootWeaponRulePipeline,
    IGameRepository gameRepository,
    IGameStatePersistenceHandler persistenceHandler,
    ISimulateEncounterInputProvider inputProvider)
{
    public async Task<SimulateEncounterResult> RunAsync(
        Operative attacker,
        Team attackerTeam,
        Operative defender,
        Team defenderTeam,
        ActionType actionType,
        Action<GameEventStream>? configureStream = null)
    {
        var game = new Game
        {
            Id = Guid.NewGuid(),
            Participant1 = new GameParticipant
            {
                TeamId = attackerTeam.Id,
                TeamName = attackerTeam.Name,
                PlayerId = Guid.Empty,
                // CommandPoints = 0 suppresses CP re-roll prompts (RerollEngine skips when game not in DB)
                CommandPoints = 0,
            },
            Participant2 = new GameParticipant
            {
                TeamId = defenderTeam.Id,
                TeamName = defenderTeam.Name,
                PlayerId = Guid.Empty,
                CommandPoints = 0,
            },
            StartedAt = DateTime.UtcNow,
        };

        var turningPoint = new TurningPoint
        {
            Id = Guid.NewGuid(),
            GameId = game.Id,
            Number = 1,
        };

        var activation = new Activation
        {
            Id = Guid.NewGuid(),
            TurningPointId = turningPoint.Id,
            OperativeId = attacker.Id,
            TeamId = attackerTeam.Id,
            OrderSelected = Order.Engage,
            SequenceNumber = 1,
        };

        var attackerState = new GameOperativeState
        {
            GameId = game.Id,
            OperativeId = attacker.Id,
            CurrentWounds = attacker.Wounds,
            Order = Order.Engage,
        };

        var defenderState = new GameOperativeState
        {
            GameId = game.Id,
            OperativeId = defender.Id,
            CurrentWounds = defender.Wounds,
            Order = Order.Engage,
        };

        var stateRepo = new InMemoryGameOperativeStateRepository();
        stateRepo.Seed([attackerState, defenderState]);

        var actionRepo = new InMemoryActionRepository();
        var allStates = stateRepo.GetAll();

        var allOperatives = new Dictionary<Guid, Operative>
        {
            [attacker.Id] = attacker,
            [defender.Id] = defender,
        };

        var stream = new GameEventStream(game.Id, persistenceHandler.HandleAsync);
        configureStream?.Invoke(stream);

        if (actionType == ActionType.Fight)
        {
            var rerollEngine = new RerollEngine(rerollInputProvider, gameRepository);
            var fightEngine = new FightEngine(
                fightInputProvider,
                rerollEngine,
                actionRepo,
                new FightWeaponRulePipeline());

            var result = await fightEngine.RunAsync(
                game,
                activation,
                attacker,
                attackerState,
                allStates,
                allOperatives,
                stream);

            return new SimulateEncounterResult(
                AttackerDamageDealt: result.AttackerDamageDealt,
                DefenderDamageDealt: result.TargetDamageDealt,
                AttackerIncapacitated: result.TargetCausedIncapacitation,
                DefenderIncapacitated: result.AttackerCausedIncapacitation,
                AttackerCurrentWounds: attackerState.CurrentWounds,
                DefenderCurrentWounds: defenderState.CurrentWounds);
        }
        else
        {
            var order = await inputProvider.SelectOrderAsync(attacker.Name);

            attackerState.Order = order;
            activation.OrderSelected = order;

            var rerollEngine = new RerollEngine(rerollInputProvider, gameRepository);
            var aoeEngine = new AoEEngine(aoeInputProvider, shootWeaponRulePipeline, rerollEngine, actionRepo);
            var shootEngine = new ShootEngine(
                shootInputProvider,
                rerollEngine,
                aoeEngine,
                actionRepo,
                shootWeaponRulePipeline);

            var result = await shootEngine.RunAsync(
                game,
                activation,
                attacker,
                attackerState,
                allStates,
                allOperatives,
                false,
                stream);

            return new SimulateEncounterResult(
                AttackerDamageDealt: result.DamageDealt,
                DefenderDamageDealt: 0,
                AttackerIncapacitated: false,
                DefenderIncapacitated: result.CausedIncapacitation,
                AttackerCurrentWounds: attackerState.CurrentWounds,
                DefenderCurrentWounds: defenderState.CurrentWounds);
        }
    }
}
