using Spectre.Console;

namespace KillTeam.DataSlate.Console.Rendering;

/// <summary>
/// Renders output in a two-column layout: a fixed-width participant label column on the left,
/// and free-form content on the right separated by │.
///
/// Column width adapts dynamically to the longest participant name so that all labels align
/// regardless of name length. System output uses a [System] label in dim grey.
/// </summary>
public class TwoColumnRenderer
{
    private readonly IAnsiConsole _console;
    private readonly Dictionary<string, string> _labelMarkup;
    private readonly string _systemLabel;
    private readonly string _separator = " │ ";

    public TwoColumnRenderer(
        IAnsiConsole console,
        IReadOnlyDictionary<string, string> participantLabels,
        IReadOnlyDictionary<string, string> participantColours,
        ColumnContext columnContext)
    {
        _console = console;

        var columnWidth = Math.Max(
            8,  // minimum: "[System]" is 8 visible chars
            participantLabels.Count > 0
                ? participantLabels.Values.Max(name => name.Length + 2)
                : 0);

        columnContext.ColumnWidth = columnWidth;

        var systemText = "[System]";
        var systemPadding = new string(' ', columnWidth - systemText.Length);
        _systemLabel = $"[dim grey]{Markup.Escape(systemText)}[/]{systemPadding}";

        _labelMarkup = new Dictionary<string, string>();

        foreach (var (participantId, name) in participantLabels)
        {
            var colour = participantColours.GetValueOrDefault(participantId, "white");
            var rendered = $"[{name}]";
            var padding = new string(' ', columnWidth - rendered.Length);

            _labelMarkup[participantId] = $"[bold {colour}]{Markup.Escape(rendered)}[/]{padding}";
        }
    }

    /// <summary>Prints a labelled line: [Name] │ content</summary>
    public void PrintLine(string participantId, string content)
    {
        var label = _labelMarkup.GetValueOrDefault(participantId, _systemLabel);

        _console.MarkupLine($"{label}{_separator}{content}");
    }

    /// <summary>Prints a system line: [System] │ content</summary>
    public void PrintLine(string content)
    {
        _console.MarkupLine($"{_systemLabel}{_separator}{content}");
    }

    /// <summary>Prints a labelled sub-line with 2-space indent inside the content column.</summary>
    public void PrintSubLine(string participantId, string content)
    {
        PrintLine(participantId, "  " + content);
    }
}
