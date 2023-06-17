using System.IO;
using System.Threading.Tasks;
using HpbScraper.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using AppConsole = System.Console;

namespace HpbScraper.Console;

public static class Program
{
    private const string AppName = "HPB Scraper";

    public static async Task Main(string[] args)
    {
        AppConsole.Title = AppName;

        await CreateHostBuilder(args)
            .Build()
            .RunAsync();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) => new HostBuilder()
        .UseConsoleLifetime()
        .ConfigureHostConfiguration(config =>
        {
            config.SetBasePath(Directory.GetCurrentDirectory());

            config.AddEnvironmentVariables("DOTNET_");
            config.AddCommandLine(args);
        })
        .ConfigureAppConfiguration((context, config) =>
        {
            context.HostingEnvironment.ApplicationName = AppName;

            config.AddJsonFile("appsettings.json", false, true);
        })
        .ConfigureServices(services =>
        {
            services.AddOptions<HpbScraperOptions>()
                .BindConfiguration("HpbScraper")
                .ValidateDataAnnotations()
                .ValidateOnStart();

            // Configure Services
            services.AddHostedService<HpbPoller>();
            services.AddScoped<HpbAvailabilityScraper>();
            services.AddScoped<HpbPropertyParser>();
            services.AddScoped<HpbHtmlWriter>();
        })
        .ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddNLog();
        });
}
