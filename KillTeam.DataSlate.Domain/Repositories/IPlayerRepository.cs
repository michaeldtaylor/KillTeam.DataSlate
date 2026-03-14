using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Repositories;

public interface IPlayerRepository
{
    Task AddAsync(Player player);
    Task<IEnumerable<Player>> GetAllAsync();
    Task DeleteAsync(Guid id);
    Task<Player?> FindByNameAsync(string name);
}
