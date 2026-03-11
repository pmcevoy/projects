namespace Wh40kSim.Models;

public sealed record WeaponProfile
{
    public string Name { get; init; } = string.Empty;
    public DiceExpression Attacks { get; init; } = DiceExpression.Fixed(1);
    public int Skill { get; init; }
    public int Strength { get; init; }
    public int Ap { get; init; }
    public DiceExpression Damage { get; init; } = DiceExpression.Fixed(1);
    public WeaponAbilities Abilities { get; init; } = new();
    public bool WithinHalfRange { get; init; }
}
