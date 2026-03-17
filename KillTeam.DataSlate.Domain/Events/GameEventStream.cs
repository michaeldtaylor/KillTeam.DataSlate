namespace KillTeam.DataSlate.Domain.Events;

/// <summary>
/// Collects game events in order and fires a synchronous callback on each emission.
/// The callback renders immediately in the same call stack — no threading required.
/// </summary>
public class GameEventStream(Guid gameSessionId)
{
    private readonly List<GameEvent> _events = [];
    private int _sequence;

    public Guid GameSessionId { get; } = gameSessionId;

    public event Action<GameEvent>? OnEventEmitted;

    public IReadOnlyList<GameEvent> Events => _events.AsReadOnly();

    public void Emit(GameEvent evt)
    {
        _events.Add(evt);
        OnEventEmitted?.Invoke(evt);
    }

    /// <summary>
    /// Convenience: emit with auto-incremented sequence number and current timestamp.
    /// </summary>
    public void Emit(Func<int, DateTime, GameEvent> factory)
    {
        var evt = factory(++_sequence, DateTime.UtcNow);
        Emit(evt);
    }
}
