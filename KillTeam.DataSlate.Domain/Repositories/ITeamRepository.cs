using Models = KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Repositories;

public interface ITeamRepository
{
    Task UpsertAsync(Models.Team team);

    Task<IEnumerable<Models.Team>> GetAllAsync();

    Task<Models.Team?> GetByNameAsync(string name);

    Task<Models.Team?> GetWithOperativesAsync(string name);
}
