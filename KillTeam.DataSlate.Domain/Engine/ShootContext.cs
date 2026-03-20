namespace KillTeam.DataSlate.Domain.Engine;

public record ShootContext(
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

public record ShootResult(
    int UnblockedCrits,
    int UnblockedNormals,
    int TotalDamage,
    int AttackerRawCritHits,
    bool StunApplied,
    int SelfDamageDealt
);
