using ProbHammer.Core.Simulation;

namespace ProbHammer.Tests.Simulation;

// Test helper: returns a predetermined sequence of rolls.
internal sealed class SequenceRoller : IDiceRoller
{
    private readonly Queue<int> _rolls;

    public SequenceRoller(params int[] rolls) => _rolls = new Queue<int>(rolls);

    public int Roll(int sides)
    {
        if (_rolls.Count == 0)
            throw new InvalidOperationException("SequenceRoller exhausted — not enough rolls provided for this test.");
        return _rolls.Dequeue();
    }

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

// Test helper: always returns the same value for every roll.
internal sealed class ConstantRoller : IDiceRoller
{
    private readonly int _value;
    public ConstantRoller(int value) => _value = value;

    public int Roll(int sides) => _value;

    public int RollWithReroll(DiceExpression expr)
    {
        if (expr.Count == 0) return expr.Modifier;
        // Fixed value — if value > sides/2 no reroll; just sum.
        return expr.Modifier + expr.Count * _value;
    }
}
