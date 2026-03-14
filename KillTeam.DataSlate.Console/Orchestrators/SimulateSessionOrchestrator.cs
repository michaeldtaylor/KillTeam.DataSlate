using KillTeam.DataSlate.Console.Infrastructure.Repositories;
using KillTeam.DataSlate.Domain.Repositories;
using KillTeam.DataSlate.Domain.Services;
using Spectre.Console;
using Models = KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Console.Orchestrators;

/// <summary>
/// Drives the simulate command: operative selection and an ad-hoc fight/shoot session loop.
/// Uses in-memory repositories so no game data is persisted.
/// </summary>
public class SimulateSessionOrchestrator(
    IAnsiConsole console,
    IKillTeamRepository killTeamRepository,
    CombatResolutionService combatResolutionService,
    FightResolutionService fightResolutionService,
    RerollOrchestrator rerollOrchestrator)
{
    public async Task RunAsync()
    {
        console.Write(new Rule("[bold cyan]⚔ Simulate Mode[/]"));
        console.MarkupLine("[dim]Test fight and shoot encounters without a full game session. Nothing is saved.[/]");
        console.WriteLine();

        var (playerOp, aiOp, playerTeam, aiTeam) = await SelectOperativesAsync();
        if (playerOp is null || aiOp is null)
        {
            return;
        }

        await RunSessionLoopAsync(playerOp, aiOp, playerTeam!, aiTeam!);
    }

    // --- Operative selection -------------------------------------------------

    private async Task<(Models.Operative? playerOp, Models.Operative? aiOp, Models.KillTeam? playerTeam, Models.KillTeam? aiTeam)> SelectOperativesAsync()
    {
        var allTeams = (await killTeamRepository.GetAllAsync()).ToList();
        if (allTeams.Count == 0)
        {
            console.MarkupLine("[red]No kill teams imported. Run [bold]import-kill-teams[/] first.[/]");
            return (null, null, null, null);
        }

        // Step 1 - your kill team
        var playerTeamStub = console.Prompt(
            new SelectionPrompt<Models.KillTeam>()
                .Title("Select [bold]your[/] kill team:")
                .UseConverter(t => $"{Markup.Escape(t.Name)} [dim]({Markup.Escape(t.Faction)})[/]")
                .AddChoices(allTeams));

        var playerTeam = (await killTeamRepository.GetWithOperativesAsync(playerTeamStub.Name))!;

        // Step 2 - your operative
        var playerOp = console.Prompt(
            new SelectionPrompt<Models.Operative>()
                .Title("Select [bold]your[/] operative:")
                .UseConverter(FormatOperative)
                .AddChoices(playerTeam.Operatives));

        // Step 3 - AI's kill team (any team, including same as player)
        var aiTeamStub = console.Prompt(
            new SelectionPrompt<Models.KillTeam>()
                .Title("Select the [bold]AI[/] kill team:")
                .UseConverter(t => $"{Markup.Escape(t.Name)} [dim]({Markup.Escape(t.Faction)})[/]")
                .AddChoices(allTeams));

        var aiTeam = aiTeamStub.Name == playerTeam.Name
            ? playerTeam
            : (await killTeamRepository.GetWithOperativesAsync(aiTeamStub.Name))!;

        // Step 4 - AI's operative
        var aiOp = console.Prompt(
            new SelectionPrompt<Models.Operative>()
                .Title("Select the [bold]AI[/] operative:")
                .UseConverter(FormatOperative)
                .AddChoices(aiTeam.Operatives));

        DisplayMatchup(playerOp, playerTeam, aiOp, aiTeam);
        return (playerOp, aiOp, playerTeam, aiTeam);
    }

    // --- Session loop --------------------------------------------------------

    private async Task RunSessionLoopAsync(
        Models.Operative playerOp, Models.Operative aiOp,
        Models.KillTeam playerTeam, Models.KillTeam aiTeam)
    {
        while (true)
        {
            var choice = console.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select an action:")
                    .AddChoices("Fight", "Shoot", "Change operatives", "Done"));

            switch (choice)
            {
                case "Fight":
                    await RunEncounterAsync(playerOp, aiOp, playerTeam, aiTeam, Models.ActionType.Fight);
                    break;

                case "Shoot":
                    await RunEncounterAsync(playerOp, aiOp, playerTeam, aiTeam, Models.ActionType.Shoot);
                    break;

                case "Change operatives":
                    var result = await SelectOperativesAsync();
                    if (result.playerOp is not null)
                    {
                        playerOp = result.playerOp;
                        aiOp = result.aiOp!;
                        playerTeam = result.playerTeam!;
                        aiTeam = result.aiTeam!;
                    }
                    break;

                case "Done":
                    console.MarkupLine("[dim]Simulate session ended.[/]");
                    return;
            }
        }
    }

    // --- Single encounter ----------------------------------------------------

    private async Task RunEncounterAsync(
        Models.Operative playerOp, Models.Operative aiOp,
        Models.KillTeam playerTeam, Models.KillTeam aiTeam,
        Models.ActionType actionType)
    {
        // Synthetic domain objects - no DB interaction
        var game = new Models.Game
        {
            Id = Guid.NewGuid(),
            TeamAName = playerTeam.Name,
            TeamBName = aiTeam.Name,
            PlayerAId = Guid.Empty,
            PlayerBId = Guid.Empty,
            CpTeamA = 0,   // suppresses CP re-roll prompts (RerollOrchestrator skips when game not in DB)
            CpTeamB = 0,
            PlayedAt = DateTime.UtcNow
        };

        var tp = new Models.TurningPoint
        {
            Id = Guid.NewGuid(),
            GameId = game.Id,
            Number = 1
        };

        var activation = new Models.Activation
        {
            Id = Guid.NewGuid(),
            TurningPointId = tp.Id,
            OperativeId = playerOp.Id,
            TeamName = playerTeam.Name,
            OrderSelected = Models.Order.Engage,
            SequenceNumber = 1
        };

        // Fresh in-memory repos - wound state resets each encounter
        var stateRepo = new InMemoryGameOperativeStateRepository();
        var playerState = new Models.GameOperativeState
        {
            GameId = game.Id,
            OperativeId = playerOp.Id,
            CurrentWounds = playerOp.Wounds,
            Order = Models.Order.Engage
        };
        var aiState = new Models.GameOperativeState
        {
            GameId = game.Id,
            OperativeId = aiOp.Id,
            CurrentWounds = aiOp.Wounds,
            Order = Models.Order.Engage
        };
        stateRepo.Seed([playerState, aiState]);

        var actionRepo = new InMemoryActionRepository();
        var blastTargetRepo = new InMemoryBlastTargetRepository();

        var allStates = stateRepo.GetAll();
        var allOperatives = new Dictionary<Guid, Models.Operative>
        {
            [playerOp.Id] = playerOp,
            [aiOp.Id] = aiOp
        };

        if (actionType == Models.ActionType.Fight)
        {
            var fightOrchestrator = new FightSessionOrchestrator(
                console, fightResolutionService, rerollOrchestrator, stateRepo, actionRepo);

            var fightResult = await fightOrchestrator.RunAsync(
                playerOp, playerState, allStates, allOperatives, game, tp, activation);

            DisplayEncounterSummary(playerOp, aiOp,
                fightResult.AttackerDamageDealt, fightResult.DefenderDamageDealt,
                fightResult.AttackerCausedIncapacitation, fightResult.DefenderCausedIncapacitation);
        }
        else
        {
            var blastOrchestrator = new BlastTorrentSessionOrchestrator(
                console, combatResolutionService, rerollOrchestrator, stateRepo, actionRepo, blastTargetRepo);

            var shootOrchestrator = new ShootSessionOrchestrator(
                console, combatResolutionService, rerollOrchestrator, blastOrchestrator, stateRepo, actionRepo);

            var shootResult = await shootOrchestrator.RunAsync(
                playerOp, playerState, allStates, allOperatives, game, tp, activation);

            console.MarkupLine(shootResult.CausedIncapacitation
                ? $"[red]💀 {Markup.Escape(aiOp.Name)} incapacitated! Dealt {shootResult.DamageDealt} damage.[/]"
                : $"Dealt [bold]{shootResult.DamageDealt}[/] damage to {Markup.Escape(aiOp.Name)}.");
        }
    }

    // --- Display helpers -----------------------------------------------------

    private static string FormatOperative(Models.Operative o) =>
        $"{Markup.Escape(o.Name)} [dim](M{o.Move}\" APL{o.Apl} W{o.Wounds} SV{o.Save}+)[/]";

    private void DisplayMatchup(
        Models.Operative playerOp, Models.KillTeam playerTeam,
        Models.Operative aiOp, Models.KillTeam aiTeam)
    {
        console.WriteLine();
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold cyan]You[/]")
            .AddColumn("[bold red]AI[/]");

        table.AddRow(
            $"[bold]{Markup.Escape(playerOp.Name)}[/]\n[dim]{Markup.Escape(playerTeam.Name)}[/]",
            $"[bold]{Markup.Escape(aiOp.Name)}[/]\n[dim]{Markup.Escape(aiTeam.Name)}[/]");

        table.AddRow(
            $"W{playerOp.Wounds}  SV{playerOp.Save}+  APL{playerOp.Apl}",
            $"W{aiOp.Wounds}  SV{aiOp.Save}+  APL{aiOp.Apl}");

        console.Write(table);
        console.WriteLine();
    }

    private void DisplayEncounterSummary(
        Models.Operative attacker, Models.Operative defender,
        int atkDmg, int defDmg,
        bool atkIncap, bool defIncap)
    {
        console.WriteLine();
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Result")
            .AddColumn("[bold cyan]You[/]")
            .AddColumn("[bold red]AI[/]");

        table.AddRow("Damage dealt", $"[bold]{atkDmg}[/]", $"[bold]{defDmg}[/]");
        table.AddRow("Incapacitated?",
            atkIncap ? "[red]No — you were incapacitated[/]" : "[green]Alive[/]",
            defIncap ? "[red]Incapacitated[/]" : "[green]Alive[/]");

        console.Write(table);
        console.WriteLine();
    }
}
