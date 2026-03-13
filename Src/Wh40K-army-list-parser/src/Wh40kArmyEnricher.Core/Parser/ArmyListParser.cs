using System.Text.RegularExpressions;
using Wh40kArmyEnricher.Core.Models;

namespace Wh40kArmyEnricher.Core.Parser;

/// <summary>
/// Parses Warhammer 40,000 (10th edition) army list text exports from the official app.
/// </summary>
public class ArmyListParser
{
    private const char ModelBullet = '\u2022';   // •
    private const char WeaponBullet = '\u25E6';  // ◦

    private static readonly Regex PointsHeaderRegex =
        new(@"^(.+?)\s+\((\d[\d,]*)\s+Points?\)$", RegexOptions.Compiled);

    private static readonly Regex CountPrefixRegex =
        new(@"^(\d+)x\s+(.+)$", RegexOptions.Compiled);

    private static readonly HashSet<string> SectionHeadings = new(StringComparer.OrdinalIgnoreCase)
    {
        "CHARACTERS", "BATTLELINE", "DEDICATED TRANSPORTS", "OTHER DATASHEETS",
        "ALLIED UNITS", "FORTIFICATIONS"
    };

    // ---------------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------------

    public ArmyList Parse(string text)
    {
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        // Line 0: army name + total points
        var armyLine = lines.Count > 0 ? lines[0].Trim() : "";
        var armyMatch = PointsHeaderRegex.Match(armyLine);
        var armyName = armyMatch.Success ? armyMatch.Groups[1].Value.Trim() : armyLine;
        var armyPoints = armyMatch.Success ? ParsePoints(armyMatch.Groups[2].Value) : 0;

        // Metadata block: up to 4 non-blank lines after the army header, before first section
        string gameSystem = "", faction = "", detachment = "";
        int metaIdx = 1;
        var metaLines = new List<string>();
        while (metaIdx < lines.Count)
        {
            var l = lines[metaIdx].Trim();
            if (l.Length == 0) { metaIdx++; continue; }
            if (SectionHeadings.Contains(l)) break;
            // Stop if we hit something that looks like a unit header (has "(N Points)")
            if (PointsHeaderRegex.IsMatch(l) && !l.Contains(",")) break;
            metaLines.Add(l);
            metaIdx++;
            if (metaLines.Count == 4) break;
        }
        if (metaLines.Count >= 1) gameSystem = metaLines[0];
        if (metaLines.Count >= 2) faction = metaLines[1];
        if (metaLines.Count >= 3) detachment = metaLines[2];

        // Find start of section/unit content
        int bodyStart = metaIdx;

        // Parse sections and units
        var units = ParseUnits(lines, bodyStart, faction);

        return new ArmyList(armyName, armyPoints, gameSystem, faction, detachment, units);
    }

    // ---------------------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------------------

    private static List<UnitEntry> ParseUnits(List<string> lines, int start, string faction)
    {
        var units = new List<UnitEntry>();
        var currentCategory = "UNCATEGORISED";
        int i = start;

        while (i < lines.Count)
        {
            var line = lines[i].Trim();

            if (line.Length == 0) { i++; continue; }

            // Skip the app export footer
            if (line.StartsWith("Exported with App Version", StringComparison.OrdinalIgnoreCase))
            {
                i++;
                continue;
            }

            // Section heading?
            if (SectionHeadings.Contains(line))
            {
                currentCategory = line.ToUpperInvariant();
                i++;
                continue;
            }

            // Unit header?
            var unitMatch = PointsHeaderRegex.Match(line);
            if (unitMatch.Success)
            {
                var unitName = unitMatch.Groups[1].Value.Trim();
                var unitPoints = ParsePoints(unitMatch.Groups[2].Value);

                // Collect all bullet lines belonging to this unit
                i++;
                var bulletLines = new List<(char Bullet, string Content)>();
                while (i < lines.Count)
                {
                    var bl = lines[i];
                    var trimmed = bl.Trim();
                    if (trimmed.Length == 0) { i++; break; }
                    if (SectionHeadings.Contains(trimmed)) break;
                    if (PointsHeaderRegex.IsMatch(trimmed) && !trimmed.StartsWith("•") && !trimmed.StartsWith("◦")) break;
                    if (trimmed.StartsWith("Exported with App Version", StringComparison.OrdinalIgnoreCase)) break;

                    if (trimmed.Length > 0 && (trimmed[0] == ModelBullet || trimmed[0] == WeaponBullet))
                    {
                        bulletLines.Add((trimmed[0], trimmed.Substring(1).Trim()));
                    }
                    i++;
                }

                var (enhancements, models) = ParseUnitItems(unitName, bulletLines);
                units.Add(new UnitEntry(unitName, unitPoints, currentCategory, enhancements, models));
                continue;
            }

            i++;
        }

        return units;
    }

