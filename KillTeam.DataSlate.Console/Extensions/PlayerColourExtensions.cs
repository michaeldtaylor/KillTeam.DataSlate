using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Console.Extensions;

public static class PlayerColourExtensions
{
    public static string ToMarkupString(this PlayerColour colour)
    {
        return colour.ToString().ToLowerInvariant();
    }
}
