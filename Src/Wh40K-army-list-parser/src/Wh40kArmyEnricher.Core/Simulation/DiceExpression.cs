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

    public override string ToString() =>
        Count == 0 ? Modifier.ToString() :
        Count == 1 ? (Modifier == 0 ? $"D{Sides}" : $"D{Sides}{Modifier:+#;-#}") :
        Modifier == 0 ? $"{Count}D{Sides}" : $"{Count}D{Sides}{Modifier:+#;-#}";
}
