using System.Runtime.CompilerServices;
using VerifyTests;

namespace KillTeam.TeamExtractor.Tests;

/// <summary>
/// Global Verify configuration applied once per test session.
/// </summary>
public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifierSettings.UseStrictJson();
    }
}
