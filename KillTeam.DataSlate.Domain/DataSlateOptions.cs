using System.ComponentModel.DataAnnotations;

namespace KillTeam.DataSlate.Domain;

public class DataSlateOptions
{
    [Required]
    public required string DatabasePath { get; init; }

    [Required]
    public required string TeamsFolder { get; init; }
}
