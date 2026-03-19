namespace KillTeam.DataSlate.Domain.Events;

public class GameEventStream(Guid gameSessionId, Func<GameEvent, Task>? persistenceHandler = null)
{
    private int _sequenceNumber;

    public Guid GameSessionId { get; } = gameSessionId;

    public event Action<GameEvent>? OnEventEmitted;

    public async ValueTask EmitAsync(GameEvent gameEvent)
    {
        OnEventEmitted?.Invoke(gameEvent);

        if (persistenceHandler is not null)
        {
            await persistenceHandler(gameEvent);
        }
    }

    public async ValueTask EmitAsync(Func<Guid, int, DateTime, GameEvent> factory)
    {
        var gameEvent = factory(GameSessionId, _sequenceNumber++, DateTime.UtcNow);

        await EmitAsync(gameEvent);
    }
}
