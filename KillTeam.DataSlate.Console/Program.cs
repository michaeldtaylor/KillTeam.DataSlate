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
                player.AddCommand<PlayerCreateCommand>("create")
                      .WithDescription("Register a new player.");
                player.AddCommand<PlayerListCommand>("list")
                      .WithDescription("List all players with stats.");
                player.AddCommand<PlayerDeleteCommand>("delete")
                      .WithDescription("Remove a player (blocked if they have games).");
            });

            cfg.AddBranch("team", team =>
            {
                team.AddCommand<TeamImportCommand>("import")
                    .WithDescription("Import team files (YAML or JSON) from a file or folder.");
            });

            cfg.AddBranch("game", game =>
            {
                game.AddCommand<GameNewCommand>("new")
                    .WithDescription("Start a new team game.");
                game.AddCommand<GamePlayCommand>("play")
                    .WithDescription("Play a team game (strategy + firefight phases).");
                game.AddCommand<GameViewCommand>("view")
                    .WithDescription("View full detail of a game.");
                game.AddCommand<GameAnnotateCommand>("annotate")
                    .WithDescription("Add narrative notes to activations and actions.");
                game.AddCommand<GameHistoryCommand>("history")
                    .WithDescription("View completed game history.");
                game.AddCommand<GameStatsCommand>("stats")
                    .WithDescription("View player and team statistics.");
                game.AddCommand<GameSimulateCommand>("simulate")
                    .WithDescription("Simulate fight/shoot encounters without a saved game.");
            });
        });

        if (args.Length == 0)
        {
            return await RunReplAsync(app);
        }

        return await app.RunAsync(args);
    }

    private static readonly HashSet<string> ValidContexts = ["player", "team", "game"];

    private static PrettyPrompt.Prompt BuildPrompt(string? context, string historyPath)
    {
        var promptText = context is null ? "ktds> " : $"{context}> ";

        return new PrettyPrompt.Prompt(
            persistentHistoryFilepath: historyPath,
            callbacks: new Completion.KillTeamPromptCallbacks(context),
            configuration: new PrettyPrompt.PromptConfiguration(
                prompt: new PrettyPrompt.Highlighting.FormattedString(
                    promptText,
                    new PrettyPrompt.Highlighting.ConsoleFormat(
                        Foreground: PrettyPrompt.Highlighting.AnsiColor.Cyan,
                        Bold: true))));
    }

    private static async Task<int> RunReplAsync(CommandApp app)
    {
        AnsiConsole.Write(new FigletText("KTDS").Color(Color.Cyan1));
        AnsiConsole.MarkupLine("[dim]Type [bold]/player[/], [bold]/game[/] or [bold]/team[/] to enter a context, or [bold]exit[/] to quit.[/]");
        AnsiConsole.WriteLine();

        // Prevent Ctrl-C from terminating the process; commands handle it as OperationCanceledException.
        System.Console.CancelKeyPress += (_, e) => e.Cancel = true;

        var historyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ktds_history");

        string? context = null;
        var prompt = BuildPrompt(null, historyPath);

        while (true)
        {
            var response = await prompt.ReadLineAsync();

            if (!response.IsSuccess)
            {
                if (context is not null)
                {
                    context = null;
                    prompt = BuildPrompt(null, historyPath);
                    AnsiConsole.WriteLine();

                    continue;
                }

                AnsiConsole.MarkupLine("[dim]Goodbye.[/]");

                return 0;
            }

            var line = response.Text.Trim();

            if (line.Length == 0)
            {
                continue;
            }

            if (line is "exit" or "quit")
            {
                AnsiConsole.MarkupLine("[dim]Goodbye.[/]");

                return 0;
            }

            if (line.StartsWith('/') && context is null)
            {
                var parts = ParseReplArgs(line[1..]).ToArray();
                var noun = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;

                if (ValidContexts.Contains(noun))
                {
                    if (parts.Length == 1)
                    {
                        context = noun;
                        prompt = BuildPrompt(noun, historyPath);
                        AnsiConsole.WriteLine();

                        continue;
                    }

                    await RunCommandSafeAsync(app, parts);
                    AnsiConsole.WriteLine();

                    continue;
                }

                AnsiConsole.MarkupLine($"[red]Unknown context '{Markup.Escape(noun)}'. Try /player, /game or /team.[/]");
                AnsiConsole.WriteLine();

                continue;
            }

            IEnumerable<string> commandArgs = context is not null
                ? new[] { context }.Concat(ParseReplArgs(line))
                : ParseReplArgs(line);

            await RunCommandSafeAsync(app, commandArgs);
            AnsiConsole.WriteLine();
        }
    }

    private static async Task RunCommandSafeAsync(CommandApp app, IEnumerable<string> args)
    {
        try
        {
            await app.RunAsync(args);
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[dim](cancelled)[/]");
        }
    }

    private static IEnumerable<string> ParseReplArgs(string line)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var ch in line)
        {
            switch (ch)
            {
                case '"':
                    inQuotes = !inQuotes;
                    break;
                case ' ' when !inQuotes:
                {
                    if (current.Length > 0)
                    {
                        args.Add(current.ToString());
                        current.Clear();
                    }

                    break;
                }
                default:
                    current.Append(ch);
                    break;
            }
        }

        if (current.Length > 0)
        {
            args.Add(current.ToString());
        }

        return args;
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
