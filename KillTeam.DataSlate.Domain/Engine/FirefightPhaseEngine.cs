using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Engine.Input;
using KillTeam.DataSlate.Domain.Repositories;

namespace KillTeam.DataSlate.Domain.Engine;

/// <summary>
/// Encapsulates all game logic for the Firefight Phase:
/// activation sequencing, AP management, action eligibility, counteraction eligibility,
/// turning-point-over and game-over checks, end-of-turning-point state resets,
/// and winner determination.
/// </summary>
public class FirefightPhaseEngine(
    IFirefightInputProvider inputProvider,
    ShootEngine shootEngine,
    FightEngine fightEngine,
    GuardInterruptEngine guardInterruptEngine,
    IGameRepository gameRepository,
    IGameOperativeStateRepository stateRepository,
    IActivationRepository activationRepository,
    IActionRepository actionRepository)
{
    public async Task RunAsync(
        GameContext context,
        TurningPoint turningPoint)
    {
        await inputProvider.DisplayTurningPointHeaderAsync(turningPoint.Number);

        var initiativeTeamId = turningPoint.TeamWithInitiativeId ?? context.Game.Participant1.TeamId;
        var currentTeamId = initiativeTeamId;

        var existingActivations = (await activationRepository.GetByTurningPointAsync(turningPoint.Id)).ToList();
        var sequenceCounter = existingActivations.Count > 0 ? existingActivations.Max(a => a.SequenceNumber) : 0;

        await inputProvider.DisplayBoardStateAsync(context, turningPoint);

        while (!IsTurningPointOver(context.OperativeStates, context.Operatives, context.Game))
        {
            var readyThisTeam = GetReadyOperatives(currentTeamId, context.OperativeStates, context.Operatives);
            var otherTeamId = currentTeamId == context.Game.Participant1.TeamId
                ? context.Game.Participant2.TeamId
                : context.Game.Participant1.TeamId;
            var readyOtherTeam = GetReadyOperatives(otherTeamId, context.OperativeStates, context.Operatives);

            if (readyThisTeam.Count > 0)
            {
                var (operative, state) = await inputProvider.SelectActivatingOperativeAsync(readyThisTeam);

                sequenceCounter++;

                var activation = new Activation
                {
                    Id = Guid.NewGuid(),
                    TurningPointId = turningPoint.Id,
                    SequenceNumber = sequenceCounter,
                    OperativeId = operative.Id,
                    TeamId = operative.TeamId,
                    IsCounteract = false,
                };

                await activationRepository.CreateAsync(activation);

                sequenceCounter = await RunActivationAsync(
                    operative,
                    state,
                    turningPoint,
                    activation,
                    context,
                    sequenceCounter);

                context = context with { Game = (await gameRepository.GetByIdAsync(context.Game.Id))! };

                if (IsGameOver(context.OperativeStates, context.Operatives, context.Game, turningPoint))
                {
                    await EndGameAsync(context.Game, context.OperativeStates, context.Operatives);

                    return;
                }

                currentTeamId = otherTeamId;
            }
            else if (readyOtherTeam.Count > 0)
            {
                var (counteractTaken, updatedSeqCounter) = await TryOfferCounteractAsync(
                    currentTeamId,
                    turningPoint,
                    context,
                    sequenceCounter);

                sequenceCounter = updatedSeqCounter;
                context = context with { Game = (await gameRepository.GetByIdAsync(context.Game.Id))! };

                if (IsGameOver(context.OperativeStates, context.Operatives, context.Game, turningPoint))
                {
                    await EndGameAsync(context.Game, context.OperativeStates, context.Operatives);

                    return;
                }

                if (counteractTaken)
                {
                    currentTeamId = otherTeamId;
                }
                else
                {
                    var (operative, state) = await inputProvider.SelectActivatingOperativeAsync(readyOtherTeam);
                    sequenceCounter++;

                    var activation = new Activation
                    {
                        Id = Guid.NewGuid(),
                        TurningPointId = turningPoint.Id,
                        SequenceNumber = sequenceCounter,
                        OperativeId = operative.Id,
                        TeamId = operative.TeamId,
                        IsCounteract = false,
                    };

                    await activationRepository.CreateAsync(activation);

                    sequenceCounter = await RunActivationAsync(
                        operative,
                        state,
                        turningPoint,
                        activation,
                        context,
                        sequenceCounter);

                    context = context with { Game = (await gameRepository.GetByIdAsync(context.Game.Id))! };

                    if (IsGameOver(context.OperativeStates, context.Operatives, context.Game, turningPoint))
                    {
                        await EndGameAsync(context.Game, context.OperativeStates, context.Operatives);

                        return;
                    }

                    // Do NOT swap currentTeamId — other team used their opportunity
                }
            }
            else
            {
                break;
            }
        }

        await EndTurningPointAsync(context.Game, turningPoint, context.OperativeStates, context.Operatives);
    }

    private async Task<int> RunActivationAsync(
        Operative operative,
        GameOperativeState state,
        TurningPoint turningPoint,
        Activation activation,
        GameContext context,
        int seqCounter)
    {
        await inputProvider.DisplayActivationHeaderAsync(operative.Name);

        var order = await inputProvider.SelectOrderAsync(operative.Name);

        state.Order = order;
        await stateRepository.UpdateOrderAsync(state.Id, order);
        activation.OrderSelected = order;

        var remainingAp = Math.Max(1, operative.Apl + state.AplModifier);
        var hasMovedNonDash = false;

        while (remainingAp > 0 && !state.IsIncapacitated)
        {
            await inputProvider.DisplayBoardStateAsync(context, turningPoint);

            var availableActions = BuildActionMenu(operative, state, remainingAp);
            var selectedAction = await inputProvider.SelectActionAsync(operative.Name, remainingAp, availableActions);

            if (selectedAction == "End Activation")
            {
                break;
            }

            if (selectedAction == "Shoot")
            {
                await shootEngine.RunAsync(context, activation, operative, state, hasMovedNonDash);
            }
            else if (selectedAction == "Fight")
            {
                await fightEngine.RunAsync(context, activation, operative, state);
            }
            else if (selectedAction == "Guard")
            {
                state.IsOnGuard = true;
                await stateRepository.UpdateGuardAsync(state.Id, true);
                await inputProvider.DisplayGuardSetAsync(operative.Name);

                var guardAction = new GameAction
                {
                    Id = Guid.NewGuid(),
                    ActivationId = activation.Id,
                    Type = ActionType.Guard,
                    ApCost = 1,
                };

                await actionRepository.CreateAsync(guardAction);
            }
            else
            {
                var isDash = selectedAction == "Dash";
                var actionType = selectedAction switch
                {
                    "Reposition" => ActionType.Reposition,
                    "Dash" => ActionType.Dash,
                    "Fall Back" => ActionType.FallBack,
                    "Charge" => ActionType.Charge,
                    _ => ActionType.Other,
                };

                var distanceStr = await inputProvider.GetMoveDistanceAsync(operative.Name);

                var moveAction = new GameAction
                {
                    Id = Guid.NewGuid(),
                    ActivationId = activation.Id,
                    Type = actionType,
                    ApCost = 1,
                    NarrativeNote = string.IsNullOrWhiteSpace(distanceStr) ? null : $"Moved {distanceStr}",
                };

                await actionRepository.CreateAsync(moveAction);

                if (!isDash)
                {
                    hasMovedNonDash = true;
                }
            }

            remainingAp--;

            if (selectedAction is "Shoot" or "Fight" or "Reposition" or "Dash" or "Fall Back" or "Charge" or "Other")
            {
                seqCounter = await guardInterruptEngine.CheckAndRunInterruptsAsync(context, turningPoint, operative, seqCounter);

                context = context with { Game = (await gameRepository.GetByIdAsync(context.Game.Id))! };
            }

            if (state.IsIncapacitated)
            {
                break;
            }
        }

        state.IsReady = false;
        await stateRepository.SetReadyAsync(state.Id, false);

        return seqCounter;
    }

    private async Task<(bool taken, int seqCounter)> TryOfferCounteractAsync(
        string exhaustedTeamId,
        TurningPoint turningPoint,
        GameContext context,
        int seqCounter)
    {
        var eligibles = context.OperativeStates
            .Where(s => !s.IsReady
                && s.Order == Order.Engage
                && !s.HasUsedCounteractThisTurningPoint
                && !s.IsIncapacitated
                && context.Operatives.TryGetValue(s.OperativeId, out var o)
                && o.TeamId == exhaustedTeamId)
            .ToList();

        if (eligibles.Count == 0)
        {
            return (false, seqCounter);
        }

        var candidateNames = eligibles
            .Where(s => context.Operatives.ContainsKey(s.OperativeId))
            .Select(s => context.Operatives[s.OperativeId].Name)
            .ToList();

        var selectedName = await inputProvider.SelectCounteractOperativeAsync(candidateNames);

        if (selectedName is null)
        {
            return (false, seqCounter);
        }

        var counterState = eligibles.First(s =>
            context.Operatives.TryGetValue(s.OperativeId, out var o) && o.Name == selectedName);
        var counterOperative = context.Operatives[counterState.OperativeId];

        seqCounter++;

        var counterActivation = new Activation
        {
            Id = Guid.NewGuid(),
            TurningPointId = turningPoint.Id,
            SequenceNumber = seqCounter,
            OperativeId = counterOperative.Id,
            TeamId = counterOperative.TeamId,
            OrderSelected = counterState.Order,
            IsCounteract = true,
        };

        await activationRepository.CreateAsync(counterActivation);

        await inputProvider.DisplayCounteractAvailableAsync(counterOperative.Name);

        var counterChoice = await inputProvider.SelectCounteractActionAsync(counterOperative.Name);

        if (counterChoice == "Move (max 2\")")
        {
            var action = new GameAction
            {
                Id = Guid.NewGuid(),
                ActivationId = counterActivation.Id,
                Type = ActionType.Reposition,
                ApCost = 1,
                NarrativeNote = "Counteract move (max 2\")",
            };

            await actionRepository.CreateAsync(action);
        }
        else if (counterChoice == "Shoot")
        {
            await shootEngine.RunAsync(
                context,
                counterActivation,
                counterOperative,
                counterState);
        }
        else if (counterChoice == "Fight")
        {
            await fightEngine.RunAsync(
                context,
                counterActivation,
                counterOperative,
                counterState);
        }

        counterState.HasUsedCounteractThisTurningPoint = true;
        await stateRepository.SetCounteractUsedAsync(counterState.Id, true);

        return (true, seqCounter);
    }

    private async Task EndTurningPointAsync(
        Game game,
        TurningPoint turningPoint,
        IReadOnlyList<GameOperativeState> allOperativeStates,
        IReadOnlyDictionary<Guid, Operative> allOperatives)
    {
        if (IsGameOver(allOperativeStates, allOperatives, game, turningPoint))
        {
            await EndGameAsync(game, allOperativeStates, allOperatives);

            return;
        }

        await inputProvider.DisplayTurningPointCompleteAsync(turningPoint.Number);

        foreach (var state in allOperativeStates.Where(s => !s.IsIncapacitated))
        {
            state.IsReady = true;
            state.HasUsedCounteractThisTurningPoint = false;
            state.AplModifier = 0;
            state.IsOnGuard = false;

            await stateRepository.SetReadyAsync(state.Id, true);
            await stateRepository.SetCounteractUsedAsync(state.Id, false);
            await stateRepository.SetAplModifierAsync(state.Id, 0);
            await stateRepository.UpdateGuardAsync(state.Id, false);
        }
    }

    private async Task EndGameAsync(
        Game game,
        IReadOnlyList<GameOperativeState> allOperativeStates,
        IReadOnlyDictionary<Guid, Operative> allOperatives)
    {
        await inputProvider.DisplayGameOverAsync();

        var participant1AllIncap = allOperativeStates
            .Where(s => allOperatives.TryGetValue(s.OperativeId, out var o) && o.TeamId == game.Participant1.TeamId)
            .All(s => s.IsIncapacitated);
        var participant2AllIncap = allOperativeStates
            .Where(s => allOperatives.TryGetValue(s.OperativeId, out var o) && o.TeamId == game.Participant2.TeamId)
            .All(s => s.IsIncapacitated);

        var vp1 = await inputProvider.GetFinalVpAsync(game.Participant1.TeamName);
        var vp2 = await inputProvider.GetFinalVpAsync(game.Participant2.TeamName);

        string? winnerTeamId;

        if (participant1AllIncap && !participant2AllIncap)
        {
            winnerTeamId = game.Participant2.TeamId;
        }
        else if (participant2AllIncap && !participant1AllIncap)
        {
            winnerTeamId = game.Participant1.TeamId;
        }
        else
        {
            winnerTeamId = vp1 > vp2
                ? game.Participant1.TeamId
                : vp2 > vp1
                    ? game.Participant2.TeamId
                    : null;
        }

        await gameRepository.UpdateStatusAsync(game.Id, GameStatus.Completed, winnerTeamId, vp1, vp2);

        var winnerTeamName = winnerTeamId is null
            ? null
            : winnerTeamId == game.Participant1.TeamId
                ? game.Participant1.TeamName
                : game.Participant2.TeamName;

        var winnerVp = winnerTeamId == game.Participant1.TeamId ? vp1 : vp2;

        await inputProvider.DisplayWinnerAsync(
            winnerTeamName,
            winnerVp,
            game.Participant1.TeamName,
            vp1,
            game.Participant2.TeamName,
            vp2);
    }

    private static bool IsTurningPointOver(
        IReadOnlyList<GameOperativeState> allOperativeStates,
        IReadOnlyDictionary<Guid, Operative> allOperatives,
        Game game)
    {
        var teamAAllIncap = allOperativeStates
            .Where(s => allOperatives.TryGetValue(s.OperativeId, out var o) && o.TeamId == game.Participant1.TeamId)
            .All(s => s.IsIncapacitated);
        var teamBAllIncap = allOperativeStates
            .Where(s => allOperatives.TryGetValue(s.OperativeId, out var o) && o.TeamId == game.Participant2.TeamId)
            .All(s => s.IsIncapacitated);

        if (teamAAllIncap || teamBAllIncap)
        {
            return true;
        }

        var teamAReady = allOperativeStates.Any(s =>
            s is { IsIncapacitated: false, IsReady: true }
            && allOperatives.TryGetValue(s.OperativeId, out var o) && o.TeamId == game.Participant1.TeamId);
        var teamBReady = allOperativeStates.Any(s =>
            s is { IsIncapacitated: false, IsReady: true }
            && allOperatives.TryGetValue(s.OperativeId, out var o) && o.TeamId == game.Participant2.TeamId);

        return !teamAReady && !teamBReady;
    }

    private static bool IsGameOver(
        IReadOnlyList<GameOperativeState> allOperativeStates,
        IReadOnlyDictionary<Guid, Operative> allOperatives,
        Game game,
        TurningPoint turningPoint)
    {
        var teamAAllIncap = allOperativeStates
            .Where(s => allOperatives.TryGetValue(s.OperativeId, out var o) && o.TeamId == game.Participant1.TeamId)
            .All(s => s.IsIncapacitated);
        var teamBAllIncap = allOperativeStates
            .Where(s => allOperatives.TryGetValue(s.OperativeId, out var o) && o.TeamId == game.Participant2.TeamId)
            .All(s => s.IsIncapacitated);

        if (teamAAllIncap || teamBAllIncap)
        {
            return true;
        }

        return turningPoint.Number >= 4 && IsTurningPointOver(allOperativeStates, allOperatives, game);
    }

    private static List<(Operative operative, GameOperativeState state)> GetReadyOperatives(
        string teamId,
        IReadOnlyList<GameOperativeState> allOperativeStates,
        IReadOnlyDictionary<Guid, Operative> allOperatives)
    {
        return allOperativeStates
            .Where(s => !s.IsIncapacitated && s.IsReady
                && allOperatives.TryGetValue(s.OperativeId, out var o) && o.TeamId == teamId)
            .Select(s => (allOperatives[s.OperativeId], s))
            .ToList();
    }

    private static List<string> BuildActionMenu(
        Operative operative,
        GameOperativeState state,
        int remainingAp)
    {
        var actions = new List<string>();

        if (remainingAp > 0)
        {
            actions.Add("Reposition");
            actions.Add("Dash");

            if (state.Order == Order.Engage)
            {
                actions.Add("Charge");
            }

            if (operative.Weapons.Any(w => w.Type == WeaponType.Ranged))
            {
                actions.Add("Shoot");
            }

            if (operative.Weapons.Any(w => w.Type == WeaponType.Melee))
            {
                actions.Add("Fight");
            }

            actions.Add("Fall Back");
            actions.Add("Guard");
            actions.Add("Other");
        }

        actions.Add("End Activation");

        return actions;
    }
}
