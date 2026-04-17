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
    /// <summary>
    /// Flat modifier added to each individual damage roll after rolling, applied before FNP.
    /// Positive values increase damage; negative values decrease it (minimum 0 per wound).
    /// </summary>
    public int DamageModifier { get; init; } = 0;
    /// <summary>
    /// When true and the weapon has variable damage (D3/D6), reroll the damage die once per wound
    /// if the result is below the expected average (D6: reroll 1-3; D3: reroll 1).
    /// </summary>
    public bool RerollDamageDice { get; init; }
    /// <summary>
    /// When true and the weapon has variable attacks (D3/D6), reroll each attack die independently
    /// if the result is below the expected average (D6: reroll 1-3; D3: reroll 1).
    /// Applied before Blast and Rapid Fire bonuses.
    /// </summary>
    public bool RerollAttackDice { get; init; }
    /// <summary>
    /// Flat modifier applied to the total attack count after all other adjustments (Blast, Rapid Fire).
    /// May be negative (clamped to min 0 total attacks).
    /// </summary>
    public int AttackModifier { get; init; } = 0;
}
