namespace KillTeam.DataSlate.Domain.Models;
public class Activation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TurningPointId { get; set; }
    public int SequenceNumber { get; set; }
    public Guid OperativeId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public Order OrderSelected { get; set; }
    public bool IsCounteract { get; set; }
    public bool IsGuardInterrupt { get; set; }
    public string? NarrativeNote { get; set; }
}
