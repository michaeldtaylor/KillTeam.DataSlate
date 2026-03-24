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

    internal static readonly Dictionary<string, string[]> ContextVerbs = new()
    {
        ["player"] = ["create", "list", "delete"],
        ["team"] = ["import"],
        ["game"] = ["new", "play", "view", "annotate", "history", "stats", "simulate"],
    };

    internal static readonly Dictionary<string, string> Descriptions = new()
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
        if (context is null && TryParseNounVerbPrefix(text, caret, out _, out _, out var verbStart, out var verbLength))
        {
            return Task.FromResult(new TextSpan(verbStart, verbLength));
        }

        return Task.FromResult(new TextSpan(0, caret));
    }

    protected override Task<IReadOnlyList<CompletionItem>> GetCompletionItemsAsync(
        string text, int caret, TextSpan spanToBeReplaced, CancellationToken cancellationToken)
    {
        IEnumerable<string> candidates;
        string prefix;

        if (context is null && TryParseNounVerbPrefix(text, caret, out var detectedNoun, out var verbPrefix, out _, out _))
        {
            candidates = ContextVerbs.TryGetValue(detectedNoun, out var nounVerbs) ? nounVerbs : [];
            prefix = verbPrefix;
        }
        else if (context is null)
        {
            candidates = RootCommands;
            prefix = text[..caret];
        }
        else
        {
            candidates = ContextVerbs.TryGetValue(context, out var verbs) ? verbs : [];
            prefix = text[..caret];
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

    /// <summary>
    /// Detects the "/noun verbPrefix" pattern at root level (e.g. "/game cr").
    /// Returns false if the pattern doesn't match or the noun is not a valid context.
    /// </summary>
    private static bool TryParseNounVerbPrefix(
        string text, int caret,
        out string noun, out string verbPrefix, out int verbStart, out int verbLength)
    {
        noun = string.Empty;
        verbPrefix = string.Empty;
        verbStart = 0;
        verbLength = 0;

        if (!text.StartsWith('/'))
        {
            return false;
        }

        var beforeCaret = text[1..caret];
        var spaceIndex = beforeCaret.IndexOf(' ');

        if (spaceIndex < 0)
        {
            return false;
        }

        var potentialNoun = beforeCaret[..spaceIndex].ToLowerInvariant();

        if (!ContextVerbs.ContainsKey(potentialNoun))
        {
            return false;
        }

        noun = potentialNoun;
        verbStart = spaceIndex + 2; // +1 for leading slash, +1 for the space
        verbPrefix = beforeCaret[(spaceIndex + 1)..];
        verbLength = verbPrefix.Length;

        return true;
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
