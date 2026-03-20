using KillTeam.DataSlate.Console.Commands;
using KillTeam.DataSlate.Console.InputProviders;
using KillTeam.DataSlate.Console.Orchestrators;
using KillTeam.DataSlate.Console.Rendering;
using KillTeam.DataSlate.Domain;
using KillTeam.DataSlate.Domain.Engine;
using KillTeam.DataSlate.Domain.Engine.Input;
using KillTeam.DataSlate.Domain.Engine.WeaponRules;
using KillTeam.DataSlate.Domain.Repositories;
using KillTeam.DataSlate.Infrastructure;
using KillTeam.DataSlate.Infrastructure.Repositories;
using KillTeam.DataSlate.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NLog.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel.DataAnnotations;

namespace KillTeam.DataSlate.Console;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        // Validate options and initialise the database before the DI container is built.
        // This gives a clear startup error rather than a cryptic type-resolution failure.
        var rawOptions = config.GetSection("DataSlate").Get<DataSlateOptions>()
            ?? throw new InvalidOperationException("KillTeam: Data Slate configuration section is missing.");

        Validator.ValidateObject(rawOptions, new ValidationContext(rawOptions), validateAllProperties: true);

        await new DatabaseInitialiser(Options.Create(rawOptions)).InitialiseAsync();

        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(config);

        services.AddLogging(logging => logging.AddNLog());

        services
            .AddOptions<DataSlateOptions>()
            .BindConfiguration("DataSlate")
            .ValidateDataAnnotations();

        services.AddSingleton(AnsiConsole.Console);
        services.AddSingleton<ColumnContext>();
        services.AddSingleton<ISqlExecutor, SqliteExecutor>();
        services.AddSingleton<IPlayerRepository, SqlitePlayerRepository>();
        services.AddSingleton<ITeamRepository, SqliteTeamRepository>();
        services.AddSingleton<IGameRepository, SqliteGameRepository>();
        services.AddSingleton<IGameOperativeStateRepository, SqliteGameOperativeStateRepository>();
        services.AddSingleton<IActivationRepository, SqliteActivationRepository>();
        services.AddSingleton<ITurningPointRepository, SqliteTurningPointRepository>();
        services.AddSingleton<IPloyRepository, SqlitePloyRepository>();
        services.AddSingleton<IActionRepository, SqliteActionRepository>();
        services.AddSingleton<IOperativeRepository, SqliteOperativeRepository>();
        services.AddSingleton<TeamJsonImporter>();
        services.AddSingleton<TeamYamlImporter>();
        services.AddSingleton<IFightInputProvider, ConsoleFightInputProvider>();
        services.AddSingleton<IShootInputProvider, ConsoleShootInputProvider>();
        services.AddSingleton<IRerollInputProvider, ConsoleRerollInputProvider>();
        services.AddSingleton<IAoEInputProvider, ConsoleAoEInputProvider>();
        services.AddSingleton<IStrategyPhaseInputProvider, ConsoleStrategyPhaseInputProvider>();
        services.AddSingleton<IGuardInterruptInputProvider, ConsoleGuardInterruptInputProvider>();
        services.AddSingleton<IFirefightInputProvider, ConsoleFirefightInputProvider>();
        services.AddSingleton<ISimulateEncounterInputProvider, ConsoleSimulateEncounterInputProvider>();
        services.AddSingleton<RerollEngine>();
        services.AddSingleton<AoEEngine>();
        services.AddSingleton<ShootWeaponRulePipeline>();
        services.AddSingleton<FightWeaponRulePipeline>();
        services.AddSingleton<ShootEngine>();
        services.AddSingleton<FightEngine>();
        services.AddSingleton<StrategyPhaseEngine>();
        services.AddSingleton<GuardInterruptEngine>();
        services.AddSingleton<SimulateEncounterEngine>();
        services.AddSingleton<FirefightPhaseEngine>();
        services.AddSingleton<IGameStatePersistenceHandler, SqliteGameStatePersistenceHandler>();
        services.AddSingleton<StrategyPhaseOrchestrator>();
        services.AddSingleton<FirefightPhaseOrchestrator>();
        services.AddSingleton<SimulateOrchestrator>();

        var registrar = new MyTypeRegistrar(services);
        var app = new CommandApp(registrar);

        app.Configure(cfg =>
        {
            cfg.AddBranch("player", player =>
            {
                player.AddCommand<PlayerAddCommand>("add")
                      .WithDescription("Register a new player.");
                player.AddCommand<PlayerListCommand>("list")
                      .WithDescription("List all players with stats.");
                player.AddCommand<PlayerDeleteCommand>("delete")
                      .WithDescription("Remove a player (blocked if they have games).");
            });
            cfg.AddCommand<ImportTeamsCommand>("import-teams")
               .WithDescription("Import team files (YAML or JSON) from a file or folder.");
            cfg.AddCommand<NewGameCommand>("new-game").WithDescription("Start a new team game.");
            cfg.AddCommand<HistoryCommand>("history")
               .WithDescription("View completed game history.");
            cfg.AddCommand<StatsCommand>("stats")
               .WithDescription("View player and team statistics.");
            cfg.AddCommand<ViewGameCommand>("view-game")
               .WithDescription("View full detail of a game.");
            cfg.AddCommand<AnnotateCommand>("annotate")
               .WithDescription("Add narrative notes to activations and actions.");
            cfg.AddCommand<PlayCommand>("play")
               .WithDescription("Play a team game (strategy + firefight phases).");
            cfg.AddCommand<SimulateCommand>("simulate")
               .WithDescription("Simulate fight/shoot encounters without a saved game.");
        });

        return await app.RunAsync(args);
    }
}

public sealed class MyTypeRegistrar(IServiceCollection builder) : ITypeRegistrar
{
    public ITypeResolver Build() => new MyTypeResolver(builder.BuildServiceProvider());

    public void Register(Type service, Type implementation) =>
        builder.AddSingleton(service, implementation);

    public void RegisterInstance(Type service, object implementation) =>
        builder.AddSingleton(service, implementation);

    public void RegisterLazy(Type service, Func<object> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        builder.AddSingleton(service, _ => func());
    }
}

public sealed class MyTypeResolver(IServiceProvider provider) : ITypeResolver, IDisposable
{
    public object? Resolve(Type? type) =>
        type == null ? null : provider.GetService(type);

    public void Dispose()
    {
        if (provider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
