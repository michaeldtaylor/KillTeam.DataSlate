namespace KillTeam.DataSlate.Domain.Models;

public enum GameStatus { InProgress, Completed }

public enum Order { Engage, Conceal }

public enum ActionType { Reposition, Dash, FallBack, Charge, Shoot, Fight, Guard, Other }

public enum WeaponType { Ranged, Melee }

public enum SpecialRuleKind
{
    Accurate, Balanced, Blast, Brutal, Ceaseless, Devastating, DDevastating,
    Heavy, HeavyDashOnly, Hot, Lethal, Limited, Piercing, PiercingCrits,
    Punishing, Range, Relentless, Rending, Saturate, Seek, SeekLight,
    Severe, Shock, Silent, Stun, Torrent, Unknown
}
