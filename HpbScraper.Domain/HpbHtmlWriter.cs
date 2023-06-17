using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Options;

namespace HpbScraper.Domain;

public class HpbHtmlWriter
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly HpbScraperOptions _hpbScraperOptions;

    public HpbHtmlWriter(IOptions<HpbScraperOptions> hpbScraperOptions)
    {
        _hpbScraperOptions = hpbScraperOptions.Value ?? throw new ArgumentNullException(nameof(hpbScraperOptions));
    }

    public void Write(string outputPath, Dictionary<string, List<HpbProperty>> hpbPropertyMap)
    {
        var filePath = Path.Combine(outputPath, $"HpbScraper-{DateTime.Now:yyyy-MM-dd-hhmmss}.html");

        Log.Info($"Creating file {filePath}...");

        using var writer = new StreamWriter(filePath);

        writer.WriteLine("<html>");
        writer.WriteLine("<head>");
        writer.WriteLine("<title>HPB Scraper Results</title>");
        writer.WriteLine("</head>");

        writer.WriteLine("<body>");

        writer.WriteLine("<h1>HPB properties matching the search criteria</h1>");
        writer.WriteLine("<table>");

        writer.WriteLine("<tr>");
        writer.WriteLine("<td width=\"200\">Filter</td>");
        writer.WriteLine("<td>HPB UK properties</td>");
        writer.WriteLine("</tr>");

        writer.WriteLine("<tr>");
        writer.WriteLine("<td>Sleeps</td>");
        writer.WriteLine($"<td>{_hpbScraperOptions.SearchSleeps}</td>");
        writer.WriteLine("</tr>");

        writer.WriteLine("<tr>");
        writer.WriteLine("<td>Property size</td>");
        writer.WriteLine($"<td>{HpbHelpers.GetTextFromSearchBedrooms(_hpbScraperOptions.SearchBedrooms)}</td>");
        writer.WriteLine("</tr>");

        writer.WriteLine("<tr>");
        writer.WriteLine("<td>Short notice</td>");
        writer.WriteLine($"<td>{_hpbScraperOptions.SearchShortNotice}</td>");
        writer.WriteLine("</tr>");

        writer.WriteLine("<tr>");
        writer.WriteLine("<td>Pet friendly</td>");
        writer.WriteLine("<td>True</td>");
        writer.WriteLine("</tr>");

        writer.WriteLine("</table>");

        foreach (var hpbPropertyLine in hpbPropertyMap)
        {
            writer.WriteLine($"<h2>{hpbPropertyLine.Key}</h2>");
            writer.WriteLine("<ul>");

            foreach (var hpbProperty in hpbPropertyLine.Value)
            {
                writer.WriteLine($"<li><a href=\"{hpbProperty.Uri}\" target=\"_blank\">{hpbProperty.Name} ({hpbProperty.Location})</a></li>");
            }

            writer.WriteLine("</ul>");
        }

        writer.WriteLine("</body>");
        writer.WriteLine("</html>");
    }
}
