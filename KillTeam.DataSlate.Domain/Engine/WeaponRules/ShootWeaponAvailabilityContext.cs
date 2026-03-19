namespace KillTeam.DataSlate.Domain.Engine.WeaponRules;

public record ShootWeaponAvailabilityContext(
    bool HasMovedNonDash,
    bool IsOnConceal,
    int TargetDistance);
