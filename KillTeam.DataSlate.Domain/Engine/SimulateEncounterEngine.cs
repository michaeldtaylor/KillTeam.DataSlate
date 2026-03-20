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
        Operative target,
        Team targetTeam,
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
                TeamId = targetTeam.Id,
                TeamName = targetTeam.Name,
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

        var targetState = new GameOperativeState
        {
            GameId = game.Id,
            OperativeId = target.Id,
            CurrentWounds = target.Wounds,
            Order = Order.Engage,
        };

        var stateRepo = new InMemoryGameOperativeStateRepository();
        stateRepo.Seed([attackerState, targetState]);

        var actionRepo = new InMemoryActionRepository();
        var allStates = stateRepo.GetAll();

        var allOperatives = new Dictionary<Guid, Operative>
        {
            [attacker.Id] = attacker,
            [target.Id] = target,
        };

        var stream = new GameEventStream(game.Id, persistenceHandler.HandleAsync);
        configureStream?.Invoke(stream);

        var context = new GameContext(game, allStates, allOperatives, stream);

        if (actionType == ActionType.Fight)
        {
            var rerollEngine = new RerollEngine(rerollInputProvider, gameRepository);
            var fightEngine = new FightEngine(
                fightInputProvider,
                rerollEngine,
                actionRepo,
                new FightWeaponRulePipeline());

            var result = await fightEngine.RunAsync(
                context,
                activation,
                attacker,
                attackerState);

            return new SimulateEncounterResult(
                AttackerDamageDealt: result.AttackerDamageDealt,
                TargetDamageDealt: result.TargetDamageDealt,
                AttackerIncapacitated: result.TargetCausedIncapacitation,
                TargetIncapacitated: result.AttackerCausedIncapacitation,
                AttackerCurrentWounds: attackerState.CurrentWounds,
                TargetCurrentWounds: targetState.CurrentWounds);
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
                context,
                activation,
                attacker,
                attackerState,
                false);

            return new SimulateEncounterResult(
                AttackerDamageDealt: result.DamageDealt,
                TargetDamageDealt: 0,
                AttackerIncapacitated: false,
                TargetIncapacitated: result.CausedIncapacitation,
                AttackerCurrentWounds: attackerState.CurrentWounds,
                TargetCurrentWounds: targetState.CurrentWounds);
        }
    }
}
