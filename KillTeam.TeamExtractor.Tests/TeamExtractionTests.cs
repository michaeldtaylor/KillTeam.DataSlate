using FluentAssertions;
using KillTeam.TeamExtractor.Models;
using KillTeam.TeamExtractor.Services;
using VerifyXunit;

namespace KillTeam.TeamExtractor.Tests;

/// <summary>
/// Integration tests that extract all 6 teams from their reference PDFs and verify
/// the resulting JSON output matches approved snapshots.
/// </summary>
public class TeamExtractionTests
{
    private static readonly string ReferencesRoot = ResolveReferencesRoot();

    private static string ResolveReferencesRoot()
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

        throw new DirectoryNotFoundException("Could not find references/kill-teams/ directory.");
    }

    private static ExtractedTeam Extract(string teamFolderName)
    {
        var teamFolder = Path.Combine(ReferencesRoot, teamFolderName);
        var extractor = new PdfTeamExtractor(new PdfWeaponTypeDetector());

        return extractor.Extract(teamFolderName, teamFolder);
    }

    // ─── Snapshot tests ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("Angels of Death")]
    [InlineData("Blades of Khaine")]
    [InlineData("Corsair Voidscarred")]
    [InlineData("Nemesis Claw")]
    [InlineData("Plague Marines")]
    [InlineData("Void-Dancer Troupe")]
    public Task When_team_extracted_then_json_matches_snapshot(string teamFolderName)
    {
        var team = Extract(teamFolderName);

        return Verify(team.ToJson(), extension: "json")
            .UseDirectory("snapshots")
            .UseParameters(teamFolderName);
    }

    // ─── Sanity assertion tests ───────────────────────────────────────────────

    [Fact]
    public void When_all_teams_extracted_then_each_has_faction_rules()
    {
        var teamFolderNames = new[]
        {
            "Angels of Death",
            "Blades of Khaine",
            "Corsair Voidscarred",
            "Nemesis Claw",
            "Plague Marines",
            "Void-Dancer Troupe",
        };

        foreach (var name in teamFolderNames)
        {
            var team = Extract(name);

            team.FactionRules.Should().NotBeEmpty(because: $"{name} should have faction rules");
            team.StrategyPloys.Should().NotBeEmpty(because: $"{name} should have strategy ploys");
            team.FirefightPloys.Should().NotBeEmpty(because: $"{name} should have firefight ploys");
        }
    }

    [Fact]
    public void When_all_teams_extracted_then_equipment_has_descriptions()
    {
        var teamFolderNames = new[]
        {
            "Angels of Death",
            "Blades of Khaine",
            "Corsair Voidscarred",
            "Nemesis Claw",
            "Plague Marines",
            "Void-Dancer Troupe",
        };

        foreach (var name in teamFolderNames)
        {
            var team = Extract(name);

            team.Equipment.Should().NotBeEmpty(because: $"{name} should have equipment");

            foreach (var item in team.Equipment)
            {
                item.Description.Should().NotBeNullOrWhiteSpace(
                    because: $"Equipment item '{item.Name}' in {name} should have a description");
            }
        }
    }

    [Fact]
    public void When_space_marine_captain_extracted_then_keywords_are_correct()
    {
        var team = Extract("Angels of Death");
        var captain = team.Operatives.Single(o => o.Name.Contains("Captain"));

        captain.PrimaryKeyword.Should().Be("Angel Of Death");
        captain.Keywords.Should().Contain("Imperium");
        captain.Keywords.Should().Contain("Adeptus Astartes");
        captain.Keywords.Should().Contain("Leader");
        captain.Keywords.Should().Contain("Space Marine Captain");
    }

    [Fact]
    public void When_shadowseer_extracted_then_has_one_ap_abilities()
    {
        var team = Extract("Void-Dancer Troupe");
        var shadowseer = team.Operatives.Single(o => o.Name.Contains("Shadowseer"));

        shadowseer.Abilities.Should().Contain(
            a => a.Name == "Mirror Of Minds" && a.ApCost == 1,
            because: "Mirror Of Minds is a 1AP action on the Shadowseer datacard");

        shadowseer.Abilities.Should().Contain(
            a => a.Name == "Fog Of Dreams" && a.ApCost == 1,
            because: "Fog Of Dreams is a 1AP action on the Shadowseer datacard");
    }

    [Fact]
    public void When_death_jester_extracted_then_has_humbling_cruelty_weapon_rule()
    {
        var team = Extract("Void-Dancer Troupe");
        var deathJester = team.Operatives.Single(o => o.Name.Contains("Death Jester"));

        deathJester.WeaponRules.Should().Contain(
            r => r.Name == "Humbling Cruelty",
            because: "Humbling Cruelty is a custom weapon rule on the Death Jester datacard");
    }

    [Fact]
    public void When_all_operatives_extracted_then_all_have_keywords()
    {
        var teamFolderNames = new[]
        {
            "Angels of Death",
            "Blades of Khaine",
            "Corsair Voidscarred",
            "Nemesis Claw",
            "Plague Marines",
            "Void-Dancer Troupe",
        };

        foreach (var name in teamFolderNames)
        {
            var team = Extract(name);

            foreach (var operative in team.Operatives)
            {
                operative.Keywords.Should().NotBeEmpty(
                    because: $"Operative '{operative.Name}' in {name} should have keywords");

                operative.PrimaryKeyword.Should().NotBeNullOrEmpty(
                    because: $"Operative '{operative.Name}' in {name} should have a primary keyword");
            }
        }
    }
}
