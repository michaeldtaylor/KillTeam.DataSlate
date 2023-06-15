using System;
using System.IO;
using System.Threading.Tasks;
using HpbScraper.Domain;

namespace HpbScraper.Console
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            var options = new HpbOptions();
            var availabilityScraper = new AvailabilityScraper(options);
            var outputPath = GetOutputPath();

            await availabilityScraper.ExecuteAsync(outputPath);
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
}
