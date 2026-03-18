using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Repositories;

public interface IActivationRepository
{
    Task CreateAsync(Activation activation);

    Task<IEnumerable<Activation>> GetByTurningPointAsync(Guid turningPointId);

    Task UpdateNarrativeAsync(Guid id, string? note);
}
