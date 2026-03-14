using System.ComponentModel;
using KillTeam.DataSlate.Console.Infrastructure.Repositories;
using KillTeam.DataSlate.Console.Services;
using KillTeam.DataSlate.Domain.Repositories;
using KillTeam.DataSlate.Domain.Services;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace KillTeam.DataSlate.Console.Commands;

/// <summary>Imports a kill team killTeam from a JSON file or scans a folder for all killTeam files.</summary>
[Description("Import a kill team killTeam from a JSON file (or scan a folder).")]
public class ImportKillTeamsCommand(
    KillTeamJsonImporter importer,
    IKillTeamRepository killTeams,
    SqliteOperativeRepository operatives,
    SqliteWeaponRepository weapons,
    IConfiguration config) : AsyncCommand<ImportKillTeamsCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [Description("Path to a killTeam JSON file, or a folder to scan. Defaults to the configured KillTeamFolder.")]
        [CommandArgument(0, "[filepath]")]
        public string? FilePath { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.FilePath))
            return await ImportSingleFile(settings.FilePath);

        // Folder scan
        var folder = config["DataSlate:KillTeamFolder"] ?? "../kill-teams/";
        if (!Directory.Exists(folder))
        {
            AnsiConsole.MarkupLine($"[yellow]killTeam folder not found: {Markup.Escape(folder)}[/]");
            return 1;
        }

        var files = Directory.GetFiles(folder, "*.json");
        if (files.Length == 0)
        {
            AnsiConsole.MarkupLine("[dim]No JSON files found in killTeam folder.[/]");
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

        AnsiConsole.MarkupLine($"[green]Imported {success} of {files.Length} killTeam file(s).[/]");
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
        catch (KillTeamValidationException ex)
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
        var json = await File.ReadAllTextAsync(path);
        var team = importer.Import(json);

        // Reuse existing ID so game references remain valid on re-import
        var existing = await killTeams.FindByNameAsync(team.Name);
        if (existing is not null)
            team.Id = existing.Id;

        await killTeams.UpsertAsync(team);
        await operatives.UpsertByTeamAsync(team.Operatives, team.Id);
        foreach (var op in team.Operatives)
            await weapons.UpsertByOperativeAsync(op.Weapons, op.Id);

        var opCount = team.Operatives.Count;
        var wCount = team.Operatives.Sum(o => o.Weapons.Count);
        AnsiConsole.MarkupLine($"[green]Imported '{Markup.Escape(team.Name)}' — {opCount} operatives, {wCount} weapons.[/]");
    }
}
