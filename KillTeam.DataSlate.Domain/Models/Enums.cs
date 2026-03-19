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

public enum WeaponRuleKind
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
    Limited,
    Piercing,
    PiercingCrits,
    Punishing,
    Range,
    Relentless,
    Rending,
    Saturate,
    Seek,
    SeekLight,
    Severe,
    Shock,
    Silent,
    Stun,
    Torrent,
    Unknown,
}

public enum WeaponRulePhase
{
    Fight,
    Shoot,
    Both,
}

public enum DieResult
{
    Crit,
    Hit,
    Miss,
    Save,
    Fail,
}

public enum DieOwner
{
    Attacker,
    Target,
}

public enum FightActionType
{
    Strike,
    Block,
}
