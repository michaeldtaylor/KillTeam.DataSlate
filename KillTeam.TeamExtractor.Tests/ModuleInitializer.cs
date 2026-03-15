using System.Runtime.CompilerServices;

namespace KillTeam.TeamExtractor.Tests;

/// <summary>
/// Global Verify configuration applied once per test session.
/// </summary>
public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        // No strict JSON mode — snapshots are YAML format
    }
}
