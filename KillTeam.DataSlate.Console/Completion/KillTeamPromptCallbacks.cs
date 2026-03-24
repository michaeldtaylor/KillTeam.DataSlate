using PrettyPrompt.Completion;
using PrettyPrompt.Documents;
using PrettyPrompt.Highlighting;

namespace KillTeam.DataSlate.Console.Completion;

/// <summary>
/// Provides context-aware tab completion for the interactive REPL.
/// Completions are full command strings (e.g. "game new") matched from position zero,
/// so the entire typed prefix is replaced with the chosen command.
/// </summary>
public class KillTeamPromptCallbacks : PrettyPrompt.PromptCallbacks
{
    private static readonly string[] Commands =
    [
        "player create",
        "player list",
        "player delete",
        "team import",
        "game new",
        "game play",
        "game view",
        "game annotate",
        "game history",
        "game stats",
        "game simulate",
        "exit",
        "quit",
    ];

    protected override Task<TextSpan> GetSpanToReplaceByCompletionAsync(
        string text, int caret, CancellationToken cancellationToken)
    {
        return Task.FromResult(new TextSpan(0, caret));
    }

    protected override Task<IReadOnlyList<CompletionItem>> GetCompletionItemsAsync(
        string text, int caret, TextSpan spanToBeReplaced, CancellationToken cancellationToken)
    {
        var prefix = text[..caret];

        var items = Commands
            .Where(cmd => cmd.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && cmd.Length > prefix.Length)
            .Select(cmd => new CompletionItem(
                replacementText: cmd,
                displayText: new FormattedString(cmd, new ConsoleFormat(Foreground: AnsiColor.Cyan))))
            .ToList();

        return Task.FromResult<IReadOnlyList<CompletionItem>>(items);
    }
}
