using System.Text.Json;
using FluentAssertions;

namespace KillTeam.TeamExtractor.Tests;

/// <summary>
/// Validates the committed <c>teams/*.json</c> files without running the PDF extractor.
/// These tests have no external tool dependencies and run anywhere (CI, fresh clones).
/// </summary>
public class TeamJsonTests
{
    private static readonly string TeamsRoot = ResolveTeamsRoot();

    private static string ResolveTeamsRoot()
    {
        var dir = AppContext.BaseDirectory;

        while (dir != null)
        {
            var candidate = Path.Combine(dir, "teams");

            if (Directory.Exists(candidate) && Directory.GetFiles(candidate, "*.json").Length > 0)
            {
                return candidate;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not find teams/ directory with JSON files.");
    }

    private static JsonDocument LoadTeam(string slug)
    {
        var path = Path.Combine(TeamsRoot, $"{slug}.json");
        File.Exists(path).Should().BeTrue($"teams/{slug}.json should exist in the repo");

        return JsonDocument.Parse(File.ReadAllText(path));
    }

    // ─── Schema completeness ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("angels-of-death")]
    [InlineData("blades-of-khaine")]
    [InlineData("corsair-voidscarred")]
    [InlineData("nemesis-claw")]
    [InlineData("plague-marines")]
    [InlineData("void-dancer-troupe")]
    public void When_team_json_loaded_then_has_all_required_top_level_fields(string slug)
    {
        using var doc = LoadTeam(slug);
        var root = doc.RootElement;

        root.TryGetProperty("id", out _).Should().BeTrue($"{slug}.json must have 'id'");
        root.TryGetProperty("name", out _).Should().BeTrue($"{slug}.json must have 'name'");
        root.TryGetProperty("faction", out _).Should().BeTrue($"{slug}.json must have 'faction'");
        root.TryGetProperty("operatives", out var operatives).Should().BeTrue($"{slug}.json must have 'operatives'");
        operatives.GetArrayLength().Should().BeGreaterThan(0, $"{slug} should have at least one operative");
    }

    [Theory]
    [InlineData("angels-of-death")]
    [InlineData("blades-of-khaine")]
    [InlineData("corsair-voidscarred")]
    [InlineData("nemesis-claw")]
    [InlineData("plague-marines")]
    [InlineData("void-dancer-troupe")]
    public void When_team_json_loaded_then_has_faction_rules_and_ploys(string slug)
    {
        using var doc = LoadTeam(slug);
        var root = doc.RootElement;

        root.TryGetProperty("factionRules", out var factionRules).Should().BeTrue();
        factionRules.GetArrayLength().Should().BeGreaterThan(0, $"{slug} should have faction rules");

        root.TryGetProperty("strategyPloys", out var strategy).Should().BeTrue();
        strategy.GetArrayLength().Should().BeGreaterThan(0, $"{slug} should have strategy ploys");

        root.TryGetProperty("firefightPloys", out var firefight).Should().BeTrue();
        firefight.GetArrayLength().Should().BeGreaterThan(0, $"{slug} should have firefight ploys");
    }

    [Theory]
    [InlineData("angels-of-death")]
    [InlineData("blades-of-khaine")]
    [InlineData("corsair-voidscarred")]
    [InlineData("nemesis-claw")]
    [InlineData("plague-marines")]
    [InlineData("void-dancer-troupe")]
    public void When_team_json_loaded_then_equipment_items_have_descriptions(string slug)
    {
        using var doc = LoadTeam(slug);
        var root = doc.RootElement;

        root.TryGetProperty("equipment", out var equipment).Should().BeTrue();
        equipment.GetArrayLength().Should().BeGreaterThan(0, $"{slug} should have equipment items");

        foreach (var item in equipment.EnumerateArray())
        {
            item.TryGetProperty("description", out var desc).Should().BeTrue();
            desc.GetString().Should().NotBeNullOrWhiteSpace(
                because: $"Equipment '{item.GetProperty("name").GetString()}' in {slug} should have a description");
        }
    }

    [Theory]
    [InlineData("angels-of-death")]
    [InlineData("blades-of-khaine")]
    [InlineData("corsair-voidscarred")]
    [InlineData("nemesis-claw")]
    [InlineData("plague-marines")]
    [InlineData("void-dancer-troupe")]
    public void When_team_json_loaded_then_all_operatives_have_keywords(string slug)
    {
        using var doc = LoadTeam(slug);

        foreach (var op in doc.RootElement.GetProperty("operatives").EnumerateArray())
        {
            var name = op.GetProperty("name").GetString();

            op.TryGetProperty("keywords", out var keywords).Should().BeTrue($"Operative {name} must have keywords");
            keywords.GetArrayLength().Should().BeGreaterThan(0, $"Operative {name} in {slug} should have keywords");

            op.TryGetProperty("primaryKeyword", out var pk).Should().BeTrue();
            pk.GetString().Should().NotBeNullOrWhiteSpace($"Operative {name} in {slug} should have a primaryKeyword");
        }
    }

    // ─── Known-value assertions ───────────────────────────────────────────────────

    [Fact]
    public void When_angels_of_death_loaded_then_space_marine_captain_has_correct_keywords()
    {
        using var doc = LoadTeam("angels-of-death");
        var captain = doc.RootElement
            .GetProperty("operatives")
            .EnumerateArray()
            .Single(o => o.GetProperty("name").GetString()!.Contains("Captain"));

        captain.GetProperty("primaryKeyword").GetString().Should().Be("Angel Of Death");

        var keywords = captain.GetProperty("keywords")
            .EnumerateArray()
            .Select(k => k.GetString())
            .ToList();

        keywords.Should().Contain("Imperium");
        keywords.Should().Contain("Adeptus Astartes");
        keywords.Should().Contain("Leader");
        keywords.Should().Contain("Space Marine Captain");
    }

    [Fact]
    public void When_void_dancer_troupe_loaded_then_shadowseer_has_one_ap_abilities()
    {
        using var doc = LoadTeam("void-dancer-troupe");
        var shadowseer = doc.RootElement
            .GetProperty("operatives")
            .EnumerateArray()
            .Single(o => o.GetProperty("name").GetString()!.Contains("Shadowseer"));

        var abilities = shadowseer.GetProperty("abilities").EnumerateArray().ToList();

        var mirrorOfMinds = abilities.SingleOrDefault(a => a.GetProperty("name").GetString() == "Mirror Of Minds");
        mirrorOfMinds.ValueKind.Should().NotBe(JsonValueKind.Undefined, because: "Mirror Of Minds is a 1AP action on the Shadowseer datacard");
        mirrorOfMinds.GetProperty("apCost").GetInt32().Should().Be(1);

        var fogOfDreams = abilities.SingleOrDefault(a => a.GetProperty("name").GetString() == "Fog Of Dreams");
        fogOfDreams.ValueKind.Should().NotBe(JsonValueKind.Undefined, because: "Fog Of Dreams is a 1AP action on the Shadowseer datacard");
        fogOfDreams.GetProperty("apCost").GetInt32().Should().Be(1);
    }

    [Fact]
    public void When_void_dancer_troupe_loaded_then_death_jester_has_humbling_cruelty()
    {
        using var doc = LoadTeam("void-dancer-troupe");
        var deathJester = doc.RootElement
            .GetProperty("operatives")
            .EnumerateArray()
            .Single(o => o.GetProperty("name").GetString()!.Contains("Death Jester"));

        var weaponRules = deathJester.GetProperty("weaponRules").EnumerateArray().ToList();

        weaponRules.Should().Contain(
            r => r.GetProperty("name").GetString() == "Humbling Cruelty",
            because: "Humbling Cruelty is a custom weapon rule on the Death Jester datacard");
    }
}
