using Wh40kSim.Models;

namespace Wh40kSim.Tests;

public class DiceExpressionTests
{
    [Theory]
    [InlineData("1",     0, 0, 1)]
    [InlineData("10",    0, 0, 10)]
    [InlineData("D6",    1, 6, 0)]
    [InlineData("D3",    1, 3, 0)]
    [InlineData("2D6",   2, 6, 0)]
    [InlineData("D6+2",  1, 6, 2)]
    [InlineData("2D3+1", 2, 3, 1)]
    [InlineData("3D6-1", 3, 6, -1)]
    [InlineData("d6",    1, 6, 0)]  // case insensitive
    public void Parse_ValidExpression_ReturnsExpected(string input, int count, int sides, int modifier)
    {
        var expr = DiceExpression.Parse(input);
        Assert.Equal(count, expr.Count);
        Assert.Equal(sides, expr.Sides);
        Assert.Equal(modifier, expr.Modifier);
    }

    [Theory]
    [InlineData("")]
    [InlineData("XD6")]
    [InlineData("D7")]
    [InlineData("D0")]
    [InlineData("abc")]
    public void Parse_InvalidExpression_Throws(string input)
    {
        Assert.Throws<ArgumentException>(() => DiceExpression.Parse(input));
    }

    [Fact]
    public void Fixed_ReturnsCorrectValue()
    {
        var expr = DiceExpression.Fixed(5);
        Assert.Equal(0, expr.Count);
        Assert.Equal(0, expr.Sides);
        Assert.Equal(5, expr.Modifier);
    }

    [Fact]
    public void ToString_Fixed_ReturnsNumber()
    {
        Assert.Equal("3", DiceExpression.Fixed(3).ToString());
    }

    [Fact]
    public void ToString_D6_ReturnsD6()
    {
        Assert.Equal("D6", DiceExpression.Parse("D6").ToString());
    }

    [Fact]
    public void ToString_2D6Plus1_ReturnsCorrect()
    {
        Assert.Equal("2D6+1", DiceExpression.Parse("2D6+1").ToString());
    }
}
