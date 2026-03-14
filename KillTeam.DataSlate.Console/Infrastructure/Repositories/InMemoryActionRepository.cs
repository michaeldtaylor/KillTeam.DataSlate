using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;

namespace KillTeam.DataSlate.Console.Infrastructure.Repositories;

/// <summary>No-op action store used by the simulate command — discards all writes.</summary>
public class InMemoryActionRepository : IActionRepository
{
    public Task<GameAction> CreateAsync(GameAction action) => Task.FromResult(action);

    public Task UpdateNarrativeAsync(Guid id, string? note) => Task.CompletedTask;

    public Task<IEnumerable<GameAction>> GetByActivationAsync(Guid activationId) =>
        Task.FromResult<IEnumerable<GameAction>>([]);
}
