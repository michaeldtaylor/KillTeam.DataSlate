namespace KillTeam.DataSlate.Domain.Models;

public class Activation
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid TurningPointId { get; init; }

    public int SequenceNumber { get; init; }

    public Guid OperativeId { get; init; }

    public required string TeamId { get; init; }

    public Order OrderSelected { get; set; }

    public bool IsCounteract { get; init; }

    public bool IsGuardInterrupt { get; init; }

    public string? NarrativeNote { get; init; }
}
