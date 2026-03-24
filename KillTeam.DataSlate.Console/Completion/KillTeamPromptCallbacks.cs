using PrettyPrompt.Completion;
using PrettyPrompt.Consoles;
using PrettyPrompt.Documents;
using PrettyPrompt.Highlighting;

namespace KillTeam.DataSlate.Console.Completion;

/// <summary>
/// Provides context-aware tab completion for the interactive REPL.
/// At the root context completions are slash-prefixed nouns (/player, /game, /team).
/// Inside a noun context completions are the verbs for that context.
/// Tab is suppressed when no completions are available.
/// </summary>
public class KillTeamPromptCallbacks(string? context = null) : PrettyPrompt.PromptCallbacks
{
    private static readonly string[] RootCommands =
    [
        "/player",
        "/team",
        "/game",
        "exit",
        "quit",
    ];

    private static readonly Dictionary<string, string[]> ContextVerbs = new()
    {
        ["player"] = ["create", "list", "delete"],
        ["team"] = ["import"],
        ["game"] = ["new", "play", "view", "annotate", "history", "stats", "simulate"],
    };

    private static readonly Dictionary<string, string> Descriptions = new()
    {
        ["/player"]  = "Enter the player context.",
        ["/team"]    = "Enter the team context.",
        ["/game"]    = "Enter the game context.",
        ["exit"]     = "Exit the REPL.",
        ["quit"]     = "Exit the REPL.",
        ["create"]   = "Register a new player.",
        ["list"]     = "List all players with stats.",
        ["delete"]   = "Remove a player (blocked if they have games).",
        ["import"]   = "Import team files (YAML or JSON) from a file or folder.",
        ["new"]      = "Start a new game — select players, teams, and mission.",
        ["play"]     = "Play or resume a game session.",
        ["view"]     = "View full detail of a game.",
        ["annotate"] = "Add narrative notes to activations and actions.",
        ["history"]  = "View completed game history.",
        ["stats"]    = "View player and team statistics.",
        ["simulate"] = "Simulate fight/shoot encounters without a saved game.",
    };

    protected override Task<TextSpan> GetSpanToReplaceByCompletionAsync(
        string text, int caret, CancellationToken cancellationToken)
    {
        return Task.FromResult(new TextSpan(0, caret));
    }

    protected override Task<IReadOnlyList<CompletionItem>> GetCompletionItemsAsync(
        string text, int caret, TextSpan spanToBeReplaced, CancellationToken cancellationToken)
    {
        var prefix = text[..caret];
        IEnumerable<string> candidates;

        if (context is null)
        {
            candidates = RootCommands;
        }
        else
        {
            candidates = ContextVerbs.TryGetValue(context, out var verbs) ? verbs : [];
        }

        var items = candidates
            .Where(cmd => cmd.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && cmd.Length > prefix.Length)
            .Select(cmd =>
            {
                CompletionItem.GetExtendedDescriptionHandler? descriptionHandler = Descriptions.TryGetValue(cmd, out var desc)
                    ? _ => Task.FromResult(new FormattedString(desc, new ConsoleFormat(Foreground: AnsiColor.Yellow)))
                    : null;

                return new CompletionItem(
                    replacementText: cmd,
                    displayText: new FormattedString(cmd, new ConsoleFormat(Foreground: AnsiColor.Cyan)),
                    getExtendedDescription: descriptionHandler);
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<CompletionItem>>(items);
    }

    protected override async Task<KeyPress> TransformKeyPressAsync(
        string text, int caret, KeyPress keyPress, CancellationToken cancellationToken)
    {
        if (keyPress.ConsoleKeyInfo.Key == ConsoleKey.Tab)
        {
            var span = await GetSpanToReplaceByCompletionAsync(text, caret, cancellationToken);
            var items = await GetCompletionItemsAsync(text, caret, span, cancellationToken);

            if (items.Count == 0)
            {
                return new KeyPress(new ConsoleKeyInfo('\0', ConsoleKey.NoName, false, false, false), string.Empty);
            }
        }

        return keyPress;
    }
}
