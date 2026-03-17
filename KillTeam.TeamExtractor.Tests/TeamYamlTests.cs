using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Json.Schema;
using YamlDotNet.RepresentationModel;

namespace KillTeam.TeamExtractor.Tests;

/// <summary>
/// Validates the committed <c>teams/*.yaml</c> files without running the PDF extractor.
/// These tests have no external tool dependencies and run anywhere (CI, fresh clones).
/// </summary>
public class TeamYamlTests
{
    private static readonly string TeamsRoot = ResolveTeamsRoot();

    private static readonly JsonSchema TeamSchema = LoadSchema();

    private static string ResolveTeamsRoot()
    {
        var dir = AppContext.BaseDirectory;

        while (dir != null)
        {
            var candidate = Path.Combine(dir, "teams");

            if (Directory.Exists(candidate) && Directory.GetFiles(candidate, "*.yaml").Length > 0)
            {
                return candidate;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not find teams/ directory with YAML files.");
    }

    private static JsonDocument LoadTeam(string slug)
    {
        var path = Path.Combine(TeamsRoot, $"{slug}.yaml");
        File.Exists(path).Should().BeTrue($"teams/{slug}.yaml should exist in the repo");

        var json = YamlToJsonString(File.ReadAllText(path));
        return JsonDocument.Parse(json);
    }

    // ─── Schema completeness ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("angels-of-death")]
    [InlineData("blades-of-khaine")]
    [InlineData("corsair-voidscarred")]
    [InlineData("nemesis-claw")]
    [InlineData("plague-marines")]
    [InlineData("void-dancer-troupe")]
    public void When_team_yaml_loaded_then_has_all_required_top_level_fields(string slug)
    {
        using var doc = LoadTeam(slug);
        var root = doc.RootElement;

        root.TryGetProperty("id", out _).Should().BeTrue($"{slug}.yaml must have 'id'");
        root.TryGetProperty("name", out _).Should().BeTrue($"{slug}.yaml must have 'name'");
        root.TryGetProperty("grandFaction", out _).Should().BeTrue($"{slug}.yaml must have 'grandFaction'");
        root.TryGetProperty("faction", out _).Should().BeTrue($"{slug}.yaml must have 'faction'");
        root.TryGetProperty("datacards", out var datacards).Should().BeTrue($"{slug}.yaml must have 'datacards'");
        datacards.GetArrayLength().Should().BeGreaterThan(0, $"{slug} should have at least one datacard");
    }

    [Theory]
    [InlineData("angels-of-death")]
    [InlineData("blades-of-khaine")]
    [InlineData("corsair-voidscarred")]
    [InlineData("nemesis-claw")]
    [InlineData("plague-marines")]
    [InlineData("void-dancer-troupe")]
    public void When_team_yaml_loaded_then_has_faction_rules_and_ploys(string slug)
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
    public void When_team_yaml_loaded_then_equipment_items_have_descriptions(string slug)
    {
        using var doc = LoadTeam(slug);
        var root = doc.RootElement;

        // At least factionEquipment or universalEquipment must be present
        var hasFaction = root.TryGetProperty("factionEquipment", out var factionEquipment);
        var hasUniversal = root.TryGetProperty("universalEquipment", out var universalEquipment);
        (hasFaction || hasUniversal).Should().BeTrue($"{slug} should have factionEquipment or universalEquipment");

        if (hasFaction)
        {
            factionEquipment.GetArrayLength().Should().BeGreaterThan(0, $"{slug} should have faction equipment items");

            foreach (var item in factionEquipment.EnumerateArray())
            {
                item.TryGetProperty("text", out var desc).Should().BeTrue();
                desc.GetString().Should().NotBeNullOrWhiteSpace(
                    because: $"Faction equipment '{item.GetProperty("name").GetString()}' in {slug} should have a text");
            }
        }

        if (hasUniversal)
        {
            foreach (var item in universalEquipment.EnumerateArray())
            {
                item.TryGetProperty("text", out var desc).Should().BeTrue();
                desc.GetString().Should().NotBeNullOrWhiteSpace(
                    because: $"Universal equipment '{item.GetProperty("name").GetString()}' in {slug} should have a text");
            }
        }
    }

    [Theory]
    [InlineData("angels-of-death")]
    [InlineData("blades-of-khaine")]
    [InlineData("corsair-voidscarred")]
    [InlineData("nemesis-claw")]
    [InlineData("plague-marines")]
    [InlineData("void-dancer-troupe")]
    public void When_team_yaml_loaded_then_all_datacards_have_keywords(string slug)
    {
        using var doc = LoadTeam(slug);

        foreach (var datacard in doc.RootElement.GetProperty("datacards").EnumerateArray())
        {
            var name = datacard.GetProperty("name").GetString();

            datacard.TryGetProperty("keywords", out var keywords).Should().BeTrue($"Datacard {name} must have keywords");
            keywords.GetArrayLength().Should().BeGreaterThan(0, $"Datacard {name} in {slug} should have keywords");

            datacard.TryGetProperty("primaryKeyword", out var pk).Should().BeTrue();
            pk.GetString().Should().NotBeNullOrWhiteSpace($"Datacard {name} in {slug} should have a primaryKeyword");
        }
    }

    // ─── Known-value assertions ───────────────────────────────────────────────────

    [Fact]
    public void When_angels_of_death_loaded_then_space_marine_captain_has_correct_keywords()
    {
        using var doc = LoadTeam("angels-of-death");
        var captain = doc.RootElement
            .GetProperty("datacards")
            .EnumerateArray()
            .Single(o => o.GetProperty("name").GetString()!.Contains("Captain"));

        captain.GetProperty("primaryKeyword").GetString().Should().Be("Angel of Death");

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
    public void When_angels_of_death_loaded_then_assault_intercessor_sergeant_has_both_abilities()
    {
        using var doc = LoadTeam("angels-of-death");
        var sergeant = doc.RootElement
            .GetProperty("datacards")
            .EnumerateArray()
            .Single(o => o.GetProperty("name").GetString() == "Assault Intercessor Sergeant");

        var abilities = sergeant.GetProperty("abilities").EnumerateArray().ToList();

        abilities.Should().Contain(
            a => a.GetProperty("name").GetString() == "Doctrine Warfare",
            because: "Doctrine Warfare is a left-column ability on the Assault Intercessor Sergeant back card");

        var doctrine = abilities.Single(a => a.GetProperty("name").GetString() == "Doctrine Warfare");
        doctrine.GetProperty("text").GetString().Should()
            .Contain("Combat Doctrine", because: "Doctrine Warfare text includes Combat Doctrine strategy ploy references");

        abilities.Should().Contain(
            a => a.GetProperty("name").GetString() == "Chapter Veteran",
            because: "Chapter Veteran is a right-column ability on the Assault Intercessor Sergeant back card");

        var chapterVeteran = abilities.Single(a => a.GetProperty("name").GetString() == "Chapter Veteran");
        chapterVeteran.GetProperty("text").GetString().Should()
            .Contain("**CHAPTER TACTIC**", because: "Chapter Veteran text describes selecting an extra CHAPTER TACTIC, which StructureToMarkdown converts to bold preserving original capitalisation");
    }

    [Fact]
    public void When_weapons_loaded_then_dmg_is_structured_object()
    {
        using var doc = LoadTeam("angels-of-death");
        var firstWeapon = doc.RootElement
            .GetProperty("datacards")
            .EnumerateArray()
            .First()
            .GetProperty("weapons")
            .EnumerateArray()
            .First();

        var dmg = firstWeapon.GetProperty("dmg");
        dmg.TryGetProperty("normal", out var normal).Should().BeTrue(because: "dmg must have a 'normal' field");
        dmg.TryGetProperty("crit", out var crit).Should().BeTrue(because: "dmg must have a 'crit' field");
        normal.GetInt32().Should().BeGreaterThanOrEqualTo(0);
        crit.GetInt32().Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void When_weapons_loaded_then_weapon_rules_is_array_when_present()
    {
        using var doc = LoadTeam("angels-of-death");

        var allWeapons = doc.RootElement
            .GetProperty("datacards")
            .EnumerateArray()
            .SelectMany(datacard => datacard.GetProperty("weapons").EnumerateArray())
            .ToList();

        foreach (var weapon in allWeapons)
        {
            // weaponRules is optional — only check it when the property is present
            if (weapon.TryGetProperty("weaponRules", out var rules))
            {
                rules.ValueKind.Should().Be(JsonValueKind.Array,
                    because: $"weaponRules on '{weapon.GetProperty("name").GetString()}' must be a JSON array");
            }
        }
    }

    [Fact]
    public void When_weapon_with_no_rules_has_no_weaponRules_property()
    {
        // weaponRules is omitted entirely when empty — never an empty [] in the YAML
        using var doc = LoadTeam("angels-of-death");

        var allWeapons = doc.RootElement
            .GetProperty("datacards")
            .EnumerateArray()
            .SelectMany(datacard => datacard.GetProperty("weapons").EnumerateArray())
            .ToList();

        foreach (var weapon in allWeapons)
        {
            if (weapon.TryGetProperty("weaponRules", out var rules))
            {
                rules.GetArrayLength().Should().BeGreaterThan(0,
                    because: $"weaponRules on '{weapon.GetProperty("name").GetString()}' must not be an empty array — omit the property instead when there are no rules");
            }
        }
    }

    [Fact]
    public void When_void_dancer_troupe_loaded_then_shadowseer_abilities_are_passive_only()
    {
        using var doc = LoadTeam("void-dancer-troupe");
        var shadowseer = doc.RootElement
            .GetProperty("datacards")
            .EnumerateArray()
            .Single(o => o.GetProperty("name").GetString()!.Contains("Shadowseer"));

        // After the abilities/specialActions split, abilities must contain only passive rules (no apCost)
        if (shadowseer.TryGetProperty("abilities", out var abilities))
        {
            foreach (var ability in abilities.EnumerateArray())
            {
                ability.TryGetProperty("apCost", out _).Should().BeFalse(
                    because: "abilities must be passive — apCost belongs in specialActions");
            }
        }
    }

    [Fact]
    public void When_shadowseer_has_special_actions_then_mirror_of_minds_and_fog_of_dreams_are_included()
    {
        using var doc = LoadTeam("void-dancer-troupe");
        var shadowseer = doc.RootElement
            .GetProperty("datacards")
            .EnumerateArray()
            .Single(o => o.GetProperty("name").GetString()!.Contains("Shadowseer"));

        shadowseer.TryGetProperty("specialActions", out var specialActions)
            .Should().BeTrue(because: "Shadowseer has 1AP special actions");

        var actions = specialActions.EnumerateArray().ToList();

        var mirrorOfMinds = actions.SingleOrDefault(a => a.GetProperty("name").GetString() == "Mirror of Minds");
        mirrorOfMinds.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            because: "Mirror of Minds is a 1AP action on the Shadowseer datacard");
        mirrorOfMinds.GetProperty("apCost").GetInt32().Should().Be(1);

        var fogOfDreams = actions.SingleOrDefault(a => a.GetProperty("name").GetString() == "Fog of Dreams");
        fogOfDreams.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            because: "Fog of Dreams is a 1AP action on the Shadowseer datacard");
        fogOfDreams.GetProperty("apCost").GetInt32().Should().Be(1);
    }

    [Fact]
    public void When_angels_of_death_chapter_tactics_then_numbered_list_in_markdown()
    {
        using var doc = LoadTeam("angels-of-death");

        var chapterTactics = doc.RootElement
            .GetProperty("factionRules")
            .EnumerateArray()
            .Single(r => r.GetProperty("name").GetString() == "Chapter Tactics");

        var text = chapterTactics.GetProperty("text").GetString()!;

        text.Should().Contain("1. **AGGRESSIVE**",
            because: "Chapter Tactics numbered list item 1 should be Markdown bold preserving ALL-CAPS");
        text.Should().Contain("2. **DUELLER**",
            because: "Chapter Tactics numbered list item 2 should be Markdown bold preserving ALL-CAPS");
        text.Should().Contain("3. **RESOLUTE**",
            because: "Chapter Tactics numbered list item 3 should be Markdown bold preserving ALL-CAPS");
    }

    [Fact]
    public void When_void_dancer_troupe_loaded_then_death_jester_has_humbling_cruelty()
    {
        using var doc = LoadTeam("void-dancer-troupe");
        var deathJester = doc.RootElement
            .GetProperty("datacards")
            .EnumerateArray()
            .Single(o => o.GetProperty("name").GetString()!.Contains("Death Jester"));

        var specialRules = deathJester.GetProperty("specialRules").EnumerateArray().ToList();

        specialRules.Should().Contain(
            r => r.GetProperty("name").GetString() == "Humbling Cruelty",
            because: "Humbling Cruelty is a custom weapon rule on the Death Jester datacard");
    }

    // ─── 8 PDFs completeness ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("angels-of-death")]
    [InlineData("blades-of-khaine")]
    [InlineData("corsair-voidscarred")]
    [InlineData("nemesis-claw")]
    [InlineData("plague-marines")]
    [InlineData("void-dancer-troupe")]
    public void When_all_8_pdfs_captured_then_all_fields_present(string slug)
    {
        using var doc = LoadTeam(slug);
        var root = doc.RootElement;

        // datacards has items
        root.TryGetProperty("datacards", out var datacards).Should().BeTrue($"{slug} must have 'datacards'");
        datacards.GetArrayLength().Should().BeGreaterThan(0, $"{slug} datacards must not be empty");

        // at least one equipment list has items
        var hasFaction = root.TryGetProperty("factionEquipment", out var factionEquip)
                         && factionEquip.GetArrayLength() > 0;
        var hasUniversal = root.TryGetProperty("universalEquipment", out var universalEquip)
                           && universalEquip.GetArrayLength() > 0;
        (hasFaction || hasUniversal).Should().BeTrue($"{slug} must have at least one equipment list with items");

        // factionRules has items
        root.TryGetProperty("factionRules", out var factionRules).Should().BeTrue($"{slug} must have 'factionRules'");
        factionRules.GetArrayLength().Should().BeGreaterThan(0, $"{slug} factionRules must not be empty");

        // firefightPloys has items
        root.TryGetProperty("firefightPloys", out var firefightPloys).Should().BeTrue($"{slug} must have 'firefightPloys'");
        firefightPloys.GetArrayLength().Should().BeGreaterThan(0, $"{slug} firefightPloys must not be empty");

        // operativeSelection is present
        root.TryGetProperty("operativeSelection", out _).Should().BeTrue($"{slug} must have 'operativeSelection'");

        // strategyPloys has items
        root.TryGetProperty("strategyPloys", out var strategyPloys).Should().BeTrue($"{slug} must have 'strategyPloys'");
        strategyPloys.GetArrayLength().Should().BeGreaterThan(0, $"{slug} strategyPloys must not be empty");

        // supplementaryInfo is not empty
        root.TryGetProperty("supplementaryInformation", out var suppInfo).Should().BeTrue($"{slug} must have 'supplementaryInformation'");
        suppInfo.GetString().Should().NotBeNullOrWhiteSpace($"{slug} supplementaryInformation must not be empty");
    }

    // ─── Schema validation ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("angels-of-death")]
    [InlineData("blades-of-khaine")]
    [InlineData("corsair-voidscarred")]
    [InlineData("nemesis-claw")]
    [InlineData("plague-marines")]
    [InlineData("void-dancer-troupe")]
    public void When_team_yaml_validated_against_schema_then_no_errors(string slug)
    {
        using var doc = LoadTeam(slug);

        var result = TeamSchema.Evaluate(
            doc.RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List });

        var errors = (result.Details ?? [])
            .Where(d => !d.IsValid)
            .Select(d => $"  {d.InstanceLocation}: {string.Join("; ", d.Errors?.Select(e => $"{e.Key}={e.Value}") ?? [])}")
            .ToList();

        errors.Should().BeEmpty(because: $"{slug}.yaml should conform to team.schema.yaml");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a YAML string to a JSON string for use with <see cref="JsonDocument"/> and
    /// <see cref="JsonSchema"/>. Uses YamlDotNet's RepresentationModel to correctly infer
    /// boolean, integer, and null types from plain (unquoted) YAML scalars, while preserving
    /// quoted strings exactly. This handles JSON Schema YAML (with <c>false</c> booleans) and
    /// team YAML (with all strings quoted where ambiguous) correctly.
    /// </summary>
    private static string YamlToJsonString(string yamlText)
    {
        var yaml = new YamlStream();
        yaml.Load(new StringReader(yamlText));

        if (yaml.Documents.Count == 0)
        {
            return "null";
        }

        return YamlNodeToJsonNode(yaml.Documents[0].RootNode)?.ToJsonString() ?? "null";
    }

    private static JsonNode? YamlNodeToJsonNode(YamlNode node)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
            {
                var obj = new JsonObject();

                foreach (var entry in mapping.Children)
                {
                    var key = ((YamlScalarNode)entry.Key).Value ?? string.Empty;
                    obj[key] = YamlNodeToJsonNode(entry.Value);
                }

                return obj;
            }

            case YamlSequenceNode sequence:
            {
                var arr = new JsonArray();

                foreach (var item in sequence.Children)
                {
                    arr.Add(YamlNodeToJsonNode(item));
                }

                return arr;
            }

            case YamlScalarNode scalar:
            {
                var value = scalar.Value;
                var style = scalar.Style;

                // Plain (unquoted) scalars get full type inference
                if (style is YamlDotNet.Core.ScalarStyle.Plain or YamlDotNet.Core.ScalarStyle.Any)
                {
                    if (string.IsNullOrEmpty(value) || value is "null" or "~")
                    {
                        return null;
                    }

                    if (value is "true")
                    {
                        return JsonValue.Create(true);
                    }

                    if (value is "false")
                    {
                        return JsonValue.Create(false);
                    }

                    if (long.TryParse(value, out var l))
                    {
                        return JsonValue.Create(l);
                    }

                    if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                    {
                        return JsonValue.Create(d);
                    }
                }

                // All quoted scalars and unrecognised plain scalars → string
                return JsonValue.Create(value ?? string.Empty);
            }

            default:
                return null;
        }
    }

    private static string ResolveSchemaRoot()
    {
        var dir = AppContext.BaseDirectory;

        while (dir != null)
        {
            var candidate = Path.Combine(dir, "schema");

            if (Directory.Exists(candidate) && Directory.GetFiles(candidate, "*.yaml").Length > 0)
            {
                return candidate;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not find schema/ directory.");
    }

    private static JsonSchema LoadSchema()
    {
        var schemaPath = Path.Combine(ResolveSchemaRoot(), "team.schema.yaml");
        var schemaJson = YamlToJsonString(File.ReadAllText(schemaPath));
        return JsonSchema.FromText(schemaJson);
    }
}


