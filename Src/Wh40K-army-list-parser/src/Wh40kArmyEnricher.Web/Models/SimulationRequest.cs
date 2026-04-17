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

    // ── Attack Modifiers ──────────────────────────────────────────────────────
    /// <summary>Flat modifier to total attack count per weapon group, applied after Blast/Rapid Fire. -1, 0, or +1.</summary>
    public int AttackModifier { get; set; }
    /// <summary>Enable the Blast keyword on weapons that don't already have it.</summary>
    public bool BlastOverride { get; set; }
    /// <summary>Reroll each attack die independently if below expected average (D6: 1-3; D3: 1).</summary>
    public bool RerollAttackDice { get; set; }

    // ── Hit Modifiers ─────────────────────────────────────────────────────────
    /// <summary>Roll modifier applied to hit rolls. Capped at net +1/-1. -1, 0, or +1.</summary>
    public int HitRollModifier { get; set; }
    /// <summary>BS/WS characteristic modifier. +1 means BS/WS improves by one step (e.g. 4+ → 3+). -1, 0, or +1.</summary>
    public int BsWsModifier { get; set; }
    /// <summary>"none" | "ones" | "all"</summary>
    public string HitRerolls { get; set; } = "none";
    /// <summary>When true and HitRerolls is "all", rerolls any non-critical result (not just failures).</summary>
    public bool FishForCriticalHits { get; set; }
    /// <summary>When true, weapon hits on a flat 4+ regardless of BS (Torrent overrides).</summary>
    public bool IndirectFireOverride { get; set; }
    /// <summary>When true, Critical Hits are scored on 5+.</summary>
    public bool CriticalHitsOn5 { get; set; }
    /// <summary>Enable Sustained Hits 1 on weapons that don't already have it.</summary>
    public bool SustainedHitsOverride { get; set; }
    /// <summary>Enable Lethal Hits on weapons that don't already have it.</summary>
    public bool LethalHitsOverride { get; set; }

    // ── Wound Modifiers ───────────────────────────────────────────────────────
    /// <summary>Roll modifier applied to wound rolls. Capped at net +1/-1. -1, 0, or +1.</summary>
    public int WoundRollModifier { get; set; }
    /// <summary>Attacker Strength characteristic modifier. -1, 0, or +1.</summary>
    public int StrengthModifier { get; set; }
    /// <summary>Defender Toughness characteristic modifier. -1, 0, or +1.</summary>
    public int ToughnessModifier { get; set; }
    /// <summary>"none" | "ones" | "all"</summary>
    public string WoundRerolls { get; set; } = "none";
    /// <summary>When true and WoundRerolls is "all", rerolls any non-critical wound result (not just failures).</summary>
    public bool FishForCriticalWounds { get; set; }
    /// <summary>When true, Critical Wounds are scored on 5+.</summary>
    public bool CritWoundOn5 { get; set; }
    /// <summary>Enable Devastating Wounds on weapons that don't already have it.</summary>
    public bool DevastatingWoundsOverride { get; set; }
    /// <summary>Anti keyword override (e.g. "Infantry"). Empty string means no override.</summary>
    public string AntiOverrideKeyword { get; set; } = "";
    /// <summary>Anti override critical wound threshold (e.g. 4 = 4+). Only used when AntiOverrideKeyword is set.</summary>
    public int AntiOverrideThreshold { get; set; } = 4;

    // ── Save Modifiers ────────────────────────────────────────────────────────
    /// <summary>When true, adds +1 to the defender's armour save.</summary>
    public bool InCover { get; set; }
    /// <summary>When true, negates the Cover bonus (net zero effect on save).</summary>
    public bool IgnoresCover { get; set; }
    /// <summary>AP modifier applied to all weapon AP values. +1 means AP becomes more negative (better). -1, 0, or +1.</summary>
    public int ApModifier { get; set; }
    /// <summary>Feel No Pain override for defenders without FNP. 0 = no override; e.g. 5 = 5+++.</summary>
    public int FnpOverride { get; set; }

    // ── Damage Modifiers ──────────────────────────────────────────────────────
    /// <summary>Flat modifier applied to each individual damage roll after rolling, before FNP. -1, 0, or +1.</summary>
    public int DamageModifier { get; set; }
    /// <summary>Reroll damage dice once per wound if below expected average (D6: 1-3; D3: 1).</summary>
    public bool RerollDamageDice { get; set; }

    // ── General ───────────────────────────────────────────────────────────────
    public bool WithinHalfRange { get; set; }
    public int Runs { get; set; } = 10000;
    /// <summary>
    /// Override for the number of surviving defender models. 0 means use the value from the unit profile.
    /// </summary>
    public int DefenderModelCount { get; set; }
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
