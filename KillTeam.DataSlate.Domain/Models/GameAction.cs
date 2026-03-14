namespace KillTeam.DataSlate.Domain.Models;

public class GameAction
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid ActivationId { get; set; }

    public ActionType Type { get; set; }

    public int ApCost { get; set; }

    public Guid? TargetOperativeId { get; set; }

    public Guid? WeaponId { get; set; }

    public int[] AttackerDice { get; set; } = [];

    public int[] DefenderDice { get; set; } = [];

    public bool? TargetInCover { get; set; }

    public bool? IsObscured { get; set; }

    public int NormalHits { get; set; }

    public int CriticalHits { get; set; }

    public int Blocks { get; set; }

    public int NormalDamageDealt { get; set; }

    public int CriticalDamageDealt { get; set; }

    public bool CausedIncapacitation { get; set; }

    public int SelfDamageDealt { get; set; }

    public bool StunApplied { get; set; }

    public string? NarrativeNote { get; set; }
}
