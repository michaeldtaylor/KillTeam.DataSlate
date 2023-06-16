public static class HpbHelpers
{
    public static string GetTextFromSearchBedrooms(int? searchBedrooms)
    {
        return searchBedrooms switch
        {
            0 => "Studio",
            1 => "One-bed",
            2 => "Two-bed",
            3 => "Three-bed",
            4 => "Four-bed",
            5 => "Five-bed",
            6 => "Six-bed",
            7 => "Seven-bed",
            8 => "Eight-bed",
            9 => "Fishing",
            _ => "One-bed"
        };
    }
}
