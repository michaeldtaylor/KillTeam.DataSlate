using KillTeam.DataSlate.Domain.Events;

namespace KillTeam.DataSlate.Domain.Repositories;

public interface IGameStatePersistenceHandler
{
    Task HandleAsync(GameEvent gameEvent);
}
