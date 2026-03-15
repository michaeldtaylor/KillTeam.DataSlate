using System.Diagnostics;
using KillTeam.TeamExtractor.Services;
using VerifyXunit;

namespace KillTeam.TeamExtractor.Tests;

/// <summary>
/// Snapshot tests that re-extract teams from the GW PDF reference files and compare
/// the output against approved <c>.verified.json</c> baselines.
///
/// These tests require <c>pdftotext</c> (Poppler) to be on the system PATH.
/// If it is unavailable the tests are skipped rather than failed.
/// </summary>
public class TeamExtractionTests
{
    private static readonly string? ReferencesRoot = TryResolveReferencesRoot();

    private static string? TryResolveReferencesRoot()
    {
        var dir = AppContext.BaseDirectory;

        while (dir != null)
        {
            var candidate = Path.Combine(dir, "references", "kill-teams");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }

    private static bool IsPdfToTextAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pdftotext",
                ArgumentList = { "-v" },
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };

            using var proc = Process.Start(psi);
            proc?.WaitForExit(3000);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void SkipIfPreconditionsMissing()
    {
        Skip.If(ReferencesRoot == null, "references/kill-teams/ directory not found — skipping extraction tests");
        Skip.IfNot(IsPdfToTextAvailable(), "pdftotext (Poppler) not found on PATH — skipping extraction tests");
    }

    [SkippableTheory]
    [InlineData("Angels of Death")]
    [InlineData("Blades of Khaine")]
    [InlineData("Corsair Voidscarred")]
    [InlineData("Nemesis Claw")]
    [InlineData("Plague Marines")]
    [InlineData("Void-Dancer Troupe")]
    public Task When_team_extracted_then_json_matches_snapshot(string teamFolderName)
    {
        SkipIfPreconditionsMissing();

        var teamFolder = Path.Combine(ReferencesRoot!, teamFolderName);
        var extractor = new PdfTeamExtractor(new PdfWeaponTypeDetector());
        var team = extractor.Extract(teamFolderName, teamFolder);

        return Verify(team.ToJson(), extension: "json")
            .UseDirectory("snapshots")
            .UseParameters(teamFolderName);
    }
}
