using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Repositories.InMemory;

/// <summary>No-op blast target store used by the simulate command — discards all writes.</summary>
public class InMemoryBlastTargetRepository : IBlastTargetRepository
{
    public Task CreateAsync(BlastTarget target) => Task.CompletedTask;

    public Task<IEnumerable<BlastTarget>> GetByActionIdAsync(Guid actionId) =>
        Task.FromResult<IEnumerable<BlastTarget>>([]);
}
