namespace Wh40kArmyEnricher.Core.Simulation;

public interface IDiceRoller
{
    int RollD6();

    int Roll(DiceExpression expression);
    /// <summary>
    /// Rolls each die in the expression independently, rerolling once if the result is at or below
    /// sides/2 (D6: reroll 1–3; D3: reroll 1). Fixed expressions are returned as-is.
    /// </summary>
    int RollWithReroll(DiceExpression expression);
}

public sealed class DiceRoller : IDiceRoller
{
    private readonly Random _rng;

    public DiceRoller(int? seed = null)
    {
        _rng = seed.HasValue ? new Random(seed.Value) : Random.Shared;
    }

    public int RollD6() => _rng.Next(1, 7);

    public int Roll(DiceExpression expression)
    {
        if (expression.Count == 0)
            return expression.Modifier;

        int total = 0;
        for (int i = 0; i < expression.Count; i++)
            total += _rng.Next(1, expression.Sides + 1);
        return total + expression.Modifier;
    }

    public int RollWithReroll(DiceExpression expression)
    {
        if (expression.Count == 0)
            return expression.Modifier;

        int rerollThreshold = expression.Sides / 2; // D6 → 3, D3 → 1
        int total = 0;
        for (int i = 0; i < expression.Count; i++)
        {
            int d = _rng.Next(1, expression.Sides + 1);
            if (d <= rerollThreshold)
                d = _rng.Next(1, expression.Sides + 1);
            total += d;
        }
        return total + expression.Modifier;
    }
}
