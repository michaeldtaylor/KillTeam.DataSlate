using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using KillTeam.DataSlate.Domain.Services;
using Spectre.Console;

namespace KillTeam.DataSlate.Console.Orchestrators;

public class GuardInterruptOrchestrator(
    GuardResolutionService guardResolutionService,
    ShootSessionOrchestrator shootOrchestrator,
    FightSessionOrchestrator fightOrchestrator,
    IAnsiConsole console,
    IActivationRepository activationRepository,
    IGameOperativeStateRepository stateRepository)
{
    /// <summary>
    /// Checks each eligible guard operative on the friendly side.
    /// Returns the updated sequence counter after any guard interrupt activations.
    /// </summary>
    public async Task<int> CheckAndRunInterruptsAsync(
        Operative actingEnemy,
        GameOperativeState actingEnemyState,
        IReadOnlyList<GameOperativeState> allOperativeStates,
        IReadOnlyDictionary<Guid, Operative> allOperatives,
        Game game,
        TurningPoint tp,
        int seqCounter)
    {
        // Determine which team is "friendly" (the guard team = NOT the acting enemy's team)
        var enemyTeamId = actingEnemy.TeamId;

        var friendlyStates = allOperativeStates
            .Where(s => allOperatives.TryGetValue(s.OperativeId, out var o) && o.TeamId != enemyTeamId)
            .ToList();

        var eligibleGuards = guardResolutionService.GetEligibleGuards(friendlyStates);
        if (eligibleGuards.Count == 0)
        {
            return seqCounter;
        }

        foreach (var guardState in eligibleGuards)
        {
            if (!allOperatives.TryGetValue(guardState.OperativeId, out var guardOp))
            {
                continue;
            }

            // 1. Check if enemy is in control range (6") — clears guard
            var inControlRange = console.Confirm(
                $"Is [bold]{Markup.Escape(actingEnemy.Name)}[/] within 6\" (control range) of [bold]{Markup.Escape(guardOp.Name)}[/]?",
                defaultValue: false);

            if (inControlRange)
            {
                await stateRepository.UpdateGuardAsync(guardState.Id, false);
                guardState.IsOnGuard = false;
                console.MarkupLine($"  [dim]{Markup.Escape(guardOp.Name)} is now Engaged — guard cleared.[/]");
                continue;
            }

            // 2. Visibility check
            var isVisible = console.Confirm(
                $"Is [bold]{Markup.Escape(actingEnemy.Name)}[/] visible to [bold]{Markup.Escape(guardOp.Name)}[/]?",
                defaultValue: true);

            if (!isVisible)
            {
                console.MarkupLine($"  [dim]{Markup.Escape(actingEnemy.Name)} is not visible — guard preserved.[/]");
                continue;
            }

            // 3. Guard interrupt choice
            var interruptChoice = console.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[bold yellow]⚔ Guard interrupt available![/] Use {Markup.Escape(guardOp.Name)}'s Guard?")
                    .AddChoices("Shoot", "Fight", "Skip"));

            if (interruptChoice == "Skip")
            {
                console.MarkupLine($"  [dim]{Markup.Escape(guardOp.Name)} stands guard.[/]");
                continue;
            }

            // Create guard interrupt activation
            seqCounter++;
            var interruptActivation = new Activation
            {
                Id = Guid.NewGuid(),
                TurningPointId = tp.Id,
                SequenceNumber = seqCounter,
                OperativeId = guardOp.Id,
                TeamId = guardOp.TeamId,
                OrderSelected = guardState.Order,
                IsGuardInterrupt = true
            };
            await activationRepository.CreateAsync(interruptActivation);

            if (interruptChoice == "Shoot")
            {
                await shootOrchestrator.RunAsync(
                    guardOp, guardState,
                    allOperativeStates,
                    allOperatives,
                    game, tp, interruptActivation);
            }
            else // Fight
            {
                await fightOrchestrator.RunAsync(
                    guardOp, guardState,
                    allOperativeStates,
                    allOperatives,
                    game, tp, interruptActivation);
            }

            // Clear guard after interrupt
            await stateRepository.UpdateGuardAsync(guardState.Id, false);
            guardState.IsOnGuard = false;
        }

        return seqCounter;
    }
}
