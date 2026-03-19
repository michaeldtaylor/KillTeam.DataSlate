using FluentAssertions;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Services;
using Xunit;

namespace KillTeam.DataSlate.Tests.DomainTests;

public class FightResolutionTests
{
    [Theory]
    [InlineData(6, DieResult.Crit)]
    [InlineData(4, DieResult.Hit)]
    [InlineData(3, DieResult.Hit)]
    [InlineData(2, DieResult.Miss)]
    [InlineData(1, DieResult.Miss)]
    public void CalculateDie_ReturnsCorrectResult_ForHitThreshold3(int roll, DieResult expected)
    {
        FightResolution.CalculateDie(roll, 3).Should().Be(expected);
    }

    [Fact]
    public void CalculateDice_DiscardsMisses_OnlyHitsAndCritsEnterPool()
    {
        var pool = FightResolution.CalculateDice([6, 6, 4, 3, 1], 3);

        pool.Remaining.Should().HaveCount(4);
        pool.Remaining.Count(d => d.Result == DieResult.Crit).Should().Be(2);
    }

    [Fact]
    public void ApplyStrike_WithCrit_DealsCritDamage()
    {
        var crit = new FightDie(0, 6, DieResult.Crit);

        FightResolution.ApplyStrike(crit, normalDmg: 4, critDmg: 5).Should().Be(5);
    }

    [Fact]
    public void ApplyStrike_WithNormal_DealsNormalDamage()
    {
        var hit = new FightDie(0, 4, DieResult.Hit);

        FightResolution.ApplyStrike(hit, normalDmg: 4, critDmg: 5).Should().Be(4);
    }

    [Fact]
    public void ApplySingleBlock_NormalDie_RemovesBothDiceFromPools()
    {
        // Defender D1:HIT blocks Attacker A3:HIT; A2:CRIT untouched
        var a2 = new FightDie(2, 6, DieResult.Crit);
        var a3 = new FightDie(3, 4, DieResult.Hit);
        var d1 = new FightDie(1, 4, DieResult.Hit);

        var attackerPool = new FightDicePool([a2, a3]);
        var defenderPool = new FightDicePool([d1]);

        var (newDef, newAtk) = FightResolution.ApplySingleBlock(d1, a3, defenderPool, attackerPool);

        newDef.Remaining.Should().BeEmpty("D1 was spent blocking");
        newAtk.Remaining.Should().ContainSingle(d => d.Id == a2.Id, "A2 crit should remain");
        newAtk.Remaining.Should().NotContain(d => d.Id == a3.Id, "A3 was blocked");
    }

    [Fact]
    public void GetAvailableActions_NormalDie_DoesNotOfferBlockAgainstCrit()
    {
        var defender = new FightDicePool([new FightDie(0, 4, DieResult.Hit)]); // 1 normal die
        var attacker = new FightDicePool([new FightDie(0, 6, DieResult.Crit)]); // 1 crit die in attacker pool

        // Defender's normal die cannot block attacker's crit
        var actions = FightResolution.GetAvailableActions(defender, attacker);

        actions.Should().NotContain(a => a.Type == FightActionType.Block,
            "a normal die cannot block a crit");
    }

    [Fact]
    public void GetAvailableActions_CritDie_OffersBlockAgainstCritAndNormal()
    {
        var attacker = new FightDicePool([new FightDie(0, 6, DieResult.Crit)]); // 1 crit attacking die
        var defender = new FightDicePool([new FightDie(0, 6, DieResult.Crit), new FightDie(1, 4, DieResult.Hit)]);

        // Attacker's crit can block both opponent dice
        var actions = FightResolution.GetAvailableActions(attacker, defender);

        actions.Count(a => a.Type == FightActionType.Block).Should().Be(2);
    }

    [Fact]
    public void GetAvailableActions_BrutalWeapon_NormalDieCannotBlock()
    {
        var attacker = new FightDicePool([new FightDie(0, 4, DieResult.Hit)]); // 1 normal attacking die
        var defender = new FightDicePool([new FightDie(0, 4, DieResult.Hit)]); // 1 normal defending die

        // Brutal: normal dice cannot block at all
        var actions = FightResolution.GetAvailableActions(attacker, defender, brutalWeapon: true);

        actions.Should().NotContain(a => a.Type == FightActionType.Block,
            "Brutal weapon prevents normal dice from blocking");
    }

    [Fact]
    public void GetAvailableActions_BrutalWeapon_CritCanStillBlock()
    {
        var attacker = new FightDicePool([new FightDie(0, 6, DieResult.Crit)]); // crit die
        var defender = new FightDicePool([new FightDie(0, 4, DieResult.Hit)]); // normal die

        // Even with Brutal, crits can still block
        var actions = FightResolution.GetAvailableActions(attacker, defender, brutalWeapon: true);

        actions.Should().Contain(a => a.Type == FightActionType.Block,
            "crit die can still block even with Brutal weapon");
    }
}