    private static (IReadOnlyList<string> Enhancements, IReadOnlyList<ModelEntry> Models)
        ParseUnitItems(string unitName, List<(char Bullet, string Content)> items)
    {
        var enhancements = new List<string>();

        // Separate enhancement / metadata lines out of the bullet list first
        var filtered = new List<(char Bullet, string Content)>();
        foreach (var (bullet, content) in items)
        {
            if (bullet == ModelBullet)
            {
                if (content.StartsWith("Enhancements:", StringComparison.OrdinalIgnoreCase))
                {
                    var enhancement = content.Substring("Enhancements:".Length).Trim();
                    enhancements.Add(enhancement);
                    continue;
                }
                if (string.Equals(content, "Warlord", StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            filtered.Add((bullet, content));
        }

        // Determine mode: does any • bullet have ◦ children immediately after it?
        bool modelMode = false;
        for (int i = 0; i < filtered.Count - 1; i++)
        {
            if (filtered[i].Bullet == ModelBullet && filtered[i + 1].Bullet == WeaponBullet)
            {
                modelMode = true;
                break;
            }
        }

        var models = new List<ModelEntry>();

        if (modelMode)
        {
            // • = model entry, ◦ = weapons of that model
            string? currentModelName = null;
            int currentModelCount = 1;
            var currentWeapons = new List<WeaponEntry>();

            void FlushModel()
            {
                if (currentModelName != null)
                    models.Add(new ModelEntry(currentModelName, currentModelCount, currentWeapons.ToList()));
                currentWeapons.Clear();
            }

            foreach (var (bullet, content) in filtered)
            {
                if (bullet == ModelBullet)
                {
                    FlushModel();
                    (currentModelName, currentModelCount) = ParseCountPrefix(content);
                }
                else if (bullet == WeaponBullet && currentModelName != null)
                {
                    var (wName, wCount) = ParseCountPrefix(content);
                    currentWeapons.Add(new WeaponEntry(wName, wCount));
                }
            }
            FlushModel();
        }
        else
        {
            // • items are weapons of a single implicit model named after the unit
            var weapons = new List<WeaponEntry>();
            foreach (var (bullet, content) in filtered)
            {
                if (bullet == ModelBullet)
                {
                    var (wName, wCount) = ParseCountPrefix(content);
                    weapons.Add(new WeaponEntry(wName, wCount));
                }
                // ◦ at root level (no preceding • model): treat as weapon too
                else if (bullet == WeaponBullet)
                {
                    var (wName, wCount) = ParseCountPrefix(content);
                    weapons.Add(new WeaponEntry(wName, wCount));
                }
            }
            models.Add(new ModelEntry(unitName, 1, weapons));
        }

        return (enhancements, models);
    }

    private static (string Name, int Count) ParseCountPrefix(string content)
    {
        var m = CountPrefixRegex.Match(content);
        if (m.Success)
            return (m.Groups[2].Value.Trim(), int.Parse(m.Groups[1].Value));
        return (content.Trim(), 1);
    }

    private static int ParsePoints(string raw) =>
        int.TryParse(raw.Replace(",", ""), out var n) ? n : 0;
}
