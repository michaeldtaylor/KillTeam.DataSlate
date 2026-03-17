using System.ComponentModel;
using KillTeam.DataSlate.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace KillTeam.DataSlate.Console.Commands;

/// <summary>Displays win/loss statistics per player or per team.</summary>
[Description("Show win/loss statistics per player or per team.")]
public class StatsCommand(
    ITeamRepository teams,
    IGameRepository games,
    IPlayerRepository players,
    ILogger<StatsCommand> logger) : AsyncCommand<StatsCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [Description("Show statistics for a specific team (case-insensitive).")]
        [CommandOption("--team <name>")]
        public string? TeamName { get; set; }

        [Description("Filter statistics to a specific player (case-insensitive).")]
        [CommandOption("--player <name>")]
        public string? PlayerName { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        logger.LogDebug("Stats command. Team={Team}, Player={Player}", settings.TeamName, settings.PlayerName);
        if (!string.IsNullOrWhiteSpace(settings.TeamName))
        {
            return await ShowTeamStatsAsync(settings.TeamName);
        }

        return await ShowPlayerStatsAsync(settings.PlayerName);
    }

    private async Task<int> ShowTeamStatsAsync(string teamName)
    {
        var team = await teams.GetByNameAsync(teamName);

        if (team is null)
        {
            logger.LogWarning("Team {TeamName} not found for stats", teamName);
            AnsiConsole.MarkupLine($"[red]Team '{Markup.Escape(teamName)}' not found.[/]");

            return 1;
        }

        var stats = await games.GetTeamStatsAsync(team.Id);

        if (stats is null)
        {
            logger.LogWarning("No stats found for team {TeamName}", teamName);
            AnsiConsole.MarkupLine($"[red]Could not load stats for team '{Markup.Escape(teamName)}'.[/]");

            return 1;
        }

        var losses = stats.GamesPlayed - stats.Wins;
        var winPercentage = stats.GamesPlayed > 0 ? $"{stats.Wins * 100 / stats.GamesPlayed}%" : "—";

        AnsiConsole.MarkupLine($"[bold]{Markup.Escape(team.Name)}[/] ({Markup.Escape(team.Faction)})");

        var statsTable = new Table()
            .AddColumn("Games").AddColumn("Wins").AddColumn("Losses")
            .AddColumn("Win %").AddColumn("Kills").AddColumn("Most Used Weapon");

        statsTable.AddRow(
            stats.GamesPlayed.ToString(),
            stats.Wins.ToString(),
            losses.ToString(),
            winPercentage,
            stats.Kills.ToString(),
            Markup.Escape(stats.MostUsedWeapon ?? "—"));

        AnsiConsole.Write(statsTable);

        return 0;
    }

    private async Task<int> ShowPlayerStatsAsync(string? playerNameFilter)
    {
        var playerStats = await players.GetAllWithStatsAsync(playerNameFilter);

        if (playerStats.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No players registered yet.[/]");

            return 0;
        }

        var table = new Table()
            .AddColumn("Player")
            .AddColumn(new TableColumn("Games").Centered())
            .AddColumn(new TableColumn("Wins").Centered())
            .AddColumn(new TableColumn("Win %").Centered());

        foreach (var stat in playerStats)
        {
            var winPercentage = stat.GamesPlayed > 0 ? $"{stat.Wins * 100 / stat.GamesPlayed}%" : "—";

            table.AddRow(Markup.Escape(stat.Name), stat.GamesPlayed.ToString(), stat.Wins.ToString(), winPercentage);
        }

        AnsiConsole.Write(table);

        return 0;
    }
}
