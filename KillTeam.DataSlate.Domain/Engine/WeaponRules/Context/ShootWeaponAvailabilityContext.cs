namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;

public record ShootWeaponAvailabilityContext(
    bool HasMovedNonDash,
    bool IsOnConceal,
    int TargetDistance);
