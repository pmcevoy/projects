namespace ProbHammer.Core.Simulation;

public record CombatStageStats
{
    public double AvgAttacks { get; init; }
    public double AvgHits { get; init; }
    public double AvgCritHits { get; init; }
    public double AvgSustainedHitsBonus { get; init; }
    public double AvgWounds { get; init; }
    public double AvgCritWounds { get; init; }
    public double AvgLethalHitsAutoWounds { get; init; }
    public double AvgAntiCritWounds { get; init; }
    public double AvgFailedSaves { get; init; }
    public double AvgDevastatingWoundsTriggers { get; init; }
    public double AvgArmourSaveRolls { get; init; }
    public double AvgInvulnSaveRolls { get; init; }
    public double AvgDamageBeforeFnp { get; init; }
    public double AvgFnpSaved { get; init; }
}

public record WeaponGroupStats
{
    public string WeaponName { get; init; } = "";
    public CombatStageStats Stats { get; init; } = new();
}
