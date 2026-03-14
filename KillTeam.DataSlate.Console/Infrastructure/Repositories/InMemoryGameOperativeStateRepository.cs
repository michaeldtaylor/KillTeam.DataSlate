using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;

namespace KillTeam.DataSlate.Console.Infrastructure.Repositories;

/// <summary>Ephemeral in-memory operative state store used by the simulate command.</summary>
public class InMemoryGameOperativeStateRepository : IGameOperativeStateRepository
{
    private readonly Dictionary<Guid, GameOperativeState> _states = [];

    public void Seed(IEnumerable<GameOperativeState> states)
    {
        foreach (var s in states)
            _states[s.Id] = s;
    }

    public Task CreateAsync(GameOperativeState state)
    {
        _states[state.Id] = state;
        return Task.CompletedTask;
    }

    public Task<IEnumerable<GameOperativeState>> GetByGameAsync(Guid gameId) =>
        Task.FromResult<IEnumerable<GameOperativeState>>(
            _states.Values.Where(s => s.GameId == gameId).ToList());

    public Task UpdateWoundsAsync(Guid id, int currentWounds)
    {
        if (_states.TryGetValue(id, out var s)) s.CurrentWounds = currentWounds;
        return Task.CompletedTask;
    }

    public Task UpdateOrderAsync(Guid id, Order order)
    {
        if (_states.TryGetValue(id, out var s)) s.Order = order;
        return Task.CompletedTask;
    }

    public Task UpdateGuardAsync(Guid id, bool isOnGuard)
    {
        if (_states.TryGetValue(id, out var s)) s.IsOnGuard = isOnGuard;
        return Task.CompletedTask;
    }

    public Task SetAplModifierAsync(Guid id, int aplModifier)
    {
        if (_states.TryGetValue(id, out var s)) s.AplModifier = aplModifier;
        return Task.CompletedTask;
    }

    public Task SetReadyAsync(Guid id, bool isReady)
    {
        if (_states.TryGetValue(id, out var s)) s.IsReady = isReady;
        return Task.CompletedTask;
    }

    public Task SetIncapacitatedAsync(Guid id, bool isIncapacitated)
    {
        if (_states.TryGetValue(id, out var s)) s.IsIncapacitated = isIncapacitated;
        return Task.CompletedTask;
    }

    public Task SetCounteractUsedAsync(Guid id, bool used)
    {
        if (_states.TryGetValue(id, out var s)) s.HasUsedCounteractThisTurningPoint = used;
        return Task.CompletedTask;
    }

    public IReadOnlyList<GameOperativeState> GetAll() => [.. _states.Values];
}
