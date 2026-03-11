using Wh40kSim.Models;
using Wh40kSim.Simulation;

namespace Wh40kSim.Tests;

public class DiceRollerTests
{
    [Fact]
    public void Roll_FixedExpression_ReturnsFixedValue()
    {
        var roller = new DiceRoller(seed: 0);
        var expr = DiceExpression.Fixed(7);
        for (int i = 0; i < 20; i++)
            Assert.Equal(7, roller.Roll(expr));
    }

    [Fact]
    public void RollD6_AlwaysInRange()
    {
        var roller = new DiceRoller(seed: 42);
        for (int i = 0; i < 1000; i++)
        {
            int r = roller.RollD6();
            Assert.InRange(r, 1, 6);
        }
    }

    [Fact]
    public void Roll_2D6_InRange()
    {
        var roller = new DiceRoller(seed: 42);
        var expr = DiceExpression.Parse("2D6");
        for (int i = 0; i < 1000; i++)
            Assert.InRange(roller.Roll(expr), 2, 12);
    }

    [Fact]
    public void Roll_D6Plus2_InRange()
    {
        var roller = new DiceRoller(seed: 42);
        var expr = DiceExpression.Parse("D6+2");
        for (int i = 0; i < 1000; i++)
            Assert.InRange(roller.Roll(expr), 3, 8);
    }

    [Fact]
    public void Roll_D3_InRange()
    {
        var roller = new DiceRoller(seed: 42);
        var expr = DiceExpression.Parse("D3");
        for (int i = 0; i < 1000; i++)
            Assert.InRange(roller.Roll(expr), 1, 3);
    }
}
