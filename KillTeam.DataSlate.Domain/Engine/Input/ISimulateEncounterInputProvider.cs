using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine.Input;

public interface ISimulateEncounterInputProvider
{
    Task<Order> SelectOrderAsync(string operativeName);
}
