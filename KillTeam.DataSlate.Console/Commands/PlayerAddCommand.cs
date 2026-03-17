using System.ComponentModel;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace KillTeam.DataSlate.Console.Commands;

/// <summary>Registers a new player by name.</summary>
[Description("Register a new player.")]
public class PlayerAddCommand(IAnsiConsole console, IPlayerRepository players, ILogger<PlayerAddCommand> logger) : AsyncCommand<PlayerAddCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [Description("The name of the player to register.")]
        [CommandArgument(0, "<name>")]
        public string Name { get; set; } = string.Empty;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var name = settings.Name.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            console.MarkupLine("[red]Player name cannot be empty.[/]");
            return 1;
        }

        logger.LogDebug("Adding player {Name}", name);
        var existing = await players.FindByNameAsync(name);

        if (existing is not null)
        {
            logger.LogWarning("Player {Name} already exists", name);
            console.MarkupLine($"[yellow]Player '{Markup.Escape(name)}' already exists.[/]");
            return 1;
        }

        await players.AddAsync(new Player { Id = Guid.NewGuid(), Name = name });
        logger.LogInformation("Player {Name} created", name);
        console.MarkupLine($"[green]Player '{Markup.Escape(name)}' created.[/]");
        return 0;
    }
}
