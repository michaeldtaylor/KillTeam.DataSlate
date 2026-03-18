using System.ComponentModel;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace KillTeam.DataSlate.Console.Commands;

/// <summary>Creates a new game session, prompting for players, teams, and mission.</summary>
[Description("Start a new game — select players, teams, and mission.")]
public class NewGameCommand(
    IAnsiConsole console,
    IPlayerRepository players,
    ITeamRepository teams,
    IGameRepository games,
    IGameOperativeStateRepository gameStates,
    ILogger<NewGameCommand> logger) : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        var allPlayers = (await players.GetAllAsync()).ToList();

        if (allPlayers.Count < 2)
        {
            console.MarkupLine("[red]Need at least 2 registered players. Run `player add <name>` first.[/]");
            return 1;
        }

        var allTeams = (await teams.GetAllAsync()).ToList();

        if (allTeams.Count < 2)
        {
            console.MarkupLine("[red]Not enough teams imported — run `import-teams` first.[/]");
            return 1;
        }

        // Team A selection
        var team1 = console.Prompt(
            new SelectionPrompt<Team>()
                .Title("Select [green]Team A[/]:")
                .UseConverter(FormatTeam)
                .AddChoices(allTeams));

        var player1 = console.Prompt(
            new SelectionPrompt<Player>()
                .Title($"Select player for [green]{Markup.Escape(team1.Name)}[/]:")
                .UseConverter(p => p.Name)
                .AddChoices(allPlayers));

        // Team B selection (exclude Team A)
        var teamBChoices = allTeams.Where(t => t.Name != team1.Name).ToList();
        var team2 = console.Prompt(
            new SelectionPrompt<Team>()
                .Title("Select [blue]Team B[/]:")
                .UseConverter(FormatTeam)
                .AddChoices(teamBChoices));

        var playerBChoices = allPlayers.Where(p => p.Id != player1.Id).ToList();
        var player2 = console.Prompt(
            new SelectionPrompt<Player>()
                .Title($"Select player for [blue]{Markup.Escape(team2.Name)}[/]:")
                .UseConverter(p => p.Name)
                .AddChoices(playerBChoices.Count > 0 ? playerBChoices : allPlayers));

        var missionName = console.Prompt(
            new TextPrompt<string>("Mission name [dim](optional)[/]:").AllowEmpty());

        // Load full team data with operatives
        var fullTeam1 = await teams.GetWithOperativesAsync(team1.Name);
        var fullTeam2 = await teams.GetWithOperativesAsync(team2.Name);

        var game = new Game
        {
            Id = Guid.NewGuid(),
            PlayedAt = DateTime.UtcNow,
            MissionName = string.IsNullOrWhiteSpace(missionName) ? null : missionName,
            Participant1 = new GameParticipant
            {
                TeamId = team1.Id,
                TeamName = team1.Name,
                PlayerId = player1.Id,
                CommandPoints = 2,
                VictoryPoints = 0
            },
            Participant2 = new GameParticipant
            {
                TeamId = team2.Id,
                TeamName = team2.Name,
                PlayerId = player2.Id,
                CommandPoints = 2,
                VictoryPoints = 0
            },
            Status = GameStatus.InProgress
        };

        await games.CreateAsync(game);

        logger.LogInformation("New game {GameId} created: {Team1} vs {Team2}", game.Id, team1.Name, team2.Name);

        // Create operative states for both teams
        var allOperatives = new List<Operative>();

        if (fullTeam1?.Operatives is not null)
        {
            allOperatives.AddRange(fullTeam1.Operatives);
        }

        if (fullTeam2?.Operatives is not null)
        {
            allOperatives.AddRange(fullTeam2.Operatives);
        }

        foreach (var operative in allOperatives)
        {
            await gameStates.CreateAsync(new GameOperativeState
            {
                Id = Guid.NewGuid(),
                GameId = game.Id,
                OperativeId = operative.Id,
                CurrentWounds = operative.Wounds,
                Order = Order.Conceal,
                IsReady = true,
                IsOnGuard = false,
                IsIncapacitated = false,
                HasUsedCounteractThisTurningPoint = false,
                AplModifier = 0
            });
        }

        console.MarkupLine($"[green]Game {game.Id}[/]");
        console.MarkupLine($"  {Markup.Escape(player1.Name)} ([green]{Markup.Escape(team1.Name)}[/]) vs {Markup.Escape(player2.Name)} ([blue]{Markup.Escape(team2.Name)}[/])");
        if (!string.IsNullOrEmpty(missionName))
        {
            console.MarkupLine($"  Mission: {Markup.Escape(missionName)}");
        }
        console.MarkupLine($"  {allOperatives.Count} operative state(s) initialized.");

        return 0;
    }

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
}
