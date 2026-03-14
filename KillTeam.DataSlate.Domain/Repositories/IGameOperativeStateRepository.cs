using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Repositories;

public interface IGameOperativeStateRepository
{
    Task CreateAsync(GameOperativeState state);

    Task<IEnumerable<GameOperativeState>> GetByGameAsync(Guid gameId);

    Task UpdateWoundsAsync(Guid id, int currentWounds);

    Task UpdateOrderAsync(Guid id, Order order);

    Task UpdateGuardAsync(Guid id, bool isOnGuard);

    Task SetAplModifierAsync(Guid id, int aplModifier);

    Task SetReadyAsync(Guid id, bool isReady);

    Task SetIncapacitatedAsync(Guid id, bool isIncapacitated);

    Task SetCounteractUsedAsync(Guid id, bool used);
}
