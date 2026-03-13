namespace Wh40kArmyEnricher.Core.Models;

/// <summary>Parsed BSData selectionEntry (unit, model, or upgrade).</summary>
public record CatalogueEntry
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>"unit" | "model" | "upgrade" | "rootEntry"</summary>
    public string EntryType { get; init; } = "";
    public string CatalogueId { get; init; } = "";
    public UnitStatline? Statline { get; init; }
    public IReadOnlyList<WeaponProfileData> Weapons { get; init; } = [];
    public IReadOnlyList<AbilityData> Abilities { get; init; } = [];
    public IReadOnlyList<CatalogueEntry> ChildEntries { get; init; } = [];
    public IReadOnlyList<string> Keywords { get; init; } = [];
}

/// <summary>Model statline from a BSData Unit profile.</summary>
public record UnitStatline
{
    public string Movement { get; init; } = "";
    public int Toughness { get; init; }
    public int Save { get; init; }
    public int Wounds { get; init; }
    public int Leadership { get; init; }
    public int OC { get; init; }
    public int? InvulnerableSave { get; init; }
    public int? FeelNoPain { get; init; }
}

/// <summary>Weapon profile data from a BSData Ranged Weapons / Melee Weapons profile.</summary>
public record WeaponProfileData
{
    public string Name { get; init; } = "";
    /// <summary>"Ranged Weapons" | "Melee Weapons"</summary>
    public string TypeName { get; init; } = "";
    /// <summary>Raw range string from XML, e.g. "18\"" or "Melee".</summary>
    public string Range { get; init; } = "";
    /// <summary>Raw attacks string, e.g. "4" or "D6".</summary>
    public string Attacks { get; init; } = "";
    /// <summary>WS or BS string, e.g. "3+".</summary>
    public string Skill { get; init; } = "";
    public string Strength { get; init; } = "";
    public string AP { get; init; } = "";
    /// <summary>Raw damage string, e.g. "2" or "D3".</summary>
    public string Damage { get; init; } = "";
    /// <summary>Comma-separated keywords from the Keywords characteristic, or "-".</summary>
    public string Keywords { get; init; } = "";
}

/// <summary>An ability entry (special rule) from a BSData Abilities profile.</summary>
public record AbilityData
{
    public string Name { get; init; } = "";
    public string Text { get; init; } = "";
}

/// <summary>Top-level parsed catalogue (one per .cat / .gst file).</summary>
public record CatalogueData
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsGameSystem { get; init; }
    public IReadOnlyList<CatalogueLinkData> CatalogueLinks { get; init; } = [];
    public IReadOnlyList<CatalogueEntry> Entries { get; init; } = [];
}

/// <summary>A catalogueLink element inside a .cat / .gst file.</summary>
public record CatalogueLinkData
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string TargetId { get; init; } = "";
}
