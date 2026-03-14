using KillTeam.DataSlate.Domain.Models;
namespace KillTeam.DataSlate.Domain.Repositories;
public interface IGameRepository
{
    Task<Game> CreateAsync(Game game);
    Task<Game?> GetByIdAsync(Guid id);
    Task UpdateStatusAsync(Guid gameId, GameStatus status, string? winnerTeamName, int vpTeamA, int vpTeamB);
    Task UpdateCpAsync(Guid gameId, int cpTeamA, int cpTeamB);
}
