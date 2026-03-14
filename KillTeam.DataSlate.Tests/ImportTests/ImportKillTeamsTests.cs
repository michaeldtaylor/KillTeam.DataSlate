using FluentAssertions;
using KillTeam.DataSlate.Console.Infrastructure.Repositories;
using KillTeam.DataSlate.Console.Services;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Services;
using Xunit;

namespace KillTeam.DataSlate.Tests.ImportTests;

public class ImportKillTeamsTests
{
    private static readonly string ValidKillTeamJson = """
        {
          "name": "Angels of Death",
          "faction": "Adeptus Astartes",
          "operatives": [
            {
              "name": "Assault Intercessor Sergeant",
              "operativeType": "Assault Intercessor Sergeant",
              "stats": { "move": 3, "apl": 3, "wounds": 13, "save": "3+" },
              "weapons": [
                { "name": "Bolt Pistol", "type": "Ranged", "atk": 4, "hit": "3+", "dmg": "3/4", "specialRules": "Pistol" },
                { "name": "Astartes Chainsword", "type": "Melee", "atk": 5, "hit": "3+", "dmg": "4/5", "specialRules": "Lethal 5" }
              ],
              "equipment": ["Frag grenades x2"]
            },
            {
              "name": "Assault Intercessor",
              "operativeType": "Assault Intercessor",
              "stats": { "move": 3, "apl": 2, "wounds": 13, "save": "3+" },
              "weapons": [
                { "name": "Astartes Chainsword", "type": "Melee", "atk": 5, "hit": "3+", "dmg": "4/5", "specialRules": "Lethal 5" }
              ],
              "equipment": []
            }
          ]
        }
        """;

    [Fact]
    public async Task ValidImport_CreatesKillTeamWithOperativesAndWeapons()
    {
        using var db = TestDbBuilder.Create();
        var importer = new KillTeamJsonImporter();
        var killTeamRepo = new SqliteKillTeamRepository(db.Connection);
        var opRepo = new SqliteOperativeRepository(db.Connection);
        var wpRepo = new SqliteWeaponRepository(db.Connection);

        var team = importer.Import(ValidKillTeamJson);
        await killTeamRepo.UpsertAsync(team);
        await opRepo.UpsertByTeamAsync(team.Operatives, team.Id);
        foreach (var op in team.Operatives)
            await wpRepo.UpsertByOperativeAsync(op.Weapons, op.Id);

        // Verify via raw SQL
        using var cmd1 = db.Connection.CreateCommand();
        cmd1.CommandText = "SELECT COUNT(*) FROM kill_teams WHERE name='Angels of Death'";
        Convert.ToInt32(cmd1.ExecuteScalar()).Should().Be(1);

        using var cmd2 = db.Connection.CreateCommand();
        cmd2.CommandText = "SELECT COUNT(*) FROM operatives";
        Convert.ToInt32(cmd2.ExecuteScalar()).Should().Be(2);

        using var cmd3 = db.Connection.CreateCommand();
        cmd3.CommandText = "SELECT COUNT(*) FROM weapons";
        Convert.ToInt32(cmd3.ExecuteScalar()).Should().Be(3);
    }

    [Fact]
    public async Task ReImport_SameTeamName_UpdatesExistingRecord()
    {
        using var db = TestDbBuilder.Create();
        var importer = new KillTeamJsonImporter();
        var killTeamRepo = new SqliteKillTeamRepository(db.Connection);
        var opRepo = new SqliteOperativeRepository(db.Connection);
        var wpRepo = new SqliteWeaponRepository(db.Connection);

        async Task<Guid> DoImport(string faction)
        {
            var json = ValidKillTeamJson.Replace("Adeptus Astartes", faction);
            var team = importer.Import(json);
            // Simulate the ID-preservation logic from ImportKillTeamsCommand
            var existing = await killTeamRepo.FindByNameAsync(team.Name);
            if (existing is not null) team.Id = existing.Id;
            await killTeamRepo.UpsertAsync(team);
            await opRepo.UpsertByTeamAsync(team.Operatives, team.Id);
            foreach (var op in team.Operatives)
                await wpRepo.UpsertByOperativeAsync(op.Weapons, op.Id);
            return team.Id;
        }

        var firstId  = await DoImport("Adeptus Astartes");
        var secondId = await DoImport("Space Marines"); // same name, updated faction

        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM kill_teams";
        Convert.ToInt32(cmd.ExecuteScalar()).Should().Be(1, "re-import should not create a second row");

        secondId.Should().Be(firstId, "re-import should preserve the kill team ID");

        // Verify faction was updated
        cmd.CommandText = "SELECT faction FROM kill_teams WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", firstId.ToString());
        ((string?)cmd.ExecuteScalar()).Should().Be("Space Marines");
    }

    [Fact]
    public void MissingField_ThrowsValidationError()
    {
        var importer = new KillTeamJsonImporter();
        var badJson = """
            {
              "name": "Bad Team",
              "faction": "X",
              "operatives": [
                {
                  "name": "Op1",
                  "stats": { "move": 3, "apl": 2, "wounds": 13 }
                }
              ]
            }
            """;

        var act = () => importer.Import(badJson);

        act.Should().Throw<KillTeamValidationException>()
           .WithMessage("*save*");
    }

    [Fact]
    public void InvalidJson_ThrowsValidationException()
    {
        var importer = new KillTeamJsonImporter();
        var badJson = "{ this is not valid json }";

        var act = () => importer.Import(badJson);

        act.Should().Throw<KillTeamValidationException>();
    }
}

public class SpecialRuleParserTests
{
    [Fact]
    public void Parse_HeavyDashOnly_ReturnsCorrectKind()
    {
        var rules = SpecialRuleParser.Parse("Heavy (Dash only)");
        rules.Should().ContainSingle();
        rules[0].Kind.Should().Be(SpecialRuleKind.HeavyDashOnly);
    }

    [Fact]
    public void Parse_Piercing1_ReturnsKindWithParam1()
    {
        var rules = SpecialRuleParser.Parse("Piercing 1");
        rules.Should().ContainSingle();
        rules[0].Kind.Should().Be(SpecialRuleKind.Piercing);
        rules[0].Param.Should().Be(1);
    }

    [Fact]
    public void Parse_Lethal5_ReturnsKindWithParam5()
    {
        var rules = SpecialRuleParser.Parse("Lethal 5");
        rules.Should().ContainSingle();
        rules[0].Kind.Should().Be(SpecialRuleKind.Lethal);
        rules[0].Param.Should().Be(5);
    }

    [Fact]
    public void Parse_UnknownRule_ReturnsUnknownKind()
    {
        var rules = SpecialRuleParser.Parse("Poison 2");
        rules.Should().ContainSingle();
        rules[0].Kind.Should().Be(SpecialRuleKind.Unknown);
    }

    [Fact]
    public void Parse_MultipleRules_ReturnsAll()
    {
        var rules = SpecialRuleParser.Parse("Lethal 5, Brutal, Accurate 1");
        rules.Should().HaveCount(3);
    }
}
