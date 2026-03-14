using System.ComponentModel;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using Spectre.Console;
using Spectre.Console.Cli;

namespace KillTeam.DataSlate.Console.Commands;

/// <summary>Creates a new game session, prompting for players, teams, and mission.</summary>
[Description("Start a new game — select players, teams, and mission.")]
public class NewGameCommand(
    IPlayerRepository players,
    ITeamRepository teams,
    IGameRepository games,
    IGameOperativeStateRepository gameStates) : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        var allPlayers = (await players.GetAllAsync()).ToList();
        if (allPlayers.Count < 2)
        {
            AnsiConsole.MarkupLine("[red]Need at least 2 registered players. Run `player add <name>` first.[/]");
            return 1;
        }

        var allTeams = (await teams.GetAllAsync()).ToList();
        if (allTeams.Count < 2)
        {
            AnsiConsole.MarkupLine("[red]Not enough teams imported — run `import-teams` first.[/]");
            return 1;
        }

        // Team A selection
        var teamA = AnsiConsole.Prompt(
            new SelectionPrompt<KillTeam.DataSlate.Domain.Models.Team>()
                .Title("Select [green]Team A[/]:")
                .UseConverter(t => t.Name)
                .AddChoices(allTeams));

        var playerA = AnsiConsole.Prompt(
            new SelectionPrompt<Player>()
                .Title($"Select player for [green]{Markup.Escape(teamA.Name)}[/]:")
                .UseConverter(p => p.Name)
                .AddChoices(allPlayers));

        // Team B selection (exclude Team A)
        var teamBChoices = allTeams.Where(t => t.Name != teamA.Name).ToList();
        var teamB = AnsiConsole.Prompt(
            new SelectionPrompt<KillTeam.DataSlate.Domain.Models.Team>()
                .Title("Select [blue]Team B[/]:")
                .UseConverter(t => t.Name)
                .AddChoices(teamBChoices));

        var playerBChoices = allPlayers.Where(p => p.Id != playerA.Id).ToList();
        var playerB = AnsiConsole.Prompt(
            new SelectionPrompt<Player>()
                .Title($"Select player for [blue]{Markup.Escape(teamB.Name)}[/]:")
                .UseConverter(p => p.Name)
                .AddChoices(playerBChoices.Count > 0 ? playerBChoices : allPlayers));

        var missionName = AnsiConsole.Prompt(
            new TextPrompt<string>("Mission name [dim](optional)[/]:").AllowEmpty());

        // Load full team data with operatives
        var fullTeamA = await teams.GetWithOperativesAsync(teamA.Name);
        var fullTeamB = await teams.GetWithOperativesAsync(teamB.Name);

        var game = new Game
        {
            Id = Guid.NewGuid(),
            PlayedAt = DateTime.UtcNow,
            MissionName = string.IsNullOrWhiteSpace(missionName) ? null : missionName,
            Participant1 = new GameParticipant
            {
                TeamId = teamA.Id,
                TeamName = teamA.Name,
                PlayerId = playerA.Id,
                CommandPoints = 2,
                VictoryPoints = 0
            },
            Participant2 = new GameParticipant
            {
                TeamId = teamB.Id,
                TeamName = teamB.Name,
                PlayerId = playerB.Id,
                CommandPoints = 2,
                VictoryPoints = 0
            },
            Status = GameStatus.InProgress
        };

        var created = await games.CreateAsync(game);

        // Create operative states for both teams
        var allOperatives = new List<Operative>();
        if (fullTeamA?.Operatives is not null)
        {
            allOperatives.AddRange(fullTeamA.Operatives);
        }

        if (fullTeamB?.Operatives is not null)
        {
            allOperatives.AddRange(fullTeamB.Operatives);
        }

        foreach (var op in allOperatives)
        {
            await gameStates.CreateAsync(new GameOperativeState
            {
                Id = Guid.NewGuid(),
                GameId = created.Id,
                OperativeId = op.Id,
                CurrentWounds = op.Wounds,
                Order = Order.Conceal,
                IsReady = true,
                IsOnGuard = false,
                IsIncapacitated = false,
                HasUsedCounteractThisTurningPoint = false,
                AplModifier = 0
            });
        }

        AnsiConsole.MarkupLine($"[green]Game {created.Id}[/]");
        AnsiConsole.MarkupLine($"  {Markup.Escape(playerA.Name)} ([green]{Markup.Escape(teamA.Name)}[/]) vs {Markup.Escape(playerB.Name)} ([blue]{Markup.Escape(teamB.Name)}[/])");
        if (!string.IsNullOrEmpty(missionName))
        {
            AnsiConsole.MarkupLine($"  Mission: {Markup.Escape(missionName)}");
        }
        AnsiConsole.MarkupLine($"  {allOperatives.Count} operative state(s) initialized.");

        return 0;
    }
}
