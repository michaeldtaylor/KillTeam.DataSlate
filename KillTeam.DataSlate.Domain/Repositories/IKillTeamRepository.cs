using Models = KillTeam.DataSlate.Domain.Models;
namespace KillTeam.DataSlate.Domain.Repositories;
public interface IKillTeamRepository
{
    Task UpsertAsync(Models.KillTeam team);
    Task<IEnumerable<Models.KillTeam>> GetAllAsync();
    Task<Models.KillTeam?> FindByNameAsync(string name);
    Task<Models.KillTeam?> GetWithOperativesAsync(Guid id);
}
