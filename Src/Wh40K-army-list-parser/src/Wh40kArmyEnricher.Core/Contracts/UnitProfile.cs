namespace Wh40kArmyEnricher.Core.Contracts;

public class UnitProfile
{
    public string Name { get; set; } = "";
    public string Faction { get; set; } = "";
    public int ModelCount { get; set; }
    public List<string> Keywords { get; set; } = [];
    public List<AbilityProfile> Abilities { get; set; } = [];
    public List<AbilityProfile> LeadingAbilities { get; set; } = [];
    public List<string> Enhancements { get; set; } = [];
    public RerollProfile Rerolls { get; set; } = new();
    public int CriticalHitsOn { get; set; } = 6;
    public List<ModelProfile> Models { get; set; } = [];

    // Defensive stats
    public int Toughness { get; set; }
    public int Save { get; set; }
    public int? InvulnerableSave { get; set; }
    public int Wounds { get; set; }
    public int? FeelNoPain { get; set; }
}

public class ModelProfile
{
    public string ModelName { get; set; } = "";
    public int Count { get; set; }
    public List<WeaponProfile> Weapons { get; set; } = [];
}

public class WeaponProfile
{
    public string WeaponName { get; set; } = "";
    public WeaponType Type { get; set; }
    public int Range { get; set; }
    public List<WeaponVariantProfile> Profiles { get; set; } = [];
}

public enum WeaponType { Ranged, Melee }

public class WeaponVariantProfile
{
    public string Variant { get; set; } = "";
    public ScalarValue Attacks { get; set; }
    public int Skill { get; set; }
    public int Strength { get; set; }
    public int Ap { get; set; }
    public ScalarValue Damage { get; set; }
    public WeaponAbilities Abilities { get; set; } = new();
}

public class WeaponAbilities
{
    public bool Torrent { get; set; }
    public bool Blast { get; set; }
    public int Melta { get; set; }
    public int RapidFire { get; set; }
    public int SustainedHits { get; set; }
    public bool LethalHits { get; set; }
    public bool DevastatingWounds { get; set; }
    public bool TwinLinked { get; set; }
    public Dictionary<string, int> Anti { get; set; } = [];
}

public class AbilityProfile
{
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
}

public class RerollProfile
{
    public bool HitRerollOnes { get; set; }
    public bool HitRerollAll { get; set; }
    public bool WoundRerollOnes { get; set; }
    public bool WoundRerollAll { get; set; }
}
