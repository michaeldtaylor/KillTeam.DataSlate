using KillTeam.DataSlate.Console.InputProviders;
using KillTeam.DataSlate.Console.Rendering;
using KillTeam.DataSlate.Domain.Engine;
using KillTeam.DataSlate.Domain.Engine.Input;
using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Repositories;
using KillTeam.DataSlate.Domain.Repositories.InMemory;
using KillTeam.DataSlate.Domain.Services;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Models = KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Console.Orchestrators;

/// <summary>
/// Drives the simulate command: operative selection and an ad-hoc fight/shoot session loop.
/// Uses in-memory repositories so no game data is persisted.
/// </summary>
public class SimulateSessionOrchestrator(
    IAnsiConsole console,
    ITeamRepository teamRepository,
    IGameRepository gameRepository,
    IFightInputProvider fightInputProvider,
    IShootInputProvider shootInputProvider,
    IRerollInputProvider rerollInputProvider,
    IBlastInputProvider blastInputProvider,
    CombatResolutionService combatResolutionService,
    FightResolutionService fightResolutionService,
    ILogger<SimulateSessionOrchestrator> logger)
{
    public async Task RunAsync()
    {
        logger.LogInformation("Simulate session started");
        console.Write(new Rule("[bold cyan]Simulate Mode[/]"));
        console.MarkupLine("[dim]Test fight and shoot encounters without a full game session. Nothing is saved.[/]");
        console.WriteLine();

        var (playerOperative, aiOperative, playerTeam, aiTeam) = await SelectOperativesAsync();

        if (playerOperative is null || aiOperative is null)
        {
            return;
        }

        await RunSessionLoopAsync(playerOperative, aiOperative, playerTeam!, aiTeam!);
        logger.LogInformation("Simulate session ended");
    }

    // --- Operative selection -------------------------------------------------

    private async Task<(Models.Operative? playerOperative, Models.Operative? aiOperative, Models.Team? playerTeam, Models.Team? aiTeam)> SelectOperativesAsync()
    {
        var allTeams = (await teamRepository.GetAllAsync()).ToList();

        if (allTeams.Count == 0)
        {
            console.MarkupLine("[red]No teams imported. Run [bold]import-teams[/] first.[/]");
            return (null, null, null, null);
        }

        // Step 1 - your team
        var playerTeamStub = console.Prompt(
            new SelectionPrompt<Models.Team>()
                .Title("Select [bold]your[/] team:")
                .UseConverter(FormatTeam)
                .AddChoices(allTeams));

        var playerTeam = (await teamRepository.GetWithOperativesAsync(playerTeamStub.Name))!;

        // Step 2 - your operative
        var playerOperative = console.Prompt(
            new SelectionPrompt<Models.Operative>()
                .Title("Select [bold]your[/] operative:")
                .UseConverter(FormatOperative)
                .AddChoices(playerTeam.Operatives));

        // Step 3 - AI's team (exclude player's team)
        var aiTeamChoices = allTeams.Where(t => t.Name != playerTeamStub.Name).ToList();

        if (aiTeamChoices.Count == 0)
        {
            console.MarkupLine("[red]No other teams available for the AI. Import more teams first.[/]");
            return (null, null, null, null);
        }

        var aiTeamStub = console.Prompt(
            new SelectionPrompt<Models.Team>()
                .Title("Select the [bold]AI[/] team:")
                .UseConverter(FormatTeam)
                .AddChoices(aiTeamChoices));

        var aiTeam = (await teamRepository.GetWithOperativesAsync(aiTeamStub.Name))!;

        // Step 4 - AI's operative
        var aiOperative = console.Prompt(
            new SelectionPrompt<Models.Operative>()
                .Title("Select the [bold]AI[/] operative:")
                .UseConverter(FormatOperative)
                .AddChoices(aiTeam.Operatives));

        DisplayMatchup(playerOperative, playerTeam, aiOperative, aiTeam);
        return (playerOperative, aiOperative, playerTeam, aiTeam);
    }

    // --- Session loop --------------------------------------------------------

    private async Task RunSessionLoopAsync(
        Models.Operative playerOperative, Models.Operative aiOperative,
        Models.Team playerTeam, Models.Team aiTeam)
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
                    await RunEncounterAsync(playerOperative, aiOperative, playerTeam, aiTeam, Models.ActionType.Fight);
                    break;

                case "Shoot":
                    await RunEncounterAsync(playerOperative, aiOperative, playerTeam, aiTeam, Models.ActionType.Shoot);
                    break;

                case "Change operatives":
                    var result = await SelectOperativesAsync();

                    if (result.playerOperative is not null)
                    {
                        playerOperative = result.playerOperative;
                        aiOperative = result.aiOperative!;
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
        Models.Operative playerOperative, Models.Operative aiOperative,
        Models.Team playerTeam, Models.Team aiTeam,
        Models.ActionType actionType)
    {
        // Synthetic domain objects - no DB interaction
        var game = new Models.Game
        {
            Id = Guid.NewGuid(),
            Participant1 = new Models.GameParticipant
            {
                TeamId = playerTeam.Id,
                TeamName = playerTeam.Name,
                PlayerId = Guid.Empty,
                CommandPoints = 0,  // suppresses CP re-roll prompts (RerollEngine skips when game not in DB)
            },
            Participant2 = new Models.GameParticipant
            {
                TeamId = aiTeam.Id,
                TeamName = aiTeam.Name,
                PlayerId = Guid.Empty,
                CommandPoints = 0,
            },
            PlayedAt = DateTime.UtcNow
        };

        var turningPoint = new Models.TurningPoint
        {
            Id = Guid.NewGuid(),
            GameId = game.Id,
            Number = 1
        };

        var activation = new Models.Activation
        {
            Id = Guid.NewGuid(),
            TurningPointId = turningPoint.Id,
            OperativeId = playerOperative.Id,
            TeamId = playerTeam.Id,
            OrderSelected = Models.Order.Engage,
            SequenceNumber = 1
        };

        // Fresh in-memory repos - wound state resets each encounter
        var stateRepo = new InMemoryGameOperativeStateRepository();
        var playerState = new Models.GameOperativeState
        {
            GameId = game.Id,
            OperativeId = playerOperative.Id,
            CurrentWounds = playerOperative.Wounds,
            Order = Models.Order.Engage
        };
        var aiState = new Models.GameOperativeState
        {
            GameId = game.Id,
            OperativeId = aiOperative.Id,
            CurrentWounds = aiOperative.Wounds,
            Order = Models.Order.Engage
        };
        stateRepo.Seed([playerState, aiState]);

        var actionRepo = new InMemoryActionRepository();
        var blastTargetRepo = new InMemoryBlastTargetRepository();

        var allStates = stateRepo.GetAll();
        var allOperatives = new Dictionary<Guid, Models.Operative>
        {
            [playerOperative.Id] = playerOperative,
            [aiOperative.Id] = aiOperative
        };

        var stream = new GameEventStream(game.Id);
        var participantLabels = new Dictionary<string, string>
        {
            [playerTeam.Id] = "You",
            [aiTeam.Id] = "AI"
        };
        var columns = new TwoColumnRenderer(console, participantLabels);
        var renderer = new GameEventRenderer(console, columns);

        stream.OnEventEmitted += renderer.Render;

        if (actionType == Models.ActionType.Fight)
        {
            var rerollEngine = new RerollEngine(rerollInputProvider, gameRepository);
            var fightEngine = new FightEngine(fightInputProvider, fightResolutionService, rerollEngine, stateRepo, actionRepo);

            var fightResult = await fightEngine.RunAsync(
                playerOperative, playerState, allStates, allOperatives, game, turningPoint, activation, stream);

            DisplayEncounterSummary(playerOperative, aiOperative,
                fightResult.AttackerDamageDealt, fightResult.DefenderDamageDealt,
                fightResult.AttackerCausedIncapacitation, fightResult.DefenderCausedIncapacitation);
        }
        else
        {
            var rerollEngine = new RerollEngine(rerollInputProvider, gameRepository);
            var blastEngine = new BlastEngine(blastInputProvider, combatResolutionService, rerollEngine, stateRepo, actionRepo, blastTargetRepo);
            var shootEngine = new ShootEngine(shootInputProvider, combatResolutionService, rerollEngine, blastEngine, stateRepo, actionRepo);

            var shootResult = await shootEngine.RunAsync(
                playerOperative, playerState, allStates, allOperatives, game, turningPoint, activation, false, stream);

            DisplayEncounterSummary(playerOperative, aiOperative,
                shootResult.DamageDealt, 0,
                false, shootResult.CausedIncapacitation);
        }
    }

    // --- Display helpers -----------------------------------------------------

    private static string FormatTeam(Models.Team t)
    {
        var display = Markup.Escape(t.Name);

        if (!string.IsNullOrEmpty(t.GrandFaction))
        {
            display += $" [dim]({Markup.Escape(t.Faction)} — {Markup.Escape(t.GrandFaction)})[/]";
        }
        else if (!string.IsNullOrEmpty(t.Faction))
        {
            display += $" [dim]({Markup.Escape(t.Faction)})[/]";
        }

        return display;
    }

    private static string FormatOperative(Models.Operative o) =>
        $"{Markup.Escape(o.Name)} [dim]({FormatOperativeStats(o)})[/]";

    private static string FormatOperativeStats(Models.Operative o) =>
        $"APL: [green]{o.Apl}[/] | Move: [green]{o.Move}\"[/] | Save: [green]{o.Save}+[/] | Wounds: [green]{o.Wounds}[/]";

    private static string FormatWeaponStats(Models.Weapon w) =>
        $"Attack: [green]{w.Atk}[/] | Hit: [green]{w.Hit}+[/] | Normal: [green]{w.NormalDmg}[/] | Crit: [green]{w.CriticalDmg}[/]";

    private void DisplayMatchup(
        Models.Operative playerOperative, Models.Team playerTeam,
        Models.Operative aiOperative, Models.Team aiTeam)
    {
        console.WriteLine();
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold cyan]You[/]")
            .AddColumn("[bold red]AI[/]");

        table.AddRow(
            $"[bold]{Markup.Escape(playerOperative.Name)}[/]\n[dim]{Markup.Escape(playerTeam.Name)}[/]",
            $"[bold]{Markup.Escape(aiOperative.Name)}[/]\n[dim]{Markup.Escape(aiTeam.Name)}[/]");

        table.AddRow(
            $"APL: [green]{playerOperative.Apl}[/] | Move: [green]{playerOperative.Move}\"[/] | Save: [green]{playerOperative.Save}+[/] | Wounds: [green]{playerOperative.Wounds}[/]",
            $"APL: [green]{aiOperative.Apl}[/] | Move: [green]{aiOperative.Move}\"[/] | Save: [green]{aiOperative.Save}+[/] | Wounds: [green]{aiOperative.Wounds}[/]");

        console.Write(table);
        console.WriteLine();
    }

    private void DisplayEncounterSummary(
        Models.Operative attacker, Models.Operative defender,
        int attackerDamage, int defenderDamage,
        bool attackerIncapacitated, bool defenderIncapacitated)
    {
        console.WriteLine();
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Result")
            .AddColumn("[bold cyan]You[/]")
            .AddColumn("[bold red]AI[/]");

        table.AddRow("Damage dealt", $"[bold]{attackerDamage}[/]", $"[bold]{defenderDamage}[/]");
        table.AddRow("Incapacitated?",
            attackerIncapacitated ? "[red]No — you were incapacitated[/]" : "[green]Alive[/]",
            defenderIncapacitated ? "[red]Incapacitated[/]" : "[green]Alive[/]");

        console.Write(table);
        console.WriteLine();
    }
}


