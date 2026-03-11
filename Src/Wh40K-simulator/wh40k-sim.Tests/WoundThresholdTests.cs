using Wh40kSim.Simulation;

namespace Wh40kSim.Tests;

public class WoundThresholdTests
{
    [Theory]
    [InlineData(8,  4, 2)]  // S exactly 2T
    [InlineData(10, 4, 2)]  // S > 2T
    [InlineData(5,  4, 3)]  // S > T
    [InlineData(4,  4, 4)]  // S == T
    [InlineData(3,  4, 5)]  // S < T, S > T/2
    [InlineData(2,  4, 6)]  // S == T/2 → should be 6+
    [InlineData(1,  4, 6)]  // S < T/2
    [InlineData(4,  8, 6)]  // S exactly T/2
    [InlineData(5,  8, 5)]  // S just above T/2
    [InlineData(8,  8, 4)]  // S == T
    [InlineData(9,  8, 3)]  // S > T
    [InlineData(16, 8, 2)]  // S >= 2T
    public void WoundThreshold_ReturnsExpected(int strength, int toughness, int expected)
    {
        Assert.Equal(expected, AbilityProcessor.WoundThreshold(strength, toughness));
    }
}
