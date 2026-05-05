namespace ProbHammer.Core.Simulation;

public interface IDiceRoller
{
    int Roll(int sides);

    // Rolls each die in expr individually; rerolls once if the result is <= sides/2. Fixed expressions pass through unchanged.
    int RollWithReroll(DiceExpression expr);
}

public sealed class DiceRoller : IDiceRoller
{
    private readonly Random _random;

    public DiceRoller() : this(Random.Shared) { }
    public DiceRoller(Random random) => _random = random;

    public int Roll(int sides) => _random.Next(1, sides + 1);

    public int RollWithReroll(DiceExpression expr)
    {
        if (expr.Count == 0) return expr.Modifier;
        int total = expr.Modifier;
        for (int i = 0; i < expr.Count; i++)
        {
            int roll = Roll(expr.Sides);
            if (roll <= expr.Sides / 2)
                roll = Roll(expr.Sides);
            total += roll;
        }
        return total;
    }
}
