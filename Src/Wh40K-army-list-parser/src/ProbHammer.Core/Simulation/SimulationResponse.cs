namespace ProbHammer.Core.Simulation;

public class SimulationResponse
{
    public double MeanDamage { get; set; }
    public double ExpectedKills { get; set; }
    public double PKillAtLeastOne { get; set; }
    public double StdDev { get; set; }
    public CombatStageStats StageStats { get; set; } = new();

    // Empty for single weapon group runs; populated when multiple groups exist.
    public List<WeaponGroupStats> WeaponBreakdown { get; set; } = [];
}
