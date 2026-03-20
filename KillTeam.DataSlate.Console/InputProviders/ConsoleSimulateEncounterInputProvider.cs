using KillTeam.DataSlate.Domain.Engine.Input;
using KillTeam.DataSlate.Domain.Models;
using Spectre.Console;

namespace KillTeam.DataSlate.Console.InputProviders;

public class ConsoleSimulateEncounterInputProvider(IAnsiConsole console) : ISimulateEncounterInputProvider
{
    public Task<Order> SelectOrderAsync(string operativeName)
    {
        var orderChoice = console.Prompt(
            new SelectionPrompt<string>()
                .Title($"Set order for {Markup.Escape(operativeName)}:")
                .AddChoices("Engage", "Conceal"));

        var order = orderChoice == "Engage" ? Order.Engage : Order.Conceal;

        return Task.FromResult(order);
    }
}
