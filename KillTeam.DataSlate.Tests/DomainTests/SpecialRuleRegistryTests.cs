using FluentAssertions;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Services;
using Xunit;

namespace KillTeam.DataSlate.Tests.DomainTests;

public class SpecialRuleRegistryTests
{
    [Fact]
    public void Registry_ContainsAllKnownKinds_ExceptUnknown()
    {
        var allKinds = Enum.GetValues<SpecialRuleKind>()
            .Where(k => k != SpecialRuleKind.Unknown)
            .ToList();

        foreach (var kind in allKinds)
        {
            SpecialRuleRegistry.ByKind.Should().ContainKey(kind, $"{kind} should have a registry entry");
        }
    }

    [Fact]
    public void Registry_AllEntries_HaveNonEmptyDescription()
    {
        foreach (var (kind, definition) in SpecialRuleRegistry.ByKind)
        {
            definition.Description.Should().NotBeNullOrWhiteSpace($"{kind} should have a description");
        }
    }

    [Fact]
    public void Registry_AllEntries_HaveValidPhase()
    {
        var validPhases = Enum.GetValues<SpecialRulePhase>();

        foreach (var (kind, definition) in SpecialRuleRegistry.ByKind)
        {
            validPhases.Should().Contain(definition.Phase, $"{kind} should have a valid phase");
        }
    }

    [Fact]
    public void WeaponSpecialRule_Definition_ReturnsRegistryEntry_ForKnownKind()
    {
        var rule = new WeaponSpecialRule(SpecialRuleKind.Range, 8, "Range 8\"");

        rule.Definition.Should().NotBeNull();
        rule.Definition!.Kind.Should().Be(SpecialRuleKind.Range);
        rule.Definition.Phase.Should().Be(SpecialRulePhase.Shoot);
    }

    [Fact]
    public void WeaponSpecialRule_Definition_ReturnsNull_ForUnknownKind()
    {
        var rule = new WeaponSpecialRule(SpecialRuleKind.Unknown, null, "Poison 2");

        rule.Definition.Should().BeNull();
    }
}
