namespace Wh40kArmyEnricher.Core.Simulation;

public sealed record SimAttackerProfile
{
    public string Name { get; init; } = string.Empty;
    /// <summary>
    /// One entry per distinct weapon profile. Each weapon's Attacks field already encodes the
    /// aggregated total across all contributing model groups (e.g. Marshal 7A + Castellan 6A +
    /// 4× Sword Brother 3A → Fixed(25)), so the simulator needs no separate model count.
    /// </summary>
    public IReadOnlyList<SimWeaponProfile> Weapons { get; init; } = [];
    public SimRerollOptions Rerolls { get; init; } = new();
    /// <summary>Minimum raw die roll that counts as a Critical Hit. Default 6; can be lowered e.g. to 5.</summary>
    public int CriticalHitsOn { get; init; } = 6;
    /// <summary>
    /// Flat modifier applied to wound rolls after rerolls. Capped at +1/-1 per the 40K rules.
    /// Default 0 (no modifier). Set to 1 for "+1 to wound" detachment rules.
    /// </summary>
    public int WoundRollModifier { get; init; } = 0;
}
