namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;

public class BlockingContext(int unblockedCrits, int unblockedNormals, int hitThreshold, int effectiveCritDmg)
{
    public int UnblockedCrits { get; } = unblockedCrits;

    public int UnblockedNormals { get; } = unblockedNormals;

    public int HitThreshold { get; } = hitThreshold;

    public int EffectiveCritDmg { get; set; } = effectiveCritDmg;

    public int SelfDamage { get; set; }

    public bool StunApplied { get; set; }
}
