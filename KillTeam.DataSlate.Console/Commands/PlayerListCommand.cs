using System.ComponentModel;
using KillTeam.DataSlate.Domain.Repositories;
using Spectre.Console;
using Spectre.Console.Cli;

namespace KillTeam.DataSlate.Console.Commands;

/// <summary>Lists all registered players with their win/loss statistics.</summary>
[Description("List all registered players with win/loss stats.")]
public class PlayerListCommand(IPlayerRepository players) : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        var playerStats = await players.GetAllWithStatsAsync();

        if (playerStats.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No players registered yet.[/]");

            return 0;
        }

        var table = new Table()
            .AddColumn("Name")
            .AddColumn(new TableColumn("Games Played").Centered())
            .AddColumn(new TableColumn("Wins").Centered())
            .AddColumn(new TableColumn("Win %").Centered());

        foreach (var stat in playerStats)
        {
            var winPercentage = stat.GamesPlayed == 0 ? "—" : $"{stat.Wins * 100 / stat.GamesPlayed}%";

            table.AddRow(Markup.Escape(stat.Name), stat.GamesPlayed.ToString(), stat.Wins.ToString(), winPercentage);
        }

        AnsiConsole.Write(table);

        return 0;
    }
}
