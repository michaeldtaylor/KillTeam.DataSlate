using System.ComponentModel;
using KillTeam.DataSlate.Domain.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace KillTeam.DataSlate.Console.Commands;

/// <summary>Adds or edits narrative notes on activations and actions within a game.</summary>
[Description("Add or edit narrative notes on activations and actions for a game.")]
public class AnnotateCommand(
    IActivationRepository activations,
    IActionRepository actions,
    IConfiguration config) : AsyncCommand<AnnotateCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [Description("The ID of the game to annotate.")]
        [CommandArgument(0, "<game-id>")]
        public string GameId { get; set; } = string.Empty;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (!Guid.TryParse(settings.GameId, out var gameId))
        {
            AnsiConsole.MarkupLine("[red]Invalid game ID format.[/]");
            return 1;
        }

        var dbPath = config["DataSlate:DatabasePath"] ?? "./data/kill-team.db";
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        // Verify game exists
        using var gameCheck = conn.CreateCommand();
        gameCheck.CommandText = "SELECT id FROM games WHERE id = @id";
        gameCheck.Parameters.AddWithValue("@id", gameId.ToString());
        if (await gameCheck.ExecuteScalarAsync() is null)
        {
            AnsiConsole.MarkupLine($"[red]Game {Markup.Escape(settings.GameId)} not found.[/]");
            return 1;
        }

        // Load all turning points for this game
        using var tpCmd = conn.CreateCommand();
        tpCmd.CommandText = "SELECT id, number FROM turning_points WHERE game_id = @gid ORDER BY number";
        tpCmd.Parameters.AddWithValue("@gid", gameId.ToString());
        var tpList = new List<(Guid Id, int Number)>();
        using (var r = await tpCmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                tpList.Add((Guid.Parse(r.GetString(0)), r.GetInt32(1)));

        if (tpList.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No turning points recorded for this game.[/]");
            return 0;
        }

        // Load all activations with operative names
        var allActivations = new List<ActivationEntry>();
        foreach (var (tpId, tpNum) in tpList)
        {
            var tpActivations = (await activations.GetByTurningPointAsync(tpId)).ToList();
            foreach (var act in tpActivations)
            {
                var opName = await GetOperativeNameAsync(conn, act.OperativeId);
                allActivations.Add(new ActivationEntry(act.Id, tpNum, act.SequenceNumber,
                    opName, act.OrderSelected.ToString(), act.NarrativeNote));
            }
        }

        if (allActivations.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No activations recorded for this game.[/]");
            return 0;
        }

        // SelectionPrompt for activation
        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<ActivationEntry>()
                .Title("Select an activation to annotate:")
                .UseConverter(a =>
                {
                    var note = a.NarrativeNote is not null ? " [dim]🖊[/]" : string.Empty;
                    return $"[TP{a.TpNumber}, Act {a.Seq}] {Markup.Escape(a.OperativeName)} ({a.Order}){note}";
                })
                .AddChoices(allActivations));

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What would you like to do?")
                .AddChoices("Annotate this activation", "Drill down to a specific action", "Cancel"));

        if (choice == "Cancel") return 0;

        if (choice == "Annotate this activation")
        {
            var note = AnsiConsole.Prompt(
                new TextPrompt<string>("Enter annotation:").AllowEmpty());
            await activations.UpdateNarrativeAsync(selected.ActivationId, string.IsNullOrWhiteSpace(note) ? null : note);
            AnsiConsole.MarkupLine("[green]Annotation saved.[/]");
            return 0;
        }

        // Drill down to action
        var actList = (await actions.GetByActivationAsync(selected.ActivationId)).ToList();
        if (actList.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No actions recorded for this activation.[/]");
            return 0;
        }

        var selectedAction = AnsiConsole.Prompt(
            new SelectionPrompt<KillTeam.DataSlate.Domain.Models.GameAction>()
                .Title("Select an action to annotate:")
                .UseConverter(a =>
                {
                    var note = a.NarrativeNote is not null ? " 🖊" : string.Empty;
                    return $"{a.Type} — {a.NormalDamageDealt + a.CriticalDamageDealt} dmg{note}";
                })
                .AddChoices(actList));

        var actionNote = AnsiConsole.Prompt(
            new TextPrompt<string>("Enter annotation:").AllowEmpty());
        await actions.UpdateNarrativeAsync(selectedAction.Id, string.IsNullOrWhiteSpace(actionNote) ? null : actionNote);
        AnsiConsole.MarkupLine("[green]Action annotation saved.[/]");
        return 0;
    }

    private static async Task<string> GetOperativeNameAsync(SqliteConnection conn, Guid operativeId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM operatives WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", operativeId.ToString());
        return await cmd.ExecuteScalarAsync() as string ?? operativeId.ToString()[..8];
    }

    private record ActivationEntry(
        Guid ActivationId, int TpNumber, int Seq,
        string OperativeName, string Order, string? NarrativeNote);
}
