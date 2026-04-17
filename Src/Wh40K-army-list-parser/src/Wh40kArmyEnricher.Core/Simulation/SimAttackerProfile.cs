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
    /// <summary>Minimum raw die roll that counts as a Critical Wound (base; Anti can lower further). Default 6.</summary>
    public int CriticalWoundsOn { get; init; } = 6;
    /// <summary>
    /// Flat modifier applied to wound rolls after rerolls. Capped at +1/-1 per the 40K rules.
    /// Default 0 (no modifier).
    /// </summary>
    public int WoundRollModifier { get; init; } = 0;
    /// <summary>
    /// Flat modifier applied to hit rolls after rerolls. Capped at +1/-1 per the 40K rules.
    /// Default 0 (no modifier). Tracked separately from BsWsModifier.
    /// </summary>
    public int HitRollModifier { get; init; } = 0;
    /// <summary>
    /// When true and HitRerollAll is set, rerolls any result below CriticalHitsOn
    /// (including successful non-critical hits) rather than only rerolling failures.
    /// </summary>
    public bool FishForCriticalHits { get; init; }
    /// <summary>
    /// When true and WoundRerollAll is set, rerolls any result below CriticalWoundsOn
    /// (including successful non-critical wounds) rather than only rerolling failures.
    /// </summary>
    public bool FishForCriticalWounds { get; init; }
}
