using KillTeam.DataSlate.Console.Rendering;
using KillTeam.DataSlate.Console.TestData;
using KillTeam.DataSlate.Domain.Engine;
using KillTeam.DataSlate.Domain.Engine.Input;
using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using KillTeam.DataSlate.Domain.Repositories.InMemory;
using KillTeam.DataSlate.Domain.Services;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace KillTeam.DataSlate.Console.Orchestrators;

/// <summary>
/// Drives the simulate command: operative selection and an ad-hoc fight/shoot session loop.
/// Uses in-memory repositories so no game data is persisted.
/// </summary>
public class SimulateSessionOrchestrator(
    IAnsiConsole console,
    ITeamRepository teamRepository,
    IGameRepository gameRepository,
    IPlayerRepository playerRepository,
    IFightInputProvider fightInputProvider,
    IShootInputProvider shootInputProvider,
    IRerollInputProvider rerollInputProvider,
    IBlastInputProvider blastInputProvider,
    CombatResolutionService combatResolutionService,
    FightResolutionService fightResolutionService,
    ColumnContext columnContext,
    ILogger<SimulateSessionOrchestrator> logger)
{
    public async Task RunAsync()
    {
        logger.LogInformation("Simulate session started");
        console.Write(new Rule("[bold cyan]Simulate Mode[/]"));
        console.MarkupLine("[dim]Test fight and shoot encounters without a full game session. Nothing is saved.[/]");
        console.WriteLine();

        var player = await playerRepository.FindByNameAsync("Player");
        var aiPlayer = await playerRepository.FindByNameAsync("AI");

        if (player is null || aiPlayer is null)
        {
            console.MarkupLine("[red]Internal simulate players not found. Re-initialise the database.[/]");
            return;
        }

        var (playerOperative, aiOperative, playerTeam, aiTeam) = await SelectOperativesAsync();

        if (playerOperative is null || aiOperative is null)
        {
            return;
        }

        DisplayMatchup(playerOperative, playerTeam!, aiOperative, aiTeam!, player.Name, player.Colour, aiPlayer.Name, aiPlayer.Colour);
        await RunSessionLoopAsync(playerOperative, aiOperative, playerTeam!, aiTeam!, player, aiPlayer);
        logger.LogInformation("Simulate session ended");
    }

    // --- Operative selection -------------------------------------------------

    private async Task<(Operative? playerOperative, Operative? aiOperative, Team? playerTeam, Team? aiTeam)> SelectOperativesAsync()
    {
        var allTeams = (await teamRepository.GetAllAsync()).ToList();

        if (allTeams.Count == 0)
        {
            console.MarkupLine("[red]No teams imported. Run [bold]import-teams[/] first.[/]");
            return (null, null, null, null);
        }

        var testTeam = TestTeamFactory.Create();

        // Step 1 - your team (test team appears first)
        var playerTeamStub = console.Prompt(
            new SelectionPrompt<Team>()
                .Title("Select [bold]your[/] team:")
                .UseConverter(FormatTeam)
                .AddChoices([testTeam, .. allTeams]));

        var playerTeam = playerTeamStub.Id == TestTeamFactory.TeamId
            ? testTeam
            : (await teamRepository.GetWithOperativesAsync(playerTeamStub.Name))!;

        // Step 2 - your operative (auto-select if only one)
        Operative playerOperative;
        if (playerTeam.Operatives.Count == 1)
        {
            playerOperative = playerTeam.Operatives[0];
        }
        else
        {
            playerOperative = console.Prompt(
                new SelectionPrompt<Operative>()
                    .Title("Select [bold]your[/] operative:")
                    .UseConverter(FormatOperative)
                    .AddChoices(playerTeam.Operatives));
        }

        // Step 3 - AI's team (test team always available; exclude player's real team)
        var aiTeamChoices = allTeams.Where(t => t.Name != playerTeamStub.Name).ToList();
        var aiTeamOptions = playerTeamStub.Id == TestTeamFactory.TeamId
            ? (IEnumerable<Team>)[testTeam, .. allTeams]
            : (IEnumerable<Team>)[testTeam, .. aiTeamChoices];

        if (!aiTeamOptions.Any())
        {
            console.MarkupLine("[red]No other teams available for the AI. Import more teams first.[/]");
            return (null, null, null, null);
        }

        var aiTeamStub = console.Prompt(
            new SelectionPrompt<Team>()
                .Title("Select the [bold]AI[/] team:")
                .UseConverter(FormatTeam)
                .AddChoices(aiTeamOptions));

        var aiTeam = aiTeamStub.Id == TestTeamFactory.TeamId
            ? testTeam
            : (await teamRepository.GetWithOperativesAsync(aiTeamStub.Name))!;

        // Step 4 - AI's operative (auto-select if only one)
        Operative aiOperative;
        if (aiTeam.Operatives.Count == 1)
        {
            aiOperative = aiTeam.Operatives[0];
        }
        else
        {
            aiOperative = console.Prompt(
                new SelectionPrompt<Operative>()
                    .Title("Select the [bold]AI[/] operative:")
                    .UseConverter(FormatOperative)
                    .AddChoices(aiTeam.Operatives));
        }

        return (playerOperative, aiOperative, playerTeam, aiTeam);
    }

    // --- Session loop --------------------------------------------------------

    private async Task RunSessionLoopAsync(
        Operative playerOperative, Operative aiOperative,
        Team playerTeam, Team aiTeam,
        Player youPlayer, Player aiPlayer)
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
                    await RunEncounterAsync(playerOperative, aiOperative, playerTeam, aiTeam, ActionType.Fight, youPlayer, aiPlayer);
                    break;

                case "Shoot":
                    await RunEncounterAsync(playerOperative, aiOperative, playerTeam, aiTeam, ActionType.Shoot, youPlayer, aiPlayer);
                    break;

                case "Change operatives":
                    var result = await SelectOperativesAsync();

                    if (result.playerOperative is not null)
                    {
                        playerOperative = result.playerOperative;
                        aiOperative = result.aiOperative!;
                        playerTeam = result.playerTeam!;
                        aiTeam = result.aiTeam!;
                        DisplayMatchup(playerOperative, playerTeam, aiOperative, aiTeam, youPlayer.Name, youPlayer.Colour, aiPlayer.Name, aiPlayer.Colour);
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
        Operative playerOperative, Operative aiOperative,
        Team playerTeam, Team aiTeam,
        ActionType actionType,
        Player youPlayer, Player aiPlayer)
    {
        // Synthetic domain objects - no DB interaction
        var game = new Game
        {
            Id = Guid.NewGuid(),
            Participant1 = new GameParticipant
            {
                TeamId = playerTeam.Id,
                TeamName = playerTeam.Name,
                PlayerId = Guid.Empty,
                CommandPoints = 0,  // suppresses CP re-roll prompts (RerollEngine skips when game not in DB)
            },
            Participant2 = new GameParticipant
            {
                TeamId = aiTeam.Id,
                TeamName = aiTeam.Name,
                PlayerId = Guid.Empty,
                CommandPoints = 0,
            },
            PlayedAt = DateTime.UtcNow
        };

        var turningPoint = new TurningPoint
        {
            Id = Guid.NewGuid(),
            GameId = game.Id,
            Number = 1
        };

        var activation = new Activation
        {
            Id = Guid.NewGuid(),
            TurningPointId = turningPoint.Id,
            OperativeId = playerOperative.Id,
            TeamId = playerTeam.Id,
            OrderSelected = Order.Engage,
            SequenceNumber = 1
        };

        // Fresh in-memory repos - wound state resets each encounter
        var stateRepo = new InMemoryGameOperativeStateRepository();
        var playerState = new GameOperativeState
        {
            GameId = game.Id,
            OperativeId = playerOperative.Id,
            CurrentWounds = playerOperative.Wounds,
            Order = Order.Engage
        };
        var aiState = new GameOperativeState
        {
            GameId = game.Id,
            OperativeId = aiOperative.Id,
            CurrentWounds = aiOperative.Wounds,
            Order = Order.Engage
        };
        stateRepo.Seed([playerState, aiState]);

        var actionRepo = new InMemoryActionRepository();
        var blastTargetRepo = new InMemoryBlastTargetRepository();

        var allStates = stateRepo.GetAll();
        var allOperatives = new Dictionary<Guid, Operative>
        {
            [playerOperative.Id] = playerOperative,
            [aiOperative.Id] = aiOperative
        };

        var stream = new GameEventStream(game.Id);
        var participantLabels = new Dictionary<string, string>
        {
            [playerTeam.Id] = youPlayer.Name,
            [aiTeam.Id] = aiPlayer.Name,
        };
        var participantColours = new Dictionary<string, string>
        {
            [playerTeam.Id] = youPlayer.Colour,
            [aiTeam.Id] = aiPlayer.Colour,
        };
        var columns = new TwoColumnRenderer(console, participantLabels, participantColours, columnContext);
        var renderer = new GameEventRenderer(console, columns);

        stream.OnEventEmitted += renderer.Render;

        if (actionType == ActionType.Fight)
        {
            var rerollEngine = new RerollEngine(rerollInputProvider, gameRepository);
            var fightEngine = new FightEngine(fightInputProvider, fightResolutionService, rerollEngine, stateRepo, actionRepo);

            var fightResult = await fightEngine.RunAsync(
                playerOperative, playerState, allStates, allOperatives, game, turningPoint, activation, stream);

            DisplayEncounterSummary(playerOperative, aiOperative,
                fightResult.AttackerDamageDealt, fightResult.DefenderDamageDealt,
                playerIncapacitated: fightResult.DefenderCausedIncapacitation,
                aiIncapacitated: fightResult.AttackerCausedIncapacitation,
                playerCurrentWounds: playerState.CurrentWounds,
                aiCurrentWounds: aiState.CurrentWounds,
                youName: youPlayer.Name,
                youColour: youPlayer.Colour,
                aiName: aiPlayer.Name,
                aiColour: aiPlayer.Colour);
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
                playerIncapacitated: false,
                aiIncapacitated: shootResult.CausedIncapacitation,
                playerCurrentWounds: playerState.CurrentWounds,
                aiCurrentWounds: aiState.CurrentWounds,
                youName: youPlayer.Name,
                youColour: youPlayer.Colour,
                aiName: aiPlayer.Name,
                aiColour: aiPlayer.Colour);
        }
    }

    // --- Display helpers -----------------------------------------------------

    private static string FormatTeam(Team t)
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

    private static string FormatOperative(Operative o) =>
        $"{Markup.Escape(o.Name)} [dim]({FormatOperativeStats(o)})[/]";

    private static string FormatOperativeStats(Operative o) =>
        $"APL: [green]{o.Apl}[/] | Move: [green]{o.Move}\"[/] | Save: [green]{o.Save}+[/] | Wounds: [green]{o.Wounds}[/]";

    private static string FormatWeaponStats(Weapon w) =>
        $"Attack: [green]{w.Atk}[/] | Hit: [green]{w.Hit}+[/] | Normal: [green]{w.NormalDmg}[/] | Crit: [green]{w.CriticalDmg}[/]";

    private void DisplayMatchup(
        Operative playerOperative, Team playerTeam,
        Operative aiOperative, Team aiTeam,
        string youName, string youColour, string aiName, string aiColour)
    {
        console.WriteLine();
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn($"[bold {youColour}]{Markup.Escape(youName)}[/]")
            .AddColumn($"[bold {aiColour}]{Markup.Escape(aiName)}[/]");

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
        Operative attacker, Operative defender,
        int attackerDamage, int defenderDamage,
        bool playerIncapacitated, bool aiIncapacitated,
        int playerCurrentWounds, int aiCurrentWounds,
        string youName, string youColour, string aiName, string aiColour)
    {
        console.WriteLine();
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Result")
            .AddColumn($"[bold {youColour}]{Markup.Escape(youName)}[/]")
            .AddColumn($"[bold {aiColour}]{Markup.Escape(aiName)}[/]");

        var playerWoundsColor = playerCurrentWounds > 0 ? "green" : "red";
        var aiWoundsColor = aiCurrentWounds > 0 ? "green" : "red";

        table.AddRow("Wounds remaining",
            $"[{playerWoundsColor}]{playerCurrentWounds}/{attacker.Wounds}[/]",
            $"[{aiWoundsColor}]{aiCurrentWounds}/{defender.Wounds}[/]");
        table.AddRow("Damage dealt", $"[bold]{attackerDamage}[/]", $"[bold]{defenderDamage}[/]");
        table.AddRow("Incapacitated?",
            playerIncapacitated ? "[red]Incapacitated[/]" : "[green]Alive[/]",
            aiIncapacitated ? "[red]Incapacitated[/]" : "[green]Alive[/]");

        console.Write(table);
        console.WriteLine();
    }
}


