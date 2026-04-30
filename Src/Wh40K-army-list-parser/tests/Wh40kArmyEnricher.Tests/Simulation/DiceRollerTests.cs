using FluentAssertions;
using Wh40kArmyEnricher.Core.Simulation;

namespace Wh40kArmyEnricher.Tests.Simulation;

public class DiceRollerTests
{
    [Theory]
    [InlineData(6)]
    [InlineData(3)]
    [InlineData(2)]
    public void Roll_AlwaysWithinRange(int sides)
    {
        var roller = new DiceRoller(new Random(42));
        for (int i = 0; i < 1000; i++)
        {
            int result = roller.Roll(sides);
            result.Should().BeInRange(1, sides);
        }
    }

    [Fact]
    public void RollWithReroll_FixedExpression_ReturnsModifier()
    {
        var roller = new DiceRoller();
        roller.RollWithReroll(DiceExpression.Fixed(5)).Should().Be(5);
    }

    [Fact]
    public void RollWithReroll_LowInitialRoll_RerollsOnce()
    {
        // Seed sequence: first roll = 2 (≤3, will reroll), reroll = 5
        var roller = new SequenceRoller(2, 5);
        int result = roller.RollWithReroll(DiceExpression.Parse("D6"));
        result.Should().Be(5);
    }

    [Fact]
    public void RollWithReroll_HighInitialRoll_DoesNotReroll()
    {
        // First roll = 4 (>3, no reroll needed). If it erroneously rerolled it would take 1.
        var roller = new SequenceRoller(4, 1);
        int result = roller.RollWithReroll(DiceExpression.Parse("D6"));
        result.Should().Be(4);
    }

    [Fact]
    public void RollWithReroll_MultiDice_RollsEachDieSeparately()
    {
        // 2D6: first die = 2 (reroll → 5), second die = 4 (no reroll)
        var roller = new SequenceRoller(2, 5, 4);
        int result = roller.RollWithReroll(DiceExpression.Parse("2D6"));
        result.Should().Be(9); // 5 + 4
    }

    [Fact]
    public void RollWithReroll_D3_ThresholdIsOne()
    {
        // D3 sides/2 = 1; reroll if ≤ 1 (i.e. roll of 1)
        var roller = new SequenceRoller(1, 3);
        int result = roller.RollWithReroll(DiceExpression.Parse("D3"));
        result.Should().Be(3);
    }
}
