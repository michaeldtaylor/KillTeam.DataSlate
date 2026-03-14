namespace KillTeam.DataSlate.Domain;

public class DataSlateOptions
{
    public string DatabasePath { get; set; } = "./data/kill-team.db";

    public string RosterFolder { get; set; } = "./rosters/";
}
