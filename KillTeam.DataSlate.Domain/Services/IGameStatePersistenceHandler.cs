using KillTeam.DataSlate.Domain.Events;

namespace KillTeam.DataSlate.Domain.Services;

public interface IGameStatePersistenceHandler
{
    Task HandleAsync(GameEvent gameEvent);
}
