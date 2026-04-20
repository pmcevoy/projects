namespace Wh40kArmyEnricher.Core.Simulation;

public static class AbilityProcessor
{
    /// <summary>Returns the minimum D6 roll required to wound, based on S vs T.</summary>
    public static int WoundThreshold(int strength, int toughness)
    {
        if (strength >= 2 * toughness) return 2;
        if (strength > toughness)      return 3;
        if (strength == toughness)     return 4;
        if (strength > toughness / 2)  return 5;
        return 6;
    }


}
