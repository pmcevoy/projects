namespace Wh40kArmyEnricher.Web.Models;

public class SimulationResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }

    public string AttackerName { get; set; } = "";
    public string WeaponDescription { get; set; } = "";
    public string DefenderName { get; set; } = "";
    public int Runs { get; set; }

    public double MeanDamage { get; set; }
    public double StdDeviation { get; set; }
    /// <summary>Expected number of models destroyed (mean damage / wounds per model).</summary>
    public double ExpectedKills { get; set; }
    /// <summary>Probability (0–1) of destroying at least one model.</summary>
    public double ProbKillAtLeastOne { get; set; }
    public int MinDamage { get; set; }
    public int MaxDamage { get; set; }
}
