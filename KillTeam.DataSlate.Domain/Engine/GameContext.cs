using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine;

public record GameContext(
    Game Game,
    IReadOnlyDictionary<Guid, OperativeContext> Operatives,
    GameEventStream? EventStream = null);
