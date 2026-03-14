namespace KillTeam.DataSlate.Domain.Models;
public class BlastTarget
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ActionId { get; set; }
    public Guid TargetOperativeId { get; set; }
    public string OperativeName { get; set; } = string.Empty;
    public int[] DefenderDice { get; set; } = [];
    public int NormalHits { get; set; }
    public int CriticalHits { get; set; }
    public int Blocks { get; set; }
    public int NormalDamageDealt { get; set; }
    public int CriticalDamageDealt { get; set; }
    public bool CausedIncapacitation { get; set; }
}
