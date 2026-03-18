using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Repositories;

public interface IGameRepository
{
    Task CreateAsync(Game game);

    Task<Game?> GetByIdAsync(Guid id);

    Task<GameHeader?> GetHeaderAsync(Guid gameId);

    Task<IReadOnlyList<GameHistoryEntry>> GetHistoryAsync(string? playerNameFilter = null);

    Task UpdateStatusAsync(Guid gameId, GameStatus status, string? winnerTeamId, int victoryPointsParticipant1, int victoryPointsParticipant2);

    Task UpdateCpAsync(Guid gameId, int commandPointsParticipant1, int commandPointsParticipant2);
}

