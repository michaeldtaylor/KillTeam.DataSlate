namespace KillTeam.DataSlate.Domain.Models;
public class Operative
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid KillTeamId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string OperativeType { get; set; } = string.Empty;
    public int Move { get; set; }
    public int Apl { get; set; }
    public int Wounds { get; set; }
    public int Save { get; set; }
    public string[] Equipment { get; set; } = [];
    public List<Weapon> Weapons { get; set; } = [];
}
