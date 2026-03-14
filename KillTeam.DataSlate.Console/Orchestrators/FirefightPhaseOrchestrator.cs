using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using KillTeam.DataSlate.Domain.Services;
using Spectre.Console;

namespace KillTeam.DataSlate.Console.Orchestrators;

public class FirefightPhaseOrchestrator(
    IAnsiConsole console,
    IGameRepository gameRepository,
    IGameOperativeStateRepository stateRepository,
    IActivationRepository activationRepository,
    IActionRepository actionRepository,
    ITeamRepository teamRepository,
    ShootSessionOrchestrator shootOrchestrator,
    FightSessionOrchestrator fightOrchestrator,
    GuardInterruptOrchestrator guardInterruptOrchestrator)
{
    public async Task RunAsync(Game game, TurningPoint currentTp)
    {
        var teamA = await teamRepository.GetWithOperativesAsync(game.Participant1.TeamName);
        var teamB = await teamRepository.GetWithOperativesAsync(game.Participant2.TeamName);
        var allOperatives = (teamA?.Operatives ?? [])
            .Concat(teamB?.Operatives ?? [])
            .ToDictionary(o => o.Id);

        var allStates = (await stateRepository.GetByGameAsync(game.Id)).ToList();

        await RunFirefightPhase(game, currentTp, allOperatives, allStates);
    }

    private async Task RunFirefightPhase(
        Game game,
        TurningPoint tp,
        Dictionary<Guid, Operative> allOperatives,
        List<GameOperativeState> allStates)
    {
        console.Write(new Rule($"[bold]Turning Point {tp.Number} — Firefight Phase[/]"));

        var initiativeTeamId = tp.TeamWithInitiativeId ?? game.Participant1.TeamId;
        var currentTeamId = initiativeTeamId;

        var existingActivations = (await activationRepository.GetByTurningPointAsync(tp.Id)).ToList();
        var seqCounter = existingActivations.Count > 0 ? existingActivations.Max(a => a.SequenceNumber) : 0;

        DisplayBoardState(game, tp, allStates, allOperatives);

        while (!IsTurningPointOver(allStates, allOperatives, game))
        {
            var readyThis = GetReadyOps(currentTeamId, allStates, allOperatives);
            var otherTeamId = currentTeamId == game.Participant1.TeamId ? game.Participant2.TeamId : game.Participant1.TeamId;
            var readyOther = GetReadyOps(otherTeamId, allStates, allOperatives);

            if (readyThis.Count > 0)
            {
                var (op, state) = SelectOperative(readyThis, "Select an operative to activate:");
                seqCounter++;
                var activation = new Activation
                {
                    Id = Guid.NewGuid(),
                    TurningPointId = tp.Id,
                    SequenceNumber = seqCounter,
                    OperativeId = op.Id,
                    TeamId = op.TeamId,
                    IsCounteract = false
                };
                activation = await activationRepository.CreateAsync(activation);

                seqCounter = await RunActivation(op, state, tp, activation, allStates, allOperatives, game, seqCounter);
                game = (await gameRepository.GetByIdAsync(game.Id))!;

                if (IsGameOver(allStates, allOperatives, game, tp))
                {
                    await EndGame(game, allStates, allOperatives);
                    return;
                }

                currentTeamId = otherTeamId;
            }
            else if (readyOther.Count > 0)
            {
                var counteractTaken = await TryOfferCounteract(
                    currentTeamId, tp, allStates, allOperatives, game, seqCounter);

                if (counteractTaken)
                {
                    // Re-query seq counter after counteract
                    var updated = (await activationRepository.GetByTurningPointAsync(tp.Id)).ToList();
                    seqCounter = updated.Count > 0 ? updated.Max(a => a.SequenceNumber) : seqCounter;
                }

                game = (await gameRepository.GetByIdAsync(game.Id))!;

                if (IsGameOver(allStates, allOperatives, game, tp))
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
                    var (op, state) = SelectOperative(readyOther, "Select an operative to activate:");
                    seqCounter++;
                    var activation = new Activation
                    {
                        Id = Guid.NewGuid(),
                        TurningPointId = tp.Id,
                        SequenceNumber = seqCounter,
                        OperativeId = op.Id,
                        TeamId = op.TeamId,
                        IsCounteract = false
                    };
                    activation = await activationRepository.CreateAsync(activation);

                    seqCounter = await RunActivation(op, state, tp, activation, allStates, allOperatives, game, seqCounter);
                    game = (await gameRepository.GetByIdAsync(game.Id))!;

                    if (IsGameOver(allStates, allOperatives, game, tp))
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

        await EndTurningPoint(game, tp, allStates, allOperatives);
    }

    private async Task<int> RunActivation(
        Operative op,
        GameOperativeState state,
        TurningPoint tp,
        Activation activation,
        List<GameOperativeState> allStates,
        Dictionary<Guid, Operative> allOperatives,
        Game game,
        int seqCounter)
    {
        console.Write(new Rule($"[cyan]{Markup.Escape(op.Name)}[/] activation"));

        var orderChoice = console.Prompt(
            new SelectionPrompt<string>()
                .Title($"Set order for {Markup.Escape(op.Name)}:")
                .AddChoices("Engage", "Conceal"));

        var order = orderChoice == "Engage" ? Order.Engage : Order.Conceal;
        state.Order = order;
        await stateRepository.UpdateOrderAsync(state.Id, order);
        activation.OrderSelected = order;

        var remainingAp = Math.Max(1, op.Apl + state.AplModifier);
        var hasMovedNonDash = false;

        while (remainingAp > 0 && !state.IsIncapacitated)
        {
            DisplayBoardState(game, tp, allStates, allOperatives);

            var availableActions = BuildActionMenu(op, state, remainingAp, hasMovedNonDash);
            var selectedAction = console.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[bold]{Markup.Escape(op.Name)}[/] — {remainingAp} AP remaining. Choose an action:")
                    .AddChoices(availableActions));

            if (selectedAction == "End Activation")
            {
                break;
            }

            if (selectedAction == "Shoot")
            {
                await shootOrchestrator.RunAsync(
                    op, state, allStates, allOperatives, game, tp, activation, hasMovedNonDash);
            }
            else if (selectedAction == "Fight")
            {
                await fightOrchestrator.RunAsync(
                    op, state, allStates, allOperatives, game, tp, activation);
            }
            else if (selectedAction == "Guard")
            {
                state.IsOnGuard = true;
                await stateRepository.UpdateGuardAsync(state.Id, true);
                console.MarkupLine($"[green]{Markup.Escape(op.Name)} is now On Guard.[/]");

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
                ActionType actionType = selectedAction switch
                {
                    "Reposition" => ActionType.Reposition,
                    "Dash" => ActionType.Dash,
                    "Fall Back" => ActionType.FallBack,
                    "Charge" => ActionType.Charge,
                    _ => ActionType.Other
                };

                var distanceStr = console.Prompt(
                    new TextPrompt<string>($"How far did {Markup.Escape(op.Name)} move? (e.g. '3\"', leave blank to skip):")
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
                seqCounter = await guardInterruptOrchestrator.CheckAndRunInterruptsAsync(
                    op, state, allStates, allOperatives, game, tp, seqCounter);

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
        TurningPoint tp,
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
                && allOperatives.TryGetValue(s.OperativeId, out var o)
                && o.TeamId == exhaustedTeamId)
            .ToList();

        if (eligibles.Count == 0) return false;

        var choices = eligibles
            .Where(s => allOperatives.ContainsKey(s.OperativeId))
            .Select(s => allOperatives[s.OperativeId].Name)
            .Append("Skip counteract")
            .ToList();

        var selected = console.Prompt(
            new SelectionPrompt<string>()
                .Title("[yellow]Counteract available![/] Select operative to counteract, or skip:")
                .AddChoices(choices));

        if (selected == "Skip counteract") return false;

        var counterState = eligibles.First(s =>
            allOperatives.TryGetValue(s.OperativeId, out var o) && o.Name == selected);
        var counterOp = allOperatives[counterState.OperativeId];

        seqCounter++;
        var counterActivation = new Activation
        {
            Id = Guid.NewGuid(),
            TurningPointId = tp.Id,
            SequenceNumber = seqCounter,
            OperativeId = counterOp.Id,
            TeamId = counterOp.TeamId,
            OrderSelected = counterState.Order,
            IsCounteract = true
        };
        counterActivation = await activationRepository.CreateAsync(counterActivation);

        console.MarkupLine($"[yellow]Counteract! {Markup.Escape(counterOp.Name)} gets 1 AP (max 2\" movement).[/]");

        var counterActions = new[] { "Move (max 2\")", "Shoot", "Fight", "Skip" };
        var counterChoice = console.Prompt(
            new SelectionPrompt<string>()
                .Title($"{Markup.Escape(counterOp.Name)} counteract action:")
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
            await shootOrchestrator.RunAsync(
                counterOp, counterState, allStates, allOperatives, game, tp, counterActivation);
        }
        else if (counterChoice == "Fight")
        {
            await fightOrchestrator.RunAsync(
                counterOp, counterState, allStates, allOperatives, game, tp, counterActivation);
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
            .Where(s => allOperatives.TryGetValue(s.OperativeId, out var o) && o.TeamId == game.Participant1.TeamId)
            .All(s => s.IsIncapacitated);
        var teamBAllIncap = allStates
            .Where(s => allOperatives.TryGetValue(s.OperativeId, out var o) && o.TeamId == game.Participant2.TeamId)
            .All(s => s.IsIncapacitated);

        if (teamAAllIncap || teamBAllIncap)
        {
            return true;
        }

        var teamAReady = allStates.Any(s =>
            !s.IsIncapacitated && s.IsReady
            && allOperatives.TryGetValue(s.OperativeId, out var o) && o.TeamId == game.Participant1.TeamId);
        var teamBReady = allStates.Any(s =>
            !s.IsIncapacitated && s.IsReady
            && allOperatives.TryGetValue(s.OperativeId, out var o) && o.TeamId == game.Participant2.TeamId);

        return !teamAReady && !teamBReady;
    }

    private bool IsGameOver(
        List<GameOperativeState> allStates,
        Dictionary<Guid, Operative> allOperatives,
        Game game,
        TurningPoint tp)
    {
        var teamAAllIncap = allStates
            .Where(s => allOperatives.TryGetValue(s.OperativeId, out var o) && o.TeamId == game.Participant1.TeamId)
            .All(s => s.IsIncapacitated);
        var teamBAllIncap = allStates
            .Where(s => allOperatives.TryGetValue(s.OperativeId, out var o) && o.TeamId == game.Participant2.TeamId)
            .All(s => s.IsIncapacitated);

        if (teamAAllIncap || teamBAllIncap)
        {
            return true;
        }

        return tp.Number >= 4 && IsTurningPointOver(allStates, allOperatives, game);
    }

    private async Task EndTurningPoint(
        Game game,
        TurningPoint tp,
        List<GameOperativeState> allStates,
        Dictionary<Guid, Operative> allOperatives)
    {
        if (IsGameOver(allStates, allOperatives, game, tp))
        {
            await EndGame(game, allStates, allOperatives);
            return;
        }

        console.MarkupLine($"[dim]Turning Point {tp.Number} complete. Resetting for next TP...[/]");

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
            .Where(s => allOperatives.TryGetValue(s.OperativeId, out var o) && o.TeamId == game.Participant1.TeamId)
            .All(s => s.IsIncapacitated);
        var teamBAllIncap = allStates
            .Where(s => allOperatives.TryGetValue(s.OperativeId, out var o) && o.TeamId == game.Participant2.TeamId)
            .All(s => s.IsIncapacitated);

        var vpA = console.Prompt(new TextPrompt<int>("Enter final VP for Team A:").Validate(v => v >= 0));
        var vpB = console.Prompt(new TextPrompt<int>("Enter final VP for Team B:").Validate(v => v >= 0));

        string? winnerTeamId;
        if (teamAAllIncap && !teamBAllIncap)
            winnerTeamId = game.Participant2.TeamId;
        else if (teamBAllIncap && !teamAAllIncap)
            winnerTeamId = game.Participant1.TeamId;
        else
            winnerTeamId = vpA > vpB ? game.Participant1.TeamId : vpB > vpA ? game.Participant2.TeamId : null;

        await gameRepository.UpdateStatusAsync(game.Id, GameStatus.Completed, winnerTeamId, vpA, vpB);

        if (winnerTeamId is not null)
        {
            console.MarkupLine($"[bold green]Winner: {(winnerTeamId == game.Participant1.TeamId ? "Team A" : "Team B")} — {(winnerTeamId == game.Participant1.TeamId ? vpA : vpB)} VP[/]");
        }
        else
        {
            console.MarkupLine($"[yellow]Draw! Team A: {vpA} VP  |  Team B: {vpB} VP[/]");
        }
    }

    private static List<(Operative op, GameOperativeState state)> GetReadyOps(
        string teamId,
        List<GameOperativeState> allStates,
        Dictionary<Guid, Operative> allOperatives)
    {
        return allStates
            .Where(s => !s.IsIncapacitated && s.IsReady
                && allOperatives.TryGetValue(s.OperativeId, out var o) && o.TeamId == teamId)
            .Select(s => (allOperatives[s.OperativeId], s))
            .ToList();
    }

    private (Operative op, GameOperativeState state) SelectOperative(
        List<(Operative op, GameOperativeState state)> readyOps,
        string title)
    {
        if (readyOps.Count == 1)
        {
            return readyOps[0];
        }

        return console.Prompt(
            new SelectionPrompt<(Operative op, GameOperativeState state)>()
                .Title(title)
                .UseConverter(pair =>
                    $"{Markup.Escape(pair.op.Name)} (W:{pair.state.CurrentWounds}/{pair.op.Wounds}, {pair.state.Order})")
                .AddChoices(readyOps));
    }

    private static List<string> BuildActionMenu(
        Operative op,
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

            if (op.Weapons.Any(w => w.Type == WeaponType.Ranged))
            {
                actions.Add("Shoot");
            }

            if (op.Weapons.Any(w => w.Type == WeaponType.Melee))
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
        TurningPoint tp,
        List<GameOperativeState> allStates,
        Dictionary<Guid, Operative> allOperatives)
    {
        var table = new Table()
            .Title($"[bold]TP {tp.Number} — Board State[/]")
            .Border(TableBorder.Rounded)
            .AddColumn("Name")
            .AddColumn("W")
            .AddColumn("Order")
            .AddColumn("Status")
            .AddColumn("Guard");

        foreach (var state in allStates)
        {
            if (!allOperatives.TryGetValue(state.OperativeId, out var op))
            {
                continue;
            }

            var teamTag = op.TeamId == game.Participant1.TeamId ? "[blue]A[/]" : "[red]B[/]";
            var name = $"{teamTag} {Markup.Escape(op.Name)}";

            var injured = state.CurrentWounds < op.Wounds / 2;
            var wounds = injured
                ? $"{state.CurrentWounds}/{op.Wounds} [yellow](Injured)[/]"
                : $"{state.CurrentWounds}/{op.Wounds}";

            var status = state.IsIncapacitated
                ? "[red]Incapacitated[/]"
                : state.IsReady ? "[green]Ready[/]" : "[dim]Expended[/]";

            var guard = state.IsOnGuard ? "[yellow]⚑[/]" : "";

            table.AddRow(name, wounds, state.Order.ToString(), status, guard);
        }

        console.Write(table);
        console.MarkupLine($"  CP → A:[yellow]{game.Participant1.CommandPoints}[/]  B:[yellow]{game.Participant2.CommandPoints}[/]");
    }
}
