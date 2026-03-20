namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;

public class ShootBeforeClassificationContext
{
    public int CritThreshold { get; set; } = 6;

    public int NormalThreshold { get; set; }

    public int BonusNormals { get; set; }
}
