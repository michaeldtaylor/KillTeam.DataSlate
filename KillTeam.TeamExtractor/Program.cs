using KillTeam.TeamExtractor.Services;
using Spectre.Console;

var extractor = new PdfTeamExtractor(new PdfWeaponTypeDetector());

if (args.Length == 0)
{
    AnsiConsole.MarkupLine("[red]Usage: ktte <folder>[/]");
    AnsiConsole.MarkupLine("[dim]  <folder> can be a single team folder (contains *Datacards.pdf)[/]");
    AnsiConsole.MarkupLine("[dim]           or a root folder whose sub-folders each contain *Datacards.pdf[/]");
    return 1;
}

var folder = args[0];

if (!Directory.Exists(folder))
{
    AnsiConsole.MarkupLine($"[red]Folder not found: {Markup.Escape(folder)}[/]");
    return 1;
}

// Resolve to an array of team folders to process
var teamFolders = IsTeamFolder(folder)
    ? [folder]
    : Directory.GetDirectories(folder).Where(IsTeamFolder).Order().ToArray();

if (teamFolders.Length == 0)
{
    AnsiConsole.MarkupLine($"[red]No team folders found in: {Markup.Escape(folder)}[/]");
    AnsiConsole.MarkupLine("[dim]  A team folder must contain at least one *Datacards*.pdf file.[/]");
    return 1;
}

var errors = 0;

foreach (var teamFolder in teamFolders)
{
    var teamName = Path.GetFileName(teamFolder)!;
    AnsiConsole.MarkupLine($"\n[cyan]==> Extracting: [yellow]{Markup.Escape(teamName)}[/][/]");

    try
    {
        var team = extractor.Extract(teamName, teamFolder);

        // Resolve output path: walk up from the team folder looking for an existing teams/ sibling
        var outDir = ResolveTeamsOutputDir(teamFolder);
        Directory.CreateDirectory(outDir);

        var outFile = Path.Combine(outDir, $"{team.Id}.yaml");
        var yaml = team.ToYaml();
        File.WriteAllText(outFile, yaml);

        AnsiConsole.MarkupLine($"[dim]  Datacards  : {team.Datacards.Count}[/]");
        AnsiConsole.MarkupLine($"[dim]  Faction    : {Markup.Escape(team.Faction)}[/]");
        AnsiConsole.MarkupLine($"[dim]  Faction Eq : {team.FactionEquipment.Count} items[/]");
        AnsiConsole.MarkupLine($"[dim]  Universal Eq: {team.UniversalEquipment.Count} items[/]");
        AnsiConsole.MarkupLine($"[dim]  Rules      : {team.FactionRules.Count} faction, {team.StrategyPloys.Count} strategy, {team.FirefightPloys.Count} firefight[/]");
        AnsiConsole.MarkupLine($"[green]  Written: {Markup.Escape(outFile)}[/]");
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]  Failed: {Markup.Escape(ex.Message)}[/]");
        errors++;
    }
}

return errors > 0 ? 1 : 0;

// ─── Helpers ──────────────────────────────────────────────────────────────────

/// <summary>Returns true when <paramref name="path"/> is a team folder — i.e. it contains a Datacards PDF.</summary>
static bool IsTeamFolder(string path) =>
    Directory.GetFiles(path, "*Datacards*.pdf").Length > 0;

/// <summary>
/// Walks up from <paramref name="teamFolder"/> to find the nearest ancestor that already
/// contains a <c>teams/</c> directory, or falls back to a <c>teams/</c> sibling of the
/// team folder itself.
/// </summary>
static string ResolveTeamsOutputDir(string teamFolder)
{
    var dir = Directory.GetParent(teamFolder)?.FullName;

    while (dir != null)
    {
        var candidate = Path.Combine(dir, "teams");

        if (Directory.Exists(candidate))
        {
            return candidate;
        }

        dir = Directory.GetParent(dir)?.FullName;
    }

    // Fallback: teams/ next to the team folder itself
    return Path.Combine(Directory.GetParent(teamFolder)?.FullName ?? teamFolder, "teams");
}

