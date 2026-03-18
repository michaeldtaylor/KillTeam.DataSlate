using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Repositories;

public interface ITeamRepository
{
    Task UpsertAsync(Team team);

    Task<IEnumerable<Team>> GetAllAsync();

    Task<Team?> GetByNameAsync(string name);

    Task<Team?> GetWithOperativesAsync(string name);
}
