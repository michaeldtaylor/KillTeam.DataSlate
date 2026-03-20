using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Console.TestData;

/// <summary>
/// Builds in-memory test teams used only in Simulate mode.
/// Each operative carries one weapon per weapon rule group so every rule can be exercised.
/// </summary>
public static class TestTeamFactory
{
    public const string Team1Id = "00000000-0000-0000-ffff-000000000001";
    public const string Team1Name = "Test Team 1";

    public const string Team2Id = "00000000-0000-0000-ffff-000000000002";
    public const string Team2Name = "Test Team 2";

    public static Team CreateTeam1()
    {
        var operativeId = Guid.Parse("00000000-0000-0000-ffff-000000000011");

        var operative = new Operative
        {
            Id = operativeId,
            TeamId = Team1Id,
            Name = "Test Operative 1",
            OperativeType = "Test",
            PrimaryKeyword = "Test",
            Keywords = ["Test"],
            Move = 6,
            Apl = 3,
            Wounds = 12,
            Save = 3,
            Defence = 3,
            Equipment = [],
            Weapons = BuildWeapons(),
            Abilities = [],
            SpecialActions = [],
            OperativeWeaponRules = [],
        };

        return new Team
        {
            Id = Team1Id,
            Name = Team1Name,
            Faction = "Test",
            GrandFaction = "Test",
            Operatives = [operative],
        };
    }

    public static Team CreateTeam2()
    {
        var operativeId = Guid.Parse("00000000-0000-0000-ffff-000000000022");

        var operative = new Operative
        {
            Id = operativeId,
            TeamId = Team2Id,
            Name = "Test Operative 2",
            OperativeType = "Test",
            PrimaryKeyword = "Test",
            Keywords = ["Test"],
            Move = 6,
            Apl = 3,
            Wounds = 12,
            Save = 3,
            Defence = 3,
            Equipment = [],
            Weapons = BuildWeapons(),
            Abilities = [],
            SpecialActions = [],
            OperativeWeaponRules = [],
        };

        return new Team
        {
            Id = Team2Id,
            Name = Team2Name,
            Faction = "Test",
            GrandFaction = "Test",
            Operatives = [operative],
        };
    }

    private static List<Weapon> BuildWeapons()
    {
        return
        [
            // ── Ranged weapons (Shoot phase) ────────────────────────────────────
            MakeRanged("Accurate 2 Rifle",     5, 3, 3, 4, "Accurate 2"),
            MakeRanged("Blast 2\" Launcher",   4, 3, 3, 4, "Blast 2\""),
            MakeRanged("Ceaseless Autogun",    4, 4, 3, 4, "Ceaseless"),
            MakeRanged("Devastating 4 Plasma", 4, 3, 3, 4, "Devastating 4"),
            MakeRanged("Heavy Bolter",         5, 4, 4, 5, "Heavy"),
            MakeRanged("Hot Plasma Pistol",    4, 3, 3, 5, "Hot"),
            MakeRanged("Lethal 4 Rifle",       4, 3, 3, 4, "Lethal 4"),
            MakeRanged("Limited 2 Flamer",     4, 3, 3, 5, "Limited 2"),
            MakeRanged("Piercing 2 Melta",     3, 4, 4, 6, "Piercing 2"),
            MakeRanged("PiercingCrits 1 Rifle",4, 3, 3, 4, "Piercing 1 Crits"),
            MakeRanged("Punishing Shotgun",    4, 4, 3, 4, "Punishing"),
            MakeRanged("Range 8\" Pistol",     4, 3, 3, 4, "Range 8\""),
            MakeRanged("Relentless Pistol",    4, 4, 3, 4, "Relentless"),
            MakeRanged("Rending Rifle",        4, 3, 3, 4, "Rending"),
            MakeRanged("Saturate Storm Bolter",5, 3, 3, 4, "Saturate"),
            MakeRanged("Seek Launcher",        4, 3, 3, 4, "Seek"),
            MakeRanged("Seek Light Sniper",    4, 3, 3, 5, "Seek Light"),
            MakeRanged("Severe Rifle",         4, 3, 3, 4, "Severe"),
            MakeRanged("Silent Rifle",         4, 3, 3, 4, "Silent"),
            MakeRanged("Stun Rifle",           4, 3, 3, 4, "Stun"),
            MakeRanged("Torrent 3\" Flamer",   4, 3, 3, 4, "Torrent 3\""),

            // ── Melee weapons (Fight phase) ─────────────────────────────────────
            MakeMelee("Accurate 1 Sword",   4, 3, 3, 4, "Accurate 1"),
            MakeMelee("Balanced Knife",     4, 3, 3, 4, "Balanced"),
            MakeMelee("Brutal Hammer",      4, 4, 4, 5, "Brutal"),
            MakeMelee("Ceaseless Claws",    5, 4, 3, 4, "Ceaseless"),
            MakeMelee("Devastating 3 Fist", 4, 3, 4, 3, "Devastating 3"),
            MakeMelee("Lethal 4 Blade",     4, 3, 3, 4, "Lethal 4"),
            MakeMelee("Limited 1 Charge",   3, 3, 4, 6, "Limited 1"),
            MakeMelee("Piercing 1 Lance",   4, 3, 3, 5, "Piercing 1"),
            MakeMelee("Punishing Staff",    4, 4, 3, 4, "Punishing"),
            MakeMelee("Relentless Axe",     4, 3, 3, 4, "Relentless"),
            MakeMelee("Rending Claw",       4, 3, 3, 4, "Rending"),
            MakeMelee("Severe Mace",        4, 3, 3, 4, "Severe"),
            MakeMelee("Shock Blade",        4, 3, 3, 4, "Shock"),
            MakeMelee("Stun Baton",         4, 3, 3, 4, "Stun"),
        ];
    }

    private static Weapon MakeRanged(string name, int atk, int hit, int norm, int crit, string rulesRaw)
    {
        return MakeWeapon(name, WeaponType.Ranged, atk, hit, norm, crit, rulesRaw);
    }

    private static Weapon MakeMelee(string name, int atk, int hit, int norm, int crit, string rulesRaw)
    {
        return MakeWeapon(name, WeaponType.Melee, atk, hit, norm, crit, rulesRaw);
    }

    private static Weapon MakeWeapon(string name, WeaponType type, int atk, int hit, int norm, int crit, string rulesRaw)
    {
        return new Weapon
        {
            Id = Guid.NewGuid(),
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
