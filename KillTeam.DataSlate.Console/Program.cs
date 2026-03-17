using KillTeam.DataSlate.Console.Commands;
using KillTeam.DataSlate.Infrastructure;
using KillTeam.DataSlate.Console.InputProviders;
using KillTeam.DataSlate.Console.Orchestrators;
using KillTeam.DataSlate.Infrastructure.Repositories;
using KillTeam.DataSlate.Infrastructure.Services;
using KillTeam.DataSlate.Domain;
using KillTeam.DataSlate.Domain.Engine;
using KillTeam.DataSlate.Domain.Engine.Input;
using KillTeam.DataSlate.Domain.Repositories;
using KillTeam.DataSlate.Domain.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

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

        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(config);
        services.Configure<DataSlateOptions>(config.GetSection("DataSlate"));

        services.AddSingleton<IAnsiConsole>(AnsiConsole.Console);

        services.AddSingleton<DatabaseInitialiser>();

        var sqlExecutor = new SqliteExecutor(config);
        services.AddSingleton<ISqlExecutor>(sqlExecutor);
        services.AddSingleton<IPlayerRepository>(new SqlitePlayerRepository(sqlExecutor));
        services.AddSingleton<ITeamRepository>(new SqliteTeamRepository(sqlExecutor));
        services.AddSingleton<IGameRepository>(new SqliteGameRepository(sqlExecutor));
        services.AddSingleton<IGameOperativeStateRepository>(new SqliteGameOperativeStateRepository(sqlExecutor));
        services.AddSingleton<IActivationRepository>(new SqliteActivationRepository(sqlExecutor));
        services.AddSingleton<ITurningPointRepository>(new SqliteTurningPointRepository(sqlExecutor));
        services.AddSingleton<IPloyRepository>(new SqlitePloyRepository(sqlExecutor));
        services.AddSingleton<IBlastTargetRepository>(new SqliteBlastTargetRepository(sqlExecutor));
        services.AddSingleton<IActionRepository>(new SqliteActionRepository(sqlExecutor));
        services.AddSingleton(new SqliteOperativeRepository(sqlExecutor));
        services.AddSingleton(new SqliteWeaponRepository(sqlExecutor));
        services.AddSingleton<TeamJsonImporter>();
        services.AddSingleton<TeamYamlImporter>();
        services.AddSingleton<IFightInputProvider, ConsoleFightInputProvider>();
        services.AddSingleton<IShootInputProvider, ConsoleShootInputProvider>();
        services.AddSingleton<IRerollInputProvider, ConsoleRerollInputProvider>();
        services.AddSingleton<IBlastInputProvider, ConsoleBlastInputProvider>();
        services.AddSingleton<IStrategyPhaseInputProvider, ConsoleStrategyPhaseInputProvider>();
        services.AddSingleton<IGuardInterruptInputProvider, ConsoleGuardInterruptInputProvider>();
        services.AddSingleton<RerollEngine>();
        services.AddSingleton<BlastEngine>();
        services.AddSingleton<ShootEngine>();
        services.AddSingleton<FightEngine>();
        services.AddSingleton<StrategyPhaseEngine>();
        services.AddSingleton<GuardInterruptEngine>();
        services.AddSingleton<CombatResolutionService>();
        services.AddSingleton<FightResolutionService>();
        services.AddSingleton<GuardResolutionService>();
        services.AddSingleton<StrategyPhaseOrchestrator>();
        services.AddSingleton<GuardInterruptOrchestrator>();
        services.AddSingleton<FirefightPhaseOrchestrator>();
        services.AddSingleton<SimulateSessionOrchestrator>();

        var initialiser = new DatabaseInitialiser(config);
        await initialiser.InitialiseAsync();

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
