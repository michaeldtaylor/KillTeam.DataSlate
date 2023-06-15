using System.Collections.Generic;

namespace HpbScraper.Domain
{
    public class HpbPropertyCsvOutput
    {
        public HpbPropertyCsvOutput(string dateRange)
        {
            DateRange = dateRange;
        }

        public string DateRange { get; }

        public List<HpbProperty> HpbProperties { get; set; } = new();
    }
}
