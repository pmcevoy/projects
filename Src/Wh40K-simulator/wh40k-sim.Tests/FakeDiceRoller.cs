using Wh40kSim.Models;
using Wh40kSim.Simulation;

namespace Wh40kSim.Tests;

/// <summary>
/// A deterministic dice roller that returns a pre-set sequence of values, cycling when exhausted.
/// </summary>
internal sealed class FakeDiceRoller : IDiceRoller
{
    private readonly Queue<int> _queue;
    private readonly int[] _values;
    private int _index;

    public FakeDiceRoller(params int[] values)
    {
        _values = values;
        _queue = new Queue<int>(values);
        _index = 0;
    }

    public int RollD6() => Next();
    public int Roll(int sides) => Next();
    public int Roll(DiceExpression expression)
    {
        if (expression.Count == 0) return expression.Modifier;
        int total = 0;
        for (int i = 0; i < expression.Count; i++)
            total += Next();
        return total + expression.Modifier;
    }

    private int Next()
    {
        int v = _values[_index % _values.Length];
        _index++;
        return v;
    }
}
