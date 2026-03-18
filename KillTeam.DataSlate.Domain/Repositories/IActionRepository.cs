using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Repositories;

public interface IActionRepository
{
    Task CreateAsync(GameAction action);

    Task UpdateNarrativeAsync(Guid id, string? note);

    Task<IEnumerable<GameAction>> GetByActivationAsync(Guid activationId);
}
