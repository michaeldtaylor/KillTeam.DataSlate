using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KillTeam.TeamExtractor.Models;

/// <summary>The result of extracting a team from PDFs, before writing to JSON.</summary>
public class ExtractedTeam
{
    /// <summary>URL-safe slug derived from the team name, used as the JSON file name and id field.</summary>
    public required string Id { get; init; }

    /// <summary>The human-readable team name.</summary>
    public required string Name { get; init; }

    /// <summary>The team faction name in title case.</summary>
    public required string Faction { get; init; }

    /// <summary>Operatives extracted from the datacards PDF.</summary>
    public required List<ExtractedOperative> Operatives { get; init; }

    /// <summary>Equipment items with descriptions extracted from equipment PDFs.</summary>
    public List<ExtractedEquipmentItem> Equipment { get; init; } = [];

    /// <summary>Faction-specific rules extracted from the Faction Rules PDF.</summary>
    public List<ExtractedRule> FactionRules { get; init; } = [];

    /// <summary>Strategy ploys extracted from the Strategy Ploys PDF.</summary>
    public List<ExtractedRule> StrategyPloys { get; init; } = [];

    /// <summary>Firefight ploys extracted from the Firefight Ploys PDF.</summary>
    public List<ExtractedRule> FirefightPloys { get; init; } = [];

    /// <summary>Operative selection rules, if a selection PDF was found.</summary>
    public ExtractedOperativeSelection? OperativeSelection { get; init; }

    /// <summary>Supplementary information text, if a supplementary PDF was found.</summary>
    public string SupplementaryInfo { get; init; } = "";

    /// <summary>Serialises the team to the JSON format used by the teams/ folder.</summary>
    public string ToJson()
    {
        var root = new JsonObject
        {
            ["$schema"] = "../schema/team.schema.json",
            ["id"] = this.Id,
            ["name"] = this.Name,
            ["faction"] = this.Faction,
            ["factionRules"] = new JsonArray(this.FactionRules
                .Select(r => (JsonNode)new JsonObject
                {
                    ["name"] = r.Name,
                    ["text"] = r.Text,
                })
                .ToArray()),
            ["strategyPloys"] = new JsonArray(this.StrategyPloys
                .Select(r => (JsonNode)new JsonObject
                {
                    ["name"] = r.Name,
                    ["text"] = r.Text,
                })
                .ToArray()),
            ["firefightPloys"] = new JsonArray(this.FirefightPloys
                .Select(r => (JsonNode)new JsonObject
                {
                    ["name"] = r.Name,
                    ["text"] = r.Text,
                })
                .ToArray()),
            ["equipment"] = new JsonArray(this.Equipment
                .Select(e => (JsonNode)new JsonObject
                {
                    ["name"] = e.Name,
                    ["description"] = e.Description,
                })
                .ToArray()),
            ["operatives"] = new JsonArray(this.Operatives
                .Select(op => (JsonNode)new JsonObject
                {
                    ["name"] = op.Name,
                    ["operativeType"] = op.Name,
                    ["primaryKeyword"] = op.PrimaryKeyword,
                    ["keywords"] = new JsonArray(op.Keywords
                        .Select(k => (JsonNode)JsonValue.Create(k)!)
                        .ToArray()),
                    ["stats"] = new JsonObject
                    {
                        ["move"] = op.Move,
                        ["apl"] = op.Apl,
                        ["wounds"] = op.Wounds,
                        ["save"] = op.Save,
                    },
                    ["weapons"] = new JsonArray(op.Weapons
                        .Select(w => (JsonNode)new JsonObject
                        {
                            ["name"] = w.Name,
                            ["type"] = w.Type.ToString(),
                            ["atk"] = w.Atk,
                            ["hit"] = w.Hit,
                            ["dmg"] = w.Dmg,
                            ["specialRules"] = w.SpecialRules,
                        })
                        .ToArray()),
                    ["abilities"] = new JsonArray(op.Abilities
                        .Select(a =>
                        {
                            var obj = new JsonObject
                            {
                                ["name"] = a.Name,
                                ["text"] = a.Text,
                            };

                            if (a.ApCost.HasValue)
                            {
                                obj["apCost"] = a.ApCost.Value;
                            }

                            return (JsonNode)obj;
                        })
                        .ToArray()),
                    ["weaponRules"] = new JsonArray(op.WeaponRules
                        .Select(wr => (JsonNode)new JsonObject
                        {
                            ["name"] = wr.Name,
                            ["text"] = wr.Text,
                        })
                        .ToArray()),
                })
                .ToArray()),
        };

        if (this.OperativeSelection != null)
        {
            root["operativeSelection"] = new JsonObject
            {
                ["archetype"] = this.OperativeSelection.Archetype,
                ["text"] = this.OperativeSelection.Text,
            };
        }

        if (!string.IsNullOrEmpty(this.SupplementaryInfo))
        {
            root["supplementaryInfo"] = this.SupplementaryInfo;
        }

        SanitizeStrings(root);

        return root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    /// <summary>
    /// Walks every JSON string node and strips ASCII control characters (code points 0–31).
    /// pdftotext emits BEL (0x07) for icon markers and BS (0x08) as rendering artefacts;
    /// these must not appear in the output JSON.
    /// </summary>
    private static void SanitizeStrings(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (obj[key] is JsonValue val && val.TryGetValue<string>(out var s))
                    {
                        obj[key] = StripControlChars(s);
                    }
                    else
                    {
                        SanitizeStrings(obj[key]);
                    }
                }

                break;
            }

            case JsonArray arr:
            {
                for (var i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is JsonValue val && val.TryGetValue<string>(out var s))
                    {
                        arr[i] = StripControlChars(s);
                    }
                    else
                    {
                        SanitizeStrings(arr[i]);
                    }
                }

                break;
            }
        }
    }

    private static string StripControlChars(string s)
    {
        return new string(s.Where(c => c >= 32).ToArray()).Trim();
    }
}
