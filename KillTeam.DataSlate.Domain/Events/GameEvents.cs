namespace KillTeam.DataSlate.Domain.Events;

// ── Shared warning event ──────────────────────────────────────────────────────

public enum CombatWarningKind
{
    NoValidTargets,
    NoWeaponsAvailable,
    TargetNotFound,
    Other,
}

public sealed record CombatWarningEvent(
    Guid GameSessionId, int SequenceNumber, DateTime Timestamp, string Participant,
    CombatWarningKind Kind,
    string Message)
    : GameEvent(GameSessionId, SequenceNumber, Timestamp, Participant);

// ── Fight — setup ─────────────────────────────────────────────────────────────

public sealed record FightTargetSelectedEvent(
    Guid GameSessionId, int SequenceNumber, DateTime Timestamp, string Participant,
    string TargetName,
    int CurrentWounds,
    int MaxWounds,
    bool WasAutoSelected)
    : GameEvent(GameSessionId, SequenceNumber, Timestamp, Participant);

public sealed record WeaponSelectedEvent(
    Guid GameSessionId, int SequenceNumber, DateTime Timestamp, string Participant,
    string WeaponName,
    int Attack,
    int Hit,
    int NormalDmg,
    int CritDmg,
    string Role,         // "Attacker" or "Defender"
    bool WasAutoSelected,
    bool IsInjured,
    int EffectiveHit)
    : GameEvent(GameSessionId, SequenceNumber, Timestamp, Participant);

public sealed record DefenderNoMeleeWeaponsEvent(
    Guid GameSessionId, int SequenceNumber, DateTime Timestamp, string Participant,
    string DefenderName)
    : GameEvent(GameSessionId, SequenceNumber, Timestamp, Participant);

public sealed record FightAssistSetEvent(
    Guid GameSessionId, int SequenceNumber, DateTime Timestamp, string Participant,
    int AllyCount,
    int EffectiveHitAfter)
    : GameEvent(GameSessionId, SequenceNumber, Timestamp, Participant);

// ── Dice rolling ──────────────────────────────────────────────────────────────

public sealed record DiceRolledEvent(
    Guid GameSessionId, int SequenceNumber, DateTime Timestamp, string Participant,
    string OperativeName,
    string Role,         // "Attacker" or "Defender"
    string Phase,        // "Fight" or "Shoot"
    int[] Values)
    : GameEvent(GameSessionId, SequenceNumber, Timestamp, Participant);

// ── Reroll events ─────────────────────────────────────────────────────────────

public sealed record BalancedRerollAppliedEvent(
    Guid GameSessionId, int SequenceNumber, DateTime Timestamp, string Participant,
    string OperativeName,
    int DieIndex,
    int OldValue,
    int NewValue)
    : GameEvent(GameSessionId, SequenceNumber, Timestamp, Participant);

public sealed record CeaselessRerollAppliedEvent(
    Guid GameSessionId, int SequenceNumber, DateTime Timestamp, string Participant,
    string OperativeName,
    int DieIndex,
    int OldValue,
    int NewValue)
    : GameEvent(GameSessionId, SequenceNumber, Timestamp, Participant);

public sealed record RelentlessRerollAppliedEvent(
    Guid GameSessionId, int SequenceNumber, DateTime Timestamp, string Participant,
    string OperativeName,
    int DieIndex,
    int OldValue,
    int NewValue)
    : GameEvent(GameSessionId, SequenceNumber, Timestamp, Participant);

public sealed record CpRerollAppliedEvent(
    Guid GameSessionId, int SequenceNumber, DateTime Timestamp, string Participant,
    string OperativeName,
    int DieIndex,
    int OldValue,
    int NewValue,
    int RemainingCp)
    : GameEvent(GameSessionId, SequenceNumber, Timestamp, Participant);

// ── Fight — resolution ────────────────────────────────────────────────────────

public sealed record ShockAppliedEvent(
    Guid GameSessionId, int SequenceNumber, DateTime Timestamp, string Participant,
    string TargetName,
    int DiscardedDieValue)
    : GameEvent(GameSessionId, SequenceNumber, Timestamp, Participant);

public sealed record FightPoolsDisplayedEvent(
    Guid GameSessionId, int SequenceNumber, DateTime Timestamp, string Participant,
    string AttackerName,
    int AttackerWounds,
    int AttackerMaxWounds,
    IReadOnlyList<FightDieSnapshot> AttackerDice,
    string DefenderName,
    int DefenderWounds,
    int DefenderMaxWounds,
    IReadOnlyList<FightDieSnapshot> DefenderDice)
    : GameEvent(GameSessionId, SequenceNumber, Timestamp, Participant);

