using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NLog;

namespace HpbScraper.Domain;

public class HpbPoller : BackgroundService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly HpbScraperOptions _hpbScraperOptions;
    private readonly IServiceProvider _serviceProvider;

    public HpbPoller(IOptions<HpbScraperOptions> hpbScraperOptions, IServiceProvider serviceProvider)
    {
        _hpbScraperOptions = hpbScraperOptions.Value ?? throw new ArgumentNullException(nameof(hpbScraperOptions));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var outputPath = GetOutputPath();
        var checkFrequencyMilliseconds = (int)Math.Round(_hpbScraperOptions.PollFrequencyMinutes!.Value.Minutes().TotalMilliseconds);

        Log.Debug($"Output path: {outputPath}");
        Log.Debug($"HpbPoller background task will poll every {"minute".ToQuantity(_hpbScraperOptions.PollFrequencyMinutes.Value)}");

        cancellationToken.Register(() => Log.Debug("HpbPoller background task stopping..."));

        while (!cancellationToken.IsCancellationRequested)
        {
            using var scoped = _serviceProvider.CreateScope();

            var hpbAvailabilityScraper = scoped.ServiceProvider.GetRequiredService<HpbAvailabilityScraper>();
            var hpbHtmlWriter = scoped.ServiceProvider.GetRequiredService<HpbHtmlWriter>();

            Log.Debug("HpbPoller background task starting...");

            var hpbPropertyGroups = await hpbAvailabilityScraper.ScrapeAsync();

            if (hpbPropertyGroups.Count > 0)
            {
                hpbHtmlWriter.Write(outputPath, hpbPropertyGroups);
            }

            await Task.Delay(checkFrequencyMilliseconds, cancellationToken);
        }
    }

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
