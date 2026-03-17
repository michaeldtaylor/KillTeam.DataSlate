using Spectre.Console;

namespace KillTeam.DataSlate.Console.Rendering;

/// <summary>
/// Renders output in a two-column layout: a fixed-width participant label column on the left,
/// and free-form content on the right separated by │.
///
/// Column width adapts dynamically to the longest participant name so that all labels align
/// regardless of name length (e.g. [You] vs [Michael]).
/// </summary>
public class TwoColumnRenderer
{
    private static readonly string[] LabelColors = ["cyan", "yellow", "green", "magenta", "blue"];

    private readonly IAnsiConsole _console;
    private readonly Dictionary<string, string> _labelMarkup;
    private readonly string _blankLabel;
    private readonly string _separator = " │ ";

    public TwoColumnRenderer(IAnsiConsole console, IReadOnlyDictionary<string, string> participantLabels)
    {
        _console = console;

        var columnWidth = participantLabels.Count > 0
            ? participantLabels.Values.Max(name => name.Length + 2)
            : 5;

        _blankLabel = new string(' ', columnWidth);

        _labelMarkup = new Dictionary<string, string>();

        var colorIndex = 0;

        foreach (var (participantId, name) in participantLabels)
        {
            var color = LabelColors[colorIndex % LabelColors.Length];
            colorIndex++;

            var rendered = $"[{name}]";
            var padding = new string(' ', columnWidth - rendered.Length);

            _labelMarkup[participantId] = $"[bold {color}]{Markup.Escape(rendered)}[/]{padding}";
        }
    }

    /// <summary>Prints a labelled line: [Name] │ content</summary>
    public void PrintLine(string participantId, string content)
    {
        var label = _labelMarkup.GetValueOrDefault(participantId, _blankLabel);

        _console.MarkupLine($"{label}{_separator}{content}");
    }

    /// <summary>Prints a system line with a blank label column: (spaces) │ content</summary>
    public void PrintLine(string content)
    {
        _console.MarkupLine($"{_blankLabel}{_separator}{content}");
    }

    /// <summary>Prints a labelled sub-line with 2-space indent inside the content column.</summary>
    public void PrintSubLine(string participantId, string content)
    {
        PrintLine(participantId, "  " + content);
    }
}
