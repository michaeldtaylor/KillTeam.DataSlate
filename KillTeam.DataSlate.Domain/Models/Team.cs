namespace KillTeam.DataSlate.Domain.Models;

public class Team
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string GrandFaction { get; init; } = string.Empty;

    public required string Faction { get; init; }

    public List<Operative> Operatives { get; init; } = [];

    public List<NamedRule> FactionRules { get; init; } = [];

    public List<NamedRule> StrategyPloys { get; init; } = [];

    public List<NamedRule> FirefightPloys { get; init; } = [];

    public List<EquipmentItem> FactionEquipment { get; init; } = [];

    public List<EquipmentItem> UniversalEquipment { get; init; } = [];

    public string OperativeSelectionArchetype { get; init; } = string.Empty;

    public string OperativeSelectionText { get; init; } = string.Empty;

    public string SupplementaryInfo { get; init; } = string.Empty;
}
