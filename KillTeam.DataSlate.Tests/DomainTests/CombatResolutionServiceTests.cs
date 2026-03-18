using FluentAssertions;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Services;
using Xunit;

namespace KillTeam.DataSlate.Tests.DomainTests;

public class CombatResolutionServiceTests
{
    private readonly CombatResolutionService _svc = new();

    private static ShootContext BaseCtx(int[] atk, int[] def, bool inCover = false,
        bool obscured = false, int hitThreshold = 3, int saveThreshold = 3,
        int normalDmg = 3, int critDmg = 4,
        List<WeaponRule>? rules = null)
        => new(atk, def, inCover, obscured, hitThreshold, saveThreshold, normalDmg, critDmg,
               rules ?? []);

    [Fact]
    public void ResolveShoot_TwoNormalSavesBlockOneCrit()
    {
        // Attack: crit(6) + normal(5) + normal(4) at threshold 3
        // Defence: normal(5) + normal(4) at save 3
        // Expected: 2 normals cancel 1 crit → 0 crits, 2 normals remaining
        var ctx = BaseCtx([6, 5, 4], [5, 4]);
        var result = _svc.ResolveShoot(ctx);
        result.UnblockedCrits.Should().Be(0);
        result.UnblockedNormals.Should().Be(2);
        result.TotalDamage.Should().Be(6); // 2 × 3
    }

    [Fact]
    public void ResolveShoot_OneCritSaveBlocksCrit()
    {
        // Attack: crit(6) + normal(5) at threshold 3
        // Defence: crit save(6)
        // Expected: crit blocked → 0 crits, 1 normal
        var ctx = BaseCtx([6, 5], [6]);
        var result = _svc.ResolveShoot(ctx);
        result.UnblockedCrits.Should().Be(0);
        result.UnblockedNormals.Should().Be(1);
        result.TotalDamage.Should().Be(3);
    }

    [Fact]
    public void ResolveShoot_OneCritSaveBlocksNormal_WhenNoCritsRemain()
    {
        // Attack: normal(5) + normal(4) at threshold 3
        // Defence: crit save(6)
        // Expected: crit save acts as normal save → blocks 1 normal, 1 normal remains
        var ctx = BaseCtx([5, 4], [6]);
        var result = _svc.ResolveShoot(ctx);
        result.UnblockedNormals.Should().Be(1);
        result.TotalDamage.Should().Be(3);
    }

    [Fact]
    public void ResolveShoot_InCover_UnconditionallyRetains1NormalSave()
    {
        // Attack: normal(5) at threshold 3
        // Defence: 1(miss), but in cover adds 1 normal save
        // Expected: total damage = 0
        var ctx = BaseCtx([5], [1], inCover: true);
        var result = _svc.ResolveShoot(ctx);
        result.TotalDamage.Should().Be(0);
    }

    [Fact]
    public void ResolveShoot_SingleNormalSaveBlocksNormal_NotCrit()
    {
        // Attack: crit(6) + normal(5) at threshold 3
        // Defence: normal save(4)
        // Normal save can only block a normal, not a crit
        // Expected: 1 crit unblocked, 0 normals
        var ctx = BaseCtx([6, 5], [4]);
        var result = _svc.ResolveShoot(ctx);
        result.UnblockedCrits.Should().Be(1);
        result.UnblockedNormals.Should().Be(0);
        result.TotalDamage.Should().Be(4); // 1 × crit dmg 4
    }

    [Fact]
    public void ResolveShoot_Piercing1_RemovesOneDefenceDie()
    {
        // Attack: normal(5), defence: [4, 3] but Piercing 1 removes 1 die
        // Remaining defence: [3] → save threshold 3 → 1 normal save blocks the normal
        var rules = new List<WeaponRule> { new(WeaponRuleKind.Piercing, 1, "Piercing 1") };
        var ctx = BaseCtx([5], [4, 3], rules: rules);
        var result = _svc.ResolveShoot(ctx);
        // With Piercing 1: remove first die (4), leaving [3]. Die 3 >= save 3 → 1 normal save blocks the 1 normal hit.
        result.UnblockedNormals.Should().Be(0);
        result.TotalDamage.Should().Be(0);
    }

    [Fact]
    public void ResolveShoot_Lethal5_Roll5ReturnsCrit()
    {
        // Lethal 5: rolls of 5 or 6 are crits
        // Attack: [5], defence: [6] (1 crit save)
        // 5 >= 5 = crit. Crit save blocks crit.
        var rules = new List<WeaponRule> { new(WeaponRuleKind.Lethal, 5, "Lethal 5") };
        var ctx = BaseCtx([5], [6], rules: rules);
        var result = _svc.ResolveShoot(ctx);
        result.AttackerRawCritHits.Should().BeGreaterThan(0, "Lethal 5 should produce a crit on a 5");
        result.UnblockedCrits.Should().Be(0, "crit save should block it");
    }

    [Fact]
    public void ResolveShoot_Rending_WithCritPresent_ConvertsOneHitToCrit()
    {
        // Rending: if any crit, convert 1 normal → crit
        // Attack: [6, 5] → crit + normal; with Rending → 2 crits
        var rules = new List<WeaponRule> { new(WeaponRuleKind.Rending, null, "Rending") };
        var ctx = BaseCtx([6, 5], [], rules: rules);
        var result = _svc.ResolveShoot(ctx);
        result.UnblockedCrits.Should().Be(2);
    }

    [Fact]
    public void ResolveShoot_Accurate1_AddsOneBonusNormalHit()
    {
        // Accurate 1: adds 1 bonus normal hit regardless of dice
        // Attack: [1] (miss), but +1 bonus normal from Accurate
        var rules = new List<WeaponRule> { new(WeaponRuleKind.Accurate, 1, "Accurate 1") };
        var ctx = BaseCtx([1], [], rules: rules);
        var result = _svc.ResolveShoot(ctx);
        result.UnblockedNormals.Should().Be(1);
    }

    [Fact]
    public void ResolveShoot_Stun_AppliesWhenCritRetained()
    {
        var rules = new List<WeaponRule> { new(WeaponRuleKind.Stun, null, "Stun") };
        var ctx = BaseCtx([6], [], rules: rules);
        var result = _svc.ResolveShoot(ctx);
        result.StunApplied.Should().BeTrue();
    }
}
