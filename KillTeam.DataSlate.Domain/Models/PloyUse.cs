namespace KillTeam.DataSlate.Domain.Models;

public class PloyUse
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid TurningPointId { get; init; }

    public required string TeamName { get; init; }

    public required string PloyName { get; init; }

    public string? Description { get; init; }

    public int CpCost { get; init; }
}
