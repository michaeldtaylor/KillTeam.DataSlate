using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Repositories;

public interface ITurningPointRepository
{
    Task CreateAsync(TurningPoint turningPoint);

    Task<TurningPoint?> GetCurrentAsync(Guid gameId);

    Task<IReadOnlyList<TurningPointSummary>> GetSummariesByGameAsync(Guid gameId);

    Task CompleteStrategyPhaseAsync(Guid id);
}

