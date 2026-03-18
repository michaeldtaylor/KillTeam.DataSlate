namespace KillTeam.DataSlate.Domain.Repositories;

public interface IOperativeRepository
{
    Task<string?> GetNameByIdAsync(Guid id);
}
