using KillTeam.TeamExtractor.Services;
using VerifyXunit;

namespace KillTeam.TeamExtractor.Tests;

/// <summary>
/// Snapshot tests that re-extract teams from the GW PDF reference files and compare
/// the output against approved <c>.verified.yaml</c> baselines.
///
/// These tests require the <c>references/kill-teams/</c> directory to be present.
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

    private static void SkipIfPreconditionsMissing()
    {
        Skip.If(ReferencesRoot == null, "references/kill-teams/ directory not found — skipping extraction tests");
    }

    [SkippableTheory]
    [InlineData("Angels of Death")]
    [InlineData("Blades of Khaine")]
    [InlineData("Corsair Voidscarred")]
    [InlineData("Nemesis Claw")]
    [InlineData("Plague Marines")]
    [InlineData("Void-Dancer Troupe")]
    public Task When_team_extracted_then_yaml_matches_snapshot(string teamFolderName)
    {
        SkipIfPreconditionsMissing();

        var teamFolder = Path.Combine(ReferencesRoot!, teamFolderName);
        var extractor = new PdfTeamExtractor(new PdfWeaponTypeDetector());
        var team = extractor.Extract(teamFolderName, teamFolder);

        return Verify(team.ToYaml().TrimStart('\uFEFF'), extension: "yaml")
            .UseDirectory("snapshots")
            .UseParameters(teamFolderName);
    }
}
