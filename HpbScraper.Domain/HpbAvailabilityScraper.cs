using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using NLog;

namespace HpbScraper.Domain;

public class HpbAvailabilityScraper
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly HpbScraperOptions _hpbScraperOptions;
    private readonly HpbPropertyParser _hpbPropertyParser;
    private readonly HpbPropertyHtmlWriter _hpbPropertyHtmlWriter;
        
    public HpbAvailabilityScraper(
        IOptions<HpbScraperOptions> hpbScraperOptions,
        HpbPropertyParser hpbPropertyParser,
        HpbPropertyHtmlWriter hpbPropertyHtmlWriter)
    {
        _hpbScraperOptions = hpbScraperOptions.Value ?? throw new ArgumentNullException(nameof(hpbScraperOptions));
        _hpbPropertyParser = hpbPropertyParser ?? throw new ArgumentNullException(nameof(hpbPropertyParser));
        _hpbPropertyHtmlWriter = hpbPropertyHtmlWriter ?? throw new ArgumentNullException(nameof(hpbPropertyHtmlWriter));
    }

    public async Task ScrapeAsync(string outputPath)
    {
        using var playwright = await Playwright.CreateAsync();

        var options = new BrowserTypeLaunchOptions
        {
            Headless = !_hpbScraperOptions.ShowBrowser,
        };

        if (options.Headless == false)
        {
            options.SlowMo = 50;
        }

        await using var browser = await playwright.Chromium.LaunchAsync(options);

        var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await LoginAsync(page);
        await ConfigureSearchFilter(page);

        var viewAllElementIds = await GetAllViewAllButtonIdsAsync(page);

        if (viewAllElementIds.Count == 0)
        {
            Log.Info("There were no HPB properties matching the search criteria");
        }
        else
        {
            var hpbPropertyMap = new Dictionary<string, List<HpbProperty>>();

            for (var i = 0; i < viewAllElementIds.Count; i++)
            {
                try
                {
                    var viewAllElementId = viewAllElementIds[i];
                    var viewAllButton = page.Locator("#" + viewAllElementId);

                    await viewAllButton.ClickAsync();

                    var dateRange = await page
                        .Locator("#ContentPlaceHolder1_QuickSearchControl_YearWeekChoice")
                        .EvaluateAsync<string>("sel => sel.options[sel.options.selectedIndex].textContent");

                    Log.Info($"Processing HPB properties from {dateRange} ({i + 1} of {viewAllElementIds.Count})");

                    var contentArea = await page
                        .Locator("#ContentPlaceHolder1_ThePage")
                        .EvaluateAsync<string>("el => el.innerHTML");

                    var hpbProperties = _hpbPropertyParser.Parse(contentArea);

                    if (hpbProperties.Count > 0)
                    {
                        hpbPropertyMap.Add(dateRange, hpbProperties);
                    }
                }
                finally
                {
                    await page.GoBackAsync();
                }
            }

            _hpbPropertyHtmlWriter.Write(outputPath, hpbPropertyMap);
        }
    }

    private static async Task<IReadOnlyList<string>> GetAllViewAllButtonIdsAsync(IPage page)
    {
        var elementIds = await page
            .Locator("a[id*='ViewAll']")
            .EvaluateAllAsync<string[]>("link => link.map(el => el.id)");

        return elementIds;
    }

    private async Task ConfigureSearchFilter(IPage page)
    {
        await page.GotoAsync(new Uri(HpbConstants.BaseUri, HpbConstants.AvailabilityPage).AbsoluteUri);

        await page.Locator("#ContentPlaceHolder1_QuickSearchControl_QuickSearchProperties2").SelectOptionAsync(new[] { "ALLBONDUK^^%" });
        await page.Locator("#ContentPlaceHolder1_QuickSearchControl_QuickSearchSleeps").SelectOptionAsync(new[] { _hpbScraperOptions.SearchSleeps!.Value.ToString() });
        await page.Locator("#ContentPlaceHolder1_QuickSearchControl_QuickSearchType").SelectOptionAsync(new[] { _hpbScraperOptions.SearchBedrooms!.Value.ToString() });

        if (_hpbScraperOptions.SearchShortNotice == true)
        {
            await page.Locator("#ContentPlaceHolder1_QuickSearchControl_QuickSearchShortNotice").CheckAsync();
        }

        await page.Locator("#ContentPlaceHolder1_QuickSearchControl_QuickSearchPets").CheckAsync();

        Log.Info("Searching for HPB properties matching the search criteria");
        Log.Info("- Filter:          HPB UK properties");
        Log.Info($"- Sleeps:          {_hpbScraperOptions.SearchSleeps}");
        Log.Info($"- Property size:   {HpbHelpers.GetTextFromSearchBedrooms(_hpbScraperOptions.SearchBedrooms)}");
        Log.Info($"- Short notice:    {_hpbScraperOptions.SearchShortNotice}");
        Log.Info("- Pet friendly:    True");

        // Click the Update button
        await page.Locator("#ContentPlaceHolder1_QuickSearchControl_SearchProperties").ClickAsync();
            
        await page.WaitForRequestFinishedAsync();
    }

    private async Task LoginAsync(IPage page)
    {
        await page.GotoAsync(new Uri(HpbConstants.BaseUri, HpbConstants.LoginPage).AbsoluteUri);

        await page.FillAsync("[id=\"ContentPlaceHolder1_BondNo\"]", _hpbScraperOptions.BondNo);
        await page.FillAsync("[id=\"ContentPlaceHolder1_Password\"]", _hpbScraperOptions.Password);

        await page.ClickAsync("a:has-text(\"LOGIN\")");
        await page.WaitForURLAsync(HpbConstants.BaseUri.AbsoluteUri);
    }
}
