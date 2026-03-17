using System.ComponentModel;
using KillTeam.DataSlate.Domain.Repositories;
using KillTeam.DataSlate.Domain.Services;
using KillTeam.DataSlate.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace KillTeam.DataSlate.Console.Commands;

/// <summary>Imports team data from YAML or JSON files, or scans a folder for all team files.</summary>
[Description("Import a team from a YAML/JSON file (or scan the team folder).")]
public class ImportTeamsCommand(
    TeamYamlImporter yamlImporter,
    TeamJsonImporter jsonImporter,
    ITeamRepository teams,
    IConfiguration config) : AsyncCommand<ImportTeamsCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [Description("Path to a team YAML/JSON file, or a folder to scan. Defaults to the configured TeamFolder.")]
        [CommandArgument(0, "[filepath]")]
        public string? FilePath { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.FilePath))
        {
            return await ImportSingleFile(settings.FilePath);
        }

        // Folder scan
        var folder = config["DataSlate:TeamFolder"] ?? "../teams/";
        if (!Directory.Exists(folder))
        {
            AnsiConsole.MarkupLine($"[yellow]team folder not found: {Markup.Escape(folder)}[/]");
            return 1;
        }

        var files = Directory.GetFiles(folder, "*.yaml")
            .Concat(Directory.GetFiles(folder, "*.yml"))
            .Concat(Directory.GetFiles(folder, "*.json"))
            .ToArray();

        if (files.Length == 0)
        {
            AnsiConsole.MarkupLine("[dim]No team files found in team folder.[/]");
            return 0;
        }

        var success = 0;
        foreach (var file in files)
        {
            try
            {
                await ImportFileAsync(file);
                success++;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]Warning: skipped '{Markup.Escape(Path.GetFileName(file))}' — {Markup.Escape(ex.Message)}[/]");
            }
        }

        AnsiConsole.MarkupLine($"[green]Imported {success} of {files.Length} team file(s).[/]");
        return 0;
    }

    private async Task<int> ImportSingleFile(string path)
    {
        if (!File.Exists(path))
        {
            AnsiConsole.MarkupLine($"[red]File not found: {Markup.Escape(path)}[/]");
            return 1;
        }

        try
        {
            await ImportFileAsync(path);
            return 0;
        }
        catch (TeamValidationException ex)
        {
            AnsiConsole.MarkupLine($"[red]Import failed: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Unexpected error: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    private async Task ImportFileAsync(string path)
    {
        var content = await File.ReadAllTextAsync(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();

        var team = ext switch
        {
            ".yaml" or ".yml" => yamlImporter.Import(content),
            ".json" => jsonImporter.Import(content),
            _ => throw new TeamValidationException($"Unsupported file extension: {ext}"),
        };

        await teams.UpsertAsync(team);

        var opCount = team.Operatives.Count;
        var wCount = team.Operatives.Sum(o => o.Weapons.Count);
        var aCount = team.Operatives.Sum(o => o.Abilities.Count);
        AnsiConsole.MarkupLine(
            $"[green]Imported '{Markup.Escape(team.Name)}' — {opCount} operatives, {wCount} weapons, {aCount} abilities, " +
            $"{team.FactionRules.Count} rules, {team.StrategyPloys.Count + team.FirefightPloys.Count} ploys, " +
            $"{team.FactionEquipment.Count + team.UniversalEquipment.Count} equipment.[/]");
    }
}
