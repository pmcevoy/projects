namespace Wh40kArmyEnricher.Web.Models;

public class SimulationRequest
{
    /// <summary>0-based indices into the session attacker army list.</summary>
    public List<int> AttackerUnitIndices { get; set; } = new();
    /// <summary>0-based index into the session defender army list.</summary>
    public int DefenderUnitIndex { get; set; }

    /// <summary>
    /// One entry per selected weapon row. The adapter groups these by weapon equality
    /// (Type + Skill + S + AP + D + Abilities) and aggregates attack counts within each group.
    /// </summary>
    public List<WeaponSelection> WeaponSelections { get; set; } = new();

    public bool WithinHalfRange { get; set; }
    /// <summary>When true, adds +1 to the defender's armour save.</summary>
    public bool InCover { get; set; }
    /// <summary>"none" | "ones" | "all"</summary>
    public string HitRerolls { get; set; } = "none";
    /// <summary>"none" | "ones" | "all"</summary>
    public string WoundRerolls { get; set; } = "none";
    /// <summary>When true, Critical Hits are scored on 5+.</summary>
    public bool CriticalHitsOn5 { get; set; }
    /// <summary>When true, adds +1 to all wound rolls (e.g. Black Templars detachment rule).</summary>
    public bool PlusOneToWound { get; set; }
    public int Runs { get; set; } = 10000;
}

public class WeaponSelection
{
    public string WeaponName { get; set; } = "";
    public string VariantName { get; set; } = "default";
    /// <summary>Name of the model type carrying this weapon (used to find the right variant).</summary>
    public string ModelName { get; set; } = "";
    /// <summary>
    /// Number of models contributing this weapon. For single-weapon selections this may be
    /// overridden by the user via the "models firing" input; for multi-weapon it is taken
    /// directly from the unit profile.
    /// </summary>
    public int ModelCount { get; set; }
}
