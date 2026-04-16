namespace Wh40kArmyEnricher.Contracts;

// ---------------------------------------------------------------------------
// Scalar value that can be an integer or a string (e.g. attacks: 4 or "D6").
// ---------------------------------------------------------------------------

public readonly struct ScalarValue
{
    private readonly int? _int;
    private readonly string? _str;

    public ScalarValue(int value) { _int = value; _str = null; }
    public ScalarValue(string value) { _int = null; _str = value; }

    public bool IsInt => _int.HasValue;
    public int IntValue => _int ?? 0;
    public string StringValue => _str ?? "";

    public static implicit operator ScalarValue(int v) => new(v);
    public static implicit operator ScalarValue(string v) => new(v);

    public override string ToString() => _int.HasValue ? _int.Value.ToString() : _str ?? "";
}

// ---------------------------------------------------------------------------
// Weapon abilities block
// ---------------------------------------------------------------------------

public record WeaponAbilities
{
    public bool Torrent { get; init; }
    public bool Blast { get; init; }
    public int Melta { get; init; }           // 0 = not present
    public int RapidFire { get; init; }       // 0 = not present
    public int SustainedHits { get; init; }   // 0 = not present
    public bool LethalHits { get; init; }
    public bool DevastatingWounds { get; init; }
    public bool TwinLinked { get; init; }
    public Dictionary<string, int> Anti { get; init; } = new();  // keyword -> critical wound threshold
}

// ---------------------------------------------------------------------------
// Weapon variant (e.g. plasma standard vs supercharge)
// ---------------------------------------------------------------------------

public record WeaponVariantProfile
{
    public string Variant { get; init; } = "default";
    public ScalarValue Attacks { get; init; } = new(1);
    public int Skill { get; init; }       // raw int, implies N+
    public int Strength { get; init; }
    public int Ap { get; init; }          // negative integer matching game value
    public ScalarValue Damage { get; init; } = new(1);
    public WeaponAbilities Abilities { get; init; } = new();
}

// ---------------------------------------------------------------------------
// A weapon entry (may have multiple variant profiles)
// ---------------------------------------------------------------------------

public record WeaponProfile
{
    public string WeaponName { get; init; } = "";
    public string Type { get; init; } = "Melee";   // "Melee" | "Ranged"
    public int Range { get; init; }                 // 0 = melee, inches otherwise
    /// <summary>
    /// How many models in the parent model group carry this weapon.
    /// For mixed-loadout squads (e.g. 5 Initiates where only 3 have chainswords) this will
    /// be less than ModelProfile.Count. For multi-gun vehicles (e.g. 2x Fragstorm grenade
    /// launcher on a single Impulsor) this will be the number of guns fired.
    /// </summary>
    public int ModelCount { get; init; } = 1;
    public List<WeaponVariantProfile> Profiles { get; init; } = new();
}

// ---------------------------------------------------------------------------
// A distinct model type within a unit
// ---------------------------------------------------------------------------

public record ModelProfile
{
    public string ModelName { get; init; } = "";
    public int Count { get; init; }
    public List<WeaponProfile> Weapons { get; init; } = new();
}

// ---------------------------------------------------------------------------
// Re-roll options (set per simulation run, not per weapon)
// ---------------------------------------------------------------------------

public record RerollOptions
{
    public bool HitRerollOnes { get; init; }
    public bool HitRerollAll { get; init; }
    public bool WoundRerollOnes { get; init; }
    public bool WoundRerollAll { get; init; }
}

// ---------------------------------------------------------------------------
// Ability (special rule captured from BSData)
// ---------------------------------------------------------------------------

public record AbilityProfile
{
    public string Name { get; init; } = "";
    public string Text { get; init; } = "";
    /// <summary>
    /// Optional sub-ability effects from a custom BSData profileType with the same name as this ability.
    /// E.g. "Lord of the Death Guard" has sub-entries "Diseased Influence", "Boon of Death", etc.
    /// </summary>
    public List<AbilityProfile> SubAbilities { get; init; } = [];
}

// ---------------------------------------------------------------------------
// Unit profile — used for both attacker and defender roles in a pairing.
// The Pairing.Attacker / Pairing.Defender fields encode the role; the profile
// itself carries all offensive and defensive data for the unit.
// ---------------------------------------------------------------------------

public record UnitProfile
{
    // Identity
    public string Name { get; init; } = "";
    public string Faction { get; init; } = "";
    /// <summary>1-based position of this unit in the army list as parsed. Used to
    /// disambiguate units that share a name (e.g. two Crusader Squads).</summary>
    public int ArmyListIndex { get; init; }
    public int ModelCount { get; init; }
    public List<string> Keywords { get; init; } = new();
    public List<AbilityProfile> Abilities { get; init; } = new();
    /// <summary>
    /// Abilities whose text begins with "While this model is leading a unit …".
    /// These only apply when this unit is acting as a leader in an <see cref="AttachedUnit"/>;
    /// they must not be applied when simulating the unit standalone.
    /// </summary>
    public List<AbilityProfile> LeadingAbilities { get; init; } = new();
    public List<string> Enhancements { get; init; } = new();

    // Offensive stats
    public RerollOptions Rerolls { get; init; } = new();
    public int CriticalHitsOn { get; init; } = 6;
    public List<ModelProfile> Models { get; init; } = new();

    // Defensive stats
    public int Toughness { get; init; }
    public int Save { get; init; }               // raw int, implies N+
    public int? InvulnerableSave { get; init; }  // null if absent
    public int Wounds { get; init; }
    public int? FeelNoPain { get; init; }        // null if absent
}

// ---------------------------------------------------------------------------
// Attached unit — simulation-layer composition of a bodyguard + 0–2 leaders.
// Produced by LeaderResolver, not by the Enricher directly.
// ---------------------------------------------------------------------------

public record AttachedUnit
{
    /// <summary>The non-CHARACTER bodyguard unit (or any standalone unit for the 0-leader baseline).</summary>
    public UnitProfile Bodyguard { get; init; } = new();
    /// <summary>0, 1, or 2 CHARACTER leader profiles attached to this unit.</summary>
    public List<UnitProfile> Leaders { get; init; } = new();
    /// <summary>Union of bodyguard + all leader keywords.</summary>
    public List<string> EffectiveKeywords { get; init; } = new();
    /// <summary>Merged re-roll grants from leaders' leadingAbilities (OR semantics).</summary>
    public RerollOptions EffectiveRerolls { get; init; } = new();
    /// <summary>Minimum crit-hit threshold across all leader grants; default 6.</summary>
    public int EffectiveCritHitsOn { get; init; } = 6;
    /// <summary>Bodyguard abilities plus leaders' leading abilities; individual abilities
    /// (Stealth, Infiltrate, etc.) are removed if any leader lacks them.</summary>
    public List<AbilityProfile> EffectiveAbilities { get; init; } = new();
    /// <summary>Warnings and assumptions about this combination (e.g. unverified double-primary).</summary>
    public List<string> Notes { get; init; } = new();
}
