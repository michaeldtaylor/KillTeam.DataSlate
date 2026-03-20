using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Engine.WeaponRules.Visitors;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules;

public sealed class ShootWeaponRulePipeline
{
    private readonly IReadOnlyList<IShootWeaponRuleVisitor> _handlers =
    [
        new AccurateRuleVisitor(),
        new DevastatingRuleVisitor(),
        new HeavyRuleVisitor(),
        new HotRuleVisitor(),
        new LethalRuleVisitor(),
        new PiercingCritsRuleVisitor(),
        new PiercingRuleVisitor(),
        new PunishingRuleVisitor(),
        new RangeRuleVisitor(),
        new RendingRuleVisitor(),
        new SaturateRuleVisitor(),
        new SeekLightRuleVisitor(),
        new SeekRuleVisitor(),
        new SevereRuleVisitor(),
        new SilentRuleVisitor(),
        new StunRuleVisitor(),
    ];

    public bool RequiresAoEResolution(Weapon weapon)
    {
        return weapon.Rules.Any(r => r.Kind is WeaponRuleKind.Blast or WeaponRuleKind.Torrent);
    }

    public IList<Weapon> FilterAvailableWeapons(IList<Weapon> weapons, AvailabilityContext context)
    {
        return weapons.Where(w => _handlers.All(h => h.IsAvailable(w, context))).ToList();
    }

    public async Task DetermineCoverAsync(Weapon weapon, CoverContext context)
    {
        foreach (var handler in _handlers)
        {
            await handler.ApplyBeforeCoverPromptAsync(weapon, context);
        }

        if (!context.CoverPromptSuppressed)
        {
            var coverChoice = await context.InputProvider.GetCoverStatusAsync(context.Target.Name, context.LightCoverBlocked);

            context.InCover = coverChoice == "In cover";
            context.IsObscured = coverChoice == "Obscured";
        }

        foreach (var handler in _handlers)
        {
            await handler.ApplyAfterCoverPromptAsync(weapon, context);
        }
    }

    public async Task ApplyEffectsAsync(Weapon weapon, EffectsContext context)
    {
        foreach (var handler in _handlers)
        {
            await handler.ApplyEffectsAsync(weapon, context);
        }
    }

    public async Task<ShootResolution> ResolveShootAsync(Weapon weapon, ShootContext context)
    {
        // ─── Stage 1: Pre-attack classification ─────────────────────────────────
        var attackClassificationContext = new AttackClassificationContext
        {
            CritThreshold = 6,
            NormalThreshold = context.HitThreshold - context.FightAssistBonus,
            BonusNormals = 0,
        };

        foreach (var handler in _handlers)
        {
            await handler.ApplyBeforeAttackClassificationAsync(weapon, attackClassificationContext);
        }

        // ─── Core: Classify attack dice ──────────────────────────────────────────
        var rawCrits = 0;
        var critHits = 0;
        var normalHits = attackClassificationContext.BonusNormals;

        foreach (var die in context.AttackerDice)
        {
            if (die >= attackClassificationContext.CritThreshold)
            {
                critHits++;
                rawCrits++;
            }
            else if (die >= attackClassificationContext.NormalThreshold)
            {
                normalHits++;
            }
        }

        // ─── Stage 2: Post-attack classification ─────────────────────────────────
        var classifiedAttackContext = new ClassifiedAttackContext(critHits, normalHits, rawCrits);

        foreach (var handler in _handlers)
        {
            await handler.ApplyAfterAttackClassificationAsync(weapon, classifiedAttackContext);
        }

        critHits = classifiedAttackContext.CritHits;
        normalHits = classifiedAttackContext.NormalHits;

        // ─── Obscured: convert crits → normals, discard 1 normal ────────────────
        if (context.IsObscured)
        {
            normalHits += critHits;
            critHits = 0;

            if (normalHits > 0)
            {
                normalHits--;
            }
        }

        // ─── Stage 3: Pre-defence classification ─────────────────────────────────
        var defenceClassificationContext = new DefenceClassificationContext
        {
            DefenceDice = context.TargetDice.ToList(),
        };

        foreach (var handler in _handlers)
        {
            await handler.ApplyBeforeDefenceClassificationAsync(weapon, defenceClassificationContext);
        }

        // ─── Core: Classify defence dice ─────────────────────────────────────────
        var critSaves = 0;
        var normalSaves = 0;

        foreach (var die in defenceClassificationContext.DefenceDice)
        {
            if (die == 6)
            {
                critSaves++;
            }
            else if (die >= context.SaveThreshold)
            {
                normalSaves++;
            }
        }

        // InCover: unconditionally +1 normal save
        if (context.InCover)
        {
            normalSaves++;
        }

        // ─── Stage 4: Post-defence classification ─────────────────────────────────
        var classifiedDefenceContext = new ClassifiedDefenceContext(critSaves, normalSaves, rawCrits);

        foreach (var handler in _handlers)
        {
            await handler.ApplyAfterDefenceClassificationAsync(weapon, classifiedDefenceContext);
        }

        critSaves = classifiedDefenceContext.CritSaves;
        normalSaves = classifiedDefenceContext.NormalSaves;

        // ─── Core: Blocking algorithm ─────────────────────────────────────────────
        var unblockedCrits = critHits;
        var unblockedNormals = normalHits;

        var critSavesToUse = Math.Min(critSaves, unblockedCrits);
        unblockedCrits -= critSavesToUse;
        critSaves -= critSavesToUse;

        if (unblockedCrits > 0)
        {
            var pairs = Math.Min(normalSaves / 2, unblockedCrits);
            unblockedCrits -= pairs;
            normalSaves -= pairs * 2;
        }

        var totalNormalSavesLeft = critSaves + normalSaves;
        var normalSavesToUse = Math.Min(totalNormalSavesLeft, unblockedNormals);

        unblockedNormals -= normalSavesToUse;

        // ─── Stage 5: Post-blocking ───────────────────────────────────────────────
        var blockingContext = new BlockingContext(unblockedCrits, unblockedNormals, context.HitThreshold, context.CritDmg);

        foreach (var handler in _handlers)
        {
            await handler.ApplyAfterBlockingAsync(weapon, blockingContext);
        }

        // ─── Core: Calculate damage ───────────────────────────────────────────────
        var totalDamage = blockingContext.UnblockedCrits * blockingContext.EffectiveCritDmg + blockingContext.UnblockedNormals * context.NormalDmg;

        return new ShootResolution(
            blockingContext.UnblockedCrits,
            blockingContext.UnblockedNormals,
            totalDamage,
            rawCrits,
            blockingContext.StunApplied,
            blockingContext.SelfDamage);
    }
}
