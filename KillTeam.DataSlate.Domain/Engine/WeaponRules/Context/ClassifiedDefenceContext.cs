namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;

public class ClassifiedDefenceContext
{
    public int CritSaves { get; set; }

    public int NormalSaves { get; set; }

    public int RawCrits { get; }

    public ClassifiedDefenceContext(int critSaves, int normalSaves, int rawCrits)
    {
        CritSaves = critSaves;
        NormalSaves = normalSaves;
        RawCrits = rawCrits;
    }
}
