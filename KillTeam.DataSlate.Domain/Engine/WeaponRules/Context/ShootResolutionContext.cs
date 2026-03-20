namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;

public record ShootResolutionContext(
    int[] AttackerDice,
    int[] TargetDice,
    bool InCover,
    bool IsObscured,
    int HitThreshold,
    int SaveThreshold,
    int NormalDmg,
    int CritDmg,
    int FightAssistBonus = 0
);
