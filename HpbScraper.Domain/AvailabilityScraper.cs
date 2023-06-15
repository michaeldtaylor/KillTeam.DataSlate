using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace HpbScraper.Domain
{
    public class AvailabilityScraper
    {
        private readonly HpbOptions _options;

        public AvailabilityScraper(HpbOptions options)
        {
            _options = options;
        }

        public async Task ExecuteAsync(string outputPath)
        {
            using var playwright = await Playwright.CreateAsync();

            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = false,
                SlowMo = 50,
            });

            // UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/105.0.0.0 Safari/537.36",
            var context = await browser.NewContextAsync();
            var page = await context.NewPageAsync();

            await LoginAsync(page);
            await ConfigureSearchFilter(page);

            var viewAllElementIds = await GetAllViewAllButtonIdsAsync(page);

            var hpbPropertyMap = new Dictionary<string, List<HpbProperty>>();

            foreach (var viewAllElementId in viewAllElementIds)
            {
                try
                {
                    var viewAllButton = page.Locator("#" + viewAllElementId);

                    await viewAllButton.ClickAsync();

                    var dateRange = await page
                        .Locator("#ContentPlaceHolder1_QuickSearchControl_YearWeekChoice")
                        .EvaluateAsync<string>("sel => sel.options[sel.options.selectedIndex].textContent");

                    var contentArea = await page
                        .Locator("#ContentPlaceHolder1_ThePage")
                        .EvaluateAsync<string>("el => el.innerHTML");

                    var hpbProperties = HpbPropertyParser.Parse(contentArea, _options);

                    hpbPropertyMap.Add(dateRange, hpbProperties);
                }
                finally
                {
                    await page.GoBackAsync();
                }
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
            await page.GotoAsync(new Uri(_options.BaseUri, _options.Availability).AbsoluteUri);
            await page.Locator("#ContentPlaceHolder1_QuickSearchControl_QuickSearchProperties2").SelectOptionAsync(new[] {"ALLBONDUK^^%"});
            await page.Locator("#ContentPlaceHolder1_QuickSearchControl_QuickSearchSleeps").SelectOptionAsync(new[] {"2"});
            await page.Locator("#ContentPlaceHolder1_QuickSearchControl_QuickSearchType").SelectOptionAsync(new[] {"1"});
            //await page.Locator("#ContentPlaceHolder1_QuickSearchControl_QuickSearchShortNotice").CheckAsync();
            await page.Locator("#ContentPlaceHolder1_QuickSearchControl_QuickSearchPets").CheckAsync();
            await page.Locator("#ContentPlaceHolder1_QuickSearchControl_SearchProperties").ClickAsync();

            await page.WaitForRequestFinishedAsync();
        }

        private async Task LoginAsync(IPage page)
        {
            await page.GotoAsync(_options.BaseUri + _options.Login);

            await page.FillAsync("[id=\"ContentPlaceHolder1_BondNo\"]", _options.BondNo);
            await page.FillAsync("[id=\"ContentPlaceHolder1_Password\"]", _options.Password);

            await page.ClickAsync("a:has-text(\"LOGIN\")");
            await page.WaitForURLAsync(_options.BaseUri.AbsoluteUri);

        }
    }
}
