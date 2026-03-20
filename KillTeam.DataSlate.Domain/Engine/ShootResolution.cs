namespace KillTeam.DataSlate.Domain.Engine;

public record ShootResolution(
    int UnblockedCrits,
    int UnblockedNormals,
    int TotalDamage,
    int AttackerRawCritHits,
    bool StunApplied,
    int SelfDamageDealt
);
