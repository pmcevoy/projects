namespace Wh40kArmyEnricher.Core.Simulation;

/// <summary>
/// Per-run averages for each stage of the WH40K attack pipeline.
/// All values are means across simulation runs.
/// </summary>
public sealed record CombatStageStats
{
    // Main pipeline
    public required double AvgAttacks { get; init; }
    public required double AvgHits { get; init; }
    public required double AvgCritHits { get; init; }
    public required double AvgWounds { get; init; }
    public required double AvgCritWounds { get; init; }
    public required double AvgFailedSaves { get; init; }
    public required double AvgDamageBeforeFnp { get; init; }
    public required double AvgFnpSaved { get; init; }

    // Ability contributions (shown as sub-rows when non-zero)
    public required double AvgSustainedHitsBonus { get; init; }
    public required double AvgLethalHitsAutoWounds { get; init; }
    public required double AvgDevastatingWoundsTriggers { get; init; }
    /// <summary>Wound rolls that scored a critical wound only because Anti lowered the threshold below 6.</summary>
    public required double AvgAntiCritWounds { get; init; }

    // Save type breakdown
    public required double AvgArmourSaveRolls { get; init; }
    public required double AvgInvulnSaveRolls { get; init; }
}
