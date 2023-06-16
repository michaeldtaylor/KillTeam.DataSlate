using System;
using System.ComponentModel.DataAnnotations;

namespace HpbScraper.Domain;

public class HpbScraperOptions
{
    [Required]
    public string? BondNo { get; set; }

    [Required]
    public string? Password { get; set; }

    [Required]
    public bool? SearchShortNotice { get; set; }

    [Required]
    [Range(1, 16, ErrorMessage = "You can sleep between 1-16 people")]
    public int? SearchSleeps { get; set; }

    [Required]
    [Range(0, 8, ErrorMessage = "You can choose between 0 (Studio) and 8 bedrooms")]
    public int? SearchBedrooms { get; set; }

    public bool? ShowBrowser { get; set; }

    public string[] PropertyNamesToExclude { get; set; } = Array.Empty<string>();
}

