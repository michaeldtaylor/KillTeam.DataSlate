using System.Text.Json;
using System.Text.Json.Serialization;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Services;

namespace KillTeam.DataSlate.Infrastructure.Services;

public class TeamJsonImporter
{
    private static readonly Guid OperativeNs = new("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

    /// <summary>Creates a deterministic UUID v5 from a namespace GUID and a name string.</summary>
    private static Guid CreateVersion5(Guid namespaceId, string name)
    {
        var nsBytes = namespaceId.ToByteArray();
        // Guid.ToByteArray() is mixed-endian; convert to network byte order for RFC 4122
        Array.Reverse(nsBytes, 0, 4);
        Array.Reverse(nsBytes, 4, 2);
        Array.Reverse(nsBytes, 6, 2);

        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        var input = new byte[nsBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(nsBytes, 0, input, 0, nsBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, input, nsBytes.Length, nameBytes.Length);

        var hash = System.Security.Cryptography.SHA1.HashData(input);

        hash[6] = (byte)((hash[6] & 0x0F) | 0x50); // version 5
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // variant RFC 4122

        // Convert first 16 bytes back from network byte order to Windows mixed-endian
        Array.Reverse(hash, 0, 4);
        Array.Reverse(hash, 4, 2);
        Array.Reverse(hash, 6, 2);

        return new Guid(hash[..16]);
    }

    public KillTeam.DataSlate.Domain.Models.Team Import(string json)
    {
        JsonTeam? jsonTeam;
        try
        {
            jsonTeam = JsonSerializer.Deserialize<JsonTeam>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            throw new TeamValidationException($"Invalid JSON: {ex.Message}");
        }

        if (jsonTeam is null)
        {
            throw new TeamValidationException("Empty or null JSON.");
        }

        if (string.IsNullOrWhiteSpace(jsonTeam.Name))
        {
            throw new TeamValidationException("Missing required field: 'name'.");
        }

        if (string.IsNullOrWhiteSpace(jsonTeam.Id))
        {
            throw new TeamValidationException("Missing required field: 'id'.");
        }

        if (jsonTeam.Operatives is null || jsonTeam.Operatives.Count == 0)
        {
            throw new TeamValidationException("Missing required field: 'operatives' (empty or absent).");
        }

        var team = new KillTeam.DataSlate.Domain.Models.Team
        {
            Id = jsonTeam.Id.Trim(),
            Name = jsonTeam.Name.Trim(),
            Faction = jsonTeam.Faction?.Trim() ?? string.Empty,
            Operatives = []
        };

        for (var opIdx = 0; opIdx < jsonTeam.Operatives.Count; opIdx++)
        {
            var jo = jsonTeam.Operatives[opIdx];
            var opPrefix = $"operatives[{opIdx}]";

            if (string.IsNullOrWhiteSpace(jo.Name))
            {
                throw new TeamValidationException($"Missing required field: '{opPrefix}.name'.");
            }

            if (jo.Stats is null)
            {
                throw new TeamValidationException($"Missing required field: '{opPrefix}.stats'.");
            }

            ValidateStat(jo.Stats.Move, $"{opPrefix}.stats.move");
            ValidateStat(jo.Stats.Apl, $"{opPrefix}.stats.apl");
            ValidateStat(jo.Stats.Wounds, $"{opPrefix}.stats.wounds");

            if (jo.Stats.Save is null || jo.Stats.Save.Value.ValueKind == JsonValueKind.Undefined)
                throw new TeamValidationException($"Missing required field: '{opPrefix}.stats.save'.");

            var saveRaw = jo.Stats.Save.Value.ValueKind == JsonValueKind.Number
                ? jo.Stats.Save.Value.GetInt32().ToString()
                : jo.Stats.Save.Value.GetString() ?? "0";

            var operativeType = jo.OperativeType?.Trim() ?? jo.Name.Trim();
            var operative = new Operative
            {
                Id = CreateVersion5(OperativeNs, $"{team.Name}/{operativeType}"),
                TeamId = team.Id,
                Name = jo.Name.Trim(),
                OperativeType = operativeType,
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
                    {
                        throw new TeamValidationException($"Missing required field: '{wPrefix}.name'.");
                    }

                    if (string.IsNullOrWhiteSpace(jw.Type))
                    {
                        throw new TeamValidationException($"Missing required field: '{wPrefix}.type'.");
                    }

                    if (jw.Atk is null)
                    {
                        throw new TeamValidationException($"Missing required field: '{wPrefix}.atk'.");
                    }

                    if (string.IsNullOrWhiteSpace(jw.Hit))
                    {
                        throw new TeamValidationException($"Missing required field: '{wPrefix}.hit'.");
                    }

                    if (string.IsNullOrWhiteSpace(jw.Dmg))
                    {
                        throw new TeamValidationException($"Missing required field: '{wPrefix}.dmg'.");
                    }

                    var (normalDmg, critDmg) = ParseDamage(jw.Dmg, wPrefix);
                    if (!Enum.TryParse<WeaponType>(jw.Type, ignoreCase: true, out var wt))
                        throw new TeamValidationException($"Invalid weapon type '{jw.Type}' at '{wPrefix}.type'. Expected 'Ranged' or 'Melee'.");

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
        {
            throw new TeamValidationException($"Missing required field: '{fieldPath}'.");
        }
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
            throw new TeamValidationException(
                $"Invalid damage format '{raw}' at '{fieldPath}.dmg'. Expected 'N/C' e.g. '3/4'.");
        return (n, c);
    }
}

// ─── Internal JSON DTOs ───────────────────────────────────────────────────────

internal class JsonTeam
{
    [JsonPropertyName("id")]          public string? Id { get; set; }
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
