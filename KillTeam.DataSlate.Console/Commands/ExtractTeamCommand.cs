using System.ComponentModel;
using KillTeam.DataSlate.Console.Services;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace KillTeam.DataSlate.Console.Commands;

/// <summary>Extracts a Kill Team from GW PDFs in references/kill-teams/ and writes a validated JSON file to teams/.</summary>
[Description("Extract a team from GW PDFs into a teams/ JSON file.")]
public class ExtractTeamCommand(PdfTeamExtractor extractor, IConfiguration config)
    : Command<ExtractTeamCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [Description("Name of the team folder under references/kill-teams/ to extract (e.g. \"Blades of Khaine\").")]
        [CommandOption("-t|--team")]
        public string? TeamName { get; set; }

        [Description("Extract all teams found in references/kill-teams/.")]
        [CommandOption("-a|--all")]
        public bool All { get; set; }

        [Description("Overwrite existing teams/ JSON files.")]
        [CommandOption("-f|--force")]
        public bool Force { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var projectRoot = ResolveProjectRoot(config);
        var referencesBase = Path.Combine(projectRoot, "references", "kill-teams");
        var teamsOut = Path.Combine(projectRoot, "teams");

            if (!Directory.Exists(referencesBase))
        {
            AnsiConsole.MarkupLine($"[red]References folder not found: {Markup.Escape(referencesBase)}[/]");
            return 1;
        }

        if (!Directory.Exists(teamsOut))
        {
            Directory.CreateDirectory(teamsOut);
        }

        if (settings.All)
        {
            var errors = 0;

            foreach (var dir in Directory.GetDirectories(referencesBase))
            {
                var name = Path.GetFileName(dir);

                if (!ExtractOne(name, dir, teamsOut, settings.Force))
                {
                    errors++;
                }
            }

            return errors > 0 ? 1 : 0;
        }

        if (string.IsNullOrWhiteSpace(settings.TeamName))
        {
            AnsiConsole.MarkupLine("[red]Specify a team with --team <name> or use --all.[/]");
            return 1;
        }

        var teamFolder = Path.Combine(referencesBase, settings.TeamName);

        if (!Directory.Exists(teamFolder))
        {
            AnsiConsole.MarkupLine($"[red]Team folder not found: {Markup.Escape(teamFolder)}[/]");
            return 1;
        }

        return ExtractOne(settings.TeamName, teamFolder, teamsOut, settings.Force) ? 0 : 1;
    }

    private bool ExtractOne(string teamName, string teamFolder, string teamsOut, bool force)
    {
        var slug = PdfTeamExtractor.Slugify(teamName);
        var outFile = Path.Combine(teamsOut, $"{slug}.json");

        if (File.Exists(outFile) && force == false)
        {
            AnsiConsole.MarkupLine($"[yellow]Skipping '{Markup.Escape(teamName)}' — {Markup.Escape(outFile)} already exists (use --force to overwrite).[/]");
            return true;
        }

        AnsiConsole.MarkupLine($"\n[cyan]==> Extracting: {Markup.Escape(teamName)}[/]");

        try
        {
            var team = extractor.Extract(teamName, teamFolder);

            AnsiConsole.MarkupLine($"[dim]  Operatives : {team.Operatives.Count}[/]");
            AnsiConsole.MarkupLine($"[dim]  Faction    : {Markup.Escape(team.Faction)}[/]");
            AnsiConsole.MarkupLine($"[dim]  Equipment  : {team.Equipment.Count} items[/]");

            var json = team.ToJson();

            File.WriteAllText(outFile, json);

            AnsiConsole.MarkupLine($"[green]  Written: {Markup.Escape(outFile)}[/]");

            return true;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]  Failed: {Markup.Escape(ex.Message)}[/]");
            return false;
        }
    }

    private static string ResolveProjectRoot(IConfiguration config)
    {
        var configured = config["DataSlate:ProjectRoot"];

        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
        {
            return configured;
        }

        // Walk up from the current directory to find the project root (contains references/)
        var dir = Directory.GetCurrentDirectory();

        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "references")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        return Directory.GetCurrentDirectory();
    }
}
