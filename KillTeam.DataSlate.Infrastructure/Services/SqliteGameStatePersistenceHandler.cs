using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Repositories;

namespace KillTeam.DataSlate.Infrastructure.Services;

public class SqliteGameStatePersistenceHandler(
    IGameOperativeStateRepository stateRepository,
    IGameRepository gameRepository)
    : IGameStatePersistenceHandler
{
    public async Task HandleAsync(GameEvent gameEvent)
    {
        switch (gameEvent)
        {
            case OperativeWoundsChangedEvent e:
                await stateRepository.UpdateWoundsAsync(e.OperativeStateId, e.NewWounds);
                break;

            case OperativeIncapacitatedEvent e:
                await stateRepository.SetIncapacitatedAsync(e.OperativeStateId, true);
                await stateRepository.UpdateGuardAsync(e.OperativeStateId, false);
                break;

            case OperativeGuardClearedEvent e:
                await stateRepository.UpdateGuardAsync(e.OperativeStateId, false);
                break;

            case GameCommandPointsChangedEvent e:
                await gameRepository.UpdateCommandPointsAsync(e.GameId, e.NewCommandPointsParticipant1, e.NewCommandPointsParticipant2);
                break;
        }
    }
}
