using System.ComponentModel;
using KillTeam.DataSlate.Domain;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using KillTeam.DataSlate.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Spectre.Console.Cli;

namespace KillTeam.DataSlate.Console.Commands;

/// <summary>Imports team data from YAML or JSON files, or scans a folder for all team files.</summary>
[Description("Import a team from a YAML/JSON file (or scan the team folder).")]
public class ImportTeamsCommand(
    IAnsiConsole console,
    TeamYamlImporter yamlImporter,
    TeamJsonImporter jsonImporter,
    ITeamRepository teams,
    IOptions<DataSlateOptions> options,
    ILogger<ImportTeamsCommand> logger) : AsyncCommand<ImportTeamsCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [Description("Path to a team YAML/JSON file, or a folder to scan. Defaults to the configured TeamFolder.")]
        [CommandArgument(0, "[filepath]")]
        public string? FilePath { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        logger.LogDebug("team import started. Path={Path}", settings.FilePath ?? "(folder scan)");
        if (!string.IsNullOrWhiteSpace(settings.FilePath))
        {
            return await ImportSingleFile(settings.FilePath);
        }

        // Folder scan
        var teamsFolder = options.Value.TeamsFolder;

        if (!Directory.Exists(teamsFolder))
        {
            console.MarkupLine($"[yellow]team folder not found: {Markup.Escape(teamsFolder)}[/]");
            return 1;
        }

        var files = Directory.GetFiles(teamsFolder, "*.yaml")
            .Concat(Directory.GetFiles(teamsFolder, "*.yml"))
            .Concat(Directory.GetFiles(teamsFolder, "*.json"))
            .ToArray();

        if (files.Length == 0)
        {
            console.MarkupLine("[dim]No team files found in team folder.[/]");
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
                logger.LogWarning(ex, "Skipped file {File}", Path.GetFileName(file));
                console.MarkupLine($"[yellow]Warning: skipped '{Markup.Escape(Path.GetFileName(file))}' — {Markup.Escape(ex.Message)}[/]");
            }
        }

        console.MarkupLine($"[green]Imported {success} of {files.Length} team file(s).[/]");
        return 0;
    }

    private async Task<int> ImportSingleFile(string path)
    {
        if (!File.Exists(path))
        {
            console.MarkupLine($"[red]File not found: {Markup.Escape(path)}[/]");
            return 1;
        }

        try
        {
            await ImportFileAsync(path);
            return 0;
        }
        catch (TeamValidationException ex)
        {
            logger.LogWarning(ex, "Import validation failed for {Path}", path);
            console.MarkupLine($"[red]Import failed: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error importing {Path}", path);
            console.MarkupLine($"[red]Unexpected error: {Markup.Escape(ex.Message)}[/]");
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

        logger.LogDebug("Imported team {TeamName} from {Path}", team.Name, path);

        var opCount = team.Operatives.Count;
        var wCount = team.Operatives.Sum(o => o.Weapons.Count);
        var aCount = team.Operatives.Sum(o => o.Abilities.Count);

        console.MarkupLine(
            $"[green]Imported '{Markup.Escape(team.Name)}' — {opCount} operatives, {wCount} weapons, {aCount} abilities, " +
            $"{team.FactionRules.Count} rules, {team.StrategyPloys.Count + team.FirefightPloys.Count} ploys, " +
            $"{team.FactionEquipment.Count + team.UniversalEquipment.Count} equipment.[/]");
    }
}
