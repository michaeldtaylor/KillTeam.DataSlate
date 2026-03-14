namespace KillTeam.DataSlate.Domain.Models;

public class BlastTarget
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid ActionId { get; init; }

    public Guid TargetOperativeId { get; init; }

    public string OperativeName { get; init; } = string.Empty;

    public int[] DefenderDice { get; init; } = [];

    public int NormalHits { get; init; }

    public int CriticalHits { get; init; }

    public int Blocks { get; init; }

    public int NormalDamageDealt { get; init; }

    public int CriticalDamageDealt { get; init; }

    public bool CausedIncapacitation { get; init; }
}
