using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Services;

public record ShootContext(
    int[] AttackDice,
    int[] DefenceDice,
    bool InCover,
    bool IsObscured,
    int HitThreshold,
    int SaveThreshold,
    int NormalDmg,
    int CritDmg,
    List<WeaponSpecialRule> WeaponRules,
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
