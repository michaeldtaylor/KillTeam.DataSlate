namespace KillTeam.DataSlate.Domain.Engine.WeaponRules;

public class ShootDefenceClassifiedContext
{
    public int CritSaves { get; set; }

    public int NormalSaves { get; set; }

    public int RawCrits { get; }

    public ShootDefenceClassifiedContext(int critSaves, int normalSaves, int rawCrits)
    {
        CritSaves = critSaves;
        NormalSaves = normalSaves;
        RawCrits = rawCrits;
    }
}
