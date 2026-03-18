using System.ComponentModel;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace KillTeam.DataSlate.Console.Commands;

/// <summary>Adds or edits narrative notes on activations and actions within a game.</summary>
[Description("Add or edit narrative notes on activations and actions for a game.")]
public class AnnotateCommand(
    IAnsiConsole console,
    IGameRepository games,
    IActivationRepository activations,
    IActionRepository actions,
    ITurningPointRepository turningPoints,
    IOperativeRepository operatives,
    ILogger<AnnotateCommand> logger) : AsyncCommand<AnnotateCommand.Settings>
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
            console.MarkupLine("[red]Invalid game ID format.[/]");

            return 1;
        }

        logger.LogDebug("Annotate command for game {GameId}", gameId);

        var game = await games.GetByIdAsync(gameId);

        if (game is null)
        {
            logger.LogWarning("Game {GameId} not found for annotate", gameId);
            console.MarkupLine($"[red]Game {Markup.Escape(settings.GameId)} not found.[/]");

            return 1;
        }

        var turningPointSummaries = await turningPoints.GetSummariesByGameAsync(gameId);

        if (turningPointSummaries.Count == 0)
        {
            console.MarkupLine("[dim]No turning points recorded for this game.[/]");

            return 0;
        }

        var allActivations = new List<ActivationEntry>();

        foreach (var turningPoint in turningPointSummaries)
        {
            var turningPointActivations = (await activations.GetByTurningPointAsync(turningPoint.Id)).ToList();

            foreach (var activation in turningPointActivations)
            {
                var operativeName = await operatives.GetNameByIdAsync(activation.OperativeId) ?? activation.OperativeId.ToString()[..8];

                allActivations.Add(new ActivationEntry(
                    activation.Id,
                    turningPoint.Number,
                    activation.SequenceNumber,
                    operativeName,
                    activation.OrderSelected.ToString(),
                    activation.NarrativeNote));
            }
        }

        if (allActivations.Count == 0)
        {
            console.MarkupLine("[dim]No activations recorded for this game.[/]");

            return 0;
        }

        var selected = console.Prompt(
            new SelectionPrompt<ActivationEntry>()
                .Title("Select an activation to annotate:")
                .UseConverter(entry =>
                {
                    var note = entry.NarrativeNote is not null ? " [dim]🖊[/]" : string.Empty;

                    return $"[TP{entry.TpNumber}, Act {entry.Seq}] {Markup.Escape(entry.OperativeName)} ({entry.Order}){note}";
                })
                .AddChoices(allActivations));

        var choice = console.Prompt(
            new SelectionPrompt<string>()
                .Title("What would you like to do?")
                .AddChoices("Annotate this activation", "Drill down to a specific action", "Cancel"));

        if (choice == "Cancel")
        {
            return 0;
        }

        if (choice == "Annotate this activation")
        {
            var note = console.Prompt(
                new TextPrompt<string>("Enter annotation:").AllowEmpty());

            await activations.UpdateNarrativeAsync(selected.ActivationId, string.IsNullOrWhiteSpace(note) ? null : note);
            logger.LogDebug("Annotation saved for activation {Id}", selected.ActivationId);
            console.MarkupLine("[green]Annotation saved.[/]");

            return 0;
        }

        var actList = (await actions.GetByActivationAsync(selected.ActivationId)).ToList();

        if (actList.Count == 0)
        {
            console.MarkupLine("[dim]No actions recorded for this activation.[/]");

            return 0;
        }

        var selectedAction = console.Prompt(
            new SelectionPrompt<GameAction>()
                .Title("Select an action to annotate:")
                .UseConverter(action =>
                {
                    var note = action.NarrativeNote is not null ? " 🖊" : string.Empty;

                    return $"{action.Type} — {action.NormalDamageDealt + action.CriticalDamageDealt} dmg{note}";
                })
                .AddChoices(actList));

        var actionNote = console.Prompt(
            new TextPrompt<string>("Enter annotation:").AllowEmpty());

        await actions.UpdateNarrativeAsync(selectedAction.Id, string.IsNullOrWhiteSpace(actionNote) ? null : actionNote);
        logger.LogDebug("Annotation saved for action {Id}", selectedAction.Id);
        console.MarkupLine("[green]Action annotation saved.[/]");

        return 0;
    }

    private record ActivationEntry(
        Guid ActivationId, int TpNumber, int Seq,
        string OperativeName, string Order, string? NarrativeNote);
}
