using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Repositories;

public interface ITeamRepository
{
    Task UpsertAsync(Team team);

    Task<IEnumerable<Team>> GetAllAsync();

    Task<Team?> GetByIdAsync(string id);

    Task<Team?> GetWithOperativesAsync(string id);
}
