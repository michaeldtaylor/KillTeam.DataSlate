using NLog;
using System.Collections.Generic;
using System.IO;

namespace HpbScraper.Domain
{
    public class HpbPropertyHtmlWriter
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public void Write(string filePath, Dictionary<string, List<HpbProperty>> hpbPropertyMap)
        {
            Log.Info($"Creating file {filePath}...");

            using var writer = new StreamWriter(filePath);

            writer.WriteLine("<html>");
            writer.WriteLine("<head>");
            writer.WriteLine("<title>HPB Scraper Results</title>");
            writer.WriteLine("</head>");
            writer.WriteLine("<body>");

            foreach (var hpbPropertyLine in hpbPropertyMap)
            {
                writer.WriteLine($"<h1>{hpbPropertyLine.Key}</h1>");
                writer.WriteLine("<ul>");

                foreach (var hpbProperty in hpbPropertyLine.Value)
                {
                    writer.WriteLine($"<li><a href=\"{hpbProperty.Uri}\">{hpbProperty.Name} ({hpbProperty.Location})</a></li>");
                }

                writer.WriteLine("</ul>");
            }

            writer.WriteLine("</body>");
            writer.WriteLine("</html>");
        }
    }
}
