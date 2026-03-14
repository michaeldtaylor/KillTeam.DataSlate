namespace KillTeam.DataSlate.Domain.Models;

public class PloyUse
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid TurningPointId { get; set; }

    public required string TeamName { get; set; }

    public required string PloyName { get; set; }

    public string? Description { get; set; }

    public int CpCost { get; set; }
}