/// <summary>Snapshot of a single fight die for event transport (no domain type dependency in renderer).</summary>
public sealed record FightDieSnapshot(string Result, int RolledValue); // Result: "CRIT" | "HIT" | "MISS" | "SAVE" | "FAIL"

public sealed record ShootPoolsDisplayedEvent(
    Guid GameSessionId, int SequenceNumber, DateTime Timestamp, string Participant,
    string AttackerName,
    int AttackerWounds,
    int AttackerMaxWounds,
    IReadOnlyList<FightDieSnapshot> AttackerDice,
    string DefenderName,
    int DefenderWounds,
    int DefenderMaxWounds,
    IReadOnlyList<FightDieSnapshot> DefenderDice)
    : GameEvent(GameSessionId, SequenceNumber, Timestamp, Participant);

public sealed record FightStrikeResolvedEvent(
    Guid GameSessionId, int SequenceNumber, DateTime Timestamp, string Participant,
    string ActiveOperativeName,
    string TargetOperativeName,
    int DieValue,
    string DieResult,    // "CRIT" or "HIT"
    int DamageDealt)
    : GameEvent(GameSessionId, SequenceNumber, Timestamp, Participant);

public sealed record FightBlockResolvedEvent(
    Guid GameSessionId, int SequenceNumber, DateTime Timestamp, string Participant,
    string ActiveOperativeName,
    int ActiveDieValue,
    string ActiveDieResult,
    int CancelledDieValue,
    string CancelledDieResult)
    : GameEvent(GameSessionId, SequenceNumber, Timestamp, Participant);

public sealed record IncapacitationEvent(
    Guid GameSessionId, int SequenceNumber, DateTime Timestamp, string Participant,
    string OperativeName,
    string Cause)         // "Fight" | "Shoot" | "SelfDamage"
    : GameEvent(GameSessionId, SequenceNumber, Timestamp, Participant);

public sealed record FightResolvedEvent(
    Guid GameSessionId, int SequenceNumber, DateTime Timestamp, string Participant,
    string AttackerName,
    string DefenderName,
    int AttackerDamageDealt,
    int DefenderDamageDealt,
    bool AttackerCausedIncapacitation,
    bool DefenderCausedIncapacitation)
    : GameEvent(GameSessionId, SequenceNumber, Timestamp, Participant);

// ── Shoot — setup ─────────────────────────────────────────────────────────────

public sealed record ShootTargetSelectedEvent(
    Guid GameSessionId, int SequenceNumber, DateTime Timestamp, string Participant,
    string TargetName,
    int CurrentWounds,
    int MaxWounds,
    bool WasAutoSelected)
    : GameEvent(GameSessionId, SequenceNumber, Timestamp, Participant);

public sealed record CoverStatusSetEvent(
    Guid GameSessionId, int SequenceNumber, DateTime Timestamp, string Participant,
    string TargetName,
    bool InCover,
    bool IsObscured)
    : GameEvent(GameSessionId, SequenceNumber, Timestamp, Participant);

public sealed record CoverSaveNotifiedEvent(
    Guid GameSessionId, int SequenceNumber, DateTime Timestamp, string Participant,
    string TargetName)
    : GameEvent(GameSessionId, SequenceNumber, Timestamp, Participant);

// ── Shoot — resolution ────────────────────────────────────────────────────────

public sealed record ShootResultDisplayedEvent(
    Guid GameSessionId, int SequenceNumber, DateTime Timestamp, string Participant,
    string TargetName,
    int UnblockedCrits,
    int UnblockedNormals,
    int TotalDamage,
    int TargetWoundsAfter,
    int TargetMaxWounds,
    bool InCover,
    bool IsObscured)
    : GameEvent(GameSessionId, SequenceNumber, Timestamp, Participant);

public sealed record StunAppliedEvent(
    Guid GameSessionId, int SequenceNumber, DateTime Timestamp, string Participant,
    string TargetName,
    int AplReduction)
    : GameEvent(GameSessionId, SequenceNumber, Timestamp, Participant);

public sealed record SelfDamageDealtEvent(
    Guid GameSessionId, int SequenceNumber, DateTime Timestamp, string Participant,
    string AttackerName,
    int SelfDamageAmount,
    int RemainingWounds)
    : GameEvent(GameSessionId, SequenceNumber, Timestamp, Participant);

public sealed record ShootResolvedEvent(
    Guid GameSessionId, int SequenceNumber, DateTime Timestamp, string Participant,
    string AttackerName,
    string TargetName,
    int DamageDealt,
    bool CausedIncapacitation)
    : GameEvent(GameSessionId, SequenceNumber, Timestamp, Participant);
