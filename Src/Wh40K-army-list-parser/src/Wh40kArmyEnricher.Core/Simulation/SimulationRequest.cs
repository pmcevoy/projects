namespace Wh40kArmyEnricher.Core.Simulation;

public class WeaponSelection
{
    public string WeaponName { get; set; } = "";
    public string VariantName { get; set; } = "";
    public string ModelName { get; set; } = "";
    public int ModelCount { get; set; }
}

public class SimulationRequest
{
    public string AttackerName { get; set; } = "";
    public string DefenderName { get; set; } = "";
    public List<WeaponSelection> WeaponSelections { get; set; } = [];

    // 0 = use defender.ModelCount from session
    public int DefenderModelCount { get; set; }

    // Attack modifiers
    public int AttackModifier { get; set; }
    public bool BlastOverride { get; set; }
    public bool RerollAttackDice { get; set; }

    // Hit modifiers
    public bool WithinHalfRange { get; set; }
    public int HitRollModifier { get; set; }      // net modifier; capped ±1 in adapter
    public int BsWsModifier { get; set; }          // characteristic modifier, applied to Skill directly
    public bool RerollHitOnes { get; set; }
    public bool RerollHitAll { get; set; }
    public bool FishForCritHits { get; set; }
    public bool IndirectFire { get; set; }
    public bool CritHitOn5Plus { get; set; }
    public bool SustainedHitsOverride { get; set; }
    public bool LethalHitsOverride { get; set; }

    // Wound modifiers
    public int WoundRollModifier { get; set; }     // net modifier; capped ±1 in adapter
    public int StrengthModifier { get; set; }
    public int ToughnessModifier { get; set; }
    public bool RerollWoundOnes { get; set; }
    public bool RerollWoundAll { get; set; }
    public bool FishForCritWounds { get; set; }
    public bool CritWoundOn5Plus { get; set; }
    public bool DevastatingWoundsOverride { get; set; }
    public string AntiKeyword { get; set; } = "";
    public int AntiThreshold { get; set; }

    // Save modifiers
    public bool Cover { get; set; }
    public bool IgnoresCover { get; set; }
    public int ApModifier { get; set; }

    // Damage modifiers
    public int DamageModifier { get; set; }
    public bool RerollDamageDice { get; set; }
    public int FnpOverride { get; set; }           // 0 = none; int = FNP value (e.g. 5 means 5+++)
}
