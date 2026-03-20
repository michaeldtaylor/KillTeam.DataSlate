using FluentAssertions;
using KillTeam.DataSlate.Domain.Engine;
using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Engine.WeaponRules.Visitors;
using KillTeam.DataSlate.Domain.Models;
using Xunit;

namespace KillTeam.DataSlate.Tests.DomainTests;

public class ShockRuleVisitorTests
{
    private readonly ShockRuleVisitor _handler = new();

    private static Operative MakeOperative(string name = "Operative") =>
        new() { TeamId = "team-1", Name = name, OperativeType = "Test", Wounds = 12 };

    private static Weapon MakeWeapon(bool hasShock = false) =>
        new()
        {
            Name = "Test Weapon",
            Type = WeaponType.Melee,
            Atk = 4,
            Hit = 3,
            NormalDmg = 3,
            CriticalDmg = 4,
            Rules = hasShock ? [new WeaponRule(WeaponRuleKind.Shock, null)] : [],
        };

    private static FightSetupContext MakeContext(
        FightDicePool attackerPool,
        FightDicePool targetPool) =>
        new()
        {
            Attacker = MakeOperative("Attacker"),
            Target = MakeOperative("Target"),
            EventStream = null,
            AttackerPool = attackerPool,
            TargetPool = targetPool,
        };

    [Fact]
    public async Task Shock_AttackerHasCrit_RemovesLowestTargetSuccess()
    {
        var attackerPool = new FightDicePool([new FightDie(0, 6, DieResult.Crit)]);
        var targetPool = new FightDicePool([
            new FightDie(1, 3, DieResult.Hit),
            new FightDie(2, 5, DieResult.Hit),
        ]);

        var context = MakeContext(attackerPool, targetPool);

        await _handler.SetupAsync(MakeWeapon(hasShock: true), context);

        context.TargetPool.Remaining.Should().HaveCount(1, "lowest success die discarded");
        context.TargetPool.Remaining.Single().RolledValue.Should().Be(5, "die with value 3 was the lowest and was discarded");
    }

    [Fact]
    public async Task Shock_AttackerHasNoCredit_TargetPoolUnchanged()
    {
        var attackerPool = new FightDicePool([new FightDie(0, 4, DieResult.Hit)]);
        var targetPool = new FightDicePool([new FightDie(1, 3, DieResult.Hit)]);

        var context = MakeContext(attackerPool, targetPool);

        await _handler.SetupAsync(MakeWeapon(hasShock: true), context);

        context.TargetPool.Remaining.Should().HaveCount(1, "Shock only triggers on attacker crit");
    }

    [Fact]
    public async Task Shock_TargetHasNoSuccesses_TargetPoolUnchanged()
    {
        var attackerPool = new FightDicePool([new FightDie(0, 6, DieResult.Crit)]);
        var targetPool = new FightDicePool([]);

        var context = MakeContext(attackerPool, targetPool);

        await _handler.SetupAsync(MakeWeapon(hasShock: true), context);

        context.TargetPool.Remaining.Should().BeEmpty("no target successes to remove");
    }

    [Fact]
    public async Task Shock_WeaponLacksShockRule_TargetPoolUnchanged()
    {
        var attackerPool = new FightDicePool([new FightDie(0, 6, DieResult.Crit)]);
        var targetPool = new FightDicePool([new FightDie(1, 3, DieResult.Hit)]);

        var context = MakeContext(attackerPool, targetPool);

        await _handler.SetupAsync(MakeWeapon(hasShock: false), context);

        context.TargetPool.Remaining.Should().HaveCount(1, "weapon has no Shock rule");
    }
}
