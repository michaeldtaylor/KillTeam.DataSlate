using PrettyPrompt.Completion;
using PrettyPrompt.Documents;
using PrettyPrompt.Highlighting;

namespace KillTeam.DataSlate.Console.Completion;

/// <summary>
/// Provides context-aware tab completion for the interactive REPL.
/// At the root context completions are slash-prefixed nouns (/player, /game, /team).
/// Inside a noun context completions are the verbs for that context.
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
            .Select(cmd => new CompletionItem(
                replacementText: cmd,
                displayText: new FormattedString(cmd, new ConsoleFormat(Foreground: AnsiColor.Cyan))))
            .ToList();

        return Task.FromResult<IReadOnlyList<CompletionItem>>(items);
    }
}
