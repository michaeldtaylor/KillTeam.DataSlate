using System;
using System.ComponentModel.DataAnnotations;

namespace HpbScraper.Domain
{
    public class HpbScraperOptions
    {
        [Required]
        public string? BondNo { get; set; }

        [Required]
        public string? Password { get; set; }

        [Required]
        public string? PropertySize { get; set; }

        [Required]
        public bool? ShortNotice { get; set; }
        
        public bool? Headless { get; set; }

        public string[] PropertyNamesToExclude { get; set; } = Array.Empty<string>();
    }
}
