using KillTeam.DataSlate.Domain.Models;
namespace KillTeam.DataSlate.Domain.Repositories;
public interface ITurningPointRepository
{
    Task<TurningPoint> CreateAsync(TurningPoint tp);
    Task<TurningPoint?> GetCurrentAsync(Guid gameId);
    Task CompleteStrategyPhaseAsync(Guid id);
    Task<bool> IsStrategyPhaseCompleteAsync(Guid id);
}
