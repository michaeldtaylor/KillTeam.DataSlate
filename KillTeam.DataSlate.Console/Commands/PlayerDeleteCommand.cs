using System.ComponentModel;
using KillTeam.DataSlate.Domain.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace KillTeam.DataSlate.Console.Commands;

/// <summary>Deletes a registered player (blocked if they have recorded games).</summary>
[Description("Delete a player (blocked if they have recorded games).")]
public class PlayerDeleteCommand(IPlayerRepository players, IConfiguration config) : AsyncCommand<PlayerDeleteCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [Description("The name of the player to delete.")]
        [CommandArgument(0, "<name>")]
        public string Name { get; set; } = string.Empty;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var name = settings.Name.Trim();
        var player = await players.FindByNameAsync(name);
        if (player is null)
        {
            AnsiConsole.MarkupLine($"[yellow]Player '{Markup.Escape(name)}' not found.[/]");
            return 1;
        }

        var dbPath = config["DataSlate:DatabasePath"] ?? "./data/killteam.db";
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM games WHERE player_a_id=@id OR player_b_id=@id";
        cmd.Parameters.AddWithValue("@id", player.Id.ToString());
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        if (count > 0)
        {
            AnsiConsole.MarkupLine($"[red]Cannot delete '{Markup.Escape(name)}' — they have {count} recorded game(s).[/]");
            return 1;
        }

        if (!AnsiConsole.Confirm($"Delete player '{name}'?"))
            return 0;

        await players.DeleteAsync(player.Id);
        AnsiConsole.MarkupLine($"[green]Player '{Markup.Escape(name)}' deleted.[/]");
        return 0;
    }
}
