using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Repositories;

public interface IPloyRepository
{
    Task RecordPloyUseAsync(PloyUse ploy);
    Task<IEnumerable<PloyUse>> GetByTurningPointAsync(Guid turningPointId);
}
