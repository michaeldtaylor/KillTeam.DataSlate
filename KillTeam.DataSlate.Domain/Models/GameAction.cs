namespace KillTeam.DataSlate.Domain.Models;

public class GameAction
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid ActivationId { get; init; }

    public ActionType Type { get; init; }

    public int ApCost { get; init; }

    public Guid? TargetOperativeId { get; init; }

    public Guid? WeaponId { get; init; }

    public int[] AttackerDice { get; init; } = [];

    public int[] TargetDice { get; set; } = [];

    public bool? TargetInCover { get; set; }

    public bool? IsObscured { get; set; }

    public int NormalHits { get; set; }

    public int CriticalHits { get; set; }

    public int Blocks { get; init; }

    public int NormalDamageDealt { get; set; }

    public int CriticalDamageDealt { get; set; }

    public bool CausedIncapacitation { get; set; }

    public int SelfDamageDealt { get; init; }

    public bool StunApplied { get; init; }

    public string? NarrativeNote { get; init; }
}
