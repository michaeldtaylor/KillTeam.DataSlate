using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Repositories.InMemory;

/// <summary>No-op action store used by the simulate command — discards all writes.</summary>
public class InMemoryActionRepository : IActionRepository
{
    public Task CreateAsync(GameAction action) => Task.CompletedTask;

    public Task UpdateNarrativeAsync(Guid id, string? note) => Task.CompletedTask;

    public Task<IEnumerable<GameAction>> GetByActivationAsync(Guid activationId) =>
        Task.FromResult<IEnumerable<GameAction>>([]);
}
