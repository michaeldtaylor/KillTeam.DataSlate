using System.Text.Json;
using System.Text.Json.Serialization;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Services;

namespace KillTeam.DataSlate.Console.Services;

public class RosterJsonImporter
{
    public KillTeam.DataSlate.Domain.Models.KillTeam Import(string json)
    {
        JsonRoster? roster;
        try
        {
            roster = JsonSerializer.Deserialize<JsonRoster>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            throw new RosterValidationException($"Invalid JSON: {ex.Message}");
        }

        if (roster is null)
            throw new RosterValidationException("Empty or null JSON.");

        if (string.IsNullOrWhiteSpace(roster.Name))
            throw new RosterValidationException("Missing required field: 'name'.");

        if (roster.Operatives is null || roster.Operatives.Count == 0)
            throw new RosterValidationException("Missing required field: 'operatives' (empty or absent).");

        var team = new KillTeam.DataSlate.Domain.Models.KillTeam
        {
            Id = Guid.NewGuid(),
            Name = roster.Name.Trim(),
            Faction = roster.Faction?.Trim() ?? string.Empty,
            Operatives = []
        };

        for (var opIdx = 0; opIdx < roster.Operatives.Count; opIdx++)
        {
            var jo = roster.Operatives[opIdx];
            var opPrefix = $"operatives[{opIdx}]";

            if (string.IsNullOrWhiteSpace(jo.Name))
                throw new RosterValidationException($"Missing required field: '{opPrefix}.name'.");

            if (jo.Stats is null)
                throw new RosterValidationException($"Missing required field: '{opPrefix}.stats'.");

            ValidateStat(jo.Stats.Move, $"{opPrefix}.stats.move");
            ValidateStat(jo.Stats.Apl, $"{opPrefix}.stats.apl");
            ValidateStat(jo.Stats.Wounds, $"{opPrefix}.stats.wounds");

            if (jo.Stats.Save is null || jo.Stats.Save.Value.ValueKind == JsonValueKind.Undefined)
                throw new RosterValidationException($"Missing required field: '{opPrefix}.stats.save'.");

            var saveRaw = jo.Stats.Save.Value.ValueKind == JsonValueKind.Number
                ? jo.Stats.Save.Value.GetInt32().ToString()
                : jo.Stats.Save.Value.GetString() ?? "0";

            var operative = new Operative
            {
                Id = Guid.NewGuid(),
                KillTeamId = team.Id,
                Name = jo.Name.Trim(),
                OperativeType = jo.OperativeType?.Trim() ?? jo.Name.Trim(),
                Move = jo.Stats.Move!.Value,
                Apl = jo.Stats.Apl!.Value,
                Wounds = jo.Stats.Wounds!.Value,
                Save = ParseStat(saveRaw),
                Equipment = jo.Equipment?.ToArray() ?? []
            };

            if (jo.Weapons is not null)
            {
                for (var wIdx = 0; wIdx < jo.Weapons.Count; wIdx++)
                {
                    var jw = jo.Weapons[wIdx];
                    var wPrefix = $"{opPrefix}.weapons[{wIdx}]";

                    if (string.IsNullOrWhiteSpace(jw.Name))
                        throw new RosterValidationException($"Missing required field: '{wPrefix}.name'.");
                    if (string.IsNullOrWhiteSpace(jw.Type))
                        throw new RosterValidationException($"Missing required field: '{wPrefix}.type'.");
                    if (jw.Atk is null)
                        throw new RosterValidationException($"Missing required field: '{wPrefix}.atk'.");
                    if (string.IsNullOrWhiteSpace(jw.Hit))
                        throw new RosterValidationException($"Missing required field: '{wPrefix}.hit'.");
                    if (string.IsNullOrWhiteSpace(jw.Dmg))
                        throw new RosterValidationException($"Missing required field: '{wPrefix}.dmg'.");

                    var (normalDmg, critDmg) = ParseDamage(jw.Dmg, wPrefix);
                    if (!Enum.TryParse<WeaponType>(jw.Type, ignoreCase: true, out var wt))
                        throw new RosterValidationException($"Invalid weapon type '{jw.Type}' at '{wPrefix}.type'. Expected 'Ranged' or 'Melee'.");

                    operative.Weapons.Add(new Weapon
                    {
                        Id = Guid.NewGuid(),
                        OperativeId = operative.Id,
                        Name = jw.Name.Trim(),
                        Type = wt,
                        Atk = jw.Atk.Value,
                        Hit = ParseStat(jw.Hit),
                        NormalDmg = normalDmg,
                        CriticalDmg = critDmg,
                        SpecialRules = jw.SpecialRules?.Trim() ?? string.Empty
                    });
                }
            }

            team.Operatives.Add(operative);
        }

        return team;
    }

    private static void ValidateStat(object? value, string fieldPath)
    {
        if (value is null)
            throw new RosterValidationException($"Missing required field: '{fieldPath}'.");
    }

    private static int ParseStat(string raw)
    {
        var s = raw.Trim().TrimEnd('+');
        return int.TryParse(s, out var n) ? n : 0;
    }

    private static (int normal, int crit) ParseDamage(string raw, string fieldPath)
    {
        var parts = raw.Trim().Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[0].Trim(), out var n) ||
            !int.TryParse(parts[1].Trim(), out var c))
            throw new RosterValidationException(
                $"Invalid damage format '{raw}' at '{fieldPath}.dmg'. Expected 'N/C' e.g. '3/4'.");
        return (n, c);
    }
}

// ─── Internal JSON DTOs ───────────────────────────────────────────────────────

internal class JsonRoster
{
    [JsonPropertyName("name")]        public string? Name { get; set; }
    [JsonPropertyName("faction")]     public string? Faction { get; set; }
    [JsonPropertyName("operatives")]  public List<JsonOperative>? Operatives { get; set; }
}

internal class JsonOperative
{
    [JsonPropertyName("name")]          public string? Name { get; set; }
    [JsonPropertyName("operativeType")] public string? OperativeType { get; set; }
    [JsonPropertyName("stats")]         public JsonStats? Stats { get; set; }
    [JsonPropertyName("weapons")]       public List<JsonWeapon>? Weapons { get; set; }
    [JsonPropertyName("equipment")]     public List<string>? Equipment { get; set; }
}

internal class JsonStats
{
    [JsonPropertyName("move")]   public int? Move { get; set; }
    [JsonPropertyName("apl")]    public int? Apl { get; set; }
    [JsonPropertyName("wounds")] public int? Wounds { get; set; }
    [JsonPropertyName("save")]   public JsonElement? Save { get; set; }
}

internal class JsonWeapon
{
    [JsonPropertyName("name")]         public string? Name { get; set; }
    [JsonPropertyName("type")]         public string? Type { get; set; }
    [JsonPropertyName("atk")]          public int? Atk { get; set; }
    [JsonPropertyName("hit")]          public string? Hit { get; set; }
    [JsonPropertyName("dmg")]          public string? Dmg { get; set; }
    [JsonPropertyName("specialRules")] public string? SpecialRules { get; set; }
}
