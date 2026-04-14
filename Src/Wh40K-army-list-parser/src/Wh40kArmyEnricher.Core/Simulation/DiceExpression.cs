using System.Text.RegularExpressions;

namespace Wh40kArmyEnricher.Core.Simulation;

/// <summary>
/// Represents a parsed dice expression such as D6, 2D3+1, or a fixed integer.
/// When Count == 0, Modifier holds the fixed value.
/// </summary>
public sealed record DiceExpression
{
    public int Count { get; init; }
    public int Sides { get; init; }
    public int Modifier { get; init; }

    private static readonly Regex DicePattern =
        new(@"^(\d*)D(\d+)([+-]\d+)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static DiceExpression Fixed(int value) => new() { Count = 0, Sides = 0, Modifier = value };

    public static DiceExpression Parse(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new ArgumentException("Dice expression cannot be empty.", nameof(expression));

        expression = expression.Trim();

        if (int.TryParse(expression, out int fixedVal))
            return Fixed(fixedVal);

        var match = DicePattern.Match(expression);
        if (!match.Success)
            throw new ArgumentException($"Unrecognised dice expression: '{expression}'", nameof(expression));

        int count = match.Groups[1].Value.Length == 0 ? 1 : int.Parse(match.Groups[1].Value);
        int sides = int.Parse(match.Groups[2].Value);
        int modifier = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;

        if (sides != 3 && sides != 6)
            throw new ArgumentException($"Unsupported die size D{sides}. Only D3 and D6 are supported.", nameof(expression));

        return new DiceExpression { Count = count, Sides = sides, Modifier = modifier };
    }

    /// <summary>
    /// Scales this expression by <paramref name="n"/>, as if n models each independently rolled it.
    /// Fixed(7).Scale(3) = Fixed(21); D6.Scale(3) = 3D6; (D3+1).Scale(2) = 2D3+2.
    /// </summary>
    public DiceExpression Scale(int n)
    {
        if (n <= 0) throw new ArgumentOutOfRangeException(nameof(n));
        if (Count == 0) return Fixed(Modifier * n);
        return new DiceExpression { Count = Count * n, Sides = Sides, Modifier = Modifier * n };
    }

    /// <summary>
    /// Adds two compatible expressions (same Sides, or at least one is a fixed offset).
    /// Throws if both have dice with different Sides values.
    /// </summary>
    public DiceExpression Add(DiceExpression other)
    {
        if (Count == 0 && other.Count == 0) return Fixed(Modifier + other.Modifier);
        if (Count == 0) return new DiceExpression { Count = other.Count, Sides = other.Sides, Modifier = Modifier + other.Modifier };
        if (other.Count == 0) return new DiceExpression { Count = Count, Sides = Sides, Modifier = Modifier + other.Modifier };
        if (Sides != other.Sides) throw new InvalidOperationException($"Cannot add D{Sides} and D{other.Sides} expressions.");
        return new DiceExpression { Count = Count + other.Count, Sides = Sides, Modifier = Modifier + other.Modifier };
    }

    public override string ToString() =>
        Count == 0 ? Modifier.ToString() :
        Count == 1 ? (Modifier == 0 ? $"D{Sides}" : $"D{Sides}{Modifier:+#;-#}") :
        Modifier == 0 ? $"{Count}D{Sides}" : $"{Count}D{Sides}{Modifier:+#;-#}";
}
