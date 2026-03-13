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

    private static readonly Regex InvulnRegex = new(@"(\d)\+\+(?!\+)", RegexOptions.Compiled);
    private static readonly Regex FnpRegex = new(@"(\d)\+\+\+", RegexOptions.Compiled);

    // ---------------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------------

    public async Task<CatalogueData> ParseAsync(Stream stream, string filename, CancellationToken ct = default)
    {
        XDocument doc;
        if (filename.EndsWith(".catz", StringComparison.OrdinalIgnoreCase))
        {
            // Raw deflate (no zlib or gzip header)
            await using var deflate = new DeflateStream(stream, CompressionMode.Decompress);
            doc = await XDocument.LoadAsync(deflate, LoadOptions.None, ct);
        }
        else
        {
            doc = await XDocument.LoadAsync(stream, LoadOptions.None, ct);
        }

        return ParseDocument(doc);
    }

    public CatalogueData Parse(XDocument doc) => ParseDocument(doc);

    // ---------------------------------------------------------------------------
    // Document-level parsing
    // ---------------------------------------------------------------------------

    private CatalogueData ParseDocument(XDocument doc)
    {
        var root = doc.Root!;
        var rootTag = root.Name.LocalName; // "catalogue" or "gameSystem"
        bool isGst = rootTag == "gameSystem";

        var id = (string?)root.Attribute("id") ?? "";
        var name = (string?)root.Attribute("name") ?? "";

        // Build shared profile map for the whole document (used when resolving profileLinks)
        var sharedProfiles = BuildSharedProfileMap(root);
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

        return new CatalogueData
        {
            Id = id,
            Name = name,
            IsGameSystem = isGst,
            CatalogueLinks = catalogueLinks,
            Entries = entries.Concat(sharedTopEntries).ToList()
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

        return container.Elements(Ns + "selectionEntry")
            .Select(e => ParseEntry(e, sharedProfiles, sharedEntries, catalogueId, depth))
            .ToList();
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

        // Enrich statline with invuln / FNP found in ability text
        if (statline != null)
        {
            statline = EnrichStatlineFromAbilities(statline, abilities);
        }

        // Child entries: direct selectionEntries + via entryLinks
        var children = new List<CatalogueEntry>();
        if (depth < 3) // Avoid infinite recursion
        {
            var directChildren = ParseSelectionEntries(
                el.Element(Ns + "selectionEntries"), sharedProfiles, sharedEntries, catalogueId, depth + 1);
            children.AddRange(directChildren);

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

    private static UnitStatline EnrichStatlineFromAbilities(UnitStatline statline, IReadOnlyList<AbilityData> abilities)
    {
        int? invuln = null;
        int? fnp = null;

        foreach (var ability in abilities)
        {
            var text = ability.Text + " " + ability.Name;

            if (invuln == null)
            {
                var m = new Regex(@"(\d)\+\+(?!\+)").Match(text);
                if (m.Success) invuln = int.Parse(m.Groups[1].Value);
            }

            if (fnp == null)
            {
                var m = new Regex(@"(\d)\+\+\+").Match(text);
                if (m.Success) fnp = int.Parse(m.Groups[1].Value);
            }
        }

        if (invuln == null && fnp == null) return statline;

        return statline with
        {
            InvulnerableSave = invuln ?? statline.InvulnerableSave,
            FeelNoPain = fnp ?? statline.FeelNoPain
        };
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
                return tn == "Ranged Weapons" || tn == "Melee Weapons";
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
