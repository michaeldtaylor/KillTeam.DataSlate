using KillTeam.DataSlate.Domain.Models;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace KillTeam.DataSlate.Console.Rendering;

public static class OperativeCardRenderer
{
    private const string RangedIcon = "[orange1]>[/]";
    private const string MeleeIcon  = "[red]*[/]";

    public static void Render(IAnsiConsole console, Operative operative)
    {
        var header = BuildHeader(operative);
        var content = BuildContent(operative);

        var panel = new Panel(content)
            .Header($"[bold]{Markup.Escape(operative.Name.ToUpperInvariant())}[/]")
            .Border(BoxBorder.Rounded)
            .Padding(1, 0);

        console.Write(panel);
    }

    private static string BuildHeader(Operative operative)
    {
        return $"APL [bold green]{operative.Apl}[/]  " +
               $"MOVE [bold green]{operative.Move}\"[/]  " +
               $"SAVE [bold green]{operative.Save}+[/]  " +
               $"WOUNDS [bold green]{operative.Wounds}[/]";
    }

    private static IRenderable BuildContent(Operative operative)
    {
        var rows = new List<IRenderable>
        {
            new Markup(BuildHeader(operative)),
        };

        if (operative.Weapons.Count > 0)
        {
            rows.Add(new Text(string.Empty));
            rows.Add(BuildWeaponTable(operative.Weapons));
        }

        if (operative.Abilities.Count > 0)
        {
            rows.Add(new Text(string.Empty));
            rows.Add(BuildAbilities(operative.Abilities));
        }

        if (operative.Keywords.Length > 0)
        {
            rows.Add(new Text(string.Empty));
            rows.Add(new Markup($"[dim]{Markup.Escape(string.Join(", ", operative.Keywords).ToUpperInvariant())}[/]"));
        }

        return new Rows(rows);
    }

    private static Table BuildWeaponTable(IEnumerable<Weapon> weapons)
    {
        var table = new Table()
            .NoBorder()
            .AddColumn(new TableColumn(string.Empty).NoWrap())
            .AddColumn(new TableColumn("[dim]NAME[/]"))
            .AddColumn(new TableColumn("[dim]ATK[/]").RightAligned())
            .AddColumn(new TableColumn("[dim]HIT[/]").RightAligned())
            .AddColumn(new TableColumn("[dim]DMG[/]").RightAligned())
            .AddColumn(new TableColumn("[dim]WR[/]"));

        foreach (var weapon in weapons)
        {
            var icon = weapon.Type == WeaponType.Ranged ? RangedIcon : MeleeIcon;
            var rules = string.IsNullOrWhiteSpace(weapon.WeaponRules)
                ? string.Empty
                : Markup.Escape(weapon.WeaponRules);

            table.AddRow(
                new Markup(icon),
                new Markup(Markup.Escape(weapon.Name)),
                new Markup($"[green]{weapon.Atk}[/]"),
                new Markup($"[green]{weapon.Hit}+[/]"),
                new Markup($"[green]{weapon.NormalDmg}/{weapon.CriticalDmg}[/]"),
                new Markup($"[dim]{rules}[/]"));
        }

        return table;
    }

    private static IRenderable BuildAbilities(IEnumerable<OperativeAbility> abilities)
    {
        var lines = abilities.Select(a =>
            $"[bold]{Markup.Escape(a.Name)}:[/] {Markup.Escape(a.Text.Trim())}");

        return new Markup(string.Join("\n", lines));
    }
}
