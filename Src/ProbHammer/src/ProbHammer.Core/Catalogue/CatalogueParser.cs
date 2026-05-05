using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using ProbHammer.Core.Contracts;

namespace ProbHammer.Core.Catalogue;

public static class CatalogueParser
{
    private static readonly XNamespace Ns = "http://www.battlescribe.net/schema/catalogueSchema";

    // Compiled patterns
    private static readonly Regex InvulnPattern =
        new(@"(\d)\+\+? invulnerable", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SimpleInvulnPattern =
        new(@"^(\d)\+\+?$", RegexOptions.Compiled);
    private static readonly Regex FnpPattern =
        new(@"Feel No Pain (\d)\+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SustainedHitsPattern =
        new(@"^Sustained Hits (\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RapidFirePattern =
        new(@"^Rapid Fire (\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MeltaPattern =
        new(@"^Melta (\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AntiPattern =
        new(@"^Anti-(\w+) (\d+)\+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ── Document loading ────────────────────────────────────────────────────────

    /// <summary>
    /// Loads an XDocument from raw bytes. .catz files use raw deflate (DeflateStream);
    /// .cat/.gst files are plain XML.
    /// </summary>
    public static async Task<XDocument> LoadDocumentAsync(
        byte[] rawBytes, string filename, CancellationToken ct = default)
    {
        using var ms = new MemoryStream(rawBytes);
        if (filename.EndsWith(".catz", StringComparison.OrdinalIgnoreCase) ||
            filename.EndsWith(".gstz", StringComparison.OrdinalIgnoreCase))
        {
            // Raw deflate — no zlib header; do NOT use ZLibStream or GZipStream
            using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
            return await XDocument.LoadAsync(deflate, LoadOptions.None, ct);
        }
        return await XDocument.LoadAsync(ms, LoadOptions.None, ct);
    }

    // ── Pass 1 ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts all profiles from the document into a dictionary keyed by id.
    /// Used in pass-1 to build the global map for cross-catalogue infoLink resolution.
    /// </summary>
    public static Dictionary<string, XElement> ExtractSharedProfiles(XDocument doc)
    {
        var result = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        if (doc.Root == null) return result;

        // Collect every <profile> element — BSData uses unique IDs across all catalogues
        foreach (var profile in doc.Root.Descendants(Ns + "profile"))
        {
            var id = (string?)profile.Attribute("id");
            if (!string.IsNullOrEmpty(id) && !result.ContainsKey(id))
                result[id] = profile;
        }
        return result;
    }

    // ── Pass 2 ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fully parses a catalogue document using the global shared-profiles map for
    /// cross-catalogue infoLink resolution.
    /// </summary>
    public static CatalogueData Parse(
        XDocument doc,
        string filename,
        IReadOnlyDictionary<string, XElement> globalProfiles,
        ILogger logger)
    {
        var root = doc.Root;
        if (root == null) return new CatalogueData { Filename = filename };

        var id = (string?)root.Attribute("id") ?? "";
        var name = (string?)root.Attribute("name") ?? "";
        _ = int.TryParse((string?)root.Attribute("revision"), out var revision);

        // Build per-catalogue shared-entry map so entryLinks can be resolved locally
        var localShared = BuildLocalSharedMap(root);

        var entries = new List<CatalogueEntry>();
        ParseEntriesInto(root.Element(Ns + "selectionEntries"), entries, globalProfiles, localShared, 0, logger);
        ParseEntriesInto(root.Element(Ns + "sharedSelectionEntries"), entries, globalProfiles, localShared, 0, logger);

        var sharedGroups = root.Element(Ns + "sharedSelectionEntryGroups");
        if (sharedGroups != null)
            foreach (var group in sharedGroups.Elements(Ns + "selectionEntryGroup"))
                entries.AddRange(CollectFromGroup(group, globalProfiles, localShared, 0, logger));

        return new CatalogueData
        {
            Id = id, Name = name, Revision = revision, Filename = filename, Entries = entries
        };
    }

    // ── Private helpers ─────────────────────────────────────────────────────────

    private static Dictionary<string, XElement> BuildLocalSharedMap(XElement root)
    {
        var map = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        var sharedEl = root.Element(Ns + "sharedSelectionEntries");
        if (sharedEl == null) return map;
        foreach (var entry in sharedEl.Elements(Ns + "selectionEntry"))
        {
            var eid = (string?)entry.Attribute("id");
            if (!string.IsNullOrEmpty(eid)) map[eid] = entry;
        }
        return map;
    }

    private static void ParseEntriesInto(
        XElement? container,
        List<CatalogueEntry> target,
        IReadOnlyDictionary<string, XElement> globalProfiles,
        IReadOnlyDictionary<string, XElement> localShared,
        int depth,
        ILogger logger)
    {
        if (container == null) return;
        foreach (var el in container.Elements(Ns + "selectionEntry"))
            target.Add(ParseEntry(el, globalProfiles, localShared, depth, logger));
    }

    private static IEnumerable<CatalogueEntry> CollectFromGroup(
        XElement groupEl,
        IReadOnlyDictionary<string, XElement> globalProfiles,
        IReadOnlyDictionary<string, XElement> localShared,
        int depth,
        ILogger logger)
    {
        var directEntries = groupEl.Element(Ns + "selectionEntries");
        if (directEntries != null)
            foreach (var el in directEntries.Elements(Ns + "selectionEntry"))
                yield return ParseEntry(el, globalProfiles, localShared, depth, logger);

        if (depth < 6)
        {
            var nestedGroups = groupEl.Element(Ns + "selectionEntryGroups");
            if (nestedGroups != null)
                foreach (var nested in nestedGroups.Elements(Ns + "selectionEntryGroup"))
                    foreach (var e in CollectFromGroup(nested, globalProfiles, localShared, depth + 1, logger))
                        yield return e;
        }

        var groupLinks = groupEl.Element(Ns + "entryLinks");
        if (groupLinks != null)
            foreach (var link in groupLinks.Elements(Ns + "entryLink"))
            {
                var tid = (string?)link.Attribute("targetId") ?? "";
                if (localShared.TryGetValue(tid, out var target))
                    yield return ParseEntry(target, globalProfiles, localShared, depth, logger);
            }
    }

    private static CatalogueEntry ParseEntry(
        XElement el,
        IReadOnlyDictionary<string, XElement> globalProfiles,
        IReadOnlyDictionary<string, XElement> localShared,
        int depth,
        ILogger logger)
    {
        var id = (string?)el.Attribute("id") ?? "";
        var name = (string?)el.Attribute("name") ?? "";
        var type = (string?)el.Attribute("type") ?? "";

        var (statline, abilities, weapons) = ParseProfiles(name, el.Element(Ns + "profiles"));
        var keywords = ExtractKeywords(el);
        var (invuln, fnp) = ExtractInvulnFnp(abilities, el, globalProfiles);

        var children = new List<CatalogueEntry>();
        if (depth < 6)
        {
            var directEl = el.Element(Ns + "selectionEntries");
            if (directEl != null)
                foreach (var child in directEl.Elements(Ns + "selectionEntry"))
                    children.Add(ParseEntry(child, globalProfiles, localShared, depth + 1, logger));

            var groupsEl = el.Element(Ns + "selectionEntryGroups");
            if (groupsEl != null)
                foreach (var group in groupsEl.Elements(Ns + "selectionEntryGroup"))
                    children.AddRange(CollectFromGroup(group, globalProfiles, localShared, depth + 1, logger));

            var linksEl = el.Element(Ns + "entryLinks");
            if (linksEl != null)
                foreach (var link in linksEl.Elements(Ns + "entryLink"))
                {
                    var tid = (string?)link.Attribute("targetId") ?? "";
                    if (localShared.TryGetValue(tid, out var target))
                        children.Add(ParseEntry(target, globalProfiles, localShared, depth + 1, logger));
                    else
                        logger.LogDebug("Unresolved entryLink targetId={TargetId} in entry {Name}", tid, name);
                }
        }

        return new CatalogueEntry
        {
            Id = id, Name = name, EntryType = type,
            Statline = statline, Abilities = abilities, Weapons = weapons, Keywords = keywords,
            EntryInvulnerableSave = invuln, EntryFeelNoPain = fnp,
            Children = children
        };
    }

    private static (CatalogueStatline? statline, List<AbilityProfile> abilities, List<CatalogueWeaponEntry> weapons)
        ParseProfiles(string entryName, XElement? profilesEl)
    {
        if (profilesEl == null) return (null, [], []);

        CatalogueStatline? statline = null;
        var abilities = new List<AbilityProfile>();
        var weaponData = new List<(string profileName, bool isRanged, XElement el)>();

        // Pass 1: standard profile types
        foreach (var profile in profilesEl.Elements(Ns + "profile"))
        {
            var typeName = (string?)profile.Attribute("typeName") ?? "";
            var profileName = (string?)profile.Attribute("name") ?? "";
            var chars = GetCharacteristics(profile);

            if (typeName.Equals("Unit", StringComparison.OrdinalIgnoreCase))
            {
                statline = new CatalogueStatline
                {
                    Toughness = ParseInt(chars.GetValueOrDefault("T")),
                    Save = ParseSaveValue(chars.GetValueOrDefault("Sv")),
                    Wounds = ParseInt(chars.GetValueOrDefault("W"))
                };
            }
            else if (typeName.Equals("Ranged Weapons", StringComparison.OrdinalIgnoreCase))
            {
                weaponData.Add((profileName, true, profile));
            }
            else if (typeName.Equals("Melee Weapons", StringComparison.OrdinalIgnoreCase))
            {
                weaponData.Add((profileName, false, profile));
            }
            else if (typeName.Equals("Abilities", StringComparison.OrdinalIgnoreCase))
            {
                abilities.Add(new AbilityProfile
                {
                    Name = profileName,
                    Text = chars.GetValueOrDefault("Description", "")
                });
            }
        }

        // Pass 2: non-standard typeName values are sub-ability groups (e.g. "Lord of the Death Guard").
        // Two-pass is required because sub-ability profiles may appear before their named parent ability in XML.
        var subGroups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profilesEl.Elements(Ns + "profile"))
        {
            var typeName = (string?)profile.Attribute("typeName") ?? "";
            if (string.IsNullOrEmpty(typeName) ||
                typeName.Equals("Unit", StringComparison.OrdinalIgnoreCase) ||
                typeName.Equals("Ranged Weapons", StringComparison.OrdinalIgnoreCase) ||
                typeName.Equals("Melee Weapons", StringComparison.OrdinalIgnoreCase) ||
                typeName.Equals("Abilities", StringComparison.OrdinalIgnoreCase))
                continue;

            var profileName = (string?)profile.Attribute("name") ?? "";
            var charsEl = profile.Element(Ns + "characteristics");
            var charValues = charsEl == null
                ? []
                : charsEl.Elements(Ns + "characteristic")
                         .Select(c => c.Value)
                         .Where(v => !string.IsNullOrWhiteSpace(v))
                         .ToList();
            var joined = string.Join(" — ", charValues);

            if (!subGroups.TryGetValue(typeName, out var lines))
                subGroups[typeName] = lines = [];
            lines.Add($"• {profileName}: {joined}");
        }

        foreach (var (typeName, lines) in subGroups)
        {
            var subText = string.Join("\n", lines);
            var existing = abilities.FirstOrDefault(a =>
                a.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                existing.Text = string.IsNullOrEmpty(existing.Text)
                    ? subText
                    : existing.Text + "\n" + subText;
            else
                abilities.Add(new AbilityProfile { Name = typeName, Text = subText });
        }

        List<CatalogueWeaponEntry> weapons = [];
        if (weaponData.Count > 0)
        {
            var isRanged = weaponData[0].isRanged;
            var variants = new List<CatalogueWeaponVariant>();
            int range = 0;

            foreach (var (profileName, _, profileEl) in weaponData)
            {
                var chars = GetCharacteristics(profileEl);
                range = ParseRange(chars.GetValueOrDefault("Range", "Melee"));
                var skillKey = isRanged ? "BS" : "WS";
                variants.Add(new CatalogueWeaponVariant
                {
                    VariantName = ExtractVariantName(profileName, entryName),
                    AttacksRaw = chars.GetValueOrDefault("A", "1"),
                    Skill = ParseSaveValue(chars.GetValueOrDefault(skillKey, "4+")),
                    Strength = ParseInt(chars.GetValueOrDefault("S")),
                    Ap = ParseInt(chars.GetValueOrDefault("AP")),
                    DamageRaw = chars.GetValueOrDefault("D", "1"),
                    Abilities = ParseWeaponKeywords(chars.GetValueOrDefault("Keywords", "-"))
                });
            }

            weapons = [new CatalogueWeaponEntry
            {
                Name = entryName,
                Type = isRanged ? WeaponType.Ranged : WeaponType.Melee,
                Range = range,
                Variants = variants
            }];
        }

        return (statline, abilities, weapons);
    }

    private static (int? invuln, int? fnp) ExtractInvulnFnp(
        List<AbilityProfile> abilities,
        XElement entryEl,
        IReadOnlyDictionary<string, XElement> globalProfiles)
    {
        int? invuln = null, fnp = null;

        // Pattern 1: ability text
        foreach (var ability in abilities)
        {
            if (invuln == null)
            {
                var m = InvulnPattern.Match(ability.Text);
                if (m.Success) invuln = int.Parse(m.Groups[1].Value);
            }
            if (fnp == null)
            {
                var m = FnpPattern.Match(ability.Text);
                if (m.Success) fnp = int.Parse(m.Groups[1].Value);
            }
        }

        // Pattern 2: infoLinks
        var infoLinksEl = entryEl.Element(Ns + "infoLinks");
        if (infoLinksEl != null)
        {
            foreach (var link in infoLinksEl.Elements(Ns + "infoLink"))
            {
                var linkName = (string?)link.Attribute("name") ?? "";
                var targetId = (string?)link.Attribute("targetId") ?? "";

                if (invuln == null &&
                    linkName.Equals("Invulnerable Save", StringComparison.OrdinalIgnoreCase) &&
                    globalProfiles.TryGetValue(targetId, out var invulnProfile))
                {
                    var desc = GetCharacteristics(invulnProfile).GetValueOrDefault("Description", "");
                    var m = InvulnPattern.Match(desc);
                    if (m.Success)
                        invuln = int.Parse(m.Groups[1].Value);
                    else
                    {
                        var m2 = SimpleInvulnPattern.Match(desc.Trim());
                        if (m2.Success) invuln = int.Parse(m2.Groups[1].Value);
                    }
                }

                if (fnp == null &&
                    linkName.Equals("Feel No Pain", StringComparison.OrdinalIgnoreCase))
                {
                    // Some BSData entries encode FNP threshold via a modifier value
                    var mod = link.Descendants(Ns + "modifier")
                        .FirstOrDefault(m =>
                            "set".Equals((string?)m.Attribute("type"), StringComparison.OrdinalIgnoreCase));
                    if (mod != null && int.TryParse((string?)mod.Attribute("value"), out var fnpFromMod))
                        fnp = fnpFromMod;

                    // Fallback: profile text
                    if (fnp == null && globalProfiles.TryGetValue(targetId, out var fnpProfile))
                    {
                        var desc = GetCharacteristics(fnpProfile).GetValueOrDefault("Description", "");
                        var m = FnpPattern.Match(desc);
                        if (m.Success) fnp = int.Parse(m.Groups[1].Value);
                    }
                }
            }
        }

        return (invuln, fnp);
    }

    private static List<string> ExtractKeywords(XElement entryEl)
    {
        var result = new List<string>();
        var kwEl = entryEl.Element(Ns + "keywords");
        if (kwEl == null) return result;
        foreach (var kw in kwEl.Elements(Ns + "keyword"))
        {
            var kwName = (string?)kw.Attribute("name");
            if (!string.IsNullOrEmpty(kwName))
                result.Add(kwName.ToUpperInvariant());
        }
        return result;
    }

    private static Dictionary<string, string> GetCharacteristics(XElement profileEl)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var charsEl = profileEl.Element(Ns + "characteristics");
        if (charsEl == null) return result;
        foreach (var c in charsEl.Elements(Ns + "characteristic"))
        {
            var n = (string?)c.Attribute("name") ?? "";
            if (!string.IsNullOrEmpty(n)) result[n] = c.Value;
        }
        return result;
    }

    private static string ExtractVariantName(string profileName, string entryName)
    {
        // Strip "➤ " prefix (U+27A4 followed by space)
        var name = profileName.StartsWith("➤ ") ? profileName[2..] : profileName;
        // Strip "<entryName> - " prefix
        var prefix = entryName + " - ";
        if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return name[prefix.Length..];
        if (name.Equals(entryName, StringComparison.OrdinalIgnoreCase))
            return "";
        return name;
    }

    private static CatalogueWeaponAbilities ParseWeaponKeywords(string? keywords)
    {
        var result = new CatalogueWeaponAbilities();
        if (string.IsNullOrWhiteSpace(keywords) || keywords == "-") return result;

        foreach (var kw in keywords.Split(',').Select(k => k.Trim()))
        {
            if (kw.Equals("Blast", StringComparison.OrdinalIgnoreCase)) result.Blast = true;
            else if (kw.Equals("Torrent", StringComparison.OrdinalIgnoreCase)) result.Torrent = true;
            else if (kw.Equals("Lethal Hits", StringComparison.OrdinalIgnoreCase)) result.LethalHits = true;
            else if (kw.Equals("Devastating Wounds", StringComparison.OrdinalIgnoreCase)) result.DevastatingWounds = true;
            else if (kw.Equals("Twin-linked", StringComparison.OrdinalIgnoreCase)) result.TwinLinked = true;
            else if (kw.Equals("Indirect Fire", StringComparison.OrdinalIgnoreCase)) result.IndirectFire = true;
            else
            {
                Match m;
                if ((m = SustainedHitsPattern.Match(kw)).Success)
                    result.SustainedHits = int.Parse(m.Groups[1].Value);
                else if ((m = RapidFirePattern.Match(kw)).Success)
                    result.RapidFire = int.Parse(m.Groups[1].Value);
                else if ((m = MeltaPattern.Match(kw)).Success)
                    result.Melta = int.Parse(m.Groups[1].Value);
                else if ((m = AntiPattern.Match(kw)).Success)
                    result.Anti[m.Groups[1].Value.ToUpperInvariant()] = int.Parse(m.Groups[2].Value);
            }
        }

        return result;
    }

    private static int ParseInt(string? s) =>
        int.TryParse(s?.Trim(), out var v) ? v : 0;

    private static int ParseSaveValue(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        return int.TryParse(s.Trim().TrimEnd('+'), out var v) ? v : 0;
    }

    private static int ParseRange(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        var trimmed = s.Trim().TrimEnd('"');
        if (trimmed.Equals("Melee", StringComparison.OrdinalIgnoreCase)) return 0;
        if (trimmed.Equals("N/A", StringComparison.OrdinalIgnoreCase)) return 0;
        return int.TryParse(trimmed, out var r) ? r : 0;
    }
}
