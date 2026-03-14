using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Services;

public class CombatResolutionService
{
    /// <summary>
    /// Resolves a Shoot action according to v3.0 rules.
    /// The attacker and defender dice are already rolled (passed in).
    /// </summary>
    public ShootResult ResolveShoot(ShootContext ctx)
    {
        // ─── 1. Accurate x: add x bonus normal hits to attack pool ──────────
        var accurate = ctx.WeaponRules.FirstOrDefault(r => r.Kind == SpecialRuleKind.Accurate);
        var bonusNormals = accurate?.Param ?? 0;

        // ─── 2. Classify attack dice ─────────────────────────────────────────
        var effectiveHit = ctx.HitThreshold - ctx.FightAssistBonus;
        var lethal = ctx.WeaponRules.FirstOrDefault(r => r.Kind == SpecialRuleKind.Lethal);

        var critHits = 0; // bonus normals tracked separately
        var normalHits = bonusNormals;

        // Raw crit count before Obscured conversion (for PiercingCrits)
        var rawCrits = 0;

        foreach (var die in ctx.AttackDice)
        {
            // Lethal x: threshold roll = crit
            if (lethal is not null && die >= lethal.Param)
            {
                critHits++;
                rawCrits++;
            }
            else if (die == 6)
            {
                critHits++;
                rawCrits++;
            }
            else if (die >= effectiveHit)
            {
                normalHits++;
            }
            // else: miss, discard
        }

        // Apply Rending: if >= 1 crit hit, convert 1 normal hit → crit
        if (ctx.WeaponRules.Any(r => r.Kind == SpecialRuleKind.Rending) && normalHits >= 1 && critHits >= 1)
        {
            normalHits--;
            critHits++;
        }

        // Apply Punishing: if any crit hits, all normal hits become crits
        if (ctx.WeaponRules.Any(r => r.Kind == SpecialRuleKind.Punishing) && critHits >= 1)
        {
            critHits += normalHits;
            normalHits = 0;
        }

        // Apply Severe: if any crit hits, halve normal hits (round down)
        if (ctx.WeaponRules.Any(r => r.Kind == SpecialRuleKind.Severe) && critHits >= 1)
        {
            normalHits = normalHits / 2;
        }

        // ─── 3. Obscured: convert crits → normals, discard 1 normal ─────────
        if (ctx.IsObscured)
        {
            normalHits += critHits;
            critHits = 0;
            if (normalHits > 0)
            {
                normalHits--;
            }
        }

        // ─── 4. Classify defence dice ─────────────────────────────────────────
        var piercing = ctx.WeaponRules.FirstOrDefault(r => r.Kind == SpecialRuleKind.Piercing);
        var piercingCrits = ctx.WeaponRules.FirstOrDefault(r => r.Kind == SpecialRuleKind.PiercingCrits);

        // Piercing x: remove x dice from defence pool before rolling
        var defenceDiceList = ctx.DefenceDice.ToList();
        if (piercing?.Param is > 0)
        {
            var removeCount = Math.Min(piercing.Param.Value, defenceDiceList.Count);
            defenceDiceList.RemoveRange(0, removeCount);
        }

        var critSaves = 0;
        var normalSaves = 0;

        foreach (var die in defenceDiceList)
        {
            if (die == 6)
            {
                critSaves++;
            }
            else if (die >= ctx.SaveThreshold)
            {
                normalSaves++;
            }
        }

        // In cover: unconditionally +1 normal save
        if (ctx.InCover)
        {
            normalSaves++;
        }

        // PiercingCrits x: if rawCrits >= 1, remove x saves (crit saves first, then normal)
        if (piercingCrits?.Param is > 0 && rawCrits >= 1)
        {
            var removeCount = piercingCrits.Param.Value;
            var fromCrits = Math.Min(removeCount, critSaves);
            critSaves -= fromCrits;
            removeCount -= fromCrits;
            normalSaves = Math.Max(0, normalSaves - removeCount);
        }

        // ─── 5. Blocking algorithm ────────────────────────────────────────────
        // (a) crit save → cancels 1 crit attack
        // (b) 2 normal saves → cancels 1 crit attack (if crits remain)
        // (c) normal save → cancels 1 normal attack

        var unblockedCrits = critHits;
        var unblockedNormals = normalHits;

        // Step (a): crit saves block crits
        var critSavesToUse = Math.Min(critSaves, unblockedCrits);
        unblockedCrits -= critSavesToUse;
        critSaves -= critSavesToUse;

        // Step (b): 2 normals → 1 crit
        if (unblockedCrits > 0)
        {
            var pairs = Math.Min(normalSaves / 2, unblockedCrits);
            unblockedCrits -= pairs;
            normalSaves -= pairs * 2;
        }

        // Step (c): normal → normal (remaining crit saves also act as normal saves)
        var totalNormalSavesLeft = critSaves + normalSaves; // leftover crit saves can block normals
        var normalSavesToUse = Math.Min(totalNormalSavesLeft, unblockedNormals);
        unblockedNormals -= normalSavesToUse;

        // ─── 6. Damage calculation ─────────────────────────────────────────────
        var devastating = ctx.WeaponRules.FirstOrDefault(r => r.Kind == SpecialRuleKind.Devastating);
        var effectiveCritDmg = devastating?.Param ?? ctx.CritDmg;

        var totalDamage = (unblockedCrits * effectiveCritDmg) + (unblockedNormals * ctx.NormalDmg);

        // ─── 7. Hot ────────────────────────────────────────────────────────────
        var selfDamage = 0;
        if (ctx.WeaponRules.Any(r => r.Kind == SpecialRuleKind.Hot))
        {
            var d6 = Random.Shared.Next(1, 7);            if (d6 < ctx.HitThreshold)
                selfDamage = 2 * d6;
        }

        // ─── 8. Stun ───────────────────────────────────────────────────────────
        var stun = ctx.WeaponRules.Any(r => r.Kind == SpecialRuleKind.Stun) && unblockedCrits >= 1;

        return new ShootResult(unblockedCrits, unblockedNormals, totalDamage, rawCrits, stun, selfDamage);
    }
}
