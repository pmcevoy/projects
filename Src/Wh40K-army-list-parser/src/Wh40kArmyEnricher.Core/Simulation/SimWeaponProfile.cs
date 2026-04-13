namespace Wh40kArmyEnricher.Core.Simulation;

public sealed record SimWeaponProfile
{
    public string Name { get; init; } = string.Empty;
    public DiceExpression Attacks { get; init; } = DiceExpression.Fixed(1);
    public int Skill { get; init; }
    public int Strength { get; init; }
    /// <summary>AP as a positive integer (e.g. AP-2 → 2). Stored positively for EffectiveSave calculation.</summary>
    public int Ap { get; init; }
    public DiceExpression Damage { get; init; } = DiceExpression.Fixed(1);
    public SimWeaponAbilities Abilities { get; init; } = new();
    public bool WithinHalfRange { get; init; }
}
