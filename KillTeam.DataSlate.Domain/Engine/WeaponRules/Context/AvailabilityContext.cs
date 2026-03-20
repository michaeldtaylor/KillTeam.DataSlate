namespace KillTeam.DataSlate.Domain.Engine.WeaponRules.Context;

public record AvailabilityContext(
    bool HasMovedNonDash,
    bool IsOnConceal,
    int TargetDistance);
