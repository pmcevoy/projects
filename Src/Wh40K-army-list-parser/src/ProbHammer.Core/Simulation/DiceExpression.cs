using System.Text.RegularExpressions;

namespace ProbHammer.Core.Simulation;

/// <summary>
/// Represents a dice expression such as "D6", "2D3+1", or a fixed integer.
/// Count=0 means a fixed value stored in Modifier.
/// </summary>
public record DiceExpression
{
    public int Count { get; init; }
    public int Sides { get; init; }
    public int Modifier { get; init; }

    private static readonly Regex DicePattern =
        new(@"^(\d*)D(\d+)(?:\+(\d+))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static DiceExpression Parse(string s)
    {
        s = s.Trim();
        if (int.TryParse(s, out var n))
            return Fixed(n);

        var m = DicePattern.Match(s);
        if (!m.Success)
            throw new FormatException($"Invalid dice expression: '{s}'");

        var count = m.Groups[1].Value is "" or "1" ? 1 : int.Parse(m.Groups[1].Value);
        if (m.Groups[1].Value == "") count = 1;
        var sides = int.Parse(m.Groups[2].Value);
        var mod = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;

        return new DiceExpression { Count = count, Sides = sides, Modifier = mod };
    }

    public static DiceExpression Fixed(int value) =>
        new() { Count = 0, Sides = 0, Modifier = value };

    /// <summary>Multiply this expression by n (e.g. 3 models × D6+1 → 3D6+3).</summary>
    public DiceExpression Scale(int n)
    {
        if (Count == 0) return Fixed(Modifier * n);
        return this with { Count = Count * n, Modifier = Modifier * n };
    }

    /// <summary>Add two dice expressions. Both dice components must have the same Sides.</summary>
    public DiceExpression Add(DiceExpression other)
    {
        if (Count == 0 && other.Count == 0)
            return Fixed(Modifier + other.Modifier);
        if (Count == 0)
            return other with { Modifier = other.Modifier + Modifier };
        if (other.Count == 0)
            return this with { Modifier = Modifier + other.Modifier };
        if (Sides != other.Sides)
            throw new InvalidOperationException($"Cannot add D{Sides} and D{other.Sides} expressions");
        return new DiceExpression { Count = Count + other.Count, Sides = Sides, Modifier = Modifier + other.Modifier };
    }

    public override string ToString() =>
        Count == 0 ? Modifier.ToString() :
        Count == 1 && Modifier == 0 ? $"D{Sides}" :
        Count == 1 ? $"D{Sides}+{Modifier}" :
        Modifier == 0 ? $"{Count}D{Sides}" :
        $"{Count}D{Sides}+{Modifier}";
}
