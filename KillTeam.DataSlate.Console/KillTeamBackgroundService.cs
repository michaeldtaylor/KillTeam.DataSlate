// using System;
// using System.Threading;
// using System.Threading.Tasks;
// using KillTeam.DataSlate.Domain;
// using Microsoft.Extensions.Hosting;
// using Microsoft.Extensions.Options;
// using NLog;
// using Spectre.Console;
// using Spectre.Console.Cli;
//
// namespace KillTeam.DataSlate.Console;
//
// public class KillTeamBackgroundService(IOptions<DataSlateOptions> dataSlateOptions) : BackgroundService
// {
//     private static readonly Logger Log = LogManager.GetCurrentClassLogger();
//
//     private readonly DataSlateOptions _dataSlateOptions = dataSlateOptions.Value ?? throw new ArgumentNullException(nameof(dataSlateOptions));
//
//     /// <inheritdoc/>
//     protected override Task ExecuteAsync(CancellationToken stoppingToken)
//     {
//         var app = new CommandApp();
//
//         app.Configure(config =>
//         {
//             config.AddCommand<CreateGameCommand>("create-game");
//         });
//     }
// }
//
// public class CreateGameCommand : ICommand
// {
//     public ValidationResult Validate(CommandContext context, CommandSettings settings)
//     {
//         throw new NotImplementedException();
//     }
//
//     public Task<int> Execute(CommandContext context, CommandSettings settings)
//     {
//         throw new NotImplementedException();
//     }
// }
