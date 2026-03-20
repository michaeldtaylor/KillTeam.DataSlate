using System.ComponentModel;
using KillTeam.DataSlate.Console.Orchestrators;
using Spectre.Console.Cli;

namespace KillTeam.DataSlate.Console.Commands;

/// <summary>Run ad-hoc fight or shoot encounters to test operatives and weapons without starting a full game.</summary>
[Description("Simulate fight/shoot encounters without a saved game.")]
public class SimulateCommand(SimulateOrchestrator orchestrator) : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        await orchestrator.RunAsync();

        return 0;
    }
}