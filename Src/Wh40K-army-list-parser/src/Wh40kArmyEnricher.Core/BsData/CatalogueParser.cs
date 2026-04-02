using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Wh40kArmyEnricher.Core.Models;

namespace Wh40kArmyEnricher.Core.BsData;

/// <summary>
/// Parses BSData .cat / .gst XML files into <see cref="CatalogueData"/> objects.
/// Handles both plain XML (.cat) and raw-deflate-compressed (.catz) files.
/// </summary>
public class CatalogueParser
{
    private static readonly XNamespace Ns =
        "http://www.battlescribe.net/schema/catalogueSchema";

    // BSData 10e stores invuln as "4+ invulnerable save" (single +) in ability text,
    // not the "4++" game shorthand. Match one or two + signs before the word "invulnerable".
    private static readonly Regex InvulnTextRegex = new(@"(\d)\+\+? invulnerable", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // FNP appears as "Feel No Pain 5+" in ability text (never as "5+++" in 10e BSData).
    private static readonly Regex FnpTextRegex = new(@"Feel No Pain (\d)\+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // Simple N+ value parser for infoLink-derived values (Description = "4+" or modifier value = "5+").
    private static readonly Regex StatValueRegex = new(@"^(\d)\+", RegexOptions.Compiled);

    // ---------------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------------

    public async Task<CatalogueData> ParseAsync(Stream stream, string filename, CancellationToken ct = default)
    {
        var doc = await LoadDocumentAsync(stream, filename, ct);
        return ParseDocument(doc);
    }

    /// <summary>
    /// Loads a stream (plain XML or raw-deflate .catz) into an XDocument without parsing entries.
    /// Used by <see cref="CatalogueStore"/> for the first pass of two-pass cross-catalogue loading.
    /// </summary>
    public async Task<XDocument> LoadDocumentAsync(Stream stream, string filename, CancellationToken ct = default)
    {
        if (filename.EndsWith(".catz", StringComparison.OrdinalIgnoreCase))
        {
            // Raw deflate (no zlib or gzip header)
            await using var deflate = new DeflateStream(stream, CompressionMode.Decompress);
            return await XDocument.LoadAsync(deflate, LoadOptions.None, ct);
        }
        return await XDocument.LoadAsync(stream, LoadOptions.None, ct);
    }

    /// <summary>
    /// Extracts the id→element shared profile map from a document root.
    /// Used by <see cref="CatalogueStore"/> to build a global cross-catalogue profile dictionary.
    /// </summary>
    public Dictionary<string, XElement> GetSharedProfiles(XDocument doc)
        => BuildSharedProfileMap(doc.Root!);

    /// <summary>Parses a pre-loaded XDocument, optionally with cross-catalogue shared profiles as fallback.</summary>
    public CatalogueData Parse(XDocument doc, IReadOnlyDictionary<string, XElement>? externalProfiles = null)
        => ParseDocument(doc, externalProfiles);

    // ---------------------------------------------------------------------------
    // Document-level parsing
    // ---------------------------------------------------------------------------

    private CatalogueData ParseDocument(XDocument doc, IReadOnlyDictionary<string, XElement>? externalProfiles = null)
    {
        var root = doc.Root!;
        var rootTag = root.Name.LocalName; // "catalogue" or "gameSystem"
        bool isGst = rootTag == "gameSystem";

        var id = (string?)root.Attribute("id") ?? "";
        var name = (string?)root.Attribute("name") ?? "";

        // Build local shared profile map for this document.
        // Merge with any cross-catalogue external profiles so infoLinks that reference shared
        // profiles in other catalogues (e.g. Black Templars → Space Marines "Invulnerable Save")
        // can be resolved. Local entries always take precedence over external ones.
        var localProfiles = BuildSharedProfileMap(root);
        var sharedProfiles = externalProfiles != null && externalProfiles.Count > 0
            ? MergeProfiles(externalProfiles, localProfiles)
            : localProfiles;
        var sharedEntries = BuildSharedEntryMap(root);

        // Catalogue links
        var catalogueLinks = ParseCatalogueLinks(root);

        // Top-level selection entries
        var entries = ParseSelectionEntries(
            root.Element(Ns + "selectionEntries"),
            sharedProfiles,
            sharedEntries,
            id,
            depth: 0);

        // Also include sharedSelectionEntries at top level so they can be searched
        var sharedTopEntries = ParseSelectionEntries(
            root.Element(Ns + "sharedSelectionEntries"),
            sharedProfiles,
            sharedEntries,
            id,
            depth: 0);

        // Also include entries from sharedSelectionEntryGroups (wargear option groups).
        // Use deep traversal so nested selectionEntryGroups within groups are also included.
        var sharedGroupEntries = root.Element(Ns + "sharedSelectionEntryGroups")
            ?.Elements(Ns + "selectionEntryGroup")
            .SelectMany(grp => ParseSelectionEntryGroupDeep(grp, sharedProfiles, sharedEntries, id, depth: 0))
            .ToList() ?? [];

        // Also parse root-level entryLinks — faction catalogues use these to link parent-catalogue
        // entries and add faction-specific weapon/profile overrides directly on the link element.
        // e.g. "Black Templars Repulsor Executioner" entryLink adds "Heavy Laser Destroyer" profile.
        var rootLinkEntries = root.Element(Ns + "entryLinks")
            ?.Elements(Ns + "entryLink")
            .Select(e => ParseEntry(e, sharedProfiles, sharedEntries, id, depth: 0))
            .ToList() ?? [];

        return new CatalogueData
        {
            Id = id,
            Name = name,
            IsGameSystem = isGst,
            CatalogueLinks = catalogueLinks,
            Entries = entries.Concat(sharedTopEntries).Concat(sharedGroupEntries).Concat(rootLinkEntries).ToList()
        };
    }

    // ---------------------------------------------------------------------------
    // Catalogue links
    // ---------------------------------------------------------------------------

    private static IReadOnlyList<CatalogueLinkData> ParseCatalogueLinks(XElement root)
    {
        return root.Element(Ns + "catalogueLinks")
            ?.Elements(Ns + "catalogueLink")
            .Select(e => new CatalogueLinkData
            {
                Id = (string?)e.Attribute("id") ?? "",
                Name = (string?)e.Attribute("name") ?? "",
                TargetId = (string?)e.Attribute("targetId") ?? ""
            })
            .ToList() ?? [];
    }

    // ---------------------------------------------------------------------------
    // Shared lookup maps
    // ---------------------------------------------------------------------------

    private static Dictionary<string, XElement> MergeProfiles(
        IReadOnlyDictionary<string, XElement> external,
        Dictionary<string, XElement> local)
    {
        var merged = new Dictionary<string, XElement>(external);
        foreach (var (id, element) in local)
            merged[id] = element; // local entries override external
        return merged;
    }

    private Dictionary<string, XElement> BuildSharedProfileMap(XElement root)
    {
        return root.Descendants(Ns + "sharedProfiles")
            .FirstOrDefault()
            ?.Elements(Ns + "profile")
            .ToDictionary(e => (string?)e.Attribute("id") ?? "", e => e)
            ?? [];
    }

    private Dictionary<string, XElement> BuildSharedEntryMap(XElement root)
    {
        return root.Element(Ns + "sharedSelectionEntries")
            ?.Elements(Ns + "selectionEntry")
            .ToDictionary(e => (string?)e.Attribute("id") ?? "", e => e)
            ?? [];
    }

    // ---------------------------------------------------------------------------
    // Selection entry parsing
    // ---------------------------------------------------------------------------

    private List<CatalogueEntry> ParseSelectionEntries(
        XElement? container,
        Dictionary<string, XElement> sharedProfiles,
        Dictionary<string, XElement> sharedEntries,
        string catalogueId,
        int depth)
    {
        if (container == null) return [];

        // Process both selectionEntry and entryLink elements — entryLinks carry faction-specific
        // profile overrides on the link element itself and must not be silently ignored.
        return container.Elements()
            .Where(e => e.Name == Ns + "selectionEntry" || e.Name == Ns + "entryLink")
            .Select(e => ParseEntry(e, sharedProfiles, sharedEntries, catalogueId, depth))
            .ToList();
    }

    /// <summary>
    /// Recursively collects all selectionEntry/entryLink children from a selectionEntryGroup,
    /// traversing any nested selectionEntryGroups within it at arbitrary depth.
    /// This handles structures like: Wargear > Turret Weapon > Heavy Laser Destroyer.
    /// </summary>
    private List<CatalogueEntry> ParseSelectionEntryGroupDeep(
        XElement group,
        Dictionary<string, XElement> sharedProfiles,
        Dictionary<string, XElement> sharedEntries,
        string catalogueId,
        int depth)
    {
        var result = new List<CatalogueEntry>();

        // Direct entries in this group
        result.AddRange(ParseSelectionEntries(
            group.Element(Ns + "selectionEntries"),
            sharedProfiles, sharedEntries, catalogueId, depth));

        // Recurse into nested selectionEntryGroups (no extra depth increment — groups are not entries)
        foreach (var nested in group.Element(Ns + "selectionEntryGroups")
                                    ?.Elements(Ns + "selectionEntryGroup")
                                    ?? Enumerable.Empty<XElement>())
        {
            result.AddRange(ParseSelectionEntryGroupDeep(
                nested, sharedProfiles, sharedEntries, catalogueId, depth));
        }

        return result;
    }

    private CatalogueEntry ParseEntry(
        XElement el,
        Dictionary<string, XElement> sharedProfiles,
        Dictionary<string, XElement> sharedEntries,
        string catalogueId,
        int depth)
    {
        var id = (string?)el.Attribute("id") ?? "";
        var name = (string?)el.Attribute("name") ?? "";
        var type = (string?)el.Attribute("type") ?? "";

        // Collect all profiles for this entry (direct + via profileLinks)
        var allProfiles = GetAllProfiles(el, sharedProfiles).ToList();

        var statline = ParseStatline(name, allProfiles);
        var weapons = ParseWeapons(allProfiles);
        var abilities = ParseAbilities(allProfiles);
        var keywords = ParseKeywords(el);

        // Extract invuln / FNP from ability text AND infoLinks (two distinct storage patterns in 10e).
        // Store on the entry regardless of whether a statline exists — squad-type entries
        // (type="unit") have null statlines, so the Enricher applies these to child model statlines.
        var (entryInvuln, entryFnp) = ExtractInvulnFnp(el, sharedProfiles, abilities);

        // Also apply directly to the statline when one is present (single-model / upgrade entries)
        if (statline != null)
        {
            if (entryInvuln.HasValue && statline.InvulnerableSave == null)
                statline = statline with { InvulnerableSave = entryInvuln };
            if (entryFnp.HasValue && statline.FeelNoPain == null)
                statline = statline with { FeelNoPain = entryFnp };
        }

        // Child entries: direct selectionEntries + selectionEntryGroups + entryLinks
        var children = new List<CatalogueEntry>();
        if (depth < 6) // Avoid infinite recursion on pathological files
        {
            var directChildren = ParseSelectionEntries(
                el.Element(Ns + "selectionEntries"), sharedProfiles, sharedEntries, catalogueId, depth + 1);
            children.AddRange(directChildren);

            // selectionEntryGroups — wargear/option groups whose entries become children.
            // Use deep traversal so nested selectionEntryGroups within groups are also included
            // (e.g. Repulsor Executioner: Wargear > Turret Weapon > Heavy Laser Destroyer).
            var groupChildren = el.Element(Ns + "selectionEntryGroups")
                ?.Elements(Ns + "selectionEntryGroup")
                .SelectMany(grp => ParseSelectionEntryGroupDeep(
                    grp, sharedProfiles, sharedEntries, catalogueId, depth + 1))
                .ToList() ?? [];
            children.AddRange(groupChildren);

            // entryLinks pointing to sharedSelectionEntries
            var linkChildren = el.Element(Ns + "entryLinks")
                ?.Elements(Ns + "entryLink")
                .Where(lnk => (string?)lnk.Attribute("type") == "selectionEntry")
                .Select(lnk =>
                {
                    var targetId = (string?)lnk.Attribute("targetId") ?? "";
                    if (sharedEntries.TryGetValue(targetId, out var shared))
                        return ParseEntry(shared, sharedProfiles, sharedEntries, catalogueId, depth + 1);
                    return null;
                })
                .OfType<CatalogueEntry>()
                .ToList() ?? [];
            children.AddRange(linkChildren);
        }

        return new CatalogueEntry
        {
            Id = id,
            Name = name,
            EntryType = type,
            CatalogueId = catalogueId,
            Statline = statline,
            EntryInvulnerableSave = entryInvuln,
            EntryFeelNoPain = entryFnp,
            Weapons = weapons,
            Abilities = abilities,
            ChildEntries = children,
            Keywords = keywords
        };
    }

    // ---------------------------------------------------------------------------
    // Profile collection
    // ---------------------------------------------------------------------------

    private static IEnumerable<XElement> GetAllProfiles(
        XElement entry, Dictionary<string, XElement> sharedProfiles)
    {
        // Direct profiles
        foreach (var p in entry.Element(Ns + "profiles")?.Elements(Ns + "profile")
                          ?? Enumerable.Empty<XElement>())
            yield return p;

        // Profiles via profileLinks
        foreach (var link in entry.Element(Ns + "profileLinks")?.Elements(Ns + "profileLink")
                             ?? Enumerable.Empty<XElement>())
        {
            var targetId = (string?)link.Attribute("targetId");
            if (targetId != null && sharedProfiles.TryGetValue(targetId, out var shared))
                yield return shared;
        }
    }

    // ---------------------------------------------------------------------------
    // Statline
    // ---------------------------------------------------------------------------

    private static UnitStatline? ParseStatline(string entryName, List<XElement> profiles)
    {
        var unitProfile = profiles.FirstOrDefault(p =>
            string.Equals((string?)p.Attribute("typeName"), "Unit", StringComparison.OrdinalIgnoreCase));

        if (unitProfile == null) return null;

        var chars = GetCharacteristics(unitProfile);

        return new UnitStatline
        {
            Movement = chars.GetValueOrDefault("M", ""),
            Toughness = ParseStat(chars.GetValueOrDefault("T", "0")),
            Save = ParseStatWithPlus(chars.GetValueOrDefault("Sv", "7+")),
            Wounds = ParseStat(chars.GetValueOrDefault("W", "0")),
            Leadership = ParseStatWithPlus(chars.GetValueOrDefault("Ld", "7+")),
            OC = ParseStat(chars.GetValueOrDefault("OC", "0"))
        };
    }

    /// <summary>
    /// Extracts invulnerable save and FNP values from both ability text profiles and
    /// infoLink elements on a selection entry. BSData 10e uses two patterns:
    /// 1. Inline ability text: "4+ invulnerable save" / "Feel No Pain 5+"
    /// 2. infoLink name="Invulnerable Save" type="profile" → shared profile Description = "4+"
    ///    infoLink name="Feel No Pain" type="rule" → modifier value = "5+"
    /// </summary>
    private static (int? invuln, int? fnp) ExtractInvulnFnp(
        XElement entry,
        Dictionary<string, XElement> sharedProfiles,
        IReadOnlyList<AbilityData> abilities)
    {
        int? invuln = null;
        int? fnp = null;

        // Pattern 1: ability text
        foreach (var ability in abilities)
        {
            var text = ability.Text + " " + ability.Name;
            if (invuln == null)
            {
                var m = InvulnTextRegex.Match(text);
                if (m.Success) invuln = int.Parse(m.Groups[1].Value);
            }
            if (fnp == null)
            {
                var m = FnpTextRegex.Match(text);
                if (m.Success) fnp = int.Parse(m.Groups[1].Value);
            }
        }

        // Pattern 2: infoLinks
        foreach (var link in entry.Element(Ns + "infoLinks")?.Elements(Ns + "infoLink")
                               ?? Enumerable.Empty<XElement>())
        {
            var linkName = (string?)link.Attribute("name") ?? "";
            var linkType = (string?)link.Attribute("type") ?? "";
            var targetId = (string?)link.Attribute("targetId") ?? "";

            // Invuln via shared "Invulnerable Save" profile — Description is just "4+"
            if (invuln == null
                && string.Equals(linkName, "Invulnerable Save", StringComparison.OrdinalIgnoreCase)
                && string.Equals(linkType, "profile", StringComparison.OrdinalIgnoreCase)
                && sharedProfiles.TryGetValue(targetId, out var invulnProfile))
            {
                var chars = GetCharacteristics(invulnProfile);
                var desc = chars.GetValueOrDefault("Description", "").Trim();
                var m = StatValueRegex.Match(desc);
                if (m.Success) invuln = int.Parse(m.Groups[1].Value);
            }

            // FNP via "Feel No Pain" rule link — value comes from modifier appended to link name
            if (fnp == null
                && string.Equals(linkName, "Feel No Pain", StringComparison.OrdinalIgnoreCase))
            {
                var modifier = link.Element(Ns + "modifiers")?.Elements(Ns + "modifier")
                    .FirstOrDefault(mod =>
                        string.Equals((string?)mod.Attribute("type"), "append", StringComparison.OrdinalIgnoreCase)
                        && string.Equals((string?)mod.Attribute("field"), "name", StringComparison.OrdinalIgnoreCase));
                if (modifier != null)
                {
                    var value = ((string?)modifier.Attribute("value") ?? "").Trim();
                    var m = StatValueRegex.Match(value);
                    if (m.Success) fnp = int.Parse(m.Groups[1].Value);
                }
            }
        }

        return (invuln, fnp);
    }

    // ---------------------------------------------------------------------------
    // Weapons
    // ---------------------------------------------------------------------------

    private static List<WeaponProfileData> ParseWeapons(List<XElement> profiles)
    {
        return profiles
            .Where(p =>
            {
                var tn = (string?)p.Attribute("typeName") ?? "";
                return string.Equals(tn, "Ranged Weapons", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(tn, "Melee Weapons", StringComparison.OrdinalIgnoreCase);
            })
            .Select(p =>
            {
                var chars = GetCharacteristics(p);
                var typeName = (string?)p.Attribute("typeName") ?? "";
                return new WeaponProfileData
                {
                    Name = (string?)p.Attribute("name") ?? "",
                    TypeName = typeName,
                    Range = chars.GetValueOrDefault("Range", ""),
                    Attacks = chars.GetValueOrDefault("A", ""),
                    Skill = chars.ContainsKey("WS")
                        ? chars["WS"]
                        : chars.GetValueOrDefault("BS", ""),
                    Strength = chars.GetValueOrDefault("S", ""),
                    AP = chars.GetValueOrDefault("AP", ""),
                    Damage = chars.GetValueOrDefault("D", ""),
                    Keywords = chars.GetValueOrDefault("Keywords", "-")
                };
            })
            .ToList();
    }

    // ---------------------------------------------------------------------------
    // Abilities
    // ---------------------------------------------------------------------------

    private static List<AbilityData> ParseAbilities(List<XElement> profiles)
    {
        return profiles
            .Where(p => string.Equals((string?)p.Attribute("typeName"), "Abilities",
                StringComparison.OrdinalIgnoreCase))
            .Select(p =>
            {
                var chars = GetCharacteristics(p);
                return new AbilityData
                {
                    Name = (string?)p.Attribute("name") ?? "",
                    Text = chars.GetValueOrDefault("Description",
                           chars.GetValueOrDefault("Effect",
                           chars.Values.FirstOrDefault() ?? ""))
                };
            })
            .ToList();
    }

    // ---------------------------------------------------------------------------
    // Keywords (from categoryLinks)
    // ---------------------------------------------------------------------------

    private static List<string> ParseKeywords(XElement entry)
    {
        return entry.Element(Ns + "categoryLinks")
            ?.Elements(Ns + "categoryLink")
            .Select(e => ((string?)e.Attribute("name") ?? "").Trim().ToUpperInvariant())
            .Where(n => n.Length > 0)
            .ToList() ?? [];
    }

    // ---------------------------------------------------------------------------
    // Characteristic helpers
    // ---------------------------------------------------------------------------

    private static Dictionary<string, string> GetCharacteristics(XElement profile)
    {
        return profile
            .Descendants(Ns + "characteristic")
            .ToDictionary(
                c => (string?)c.Attribute("name") ?? "",
                c => c.Value.Trim(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static int ParseStat(string raw)
    {
        if (int.TryParse(raw, out var n)) return n;
        return 0;
    }

    private static int ParseStatWithPlus(string raw)
    {
        var trimmed = raw.TrimEnd('+');
        return int.TryParse(trimmed, out var n) ? n : 7;
    }
}
