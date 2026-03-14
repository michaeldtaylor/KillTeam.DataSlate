namespace KillTeam.DataSlate.Domain.Models;
public class PloyUse
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TurningPointId { get; set; }
    public Guid TeamId { get; set; }
    public string PloyName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int CpCost { get; set; }
}
