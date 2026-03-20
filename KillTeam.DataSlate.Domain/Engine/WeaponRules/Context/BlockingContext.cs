namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;

public class BlockingContext
{
    public int UnblockedCrits { get; }

    public int UnblockedNormals { get; }

    public int HitThreshold { get; }

    public int EffectiveCritDmg { get; set; }

    public int SelfDamage { get; set; }

    public bool StunApplied { get; set; }

    public BlockingContext(int unblockedCrits, int unblockedNormals, int hitThreshold, int effectiveCritDmg)
    {
        UnblockedCrits = unblockedCrits;
        UnblockedNormals = unblockedNormals;
        HitThreshold = hitThreshold;
        EffectiveCritDmg = effectiveCritDmg;
    }
}
