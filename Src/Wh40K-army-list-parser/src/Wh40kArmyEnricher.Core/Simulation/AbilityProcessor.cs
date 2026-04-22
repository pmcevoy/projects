namespace Wh40kArmyEnricher.Core.Simulation;

public static class AbilityProcessor
{
    // Returns the minimum D6 roll needed to wound given strength vs toughness.
    public static int WoundThreshold(int strength, int toughness)
    {
        if (strength >= 2 * toughness) return 2;
        if (strength > toughness) return 3;
        if (strength == toughness) return 4;
        if (2 * strength <= toughness) return 6;
        return 5;
    }

    // Returns the minimum D6 roll needed to pass the save. AP is a positive integer in the sim engine.
    // Uses whichever of armour save (modified by AP) or invulnerable save is numerically lower (easier).
    public static int EffectiveSave(SimDefenderProfile defender, int ap)
    {
        int armourSave = defender.Save + ap;
        if (defender.InvulnerableSave.HasValue && defender.InvulnerableSave.Value < armourSave)
            return defender.InvulnerableSave.Value;
        return armourSave;
    }
}
