using System.ComponentModel;
using KillTeam.DataSlate.Console.Extensions;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace KillTeam.DataSlate.Console.Commands;

/// <summary>Registers a new player by username.</summary>
[Description("Register a new player.")]
public class PlayerCreateCommand(IAnsiConsole console, IPlayerRepository players, ILogger<PlayerCreateCommand> logger) : AsyncCommand<PlayerCreateCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [Description("The username of the player to register (unique in-game handle).")]
        [CommandArgument(0, "<username>")]
        // Spectre.Console CommandSettings — required omitted intentionally
        public string Username { get; set; } = string.Empty;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var username = settings.Username.Trim();

        if (string.IsNullOrWhiteSpace(username))
        {
            console.MarkupLine("[red]Username cannot be empty.[/]");

            return 1;
        }

        logger.LogDebug("Creating player {Username}", username);
        var existing = await players.FindByUsernameAsync(username);

        if (existing is not null)
        {
            logger.LogWarning("Player {Username} already exists", username);
            console.MarkupLine($"[yellow]Player '{Markup.Escape(username)}' already exists.[/]");

            return 1;
        }

        var firstName = console.Ask<string>("First name:");
        var lastName = console.Ask<string>("Last name:");

        var colour = console.Prompt(
            new SelectionPrompt<PlayerColour>()
                .Title("Select your colour:")
                .UseConverter(c => c.ToMarkupString())
                .AddChoices(Enum.GetValues<PlayerColour>()));

        await players.CreateAsync(new Player
        {
            Id = Guid.NewGuid(),
            Username = username,
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Colour = colour,
        });

        logger.LogInformation("Player {Username} created", username);
        console.MarkupLine($"[green]Player '{Markup.Escape(username)}' created.[/]");

        return 0;
    }
}
