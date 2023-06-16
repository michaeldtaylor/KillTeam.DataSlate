using System;
using System.IO;
using System.Threading.Tasks;
using HpbScraper.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using AppConsole = System.Console;

namespace HpbScraper.Console
{
    public static class Program
    {
        private const string AppName = "HPB Scraper";

        public static async Task Main(string[] args)
        {
            AppConsole.Title = AppName;

            var host = CreateHostBuilder(args).Build();

            var availabilityScraper = host.Services.GetRequiredService<HpbAvailabilityScraper>();
            var outputPath = GetOutputPath();

            await availabilityScraper.ExecuteAsync(outputPath);
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
                services.AddSingleton<HpbAvailabilityScraper>();
                services.AddSingleton<HpbPropertyParser>();
                services.AddSingleton<HpbPropertyHtmlWriter>();
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddNLog();
            });

        private static string GetOutputPath()
        {
            var outputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HpbScraper");

            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            return outputPath;
        }
    }
}
