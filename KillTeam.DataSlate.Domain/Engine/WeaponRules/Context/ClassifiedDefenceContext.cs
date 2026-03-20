namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;

public class ClassifiedDefenceContext(int critSaves, int normalSaves, int rawCrits)
{
    public int CritSaves { get; set; } = critSaves;

    public int NormalSaves { get; set; } = normalSaves;

    public int RawCrits { get; } = rawCrits;
}
