using FluentAssertions;
using Wh40kArmyEnricher.Core.Simulation;

namespace Wh40kArmyEnricher.Tests.Simulation;

public class AbilityProcessorTests
{
    [Theory]
    [InlineData(8, 4, 2)]  // S > 2T
    [InlineData(6, 3, 2)]  // S = 2T exactly
    [InlineData(5, 4, 3)]  // S > T
    [InlineData(4, 4, 4)]  // S == T
    [InlineData(3, 4, 5)]  // S < T
    [InlineData(2, 4, 6)]  // 2S <= T  (2*2=4=T)
    [InlineData(1, 4, 6)]  // 2S <= T  (2*1=2 < 4)
    [InlineData(4, 9, 6)]  // 2S=8 < T=9
    [InlineData(5, 10, 6)] // 2S=10 = T=10
    public void WoundThreshold_CorrectTable(int s, int t, int expected)
    {
        AbilityProcessor.WoundThreshold(s, t).Should().Be(expected);
    }

    [Fact]
    public void EffectiveSave_NoInvuln_AppliesAp()
    {
        var defender = new SimDefenderProfile { Save = 3, InvulnerableSave = null };
        AbilityProcessor.EffectiveSave(defender, 2).Should().Be(5); // 3+2=5
    }

    [Fact]
    public void EffectiveSave_InvulnBetter_UsesInvuln()
    {
        var defender = new SimDefenderProfile { Save = 3, InvulnerableSave = 4 };
        // Armour with AP3: 3+3=6; invuln 4 → 4 is better (lower)
        AbilityProcessor.EffectiveSave(defender, 3).Should().Be(4);
    }

    [Fact]
    public void EffectiveSave_ArmourBetter_UsesArmour()
    {
        var defender = new SimDefenderProfile { Save = 2, InvulnerableSave = 5 };
        // Armour with AP0: 2+0=2; invuln 5 → armour better
        AbilityProcessor.EffectiveSave(defender, 0).Should().Be(2);
    }

    [Fact]
    public void EffectiveSave_InvulnEqual_UsesInvuln()
    {
        // Both give same value — invuln path taken (value same, no practical difference)
        var defender = new SimDefenderProfile { Save = 3, InvulnerableSave = 4 };
        // Armour + AP1 = 4, invuln = 4 → invuln is NOT strictly less, so armour is used
        AbilityProcessor.EffectiveSave(defender, 1).Should().Be(4);
    }
}
