using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Services;

namespace KillTeam.DataSlate.Console.TestData;

/// <summary>
/// Builds an in-memory test team used only in Simulate mode.
/// The operative carries one weapon per weapon rule group so every rule can be exercised.
/// </summary>
public static class TestTeamFactory
{
    public const string TeamId = "00000000-0000-0000-ffff-000000000001";
    public const string TeamName = "Test Operatives (All Rules)";

    public static Team Create()
    {
        var operativeId = Guid.Parse("00000000-0000-0000-ffff-000000000002");

        var operative = new Operative
        {
            Id = operativeId,
            TeamId = TeamId,
            Name = "Test Operative",
            OperativeType = "Test",
            PrimaryKeyword = "Test",
            Keywords = ["Test"],
            Move = 6,
            Apl = 3,
            Wounds = 12,
            Save = 3,
            Defence = 3,
            Equipment = [],
            Weapons = BuildWeapons(operativeId),
            Abilities = [],
            SpecialActions = [],
            SpecialRules = [],
        };

        return new Team
        {
            Id = TeamId,
            Name = TeamName,
            Faction = "Test",
            GrandFaction = "Test",
            Operatives = [operative],
        };
    }

    private static List<Weapon> BuildWeapons(Guid operativeId)
    {
        return
        [
            // ── Ranged weapons (Shoot phase) ────────────────────────────────────
            MakeRanged(operativeId, "Accurate 2 Rifle",     5, 3, 3, 4, "Accurate 2"),
            MakeRanged(operativeId, "Blast 2\" Launcher",   4, 3, 3, 4, "Blast 2\""),
            MakeRanged(operativeId, "Ceaseless Autogun",    4, 4, 3, 4, "Ceaseless"),
            MakeRanged(operativeId, "Devastating 4 Plasma", 4, 3, 3, 4, "Devastating 4"),
            MakeRanged(operativeId, "Heavy Bolter",         5, 4, 4, 5, "Heavy"),
            MakeRanged(operativeId, "Hot Plasma Pistol",    4, 3, 3, 5, "Hot"),
            MakeRanged(operativeId, "Lethal 4 Rifle",       4, 3, 3, 4, "Lethal 4"),
            MakeRanged(operativeId, "Limited 2 Flamer",     4, 3, 3, 5, "Limited 2"),
            MakeRanged(operativeId, "Piercing 2 Melta",     3, 4, 4, 6, "Piercing 2"),
            MakeRanged(operativeId, "PiercingCrits 1 Rifle",4, 3, 3, 4, "Piercing 1 Crits"),
            MakeRanged(operativeId, "Punishing Shotgun",    4, 4, 3, 4, "Punishing"),
            MakeRanged(operativeId, "Range 8\" Pistol",     4, 3, 3, 4, "Range 8\""),
            MakeRanged(operativeId, "Relentless Pistol",    4, 4, 3, 4, "Relentless"),
            MakeRanged(operativeId, "Rending Rifle",        4, 3, 3, 4, "Rending"),
            MakeRanged(operativeId, "Saturate Storm Bolter",5, 3, 3, 4, "Saturate"),
            MakeRanged(operativeId, "Seek Launcher",        4, 3, 3, 4, "Seek"),
            MakeRanged(operativeId, "Seek Light Sniper",    4, 3, 3, 5, "Seek Light"),
            MakeRanged(operativeId, "Severe Rifle",         4, 3, 3, 4, "Severe"),
            MakeRanged(operativeId, "Silent Rifle",         4, 3, 3, 4, "Silent"),
            MakeRanged(operativeId, "Stun Rifle",           4, 3, 3, 4, "Stun"),
            MakeRanged(operativeId, "Torrent 3\" Flamer",   4, 3, 3, 4, "Torrent 3\""),

            // ── Melee weapons (Fight phase) ─────────────────────────────────────
            MakeMelee(operativeId, "Accurate 1 Sword",   4, 3, 3, 4, "Accurate 1"),
            MakeMelee(operativeId, "Balanced Knife",     4, 3, 3, 4, "Balanced"),
            MakeMelee(operativeId, "Brutal Hammer",      4, 4, 4, 5, "Brutal"),
            MakeMelee(operativeId, "Ceaseless Claws",    5, 4, 3, 4, "Ceaseless"),
            MakeMelee(operativeId, "Devastating 3 Fist", 4, 3, 4, 3, "Devastating 3"),
            MakeMelee(operativeId, "Lethal 4 Blade",     4, 3, 3, 4, "Lethal 4"),
            MakeMelee(operativeId, "Limited 1 Charge",   3, 3, 4, 6, "Limited 1"),
            MakeMelee(operativeId, "Piercing 1 Lance",   4, 3, 3, 5, "Piercing 1"),
            MakeMelee(operativeId, "Punishing Staff",    4, 4, 3, 4, "Punishing"),
            MakeMelee(operativeId, "Relentless Axe",     4, 3, 3, 4, "Relentless"),
            MakeMelee(operativeId, "Rending Claw",       4, 3, 3, 4, "Rending"),
            MakeMelee(operativeId, "Severe Mace",        4, 3, 3, 4, "Severe"),
            MakeMelee(operativeId, "Shock Blade",        4, 3, 3, 4, "Shock"),
            MakeMelee(operativeId, "Stun Baton",         4, 3, 3, 4, "Stun"),
        ];
    }

    private static Weapon MakeRanged(Guid operativeId, string name, int atk, int hit, int norm, int crit, string rulesRaw)
    {
        return MakeWeapon(operativeId, name, WeaponType.Ranged, atk, hit, norm, crit, rulesRaw);
    }

    private static Weapon MakeMelee(Guid operativeId, string name, int atk, int hit, int norm, int crit, string rulesRaw)
    {
        return MakeWeapon(operativeId, name, WeaponType.Melee, atk, hit, norm, crit, rulesRaw);
    }

    private static Weapon MakeWeapon(Guid operativeId, string name, WeaponType type, int atk, int hit, int norm, int crit, string rulesRaw)
    {
        return new Weapon
        {
            Id = Guid.NewGuid(),
            OperativeId = operativeId,
            Name = name,
            Type = type,
            Atk = atk,
            Hit = hit,
            NormalDmg = norm,
            CriticalDmg = crit,
            WeaponRules = rulesRaw,
            Rules = WeaponRuleParser.Parse(rulesRaw),
        };
    }
}
