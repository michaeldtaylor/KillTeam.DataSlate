using KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;
using KillTeam.DataSlate.Domain.Engine.WeaponRules.Handlers;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.WeaponRules;

public sealed class ShootWeaponRuleApplicator
{
    private readonly IReadOnlyList<IShootWeaponRuleHandler> _handlers =
    [
        new AccurateRuleHandler(),
        new DevastatingRuleHandler(),
        new HeavyRuleHandler(),
        new HotRuleHandler(),
        new LethalRuleHandler(),
        new PiercingCritsRuleHandler(),
        new PiercingRuleHandler(),
        new PunishingRuleHandler(),
        new RangeRuleHandler(),
        new RendingRuleHandler(),
        new SaturateRuleHandler(),
        new SeekLightRuleHandler(),
        new SeekRuleHandler(),
        new SevereRuleHandler(),
        new SilentRuleHandler(),
        new StunRuleHandler(),
    ];

    public bool RequiresAoEResolution(Weapon weapon)
    {
        return weapon.Rules.Any(r => r.Kind is WeaponRuleKind.Blast or WeaponRuleKind.Torrent);
    }

    public IList<Weapon> FilterAvailableWeapons(IList<Weapon> weapons, ShootWeaponAvailabilityContext context)
    {
        return weapons.Where(w => _handlers.All(h => h.IsAvailable(w, context))).ToList();
    }

    public async Task DetermineCoverAsync(Weapon weapon, WeaponCoverContext context)
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

    public async Task ApplyEffectsAsync(Weapon weapon, WeaponEffectContext context)
    {
        foreach (var handler in _handlers)
        {
            await handler.ApplyEffectsAsync(weapon, context);
        }
    }

    public async Task<ShootResolution> ResolveShootAsync(Weapon weapon, ShootContext context)
    {
        // ─── Stage 1: Pre-attack classification ─────────────────────────────────
        var preClassificationContext = new ShootBeforeClassificationContext
        {
            CritThreshold = 6,
            NormalThreshold = context.HitThreshold - context.FightAssistBonus,
            BonusNormals = 0,
        };

        foreach (var handler in _handlers)
        {
            await handler.ApplyBeforeAttackClassificationAsync(weapon, preClassificationContext);
        }

        // ─── Core: Classify attack dice ──────────────────────────────────────────
        var rawCrits = 0;
        var critHits = 0;
        var normalHits = preClassificationContext.BonusNormals;

        foreach (var die in context.AttackerDice)
        {
            if (die >= preClassificationContext.CritThreshold)
            {
                critHits++;
                rawCrits++;
            }
            else if (die >= preClassificationContext.NormalThreshold)
            {
                normalHits++;
            }
        }

        // ─── Stage 2: Post-attack classification ─────────────────────────────────
        var attackClassifiedContext = new ShootAttackClassifiedContext(critHits, normalHits, rawCrits);

        foreach (var handler in _handlers)
        {
            await handler.ApplyAfterAttackClassificationAsync(weapon, attackClassifiedContext);
        }

        critHits = attackClassifiedContext.CritHits;
        normalHits = attackClassifiedContext.NormalHits;

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
        var beforeDefenceContext = new ShootBeforeDefenceContext
        {
            DefenceDice = context.TargetDice.ToList(),
        };

        foreach (var handler in _handlers)
        {
            await handler.ApplyBeforeDefenceClassificationAsync(weapon, beforeDefenceContext);
        }

        // ─── Core: Classify defence dice ─────────────────────────────────────────
        var critSaves = 0;
        var normalSaves = 0;

        foreach (var die in beforeDefenceContext.DefenceDice)
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
        var defenceClassifiedContext = new ShootDefenceClassifiedContext(critSaves, normalSaves, rawCrits);

        foreach (var handler in _handlers)
        {
            await handler.ApplyAfterDefenceClassificationAsync(weapon, defenceClassifiedContext);
        }

        critSaves = defenceClassifiedContext.CritSaves;
        normalSaves = defenceClassifiedContext.NormalSaves;

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
        var afterBlockingContext = new ShootAfterBlockingContext(unblockedCrits, unblockedNormals, context.HitThreshold, context.CritDmg);

        foreach (var handler in _handlers)
        {
            await handler.ApplyAfterBlockingAsync(weapon, afterBlockingContext);
        }

        // ─── Core: Calculate damage ───────────────────────────────────────────────
        var totalDamage = (afterBlockingContext.UnblockedCrits * afterBlockingContext.EffectiveCritDmg) + (afterBlockingContext.UnblockedNormals * context.NormalDmg);

        return new ShootResolution(
            afterBlockingContext.UnblockedCrits,
            afterBlockingContext.UnblockedNormals,
            totalDamage,
            rawCrits,
            afterBlockingContext.StunApplied,
            afterBlockingContext.SelfDamage);
    }
}
