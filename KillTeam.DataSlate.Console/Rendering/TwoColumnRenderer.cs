using Spectre.Console;

namespace KillTeam.DataSlate.Console.Rendering;

/// <summary>
/// Renders output in a two-column layout: a fixed-width participant label column on the left,
/// and free-form content on the right separated by │.
///
/// Column width adapts dynamically to the longest participant name so that all labels align
/// regardless of name length. System output delegates to <see cref="ColumnContext.Prefix"/>.
/// </summary>
public class TwoColumnRenderer
{
    private const string Separator = " │ ";

    private readonly IAnsiConsole _console;
    private readonly ColumnContext _columnContext;
    private readonly Dictionary<string, string> _labelMarkup;

    public TwoColumnRenderer(
        IAnsiConsole console,
        IReadOnlyDictionary<string, string> participantLabels,
        IReadOnlyDictionary<string, string> participantColours,
        ColumnContext columnContext)
    {
        _console = console;
        _columnContext = columnContext;

        var columnWidth = Math.Max(
            8,  // minimum: "[System]" is 8 visible chars
            participantLabels.Count > 0
                ? participantLabels.Values.Max(name => name.Length + 2)
                : 0);

        columnContext.ColumnWidth = columnWidth;

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
        if (_labelMarkup.TryGetValue(participantId, out var label))
        {
            _console.MarkupLine($"{label}{Separator}{content}");
        }
        else
        {
            _console.MarkupLine($"{_columnContext.Prefix}{content}");
        }
    }

    /// <summary>Prints a system line: [System] │ content</summary>
    public void PrintLine(string content)
    {
        _console.MarkupLine($"{_columnContext.Prefix}{content}");
    }

    /// <summary>Prints a labelled sub-line with 2-space indent inside the content column.</summary>
    public void PrintSubLine(string participantId, string content)
    {
        PrintLine(participantId, "  " + content);
    }
}
