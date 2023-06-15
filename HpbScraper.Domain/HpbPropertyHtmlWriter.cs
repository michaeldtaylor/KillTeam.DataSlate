using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace HpbScraper.Domain
{
    public static class HpbPropertyHtmlWriter
    {
        public static Task WriteAsync(string filePath, Dictionary<string, List<HpbProperty>> hpbPropertyMap)
        {
            using var writer = new StreamWriter(filePath);

            writer.WriteLineAsync("<html>");
            writer.WriteLineAsync("<body>");

            foreach (var hpbPropertyLine in hpbPropertyMap)
            {
                writer.WriteLineAsync($"<h1>{hpbPropertyLine.Key}</h1>");
                writer.WriteLineAsync("<ul>");

                foreach (var hpbProperty in hpbPropertyLine.Value)
                {
                    writer.WriteLineAsync($"<li><a href=\"{hpbProperty.Uri}\">{hpbProperty.Name} ({hpbProperty.Location})</a></li>");
                }

                writer.WriteLineAsync("</ul>");
            }

            writer.WriteLineAsync("</html>");
            writer.WriteLineAsync("</body>");

            return Task.CompletedTask;
        }
    }
}
