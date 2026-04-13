namespace Wh40kArmyEnricher.Core.Simulation;

public interface IDiceRoller
{
    int RollD6();
    int Roll(int sides);
    int Roll(DiceExpression expression);
}

public sealed class DiceRoller : IDiceRoller
{
    private readonly Random _rng;

    public DiceRoller(int? seed = null)
    {
        _rng = seed.HasValue ? new Random(seed.Value) : Random.Shared;
    }

    public int RollD6() => _rng.Next(1, 7);

    public int Roll(int sides) => _rng.Next(1, sides + 1);

    public int Roll(DiceExpression expression)
    {
        if (expression.Count == 0)
            return expression.Modifier;

        int total = 0;
        for (int i = 0; i < expression.Count; i++)
            total += _rng.Next(1, expression.Sides + 1);
        return total + expression.Modifier;
    }
}
