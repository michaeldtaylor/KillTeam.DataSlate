using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CsvHelper;

namespace HpbScraper.Domain
{
    public static class HpbPropertyCsvWriter
    {
        public static void Write(string filePath, List<HpbPropertyCsvOutput> hpbPropertyCsvOutputs)
        {
            var maxStocks = hpbPropertyCsvOutputs.Max(c => c.HpbProperties.Count);

            using var writer = new StreamWriter(filePath);
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

            csv.WriteField(string.Empty);

            // Write the header row...
            foreach (var csvOutput in hpbPropertyCsvOutputs)
            {
                csv.WriteField(csvOutput.DateRange);
            }

            csv.NextRecord();

            // Write as many rows as the maximum number of stocks...
            for (var i = 0; i < maxStocks; i++)
            {
                var rowNumber = i + 1;

                csv.WriteField(rowNumber);

                // Write the stock that exists in the current cell...
                foreach (var stockCsvOutput in hpbPropertyCsvOutputs)
                {
                    if (i <= stockCsvOutput.HpbProperties.Count)
                    {
                        csv.WriteField(stockCsvOutput.HpbProperties[i].Name);
                    }
                }

                csv.NextRecord();
            }
        }
    }
}
