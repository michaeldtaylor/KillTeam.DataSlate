namespace KillTeam.DataSlate.Domain.Models;

public enum GameStatus
{
    InProgress,
    Completed,
}

public enum Order
{
    Engage,
    Conceal,
}

public enum ActionType
{
    Reposition,
    Dash,
    FallBack,
    Charge,
    Shoot,
    Fight,
    Guard,
    Counteract,
    PickUp,
    UseEquipment,
    UniqueAction,
    Other,
}

public enum WeaponType
{
    Ranged,
    Melee,
}

public enum SpecialRuleKind
{
    Accurate,
    Balanced,
    Blast,
    Brutal,
    Ceaseless,
    Devastating,
    Heavy,
    HeavyDashOnly,
    Hot,
    Lethal,
    Limited,       // parsed but not yet resolved
    Piercing,
    PiercingCrits,
    Punishing,
    Range,         // enforced at weapon-selection time (not yet implemented): weapon unusable beyond X inches
    Relentless,
    Rending,
    Saturate,
    Seek,          // parsed but not yet resolved
    SeekLight,     // parsed but not yet resolved
    Severe,
    Shock,
    Silent,        // parsed but not yet resolved
    Stun,
    Torrent,
    Unknown,
}

