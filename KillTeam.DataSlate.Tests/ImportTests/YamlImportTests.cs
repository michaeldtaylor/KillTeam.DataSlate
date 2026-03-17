using FluentAssertions;
using KillTeam.DataSlate.Infrastructure.Repositories;
using KillTeam.DataSlate.Infrastructure.Services;
using Xunit;

namespace KillTeam.DataSlate.Tests.ImportTests;

public class YamlImportTests
{
    private static readonly string MinimalYaml = """
        id: test-team
        name: Test Team
        grandFaction: Imperium
        faction: Adeptus Astartes
        datacards:
          - name: Test Sergeant
            operativeType: Test Sergeant
            primaryKeyword: Test
            keywords:
              - Test
              - Imperium
              - Adeptus Astartes
            stats:
              apl: 3
              move: 6
              save: '3+'
              wounds: 13
            weapons:
              - name: Bolt Pistol
                type: Ranged
                atk: 4
                hit: '3+'
                dmg:
                  normal: 3
                  crit: 4
                weaponRules:
                  - Range 8"
                  - Piercing 1
            abilities:
              - name: Iron Halo
                text: Once per battle, ignore one Normal Dmg.
            specialActions:
              - name: Smite
                text: Deal D3 mortal wounds to a visible enemy.
                apCost: 1
            specialRules:
              - name: Custom Rule
                text: This weapon has a special effect.
        factionRules:
          - name: Chapter Tactics
            text: Select a primary and secondary tactic.
          - name: Astartes
            text: Can perform two Shoot or two Fight actions.
        strategyPloys:
          - name: Combat Doctrine
            text: Select a doctrine for your operatives.
        firefightPloys:
          - name: Transhuman Physiology
            text: Retain one normal success as a critical.
        factionEquipment:
          - name: Frag Grenades
            text: Throwable explosive grenades.
        universalEquipment:
          - name: Portable Barricade
            text: A light, protective terrain piece.
        operativeSelection:
          archetype: Security, Seek & Destroy
          text: |-
            1 operative from the leader list.
            5 operatives from the warrior list.
        supplementaryInformation: |-
          # Errata January '26
          Some rules have been updated.
        """;

    [Fact]
    public async Task YamlImport_FullRoundtrip_AllDataPersisted()
    {
        using var db = TestDbBuilder.Create();
        var importer = new TeamYamlImporter();
        var teamRepo = new SqliteTeamRepository(db.Connection);

        var team = importer.Import(MinimalYaml);
        await teamRepo.UpsertAsync(team);

        // Team basics
        team.Id.Should().Be("test-team");
        team.Name.Should().Be("Test Team");
        team.GrandFaction.Should().Be("Imperium");
        team.Faction.Should().Be("Adeptus Astartes");

        // Operatives
        team.Operatives.Should().HaveCount(1);
        var op = team.Operatives[0];
        op.Name.Should().Be("Test Sergeant");
        op.PrimaryKeyword.Should().Be("Test");
        op.Keywords.Should().Contain("Imperium");
        op.Move.Should().Be(6);
        op.Apl.Should().Be(3);
        op.Save.Should().Be(3);
        op.Wounds.Should().Be(13);
        op.Weapons.Should().HaveCount(1);
        op.Abilities.Should().HaveCount(1);
        op.Abilities[0].Name.Should().Be("Iron Halo");
        op.SpecialActions.Should().HaveCount(1);
        op.SpecialActions[0].ApCost.Should().Be(1);
        op.SpecialRules.Should().HaveCount(1);

        // Team-level data
        team.FactionRules.Should().HaveCount(2);
        team.StrategyPloys.Should().HaveCount(1);
        team.FirefightPloys.Should().HaveCount(1);
        team.FactionEquipment.Should().HaveCount(1);
        team.UniversalEquipment.Should().HaveCount(1);
        team.OperativeSelectionArchetype.Should().Be("Security, Seek & Destroy");
        team.OperativeSelectionText.Should().Contain("5 operatives");
        team.SupplementaryInfo.Should().Contain("Errata January");

        // Verify database persistence
        CountRows(db, "teams").Should().Be(1);
        CountRows(db, "operatives").Should().Be(1);
        CountRows(db, "weapons").Should().Be(1);
        CountRows(db, "faction_rules").Should().Be(2);
        CountRows(db, "strategy_ploys").Should().Be(1);
        CountRows(db, "firefight_ploys").Should().Be(1);
        CountRows(db, "faction_equipment").Should().Be(1);
        CountRows(db, "universal_equipment").Should().Be(1);
        CountRows(db, "operative_abilities").Should().Be(1);
        CountRows(db, "operative_special_actions").Should().Be(1);
        CountRows(db, "operative_special_rules").Should().Be(1);

        // Verify extended team columns
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT grand_faction, operative_selection_archetype FROM teams WHERE id = 'test-team'";
        using var reader = cmd.ExecuteReader();
        reader.Read().Should().BeTrue();
        reader.GetString(0).Should().Be("Imperium");
        reader.GetString(1).Should().Be("Security, Seek & Destroy");
    }

