using System;

namespace HpbScraper.Domain;

public static class HpbConstants
{
    public static readonly Uri BaseUri = new("https://mybond.hpb.co.uk/");

    public const string LoginPage = "login";

    public const string AvailabilityPage = "Properties/Availability/Week/";
}
