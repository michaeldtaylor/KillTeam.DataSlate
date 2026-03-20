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

        var initiativeTeamId = turningPoint.TeamWithInitiativeId ?? context.Game.Participant1.Team.Id;
        var currentTeamId = initiativeTeamId;

        var existingActivations = (await activationRepository.GetByTurningPointAsync(turningPoint.Id)).ToList();
        var sequenceCounter = existingActivations.Count > 0 ? existingActivations.Max(a => a.SequenceNumber) : 0;

        await inputProvider.DisplayBoardStateAsync(context, turningPoint);

        while (!IsTurningPointOver(context.Operatives, context.Game))
        {
            var readyThisTeam = GetReadyOperatives(currentTeamId, context.Operatives);
            var otherTeamId = currentTeamId == context.Game.Participant1.Team.Id
                ? context.Game.Participant2.Team.Id
                : context.Game.Participant1.Team.Id;
            var readyOtherTeam = GetReadyOperatives(otherTeamId, context.Operatives);

            if (readyThisTeam.Count > 0)
            {
                var operative = await inputProvider.SelectActivatingOperativeAsync(readyThisTeam);

                sequenceCounter++;

                var activation = new Activation
                {
                    Id = Guid.NewGuid(),
                    TurningPointId = turningPoint.Id,
                    SequenceNumber = sequenceCounter,
                    OperativeId = operative.Operative.Id,
                    TeamId = operative.Operative.TeamId,
                    IsCounteract = false,
                };

                await activationRepository.CreateAsync(activation);

                sequenceCounter = await RunActivationAsync(
                    operative,
                    turningPoint,
                    activation,
                    context,
                    sequenceCounter);

                context = context with { Game = (await gameRepository.GetByIdAsync(context.Game.Id))! };

                if (IsGameOver(context.Operatives, context.Game, turningPoint))
                {
                    await EndGameAsync(context.Game, context.Operatives);

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

                if (IsGameOver(context.Operatives, context.Game, turningPoint))
                {
                    await EndGameAsync(context.Game, context.Operatives);

                    return;
                }

                if (counteractTaken)
                {
                    currentTeamId = otherTeamId;
                }
                else
                {
                    var operative = await inputProvider.SelectActivatingOperativeAsync(readyOtherTeam);
                    sequenceCounter++;

                    var activation = new Activation
                    {
                        Id = Guid.NewGuid(),
                        TurningPointId = turningPoint.Id,
                        SequenceNumber = sequenceCounter,
                        OperativeId = operative.Operative.Id,
                        TeamId = operative.Operative.TeamId,
                        IsCounteract = false,
                    };

                    await activationRepository.CreateAsync(activation);

                    sequenceCounter = await RunActivationAsync(
                        operative,
                        turningPoint,
                        activation,
                        context,
                        sequenceCounter);

                    context = context with { Game = (await gameRepository.GetByIdAsync(context.Game.Id))! };

                    if (IsGameOver(context.Operatives, context.Game, turningPoint))
                    {
                        await EndGameAsync(context.Game, context.Operatives);

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

        await EndTurningPointAsync(context.Game, turningPoint, context.Operatives);
    }

    private async Task<int> RunActivationAsync(
        OperativeContext operative,
        TurningPoint turningPoint,
        Activation activation,
        GameContext context,
        int seqCounter)
    {
        await inputProvider.DisplayActivationHeaderAsync(operative.Operative.Name);

        var order = await inputProvider.SelectOrderAsync(operative.Operative.Name);

        operative.State.Order = order;
        await stateRepository.UpdateOrderAsync(operative.State.Id, order);
        activation.OrderSelected = order;

        var remainingAp = Math.Max(1, operative.Operative.Apl + operative.State.AplModifier);
        var hasMovedNonDash = false;

        while (remainingAp > 0 && !operative.State.IsIncapacitated)
        {
            await inputProvider.DisplayBoardStateAsync(context, turningPoint);

            var availableActions = BuildActionMenu(operative.Operative, operative.State, remainingAp);
            var selectedAction = await inputProvider.SelectActionAsync(operative.Operative.Name, remainingAp, availableActions);

            if (selectedAction == "End Activation")
            {
                break;
            }

            if (selectedAction == "Shoot")
            {
                await shootEngine.RunAsync(context, activation, operative, hasMovedNonDash);
            }
            else if (selectedAction == "Fight")
            {
                await fightEngine.RunAsync(context, activation, operative);
            }
            else if (selectedAction == "Guard")
            {
                operative.State.IsOnGuard = true;
                await stateRepository.UpdateGuardAsync(operative.State.Id, true);
                await inputProvider.DisplayGuardSetAsync(operative.Operative.Name);

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

                var distanceStr = await inputProvider.GetMoveDistanceAsync(operative.Operative.Name);

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
                seqCounter = await guardInterruptEngine.CheckAndRunInterruptsAsync(context, turningPoint, operative.Operative, seqCounter);

                context = context with { Game = (await gameRepository.GetByIdAsync(context.Game.Id))! };
            }

            if (operative.State.IsIncapacitated)
            {
                break;
            }
        }

        operative.State.IsReady = false;
        await stateRepository.SetReadyAsync(operative.State.Id, false);

        return seqCounter;
    }

    private async Task<(bool taken, int seqCounter)> TryOfferCounteractAsync(
        string exhaustedTeamId,
        TurningPoint turningPoint,
        GameContext context,
        int seqCounter)
    {
        var eligibles = context.Operatives.Values
            .Where(oc => !oc.State.IsReady
                && oc.State.Order == Order.Engage
                && !oc.State.HasUsedCounteractThisTurningPoint
                && !oc.State.IsIncapacitated
                && oc.Operative.TeamId == exhaustedTeamId)
            .ToList();

        if (eligibles.Count == 0)
        {
            return (false, seqCounter);
        }

        var candidateNames = eligibles.Select(oc => oc.Operative.Name).ToList();

        var selectedName = await inputProvider.SelectCounteractOperativeAsync(candidateNames);

        if (selectedName is null)
        {
            return (false, seqCounter);
        }

        var counterOperative = eligibles.First(oc => oc.Operative.Name == selectedName);

        seqCounter++;

        var counterActivation = new Activation
        {
            Id = Guid.NewGuid(),
            TurningPointId = turningPoint.Id,
            SequenceNumber = seqCounter,
            OperativeId = counterOperative.Operative.Id,
            TeamId = counterOperative.Operative.TeamId,
            OrderSelected = counterOperative.State.Order,
            IsCounteract = true,
        };

        await activationRepository.CreateAsync(counterActivation);

        await inputProvider.DisplayCounteractAvailableAsync(counterOperative.Operative.Name);

        var counterChoice = await inputProvider.SelectCounteractActionAsync(counterOperative.Operative.Name);

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
                counterOperative);
        }
        else if (counterChoice == "Fight")
        {
            await fightEngine.RunAsync(
                context,
                counterActivation,
                counterOperative);
        }

        counterOperative.State.HasUsedCounteractThisTurningPoint = true;
        await stateRepository.SetCounteractUsedAsync(counterOperative.State.Id, true);

        return (true, seqCounter);
    }

    private async Task EndTurningPointAsync(
        Game game,
        TurningPoint turningPoint,
        IReadOnlyDictionary<Guid, OperativeContext> allOperatives)
    {
        if (IsGameOver(allOperatives, game, turningPoint))
        {
            await EndGameAsync(game, allOperatives);

            return;
        }

        await inputProvider.DisplayTurningPointCompleteAsync(turningPoint.Number);

        foreach (var oc in allOperatives.Values.Where(oc => !oc.State.IsIncapacitated))
        {
            oc.State.IsReady = true;
            oc.State.HasUsedCounteractThisTurningPoint = false;
            oc.State.AplModifier = 0;
            oc.State.IsOnGuard = false;

            await stateRepository.SetReadyAsync(oc.State.Id, true);
            await stateRepository.SetCounteractUsedAsync(oc.State.Id, false);
            await stateRepository.SetAplModifierAsync(oc.State.Id, 0);
            await stateRepository.UpdateGuardAsync(oc.State.Id, false);
        }
    }

    private async Task EndGameAsync(
        Game game,
        IReadOnlyDictionary<Guid, OperativeContext> allOperatives)
    {
        await inputProvider.DisplayGameOverAsync();

        var participant1AllIncap = allOperatives.Values
            .Where(oc => oc.Operative.TeamId == game.Participant1.Team.Id)
            .All(oc => oc.State.IsIncapacitated);
        var participant2AllIncap = allOperatives.Values
            .Where(oc => oc.Operative.TeamId == game.Participant2.Team.Id)
            .All(oc => oc.State.IsIncapacitated);

        var vp1 = await inputProvider.GetFinalVpAsync(game.Participant1.Team.Name);
        var vp2 = await inputProvider.GetFinalVpAsync(game.Participant2.Team.Name);

        string? winnerTeamId;

        if (participant1AllIncap && !participant2AllIncap)
        {
            winnerTeamId = game.Participant2.Team.Id;
        }
        else if (participant2AllIncap && !participant1AllIncap)
        {
            winnerTeamId = game.Participant1.Team.Id;
        }
        else
        {
            winnerTeamId = vp1 > vp2
                ? game.Participant1.Team.Id
                : vp2 > vp1
                    ? game.Participant2.Team.Id
                    : null;
        }

        await gameRepository.UpdateStatusAsync(game.Id, GameStatus.Completed, winnerTeamId, vp1, vp2);

        var winnerTeamName = winnerTeamId is null
            ? null
            : winnerTeamId == game.Participant1.Team.Id
                ? game.Participant1.Team.Name
                : game.Participant2.Team.Name;

        var winnerVp = winnerTeamId == game.Participant1.Team.Id ? vp1 : vp2;

        await inputProvider.DisplayWinnerAsync(
            winnerTeamName,
            winnerVp,
            game.Participant1.Team.Name,
            vp1,
            game.Participant2.Team.Name,
            vp2);
    }

    private static bool IsTurningPointOver(
        IReadOnlyDictionary<Guid, OperativeContext> allOperatives,
        Game game)
    {
        var participant1AllIncap = allOperatives.Values
            .Where(oc => oc.Operative.TeamId == game.Participant1.Team.Id)
            .All(oc => oc.State.IsIncapacitated);
        var participant2AllIncap = allOperatives.Values
            .Where(oc => oc.Operative.TeamId == game.Participant2.Team.Id)
            .All(oc => oc.State.IsIncapacitated);

        if (participant1AllIncap || participant2AllIncap)
        {
            return true;
        }

        var participant1Ready = allOperatives.Values.Any(oc =>
            !oc.State.IsIncapacitated && oc.State.IsReady && oc.Operative.TeamId == game.Participant1.Team.Id);
        var participant2Ready = allOperatives.Values.Any(oc =>
            !oc.State.IsIncapacitated && oc.State.IsReady && oc.Operative.TeamId == game.Participant2.Team.Id);

        return !participant1Ready && !participant2Ready;
    }

    private static bool IsGameOver(
        IReadOnlyDictionary<Guid, OperativeContext> allOperatives,
        Game game,
        TurningPoint turningPoint)
    {
        var participant1AllIncap = allOperatives.Values
            .Where(oc => oc.Operative.TeamId == game.Participant1.Team.Id)
            .All(oc => oc.State.IsIncapacitated);
        var participant2AllIncap = allOperatives.Values
            .Where(oc => oc.Operative.TeamId == game.Participant2.Team.Id)
            .All(oc => oc.State.IsIncapacitated);

        if (participant1AllIncap || participant2AllIncap)
        {
            return true;
        }

        return turningPoint.Number >= 4 && IsTurningPointOver(allOperatives, game);
    }

    private static List<OperativeContext> GetReadyOperatives(
        string teamId,
        IReadOnlyDictionary<Guid, OperativeContext> allOperatives)
    {
        return allOperatives.Values
            .Where(oc => !oc.State.IsIncapacitated && oc.State.IsReady && oc.Operative.TeamId == teamId)
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
