namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;

public class ClassifiedAttackContext(int critHits, int normalHits, int rawCrits)
{
    public int CritHits { get; set; } = critHits;

    public int NormalHits { get; set; } = normalHits;

    public int RawCrits { get; } = rawCrits;
}
