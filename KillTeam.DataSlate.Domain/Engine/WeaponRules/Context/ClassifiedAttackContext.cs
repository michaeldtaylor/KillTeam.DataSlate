namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;

public class ClassifiedAttackContext
{
    public int CritHits { get; set; }

    public int NormalHits { get; set; }

    public int RawCrits { get; }

    public ClassifiedAttackContext(int critHits, int normalHits, int rawCrits)
    {
        CritHits = critHits;
        NormalHits = normalHits;
        RawCrits = rawCrits;
    }
}
