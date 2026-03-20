using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine;

public record GameContext(
    Game Game,
    IReadOnlyList<GameOperativeState> OperativeStates,
    IReadOnlyDictionary<Guid, Operative> Operatives,
    GameEventStream? EventStream = null);
