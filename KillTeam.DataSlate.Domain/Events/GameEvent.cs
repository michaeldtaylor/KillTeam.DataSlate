namespace KillTeam.DataSlate.Domain.Events;

/// <summary>
/// Base record for all game events. Every event carries the session it belongs to,
/// its position in the stream, and the participant (team ID) who caused it.
/// </summary>
public abstract record GameEvent(
    Guid GameSessionId,
    int SequenceNumber,
    DateTime Timestamp,
    string Participant);
