namespace KillTeam.DataSlate.Domain.Models;
public class Player
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
}
