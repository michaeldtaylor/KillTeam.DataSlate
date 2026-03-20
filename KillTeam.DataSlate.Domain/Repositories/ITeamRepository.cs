using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Repositories;

public interface ITeamRepository
{
    Task UpsertAsync(Team team);

    Task<IEnumerable<TeamSummary>> GetAllAsync();

    Task<Team?> GetByIdAsync(string id);

    Task<TeamSummary?> GetSummaryAsync(string id);

    Task<TeamStats?> GetStatsAsync(string id);
}
