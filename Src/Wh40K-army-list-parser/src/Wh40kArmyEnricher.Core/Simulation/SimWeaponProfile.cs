using Wh40kArmyEnricher.Core.Contracts;

namespace Wh40kArmyEnricher.Core.Simulation;

public record SimWeaponProfile
{
    public string WeaponName { get; init; } = "";
    public WeaponType Type { get; init; }
    public DiceExpression Attacks { get; init; } = DiceExpression.Fixed(1);
    public int Skill { get; init; }              // effective after BS/WS characteristic modifier
    public int Strength { get; init; }           // effective after strength modifier
    public int Ap { get; init; }                 // positive integer (negated from Contracts)
    public DiceExpression Damage { get; init; } = DiceExpression.Fixed(1);
    public SimWeaponAbilities Abilities { get; init; } = new();
    public int AttackModifier { get; init; }
    public int HitRollModifier { get; init; }    // pre-capped at ±1; positive = beneficial
    public int WoundRollModifier { get; init; }  // pre-capped at ±1; positive = beneficial
    public int DamageModifier { get; init; }
    public bool RerollAttackDice { get; init; }
    public bool RerollDamageDice { get; init; }
    public bool WithinHalfRange { get; init; }
}
