using System.ComponentModel;
using KillTeam.DataSlate.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace KillTeam.DataSlate.Console.Commands;

/// <summary>Deletes a registered player (blocked if they have recorded games).</summary>
[Description("Delete a player (blocked if they have recorded games).")]
public class PlayerDeleteCommand(IAnsiConsole console, IPlayerRepository players, ILogger<PlayerDeleteCommand> logger) : AsyncCommand<PlayerDeleteCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [Description("The username of the player to delete.")]
        [CommandArgument(0, "<username>")]
        // Spectre.Console CommandSettings — required omitted intentionally
        public string Username { get; set; } = string.Empty;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var username = settings.Username.Trim();
        var player = await players.FindByUsernameAsync(username);

        if (player is null)
        {
            logger.LogWarning("Player {Username} not found for delete", username);
            console.MarkupLine($"[yellow]Player '{Markup.Escape(username)}' not found.[/]");

            return 1;
        }

        var gameCount = await players.CountGamesAsync(player.Id);

        if (gameCount > 0)
        {
            logger.LogWarning("Cannot delete player {Username} — has {GameCount} games", username, gameCount);
            console.MarkupLine($"[red]Cannot delete '{Markup.Escape(username)}' — they have {gameCount} recorded game(s).[/]");

            return 1;
        }

        if (!console.Confirm($"Delete player '{username}'?"))
        {
            return 0;
        }

        await players.DeleteAsync(player.Id);
        logger.LogInformation("Player {Username} deleted", username);
        console.MarkupLine($"[green]Player '{Markup.Escape(username)}' deleted.[/]");

        return 0;
    }
}
