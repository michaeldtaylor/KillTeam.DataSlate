using System.ComponentModel;
using KillTeam.DataSlate.Console.Orchestrators;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace KillTeam.DataSlate.Console.Commands;

/// <summary>Run ad-hoc fight or shoot encounters to test operatives and weapons without starting a full game.</summary>
[Description("Simulate fight/shoot encounters without a saved game.")]
public class SimulateCommand(SimulateSessionOrchestrator orchestrator, ILogger<SimulateCommand> logger) : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        logger.LogInformation("Simulate session started");
        await orchestrator.RunAsync();
        logger.LogInformation("Simulate session ended");
        return 0;
    }
}
