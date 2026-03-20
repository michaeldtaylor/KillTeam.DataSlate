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
        Game game,
        TurningPoint turningPoint,
        List<GameOperativeState> allOperativeStates,
        IReadOnlyDictionary<Guid, Operative> allOperatives)
    {
        await inputProvider.DisplayTurningPointHeaderAsync(turningPoint.Number);

        var initiativeTeamId = turningPoint.TeamWithInitiativeId ?? game.Participant1.TeamId;
        var currentTeamId = initiativeTeamId;

        var existingActivations = (await activationRepository.GetByTurningPointAsync(turningPoint.Id)).ToList();
        var sequenceCounter = existingActivations.Count > 0 ? existingActivations.Max(a => a.SequenceNumber) : 0;

        await inputProvider.DisplayBoardStateAsync(game, turningPoint, allOperativeStates, allOperatives);

        while (!IsTurningPointOver(allOperativeStates, allOperatives, game))
        {
            var readyThisTeam = GetReadyOperatives(currentTeamId, allOperativeStates, allOperatives);
            var otherTeamId = currentTeamId == game.Participant1.TeamId
                ? game.Participant2.TeamId
                : game.Participant1.TeamId;
            var readyOtherTeam = GetReadyOperatives(otherTeamId, allOperativeStates, allOperatives);

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
                    allOperativeStates,
                    allOperatives,
                    game,
                    sequenceCounter);

                game = (await gameRepository.GetByIdAsync(game.Id))!;

                if (IsGameOver(allOperativeStates, allOperatives, game, turningPoint))
                {
                    await EndGameAsync(game, allOperativeStates, allOperatives);

                    return;
                }

                currentTeamId = otherTeamId;
            }
            else if (readyOtherTeam.Count > 0)
            {
                var (counteractTaken, updatedSeqCounter) = await TryOfferCounteractAsync(
                    currentTeamId,
                    turningPoint,
                    allOperativeStates,
                    allOperatives,
                    game,
                    sequenceCounter);

                sequenceCounter = updatedSeqCounter;
                game = (await gameRepository.GetByIdAsync(game.Id))!;

                if (IsGameOver(allOperativeStates, allOperatives, game, turningPoint))
                {
                    await EndGameAsync(game, allOperativeStates, allOperatives);

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
                        allOperativeStates,
                        allOperatives,
                        game,
                        sequenceCounter);

                    game = (await gameRepository.GetByIdAsync(game.Id))!;

                    if (IsGameOver(allOperativeStates, allOperatives, game, turningPoint))
                    {
                        await EndGameAsync(game, allOperativeStates, allOperatives);

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

        await EndTurningPointAsync(game, turningPoint, allOperativeStates, allOperatives);
    }

    private async Task<int> RunActivationAsync(
        Operative operative,
        GameOperativeState state,
        TurningPoint turningPoint,
        Activation activation,
        IReadOnlyList<GameOperativeState> allOperativeStates,
        IReadOnlyDictionary<Guid, Operative> allOperatives,
        Game game,
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
            await inputProvider.DisplayBoardStateAsync(game, turningPoint, allOperativeStates, allOperatives);

            var availableActions = BuildActionMenu(operative, state, remainingAp);
            var selectedAction = await inputProvider.SelectActionAsync(operative.Name, remainingAp, availableActions);

            if (selectedAction == "End Activation")
            {
                break;
            }

            if (selectedAction == "Shoot")
            {
                await shootEngine.RunAsync(game, activation, operative, state, allOperativeStates, allOperatives, hasMovedNonDash);
            }
            else if (selectedAction == "Fight")
            {
                await fightEngine.RunAsync(game, activation, operative, state, allOperativeStates, allOperatives);
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
                seqCounter = await guardInterruptEngine.CheckAndRunInterruptsAsync(game, turningPoint, operative, allOperativeStates, allOperatives, seqCounter);

                game = (await gameRepository.GetByIdAsync(game.Id))!;
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
        IReadOnlyList<GameOperativeState> allOperativeStates,
        IReadOnlyDictionary<Guid, Operative> allOperatives,
        Game game,
        int seqCounter)
    {
        var eligibles = allOperativeStates
            .Where(s => !s.IsReady
                && s.Order == Order.Engage
                && !s.HasUsedCounteractThisTurningPoint
                && !s.IsIncapacitated
                && allOperatives.TryGetValue(s.OperativeId, out var o)
                && o.TeamId == exhaustedTeamId)
            .ToList();

        if (eligibles.Count == 0)
        {
            return (false, seqCounter);
        }

        var candidateNames = eligibles
            .Where(s => allOperatives.ContainsKey(s.OperativeId))
            .Select(s => allOperatives[s.OperativeId].Name)
            .ToList();

        var selectedName = await inputProvider.SelectCounteractOperativeAsync(candidateNames);

        if (selectedName is null)
        {
            return (false, seqCounter);
        }

        var counterState = eligibles.First(s =>
            allOperatives.TryGetValue(s.OperativeId, out var o) && o.Name == selectedName);
        var counterOperative = allOperatives[counterState.OperativeId];

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
                game,
                counterActivation,
                counterOperative,
                counterState,
                allOperativeStates,
                allOperatives);
        }
        else if (counterChoice == "Fight")
        {
            await fightEngine.RunAsync(
                game,
                counterActivation,
                counterOperative,
                counterState,
                allOperativeStates,
                allOperatives);
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
