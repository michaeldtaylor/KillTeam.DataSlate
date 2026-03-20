using KillTeam.DataSlate.Domain.Engine;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace KillTeam.DataSlate.Console.Orchestrators;

public class FirefightPhaseOrchestrator(
    IAnsiConsole console,
    IGameRepository gameRepository,
    IGameOperativeStateRepository stateRepository,
    IActivationRepository activationRepository,
    IActionRepository actionRepository,
    ITeamRepository teamRepository,
    ShootEngine shootEngine,
    FightEngine fightEngine,
    GuardInterruptOrchestrator guardInterruptOrchestrator,
    ILogger<FirefightPhaseOrchestrator> logger)
{
    public async Task RunAsync(Game game, TurningPoint currentTurningPoint)
    {
        logger.LogDebug("Firefight phase TP{TpNumber} started for game {GameId}", currentTurningPoint.Number, game.Id);
        var team1 = await teamRepository.GetByIdAsync(game.Participant1.TeamId);
        var team2 = await teamRepository.GetByIdAsync(game.Participant2.TeamId);
        var allOperatives = (team1?.Operatives ?? [])
            .Concat(team2?.Operatives ?? [])
            .ToDictionary(o => o.Id);

        var allStates = (await stateRepository.GetByGameAsync(game.Id)).ToList();

        await RunFirefightPhase(game, currentTurningPoint, allOperatives, allStates);
    }

    private async Task RunFirefightPhase(
        Game game,
        TurningPoint turningPoint,
        Dictionary<Guid, Operative> allOperatives,
        List<GameOperativeState> allStates)
    {
        console.Write(new Rule($"[bold]Turning Point {turningPoint.Number} — Firefight Phase[/]"));

        var initiativeTeamId = turningPoint.TeamWithInitiativeId ?? game.Participant1.TeamId;
        var currentTeamId = initiativeTeamId;

        var existingActivations = (await activationRepository.GetByTurningPointAsync(turningPoint.Id)).ToList();
        var seqCounter = existingActivations.Count > 0 ? existingActivations.Max(a => a.SequenceNumber) : 0;

        DisplayBoardState(game, turningPoint, allStates, allOperatives);

        while (!IsTurningPointOver(allStates, allOperatives, game))
        {
            var readyThisTeam = GetReadyOperatives(currentTeamId, allStates, allOperatives);
            var otherTeamId = currentTeamId == game.Participant1.TeamId ? game.Participant2.TeamId : game.Participant1.TeamId;
            var readyOtherTeam = GetReadyOperatives(otherTeamId, allStates, allOperatives);

            if (readyThisTeam.Count > 0)
            {
                var (operative, state) = SelectOperative(readyThisTeam, "Select an operative to activate:");
                seqCounter++;
                var activation = new Activation
                {
                    Id = Guid.NewGuid(),
                    TurningPointId = turningPoint.Id,
                    SequenceNumber = seqCounter,
                    OperativeId = operative.Id,
                    TeamId = operative.TeamId,
                    IsCounteract = false
                };
                await activationRepository.CreateAsync(activation);

                seqCounter = await RunActivation(operative, state, turningPoint, activation, allStates, allOperatives, game, seqCounter);
                game = (await gameRepository.GetByIdAsync(game.Id))!;

                if (IsGameOver(allStates, allOperatives, game, turningPoint))
                {
                    await EndGame(game, allStates, allOperatives);
                    return;
                }

                currentTeamId = otherTeamId;
            }
            else if (readyOtherTeam.Count > 0)
            {
                var counteractTaken = await TryOfferCounteract(
                    currentTeamId, turningPoint, allStates, allOperatives, game, seqCounter);

                if (counteractTaken)
                {
                    // Re-query seq counter after counteract
                    var updated = (await activationRepository.GetByTurningPointAsync(turningPoint.Id)).ToList();
                    seqCounter = updated.Count > 0 ? updated.Max(a => a.SequenceNumber) : seqCounter;
                }

                game = (await gameRepository.GetByIdAsync(game.Id))!;

                if (IsGameOver(allStates, allOperatives, game, turningPoint))
                {
                    await EndGame(game, allStates, allOperatives);
                    return;
                }

                if (counteractTaken)
                {
                    currentTeamId = otherTeamId;
                }
                else
                {
                    var (operative, state) = SelectOperative(readyOtherTeam, "Select an operative to activate:");
                    seqCounter++;
                    var activation = new Activation
                    {
                        Id = Guid.NewGuid(),
                        TurningPointId = turningPoint.Id,
                        SequenceNumber = seqCounter,
                        OperativeId = operative.Id,
                        TeamId = operative.TeamId,
                        IsCounteract = false
                    };
                    await activationRepository.CreateAsync(activation);

                    seqCounter = await RunActivation(operative, state, turningPoint, activation, allStates, allOperatives, game, seqCounter);
                    game = (await gameRepository.GetByIdAsync(game.Id))!;

                    if (IsGameOver(allStates, allOperatives, game, turningPoint))
                    {
                        await EndGame(game, allStates, allOperatives);
                        return;
                    }
                    // Do NOT swap currentTeamId
                }
            }
            else
            {
                break;
            }
        }

        await EndTurningPoint(game, turningPoint, allStates, allOperatives);
    }

    private async Task<int> RunActivation(
        Operative operative,
        GameOperativeState state,
        TurningPoint turningPoint,
        Activation activation,
        List<GameOperativeState> allStates,
        Dictionary<Guid, Operative> allOperatives,
        Game game,
        int seqCounter)
    {
        console.Write(new Rule($"[cyan]{Markup.Escape(operative.Name)}[/] activation"));

        var orderChoice = console.Prompt(
            new SelectionPrompt<string>()
                .Title($"Set order for {Markup.Escape(operative.Name)}:")
                .AddChoices("Engage", "Conceal"));

        var order = orderChoice == "Engage" ? Order.Engage : Order.Conceal;

        state.Order = order;
        await stateRepository.UpdateOrderAsync(state.Id, order);
        activation.OrderSelected = order;

        var remainingAp = Math.Max(1, operative.Apl + state.AplModifier);
        var hasMovedNonDash = false;

        while (remainingAp > 0 && !state.IsIncapacitated)
        {
            DisplayBoardState(game, turningPoint, allStates, allOperatives);

            var availableActions = BuildActionMenu(operative, state, remainingAp, hasMovedNonDash);
            var selectedAction = console.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[bold]{Markup.Escape(operative.Name)}[/] — {remainingAp} AP remaining. Choose an action:")
                    .AddChoices(availableActions));

            if (selectedAction == "End Activation")
            {
                break;
            }

            if (selectedAction == "Shoot")
            {
                await shootEngine.RunAsync(game, activation, operative, state, allStates, allOperatives, hasMovedNonDash);
            }
            else if (selectedAction == "Fight")
            {
                await fightEngine.RunAsync(game, activation, operative, state, allStates, allOperatives);
            }
            else if (selectedAction == "Guard")
            {
                state.IsOnGuard = true;
                await stateRepository.UpdateGuardAsync(state.Id, true);
                console.MarkupLine($"[green]{Markup.Escape(operative.Name)} is now On Guard.[/]");

                var guardAction = new GameAction
                {
                    Id = Guid.NewGuid(),
                    ActivationId = activation.Id,
                    Type = ActionType.Guard,
                    ApCost = 1
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
                    _ => ActionType.Other
                };

                var distanceStr = console.Prompt(
                    new TextPrompt<string>($"How far did {Markup.Escape(operative.Name)} move? (e.g. '3\"', leave blank to skip):")
                        .AllowEmpty());

                var moveAction = new GameAction
                {
                    Id = Guid.NewGuid(),
                    ActivationId = activation.Id,
                    Type = actionType,
                    ApCost = 1,
                    NarrativeNote = string.IsNullOrWhiteSpace(distanceStr) ? null : $"Moved {distanceStr}"
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
                seqCounter = await guardInterruptOrchestrator.CheckAndRunInterruptsAsync(operative, allStates, allOperatives, game, turningPoint, seqCounter);

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

    private async Task<bool> TryOfferCounteract(
        string exhaustedTeamId,
        TurningPoint turningPoint,
        List<GameOperativeState> allStates,
        Dictionary<Guid, Operative> allOperatives,
        Game game,
        int seqCounter)
    {
        var eligibles = allStates
            .Where(s => !s.IsReady
                && s.Order == Order.Engage
                && !s.HasUsedCounteractThisTurningPoint
                && !s.IsIncapacitated
                && allOperatives.TryGetValue(s.OperativeId, out var operative)
                && operative.TeamId == exhaustedTeamId)
            .ToList();

        if (eligibles.Count == 0)
        {
            return false;
        }

        var choices = eligibles
            .Where(s => allOperatives.ContainsKey(s.OperativeId))
            .Select(s => allOperatives[s.OperativeId].Name)
            .Append("Skip counteract")
            .ToList();

        var selected = console.Prompt(
            new SelectionPrompt<string>()
                .Title("[yellow]Counteract available![/] Select operative to counteract, or skip:")
                .AddChoices(choices));

        if (selected == "Skip counteract")
        {
            return false;
        }

        var counterState = eligibles.First(s =>
            allOperatives.TryGetValue(s.OperativeId, out var operative) && operative.Name == selected);
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
            IsCounteract = true
        };
        await activationRepository.CreateAsync(counterActivation);

        console.MarkupLine($"[yellow]Counteract! {Markup.Escape(counterOperative.Name)} gets 1 AP (max 2\" movement).[/]");

        var counterActions = new[] { "Move (max 2\")", "Shoot", "Fight", "Skip" };
        var counterChoice = console.Prompt(
            new SelectionPrompt<string>()
                .Title($"{Markup.Escape(counterOperative.Name)} counteract action:")
                .AddChoices(counterActions));

        if (counterChoice == "Move (max 2\")")
        {
            var action = new GameAction
            {
                Id = Guid.NewGuid(),
                ActivationId = counterActivation.Id,
                Type = ActionType.Reposition,
                ApCost = 1,
                NarrativeNote = "Counteract move (max 2\")"
            };
            await actionRepository.CreateAsync(action);
        }
        else if (counterChoice == "Shoot")
        {
            await shootEngine.RunAsync(game, counterActivation, counterOperative, counterState, allStates, allOperatives);
        }
        else if (counterChoice == "Fight")
        {
            await fightEngine.RunAsync(game, counterActivation, counterOperative, counterState, allStates, allOperatives);
        }

        counterState.HasUsedCounteractThisTurningPoint = true;
        await stateRepository.SetCounteractUsedAsync(counterState.Id, true);

        return true;
    }

    private bool IsTurningPointOver(
        List<GameOperativeState> allStates,
        Dictionary<Guid, Operative> allOperatives,
        Game game)
    {
        var teamAAllIncap = allStates
            .Where(s => allOperatives.TryGetValue(s.OperativeId, out var operative) && operative.TeamId == game.Participant1.TeamId)
            .All(s => s.IsIncapacitated);
        var teamBAllIncap = allStates
            .Where(s => allOperatives.TryGetValue(s.OperativeId, out var operative) && operative.TeamId == game.Participant2.TeamId)
            .All(s => s.IsIncapacitated);

        if (teamAAllIncap || teamBAllIncap)
        {
            return true;
        }

        var teamAReady = allStates.Any(s =>
            !s.IsIncapacitated && s.IsReady
            && allOperatives.TryGetValue(s.OperativeId, out var operative) && operative.TeamId == game.Participant1.TeamId);
        var teamBReady = allStates.Any(s =>
            !s.IsIncapacitated && s.IsReady
            && allOperatives.TryGetValue(s.OperativeId, out var operative) && operative.TeamId == game.Participant2.TeamId);

        return !teamAReady && !teamBReady;
    }

    private bool IsGameOver(
        List<GameOperativeState> allStates,
        Dictionary<Guid, Operative> allOperatives,
        Game game,
        TurningPoint turningPoint)
    {
        var teamAAllIncap = allStates
            .Where(s => allOperatives.TryGetValue(s.OperativeId, out var operative) && operative.TeamId == game.Participant1.TeamId)
            .All(s => s.IsIncapacitated);
        var teamBAllIncap = allStates
            .Where(s => allOperatives.TryGetValue(s.OperativeId, out var operative) && operative.TeamId == game.Participant2.TeamId)
            .All(s => s.IsIncapacitated);

        if (teamAAllIncap || teamBAllIncap)
        {
            return true;
        }

        return turningPoint.Number >= 4 && IsTurningPointOver(allStates, allOperatives, game);
    }

    private async Task EndTurningPoint(
        Game game,
        TurningPoint turningPoint,
        List<GameOperativeState> allStates,
        Dictionary<Guid, Operative> allOperatives)
    {
        if (IsGameOver(allStates, allOperatives, game, turningPoint))
        {
            await EndGame(game, allStates, allOperatives);
            return;
        }

        console.MarkupLine($"[dim]Turning Point {turningPoint.Number} complete. Resetting for next turningPoint...[/]");

        foreach (var state in allStates.Where(s => !s.IsIncapacitated))
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

    private async Task EndGame(
        Game game,
        List<GameOperativeState> allStates,
        Dictionary<Guid, Operative> allOperatives)
    {
        console.Write(new Rule("[bold red]Game Over![/]"));

        var teamAAllIncap = allStates
            .Where(s => allOperatives.TryGetValue(s.OperativeId, out var operative) && operative.TeamId == game.Participant1.TeamId)
            .All(s => s.IsIncapacitated);
        var teamBAllIncap = allStates
            .Where(s => allOperatives.TryGetValue(s.OperativeId, out var operative) && operative.TeamId == game.Participant2.TeamId)
            .All(s => s.IsIncapacitated);

        var vp1 = console.Prompt(new TextPrompt<int>("Enter final VP for Team A:").Validate(v => v >= 0));
        var vp2 = console.Prompt(new TextPrompt<int>("Enter final VP for Team B:").Validate(v => v >= 0));

        string? winnerTeamId;
        if (teamAAllIncap && !teamBAllIncap)
        {
            winnerTeamId = game.Participant2.TeamId;
        }
        else if (teamBAllIncap && !teamAAllIncap)
        {
            winnerTeamId = game.Participant1.TeamId;
        }
        else
        {
            winnerTeamId = vp1 > vp2 ? game.Participant1.TeamId : vp2 > vp1 ? game.Participant2.TeamId : null;
        }

        await gameRepository.UpdateStatusAsync(game.Id, GameStatus.Completed, winnerTeamId, vp1, vp2);

        console.MarkupLine(winnerTeamId is not null
            ? $"[bold green]Winner: {(winnerTeamId == game.Participant1.TeamId ? "Team A" : "Team B")} — {(winnerTeamId == game.Participant1.TeamId ? vp1 : vp2)} VP[/]"
            : $"[yellow]Draw! Team A: {vp1} VP  |  Team B: {vp2} VP[/]");
    }

    private static List<(Operative operative, GameOperativeState state)> GetReadyOperatives(
        string teamId,
        List<GameOperativeState> allStates,
        Dictionary<Guid, Operative> allOperatives)
    {
        return allStates
            .Where(s => !s.IsIncapacitated && s.IsReady
                && allOperatives.TryGetValue(s.OperativeId, out var operative) && operative.TeamId == teamId)
            .Select(s => (allOperatives[s.OperativeId], s))
            .ToList();
    }

    private (Operative operative, GameOperativeState state) SelectOperative(
        List<(Operative operative, GameOperativeState state)> readyOperatives,
        string title)
    {
        if (readyOperatives.Count == 1)
        {
            return readyOperatives[0];
        }

        return console.Prompt(
            new SelectionPrompt<(Operative operative, GameOperativeState state)>()
                .Title(title)
                .UseConverter(pair =>
                    $"{Markup.Escape(pair.operative.Name)} (Wounds: {pair.state.CurrentWounds}/{pair.operative.Wounds}, {pair.state.Order})")
                .AddChoices(readyOperatives));
    }

    private static List<string> BuildActionMenu(
        Operative operative,
        GameOperativeState state,
        int remainingAp,
        bool hasMovedNonDash)
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

    private void DisplayBoardState(
        Game game,
        TurningPoint turningPoint,
        List<GameOperativeState> allStates,
        Dictionary<Guid, Operative> allOperatives)
    {
        var table = new Table()
            .Title($"[bold]TP {turningPoint.Number} — Board State[/]")
            .Border(TableBorder.Rounded)
            .AddColumn("Name")
            .AddColumn("W")
            .AddColumn("Order")
            .AddColumn("Status")
            .AddColumn("Guard");

        foreach (var state in allStates)
        {
            if (!allOperatives.TryGetValue(state.OperativeId, out var operative))
            {
                continue;
            }

            var teamTag = operative.TeamId == game.Participant1.TeamId ? "[blue]A[/]" : "[red]B[/]";
            var name = $"{teamTag} {Markup.Escape(operative.Name)}";

            var injured = state.CurrentWounds < operative.Wounds / 2;
            var wounds = injured
                ? $"{state.CurrentWounds}/{operative.Wounds} [yellow](Injured)[/]"
                : $"{state.CurrentWounds}/{operative.Wounds}";

            var status = state.IsIncapacitated
                ? "[red]Incapacitated[/]"
                : state.IsReady ? "[green]Ready[/]" : "[dim]Expended[/]";

            var guard = state.IsOnGuard ? "[yellow]⚑[/]" : string.Empty;

            table.AddRow(name, wounds, state.Order.ToString(), status, guard);
        }

        console.Write(table);
        console.MarkupLine($"  CP → A:[yellow]{game.Participant1.CommandPoints}[/]  B:[yellow]{game.Participant2.CommandPoints}[/]");
    }
}


