using System;

namespace HpbScraper.Domain
{
    public class HpbOptions
    {
        public Uri BaseUri { get; set; } = new("https://mybond.hpb.co.uk/");

        public string Login { get; set; } = "login";

        public string Availability { get; set; } = "Properties/Availability/Week/";

        public string BondNo { get; set; } = "107851";

        public string Password { get; set; } = "puglia";

        public string[] NamesToExclude { get; set; } = { "Salmon Fishing" };
    }
}
