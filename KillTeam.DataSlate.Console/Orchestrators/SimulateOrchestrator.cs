using KillTeam.DataSlate.Console.Rendering;
using KillTeam.DataSlate.Console.TestData;
using KillTeam.DataSlate.Domain.Engine;
using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace KillTeam.DataSlate.Console.Orchestrators;

/// <summary>
/// Drives the simulate command: operative selection and an ad-hoc fight/shoot session loop.
/// Uses in-memory repositories so no game data is persisted.
/// </summary>
public class SimulateOrchestrator(
    IAnsiConsole console,
    ITeamRepository teamRepository,
    IPlayerRepository playerRepository,
    SimulateEncounterEngine simulateEncounterEngine,
    ColumnContext columnContext,
    ILogger<SimulateOrchestrator> logger)
{
    public async Task RunAsync()
    {
        logger.LogInformation("Simulate session started");
        console.Write(new Rule("[bold cyan]Simulate Mode[/]"));
        console.MarkupLine("[dim]Test fight and shoot encounters without a full game session. Nothing is saved.[/]");
        console.WriteLine();

        var player1 = await playerRepository.FindByNameAsync("Player 1");
        var player2 = await playerRepository.FindByNameAsync("Player 2");

        if (player1 is null || player2 is null)
        {
            console.MarkupLine("[red]Internal simulate players not found. Re-initialise the database.[/]");

            return;
        }

        var (player1Operative, player2Operative, player1Team, player2Team) = await SelectOperativesAsync();

        if (player1Operative is null || player2Operative is null)
        {
            return;
        }

        DisplayMatchup(
            player1Operative,
            player1Team!,
            player2Operative,
            player2Team!,
            player1.Name,
            player1.Colour,
            player2.Name,
            player2.Colour);

        await RunSessionLoopAsync(
            player1Operative,
            player2Operative,
            player1Team!,
            player2Team!,
            player1,
            player2);

        logger.LogInformation("Simulate session ended");
    }

    // --- Operative selection -------------------------------------------------

    private async Task<(Operative? player1Operative, Operative? player2Operative, Team? player1Team, Team? player2Team)> SelectOperativesAsync()
    {
        var allTeams = (await teamRepository.GetAllAsync()).ToList();

        if (allTeams.Count == 0)
        {
            console.MarkupLine("[red]No teams imported. Run [bold]import-teams[/] first.[/]");

            return (null, null, null, null);
        }

        var team1TestTeam = TestTeamFactory.CreateTeam1();
        var team2TestTeam = TestTeamFactory.CreateTeam2();

        // Step 1 - Player 1's team (Test Team 1 appears first)
        var player1TeamStub = console.Prompt(
            new SelectionPrompt<Team>()
                .Title("Select [bold]Player 1's[/] team:")
                .UseConverter(FormatTeam)
                .AddChoices([team1TestTeam, .. allTeams]));

        var player1Team = player1TeamStub.Id == TestTeamFactory.Team1Id
            ? team1TestTeam
            : (await teamRepository.GetByIdAsync(player1TeamStub.Id))!;

        // Step 2 - Player 1's operative (auto-select if only one)
        Operative player1Operative;

        if (player1Team.Operatives.Count == 1)
        {
            player1Operative = player1Team.Operatives[0];
        }
        else
        {
            player1Operative = console.Prompt(
                new SelectionPrompt<Operative>()
                    .Title("Select [bold]Player 1's[/] operative:")
                    .UseConverter(FormatOperative)
                    .AddChoices(player1Team.Operatives));
        }

        // Step 3 - Player 2's team (Test Team 2 appears first; exclude Player 1's real team)
        var player2TeamChoices = allTeams.Where(t => t.Name != player1TeamStub.Name).ToList();
        var player2TeamOptions = (IEnumerable<Team>)[team2TestTeam, .. player2TeamChoices];

        if (!player2TeamOptions.Any())
        {
            console.MarkupLine("[red]No other teams available for Player 2. Import more teams first.[/]");

            return (null, null, null, null);
        }

        var player2TeamStub = console.Prompt(
            new SelectionPrompt<Team>()
                .Title("Select [bold]Player 2's[/] team:")
                .UseConverter(FormatTeam)
                .AddChoices(player2TeamOptions));

        var player2Team = player2TeamStub.Id == TestTeamFactory.Team2Id
            ? team2TestTeam
            : (await teamRepository.GetByIdAsync(player2TeamStub.Id))!;

        // Step 4 - Player 2's operative (auto-select if only one)
        Operative player2Operative;

        if (player2Team.Operatives.Count == 1)
        {
            player2Operative = player2Team.Operatives[0];
        }
        else
        {
            player2Operative = console.Prompt(
                new SelectionPrompt<Operative>()
                    .Title("Select [bold]Player 2's[/] operative:")
                    .UseConverter(FormatOperative)
                    .AddChoices(player2Team.Operatives));
        }

        return (player1Operative, player2Operative, player1Team, player2Team);
    }

    // --- Session loop --------------------------------------------------------

    private async Task RunSessionLoopAsync(
        Operative player1Operative,
        Operative player2Operative,
        Team player1Team,
        Team player2Team,
        Player player1,
        Player player2)
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
                    await RunEncounterAsync(
                        player1Operative,
                        player2Operative,
                        player1Team,
                        player2Team,
                        ActionType.Fight,
                        player1,
                        player2);
                    break;

                case "Shoot":
                    await RunEncounterAsync(
                        player1Operative,
                        player2Operative,
                        player1Team,
                        player2Team,
                        ActionType.Shoot,
                        player1,
                        player2);
                    break;

                case "Change operatives":
                    var result = await SelectOperativesAsync();

                    if (result.player1Operative is not null)
                    {
                        player1Operative = result.player1Operative;
                        player2Operative = result.player2Operative!;
                        player1Team = result.player1Team!;
                        player2Team = result.player2Team!;
                        DisplayMatchup(
                            player1Operative,
                            player1Team,
                            player2Operative,
                            player2Team,
                            player1.Name,
                            player1.Colour,
                            player2.Name,
                            player2.Colour);
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
        Operative player1Operative,
        Operative player2Operative,
        Team player1Team,
        Team player2Team,
        ActionType actionType,
        Player player1,
        Player player2)
    {
        var participantLabels = new Dictionary<string, string>
        {
            [player1Team.Id] = player1.Name,
            [player2Team.Id] = player2.Name,
        };

        var participantColours = new Dictionary<string, string>
        {
            [player1Team.Id] = player1.Colour,
            [player2Team.Id] = player2.Colour,
        };

        var columns = new TwoColumnRenderer(console, participantLabels, participantColours, columnContext);
        var renderer = new GameEventRenderer(console, columns);

        var encounterResult = await simulateEncounterEngine.RunAsync(
            player1Operative,
            player1Team,
            player2Operative,
            player2Team,
            actionType,
            stream => stream.OnEventEmitted += renderer.Render);

        DisplayEncounterSummary(
            player1Operative,
            player2Operative,
            encounterResult.AttackerDamageDealt,
            encounterResult.TargetDamageDealt,
            player1Incapacitated: encounterResult.AttackerIncapacitated,
            player2Incapacitated: encounterResult.TargetIncapacitated,
            player1CurrentWounds: encounterResult.AttackerCurrentWounds,
            player2CurrentWounds: encounterResult.TargetCurrentWounds,
            player1Name: player1.Name,
            player1Colour: player1.Colour,
            player2Name: player2.Name,
            player2Colour: player2.Colour);
    }

    // --- Display helpers -----------------------------------------------------

    private static string FormatTeam(Team team)
    {
        var display = Markup.Escape(team.Name);

        if (!string.IsNullOrEmpty(team.GrandFaction))
        {
            display += $" [dim]({Markup.Escape(team.Faction)} — {Markup.Escape(team.GrandFaction)})[/]";
        }
        else if (!string.IsNullOrEmpty(team.Faction))
        {
            display += $" [dim]({Markup.Escape(team.Faction)})[/]";
        }

        return display;
    }

    private static string FormatOperative(Operative operative) =>
        $"{Markup.Escape(operative.Name)} [dim]({FormatOperativeStats(operative)})[/]";

    private static string FormatOperativeStats(Operative operative) =>
        $"APL: [green]{operative.Apl}[/] | Move: [green]{operative.Move}\"[/] | Save: [green]{operative.Save}+[/] | Wounds: [green]{operative.Wounds}[/]";

    private void DisplayMatchup(
        Operative player1Operative,
        Team player1Team,
        Operative player2Operative,
        Team player2Team,
        string player1Name,
        string player1Colour,
        string player2Name,
        string player2Colour)
    {
        console.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn($"[bold {player1Colour}]{Markup.Escape(player1Name)}[/]")
            .AddColumn($"[bold {player2Colour}]{Markup.Escape(player2Name)}[/]");

        table.AddRow(
            $"[bold]{Markup.Escape(player1Operative.Name)}[/]\n[dim]{Markup.Escape(player1Team.Name)}[/]",
            $"[bold]{Markup.Escape(player2Operative.Name)}[/]\n[dim]{Markup.Escape(player2Team.Name)}[/]");

        console.Write(table);
        console.WriteLine();

        OperativeCardRenderer.Render(console, player1Operative);
        OperativeCardRenderer.Render(console, player2Operative);

        console.WriteLine();
    }

    private void DisplayEncounterSummary(
        Operative attacker,
        Operative target,
        int attackerDamage,
        int targetDamage,
        bool player1Incapacitated,
        bool player2Incapacitated,
        int player1CurrentWounds,
        int player2CurrentWounds,
        string player1Name,
        string player1Colour,
        string player2Name,
        string player2Colour)
    {
        console.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Result")
            .AddColumn($"[bold {player1Colour}]{Markup.Escape(player1Name)}[/]")
            .AddColumn($"[bold {player2Colour}]{Markup.Escape(player2Name)}[/]");

        var player1WoundsColor = player1CurrentWounds > 0 ? "green" : "red";
        var player2WoundsColor = player2CurrentWounds > 0 ? "green" : "red";

        table.AddRow(
            "Wounds remaining",
            $"[{player1WoundsColor}]{player1CurrentWounds}/{attacker.Wounds}[/]",
            $"[{player2WoundsColor}]{player2CurrentWounds}/{target.Wounds}[/]");

        table.AddRow("Damage dealt", $"[bold]{attackerDamage}[/]", $"[bold]{targetDamage}[/]");
        table.AddRow(
            "Incapacitated?",
            player1Incapacitated ? "[red]Incapacitated[/]" : "[green]Alive[/]",
            player2Incapacitated ? "[red]Incapacitated[/]" : "[green]Alive[/]");

        console.Write(table);
        console.WriteLine();
    }
}
