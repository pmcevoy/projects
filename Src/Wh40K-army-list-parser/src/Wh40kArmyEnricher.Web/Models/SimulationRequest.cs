namespace Wh40kArmyEnricher.Web.Models;

public class SimulationRequest
{
    /// <summary>0-based indices into the session attacker army list.</summary>
    public List<int> AttackerUnitIndices { get; set; } = new();
    /// <summary>0-based index into the session defender army list.</summary>
    public int DefenderUnitIndex { get; set; }
    public string WeaponName { get; set; } = "";
    public string VariantName { get; set; } = "default";
    /// <summary>Name of the model type carrying the selected weapon.</summary>
    public string ModelName { get; set; } = "";
    /// <summary>Number of attacking models. If 0, uses the full count from the unit profile.</summary>
    public int AttackingModels { get; set; }
    public bool WithinHalfRange { get; set; }
    /// <summary>When true, adds +1 to the defender's armour save.</summary>
    public bool InCover { get; set; }
    /// <summary>"none" | "ones" | "all"</summary>
    public string HitRerolls { get; set; } = "none";
    /// <summary>"none" | "ones" | "all"</summary>
    public string WoundRerolls { get; set; } = "none";
    /// <summary>When true, Critical Hits are scored on 5+.</summary>
    public bool CriticalHitsOn5 { get; set; }
    public int Runs { get; set; } = 10000;
}
