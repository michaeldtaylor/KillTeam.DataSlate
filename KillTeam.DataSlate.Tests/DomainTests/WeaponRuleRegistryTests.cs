using FluentAssertions;
using KillTeam.DataSlate.Domain.Models;
using Xunit;

namespace KillTeam.DataSlate.Tests.DomainTests;

public class WeaponRuleRegistryTests
{
    [Fact]
    public void Registry_ContainsAllKnownKinds_ExceptUnknown()
    {
        var allKinds = Enum.GetValues<WeaponRuleKind>()
            .Where(k => k != WeaponRuleKind.Unknown)
            .ToList();

        foreach (var kind in allKinds)
        {
            WeaponRuleRegistry.ByKind.Should().ContainKey(kind, $"{kind} should have a registry entry");
        }
    }

    [Fact]
    public void Registry_AllEntries_HaveNonEmptyDescription()
    {
        foreach (var (kind, definition) in WeaponRuleRegistry.ByKind)
        {
            definition.Description.Should().NotBeNullOrWhiteSpace($"{kind} should have a description");
        }
    }

    [Fact]
    public void Registry_AllEntries_HaveValidPhase()
    {
        var validPhases = Enum.GetValues<WeaponRulePhase>();

        foreach (var (kind, definition) in WeaponRuleRegistry.ByKind)
        {
            validPhases.Should().Contain(definition.Phase, $"{kind} should have a valid phase");
        }
    }

    [Fact]
    public void WeaponRule_Definition_ReturnsRegistryEntry_ForKnownKind()
    {
        var rule = new WeaponRule(WeaponRuleKind.Range, 8);

        rule.Definition.Should().NotBeNull();
        rule.Definition!.Kind.Should().Be(WeaponRuleKind.Range);
        rule.Definition.Phase.Should().Be(WeaponRulePhase.Shoot);
    }

    [Fact]
    public void WeaponRule_Definition_ReturnsNull_ForUnknownKind()
    {
        var rule = new WeaponRule(WeaponRuleKind.Unknown, null);

        rule.Definition.Should().BeNull();
    }
}
