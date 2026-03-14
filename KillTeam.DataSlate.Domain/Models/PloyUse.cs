namespace KillTeam.DataSlate.Domain.Models;
public class PloyUse
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TurningPointId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string PloyName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int CpCost { get; set; }
}
