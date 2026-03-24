using KillTeam.DataSlate.Domain.Models;
using Spectre.Console;

namespace KillTeam.DataSlate.Console.Rendering;

/// <summary>
/// DI singleton that holds the current column context for the two-column layout.
/// Set by <see cref="TwoColumnRenderer"/> on construction; read by input providers to prefix prompts.
/// A null <see cref="CurrentPlayer"/> renders as a light grey [System] label.
/// </summary>
public class ColumnContext
{
    private const string SystemLabel = "[System]";

    public Player? CurrentPlayer { get; set; }

    public int ColumnWidth { get; set; } = 8;

    public string Prefix
    {
        get
        {
            var width = Math.Max(8, ColumnWidth);

            if (CurrentPlayer is not null)
            {
                var label = $"[{CurrentPlayer.Username}]";
                var padding = new string(' ', width - label.Length);

                return $"[bold {CurrentPlayer.Colour}]{Markup.Escape(label)}[/]{padding} │ ";
            }

            var systemPadding = new string(' ', width - SystemLabel.Length);

            return $"[grey]{Markup.Escape(SystemLabel)}[/]{systemPadding} │ ";
        }
    }
}
