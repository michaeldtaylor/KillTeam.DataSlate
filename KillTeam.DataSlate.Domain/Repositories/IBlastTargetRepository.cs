using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Repositories;

public interface IBlastTargetRepository
{
    Task CreateAsync(BlastTarget target);

    Task<IEnumerable<BlastTarget>> GetByActionIdAsync(Guid actionId);
}
