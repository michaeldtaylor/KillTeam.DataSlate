namespace KillTeam.DataSlate.Domain.Engine.WeaponRules;

public class ShootAttackClassifiedContext
{
    public int CritHits { get; set; }

    public int NormalHits { get; set; }

    public int RawCrits { get; }

    public ShootAttackClassifiedContext(int critHits, int normalHits, int rawCrits)
    {
        CritHits = critHits;
        NormalHits = normalHits;
        RawCrits = rawCrits;
    }
}
