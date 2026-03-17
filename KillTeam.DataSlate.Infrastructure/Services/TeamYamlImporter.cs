using System.Security.Cryptography;
using System.Text;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Services;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace KillTeam.DataSlate.Infrastructure.Services;

/// <summary>Imports a team from the YAML format produced by TeamExtractor.</summary>
public class TeamYamlImporter
{
    private static readonly Guid OperativeNs = new("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>Creates a deterministic UUID v5 from a namespace GUID and a name string.</summary>
    private static Guid CreateVersion5(Guid namespaceId, string name)
    {
        var nsBytes = namespaceId.ToByteArray();
        Array.Reverse(nsBytes, 0, 4);
        Array.Reverse(nsBytes, 4, 2);
        Array.Reverse(nsBytes, 6, 2);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var input = new byte[nsBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(nsBytes, 0, input, 0, nsBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, input, nsBytes.Length, nameBytes.Length);

        var hash = SHA1.HashData(input);

        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        Array.Reverse(hash, 0, 4);
        Array.Reverse(hash, 4, 2);
        Array.Reverse(hash, 6, 2);

        return new Guid(hash[..16]);
    }

    public Team Import(string yaml)
    {
        YamlTeam yamlTeam;
        try
        {
            yamlTeam = Deserializer.Deserialize<YamlTeam>(yaml);
        }
        catch (Exception ex)
        {
            throw new TeamValidationException($"Invalid YAML: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(yamlTeam.Id))
        {
            throw new TeamValidationException("Missing required field: 'id'.");
        }

        if (string.IsNullOrWhiteSpace(yamlTeam.Name))
        {
            throw new TeamValidationException("Missing required field: 'name'.");
        }

        if (yamlTeam.Datacards is null || yamlTeam.Datacards.Count == 0)
        {
            throw new TeamValidationException("Missing required field: 'datacards' (empty or absent).");
        }

        var team = new Team
        {
            Id = yamlTeam.Id.Trim(),
            Name = yamlTeam.Name.Trim(),
            GrandFaction = yamlTeam.GrandFaction?.Trim() ?? string.Empty,
            Faction = yamlTeam.Faction?.Trim() ?? string.Empty,
            FactionRules = MapNamedRules(yamlTeam.FactionRules),
            StrategyPloys = MapNamedRules(yamlTeam.StrategyPloys),
            FirefightPloys = MapNamedRules(yamlTeam.FirefightPloys),
            FactionEquipment = MapEquipment(yamlTeam.FactionEquipment),
            UniversalEquipment = MapEquipment(yamlTeam.UniversalEquipment),
            OperativeSelectionArchetype = yamlTeam.OperativeSelection?.Archetype?.Trim() ?? string.Empty,
            OperativeSelectionText = yamlTeam.OperativeSelection?.Text?.Trim() ?? string.Empty,
            SupplementaryInfo = yamlTeam.SupplementaryInformation?.Trim() ?? string.Empty,
            Operatives = [],
        };

        foreach (var dc in yamlTeam.Datacards)
        {
            if (string.IsNullOrWhiteSpace(dc.Name))
            {
                throw new TeamValidationException("Datacard missing required field: 'name'.");
            }

            var operativeType = dc.OperativeType?.Trim() ?? dc.Name.Trim();
            var operative = new Operative
            {
                Id = CreateVersion5(OperativeNs, $"{team.Name}/{operativeType}"),
                TeamId = team.Id,
                Name = dc.Name.Trim(),
                OperativeType = operativeType,
                PrimaryKeyword = dc.PrimaryKeyword?.Trim() ?? string.Empty,
                Keywords = dc.Keywords?.Select(k => k.Trim()).ToArray() ?? [],
                Move = dc.Stats?.Move ?? 0,
                Apl = dc.Stats?.Apl ?? 0,
                Wounds = dc.Stats?.Wounds ?? 0,
                Save = ParseSave(dc.Stats?.Save ?? "0"),
                Abilities = MapAbilities(dc.Abilities),
                SpecialActions = MapSpecialActions(dc.SpecialActions),
                SpecialRules = MapWeaponRules(dc.SpecialRules),
            };

            if (dc.Weapons is not null)
            {
                foreach (var w in dc.Weapons)
                {
                    if (string.IsNullOrWhiteSpace(w.Name))
                    {
                        continue;
                    }

                    if (!Enum.TryParse<WeaponType>(w.Type, ignoreCase: true, out var wt))
                    {
                        wt = WeaponType.Ranged;
                    }

                    operative.Weapons.Add(new Weapon
                    {
                        Id = Guid.NewGuid(),
                        OperativeId = operative.Id,
                        Name = w.Name.Trim(),
                        Type = wt,
                        Atk = w.Atk,
                        Hit = ParseSave(w.Hit ?? "0"),
                        NormalDmg = w.Dmg?.Normal ?? 0,
                        CriticalDmg = w.Dmg?.Crit ?? 0,
                        SpecialRules = w.WeaponRules is not null
                            ? string.Join(", ", w.WeaponRules)
                            : string.Empty,
                    });
                }
            }

            team.Operatives.Add(operative);
        }

        return team;
    }

    private static int ParseSave(string raw)
    {
        var s = raw.Trim().TrimEnd('+');

        return int.TryParse(s, out var n) ? n : 0;
    }

    private static List<NamedRule> MapNamedRules(List<YamlNamedRule>? rules)
    {
        if (rules is null)
        {
            return [];
        }

        return rules.Select(r => new NamedRule
        {
            Name = r.Name?.Trim() ?? string.Empty,
            Category = r.Category?.Trim(),
            Text = r.Text?.Trim() ?? string.Empty,
        }).ToList();
    }

    private static List<EquipmentItem> MapEquipment(List<YamlEquipmentItem>? items)
    {
        if (items is null)
        {
            return [];
        }

        return items.Select(e => new EquipmentItem
        {
            Name = e.Name?.Trim() ?? string.Empty,
            Text = e.Text?.Trim() ?? string.Empty,
        }).ToList();
    }

    private static List<OperativeAbility> MapAbilities(List<YamlAbility>? abilities)
    {
        if (abilities is null)
        {
            return [];
        }

        return abilities.Select(a => new OperativeAbility
        {
            Name = a.Name?.Trim() ?? string.Empty,
            Text = a.Text?.Trim() ?? string.Empty,
        }).ToList();
    }

    private static List<OperativeSpecialAction> MapSpecialActions(List<YamlSpecialAction>? actions)
    {
        if (actions is null)
        {
            return [];
        }

        return actions.Select(a => new OperativeSpecialAction
        {
            Name = a.Name?.Trim() ?? string.Empty,
            Text = a.Text?.Trim() ?? string.Empty,
            ApCost = a.ApCost,
        }).ToList();
    }

    private static List<OperativeWeaponRule> MapWeaponRules(List<YamlWeaponRule>? rules)
    {
        if (rules is null)
        {
            return [];
        }

        return rules.Select(r => new OperativeWeaponRule
        {
            Name = r.Name?.Trim() ?? string.Empty,
            Text = r.Text?.Trim() ?? string.Empty,
        }).ToList();
    }
}

// ─── YAML DTOs ────────────────────────────────────────────────────────────────

internal class YamlTeam
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? GrandFaction { get; set; }
    public string? Faction { get; set; }
    public List<YamlDatacard>? Datacards { get; set; }
    public List<YamlNamedRule>? FactionRules { get; set; }
    public List<YamlNamedRule>? StrategyPloys { get; set; }
    public List<YamlNamedRule>? FirefightPloys { get; set; }
    public List<YamlEquipmentItem>? FactionEquipment { get; set; }
    public List<YamlEquipmentItem>? UniversalEquipment { get; set; }
    public YamlOperativeSelection? OperativeSelection { get; set; }
    public string? SupplementaryInformation { get; set; }
}

internal class YamlDatacard
{
    public string? Name { get; set; }
    public string? OperativeType { get; set; }
    public string? PrimaryKeyword { get; set; }
    public List<string>? Keywords { get; set; }
    public YamlStats? Stats { get; set; }
    public List<YamlWeapon>? Weapons { get; set; }
    public List<YamlAbility>? Abilities { get; set; }
    public List<YamlSpecialAction>? SpecialActions { get; set; }
    public List<YamlWeaponRule>? SpecialRules { get; set; }
}

internal class YamlStats
{
    public int Move { get; set; }
    public int Apl { get; set; }
    public int Wounds { get; set; }
    public string Save { get; set; } = "0";
}

internal class YamlWeapon
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public int Atk { get; set; }
    public string? Hit { get; set; }
    public YamlDmg? Dmg { get; set; }
    public List<string>? WeaponRules { get; set; }
}

internal class YamlDmg
{
    public int Normal { get; set; }
    public int Crit { get; set; }
}

internal class YamlNamedRule
{
    public string? Name { get; set; }
    public string? Category { get; set; }
    public string? Text { get; set; }
}

internal class YamlEquipmentItem
{
    public string? Name { get; set; }
    public string? Text { get; set; }
}

internal class YamlAbility
{
    public string? Name { get; set; }
    public string? Text { get; set; }
}

internal class YamlSpecialAction
{
    public string? Name { get; set; }
    public string? Text { get; set; }
    public int ApCost { get; set; } = 1;
}

internal class YamlWeaponRule
{
    public string? Name { get; set; }
    public string? Text { get; set; }
}

internal class YamlOperativeSelection
{
    public string? Archetype { get; set; }
    public string? Text { get; set; }
}
