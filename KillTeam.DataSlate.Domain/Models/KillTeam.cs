namespace KillTeam.DataSlate.Domain.Models;
public class KillTeam
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Faction { get; set; } = string.Empty;
    public List<Operative> Operatives { get; set; } = [];
}
