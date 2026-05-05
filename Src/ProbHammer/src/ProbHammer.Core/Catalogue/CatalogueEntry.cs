using ProbHammer.Core.Contracts;

namespace ProbHammer.Core.Catalogue;

public class CatalogueStatline
{
    public int Toughness { get; set; }
    public int Save { get; set; }
    public int Wounds { get; set; }
}

public class CatalogueWeaponAbilities
{
    public bool Torrent { get; set; }
    public bool Blast { get; set; }
    public int Melta { get; set; }
    public int RapidFire { get; set; }
    public int SustainedHits { get; set; }
    public bool LethalHits { get; set; }
    public bool DevastatingWounds { get; set; }
    public bool TwinLinked { get; set; }
    public bool IndirectFire { get; set; }
    public Dictionary<string, int> Anti { get; set; } = [];
}

public class CatalogueWeaponVariant
{
    public string VariantName { get; set; } = "";
    public string AttacksRaw { get; set; } = "";
    public int Skill { get; set; }
    public int Strength { get; set; }
    public int Ap { get; set; }           // negative integer matching game value
    public string DamageRaw { get; set; } = "";
    public CatalogueWeaponAbilities Abilities { get; set; } = new();
}

public class CatalogueWeaponEntry
{
    public string Name { get; set; } = "";
    public WeaponType Type { get; set; }
    public int Range { get; set; }
    public List<CatalogueWeaponVariant> Variants { get; set; } = [];
}

public class CatalogueEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string EntryType { get; set; } = "";
    public CatalogueStatline? Statline { get; set; }
    public List<AbilityProfile> Abilities { get; set; } = [];
    public int? EntryInvulnerableSave { get; set; }
    public int? EntryFeelNoPain { get; set; }
    public List<CatalogueWeaponEntry> Weapons { get; set; } = [];
    public List<CatalogueEntry> Children { get; set; } = [];
    public List<string> Keywords { get; set; } = [];
}

public record CatalogueData
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int Revision { get; init; }
    public string Filename { get; init; } = "";
    public IReadOnlyList<CatalogueEntry> Entries { get; init; } = Array.Empty<CatalogueEntry>();
}