    [Fact]
    public async Task YamlImport_ReImport_IsIdempotent()
    {
        using var db = TestDbBuilder.Create();
        var importer = new TeamYamlImporter();
        var teamRepo = new SqliteTeamRepository(db.Connection);

        var team1 = importer.Import(MinimalYaml);
        await teamRepo.UpsertAsync(team1);

        var team2 = importer.Import(MinimalYaml);
        await teamRepo.UpsertAsync(team2);

        CountRows(db, "teams").Should().Be(1);
        CountRows(db, "operatives").Should().Be(1);
        CountRows(db, "faction_rules").Should().Be(2);
        CountRows(db, "operative_abilities").Should().Be(1);
    }

    [Fact]
    public async Task YamlImport_RealFile_AngelsOfDeath()
    {
        var yamlPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "teams", "angels-of-death.yaml"));
        if (!File.Exists(yamlPath))
        {
            // Skip if file not available (CI without teams folder)
            return;
        }

        using var db = TestDbBuilder.Create();
        var importer = new TeamYamlImporter();
        var teamRepo = new SqliteTeamRepository(db.Connection);

        var yaml = await File.ReadAllTextAsync(yamlPath);
        var team = importer.Import(yaml);
        await teamRepo.UpsertAsync(team);

        team.Id.Should().Be("angels-of-death");
        team.Name.Should().Be("Angels of Death");
        team.GrandFaction.Should().Be("Imperium");
        team.Operatives.Should().HaveCountGreaterThan(5);
        team.FactionRules.Should().NotBeEmpty();
        team.StrategyPloys.Should().NotBeEmpty();
        team.FirefightPloys.Should().NotBeEmpty();

        // Verify all data persisted
        CountRows(db, "teams").Should().Be(1);
        CountRows(db, "operatives").Should().BeGreaterThan(5);
        CountRows(db, "faction_rules").Should().BeGreaterThan(0);
    }

    [Fact]
    public void YamlImport_MissingId_Throws()
    {
        var importer = new TeamYamlImporter();
        var badYaml = """
            name: Bad Team
            faction: X
            datacards:
              - name: Op1
                stats:
                  move: 3
                  apl: 2
                  wounds: 13
                  save: '3+'
                weapons: []
            """;

        var act = () => importer.Import(badYaml);
        act.Should().Throw<Domain.Services.TeamValidationException>()
           .WithMessage("*id*");
    }

    [Fact]
    public void YamlImport_MissingDatacards_Throws()
    {
        var importer = new TeamYamlImporter();
        var badYaml = """
            id: bad
            name: Bad Team
            faction: X
            """;

        var act = () => importer.Import(badYaml);
        act.Should().Throw<Domain.Services.TeamValidationException>()
           .WithMessage("*datacards*");
    }

    private static int CountRows(TestDbBuilder db, string table)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
