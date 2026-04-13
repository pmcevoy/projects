namespace Wh40kArmyEnricher.Core.Simulation;

public static class AbilityProcessor
{
    /// <summary>Returns the minimum D6 roll required to wound, based on S vs T.</summary>
    public static int WoundThreshold(int strength, int toughness)
    {
        if (strength >= 2 * toughness) return 2;
        if (strength > toughness)      return 3;
        if (strength == toughness)     return 4;
        if (strength > toughness / 2)  return 5;
        return 6;
    }

    /// <summary>
    /// Applies a modifier (from the attacker's perspective) to a target number,
    /// with the total modifier capped at +1/-1 before application.
    /// A positive modifier means easier to succeed (target decreases).
    /// </summary>
    public static int ApplyModifierCapped(int targetNumber, int modifier)
    {
        int capped = Math.Clamp(modifier, -1, 1);
        return targetNumber - capped;
    }

    /// <summary>
    /// Returns the effective save value the defender needs to meet or beat.
    /// Chooses the easier of armour save (modified by AP) and invulnerable save.
    /// AP is expected as a positive integer (e.g. AP-2 → pass 2).
    /// </summary>
    public static int EffectiveSave(SimDefenderProfile defender, int ap)
    {
        int armourSave = defender.Save + ap;
        if (defender.InvulnerableSave.HasValue)
            return Math.Min(armourSave, defender.InvulnerableSave.Value);
        return armourSave;
    }
}
