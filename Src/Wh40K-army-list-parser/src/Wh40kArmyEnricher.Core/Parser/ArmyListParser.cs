using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Wh40kArmyEnricher.Core.Models;

namespace Wh40kArmyEnricher.Core.Parser;

/// <summary>
/// Parses Warhammer 40,000 (10th edition) army list text exports from the official app.
/// Handles the iOS format (current and legacy) and the Android format.
///
/// iOS (current, clipboard):
///   [2sp]•  model entry
///   [5sp]◦  weapon of the current model (◦ = U+25E6, any indent)
///
/// iOS (legacy fixture format, column 0):
///   •  model entry
///   ◦  weapon of the current model
///
/// Android:
///   [2sp]•  model (or single-model first weapon)
///   [4sp]•  first weapon of a squad model
///   [4-6sp] weapon continuation lines (no bullet)
///
/// Key rule: ◦ (U+25E6) at ANY indent level always means "weapon of the current model".
/// </summary>
public class ArmyListParser
{
    private readonly ILogger<ArmyListParser> _logger;

    public ArmyListParser(ILogger<ArmyListParser> logger)
    {
        _logger = logger;
    }
    private static readonly Regex PointsHeaderRegex =
        new(@"^(.+?)\s+\((\d[\d,]*)\s+Points?\)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
        // Normalise all line-separator variants to LF before splitting.
        // The iOS Warhammer 40k app clipboard emits bare \r (CR-only) between bullet lines
        // within a unit block, while using \n between unit headers. Email and messaging apps
        // normalise these before transmission, which is why the issue only appears when pasting
        // directly from the iOS app clipboard. The \r\n replacement must come first so that
        // CRLF pairs are collapsed to a single \n rather than two.
        text = text
            .Replace("\r\n", "\n")   // Windows CRLF → LF
            .Replace("\r",   "\n")   // bare CR (iOS app, old Mac) → LF
            .Replace("\u2028", "\n") // Unicode LINE SEPARATOR → LF
            .Replace("\u2029", "\n"); // Unicode PARAGRAPH SEPARATOR → LF

        var lines = text.Split('\n').ToList();

        // Line 0: army name + total points
        var armyLine = lines.Count > 0 ? lines[0].Trim() : "";
        var armyMatch = PointsHeaderRegex.Match(armyLine);
        var armyName = armyMatch.Success ? armyMatch.Groups[1].Value.Trim() : armyLine;
        var armyPoints = armyMatch.Success ? ParsePoints(armyMatch.Groups[2].Value) : 0;

        // Metadata block: non-blank lines before the first section heading.
        // iOS order:     GameSystem / Faction / Detachment / [ForceSize]
        // Android order: Faction / [ForceSize] / Detachment
        // The force-size line (e.g. "Strike Force (2,000 Points)") is consumed and not stored.
        string gameSystem = "", faction = "", detachment = "";
        int metaIdx = 1;
        var metaLines = new List<string>();
        while (metaIdx < lines.Count)
        {
            var l = lines[metaIdx].Trim();
            if (l.Length == 0) { metaIdx++; continue; }
            if (SectionHeadings.Contains(l)) break;
            if (PointsHeaderRegex.IsMatch(l)) { metaIdx++; break; }  // force-size line
            metaLines.Add(l);
            metaIdx++;
            if (metaLines.Count == 4) break;
        }

        if (metaLines.Count >= 1) gameSystem = metaLines[0];
        if (metaLines.Count >= 2) faction = metaLines[1];
        if (metaLines.Count >= 3) detachment = metaLines[2];
        if (string.IsNullOrEmpty(faction)) faction = gameSystem;

        // Android puts the detachment AFTER the force-size line, so if we broke early
        // on the force-size and detachment is still empty, scan one more line.
        if (string.IsNullOrEmpty(detachment))
        {
            while (metaIdx < lines.Count)
            {
                var l = lines[metaIdx].Trim();
                metaIdx++;
                if (l.Length == 0) continue;
                if (SectionHeadings.Contains(l)) break;
                if (PointsHeaderRegex.IsMatch(l)) continue;  // another force-size, skip
                detachment = l;
                break;
            }
        }

        int bodyStart = metaIdx;
        var units = ParseUnits(lines, bodyStart);

        return new ArmyList(armyName, armyPoints, gameSystem, faction, detachment, units);
    }

    // ---------------------------------------------------------------------------
    // Unit parsing
    // ---------------------------------------------------------------------------

    private static List<UnitEntry> ParseUnits(List<string> lines, int start)
    {
        var units = new List<UnitEntry>();
        var currentCategory = "UNCATEGORISED";
        int i = start;

        while (i < lines.Count)
        {
            var line = lines[i].Trim();

            if (line.Length == 0) { i++; continue; }

            if (line.StartsWith("Exported with App Version", StringComparison.OrdinalIgnoreCase))
            {
                i++;
                continue;
            }

            if (SectionHeadings.Contains(line))
            {
                currentCategory = line.ToUpperInvariant();
                i++;
                continue;
            }

            var unitMatch = PointsHeaderRegex.Match(line);
            if (unitMatch.Success)
            {
                var unitName = unitMatch.Groups[1].Value.Trim();
                var unitPoints = ParsePoints(unitMatch.Groups[2].Value);

                // Collect all bullet/indented lines belonging to this unit
                i++;
                var bulletLines = new List<(int Level, bool IsBullet, string Content)>();
                while (i < lines.Count)
                {
                    var bl = lines[i];
                    var trimmed = bl.Trim();

                    if (trimmed.Length == 0) { i++; break; }
                    if (SectionHeadings.Contains(trimmed)) break;
                    if (trimmed.StartsWith("Exported with App Version", StringComparison.OrdinalIgnoreCase)) break;
                    // A bare points-header line that isn't a bullet is the next unit
                    if (PointsHeaderRegex.IsMatch(trimmed)
                        && !trimmed.StartsWith('\u2022') && !trimmed.StartsWith('\u25E6')) break;

                    var classified = ClassifyBulletLine(bl);
                    if (classified.HasValue)
                        bulletLines.Add(classified.Value);

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

    // ---------------------------------------------------------------------------
    // Line classification
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Classifies a raw line as a bullet item normalised to (Level, IsBullet, Content).
    /// Level 0 = model (or single-model weapon); Level 1 = weapon of the current model.
    /// IsBullet distinguishes explicit bullet lines from Android weapon-continuation lines.
    ///
    /// iOS current (clipboard) format:
    ///   [2sp]• content     → (0, true,  content)   model entry
    ///   [5sp]◦ content     → (1, true,  content)   weapon of the current model
    ///
    /// iOS legacy (column-0) format:
    ///   • content          → (0, true,  content)   model entry
    ///   ◦ content          → (1, true,  content)   weapon of the current model
    ///
    /// Android format:
    ///   [2sp]• content     → (0, true,  content)   model or single-model first weapon
    ///   [4sp]• content     → (1, true,  content)   first weapon of a model
    ///   [4-6sp] content    → (1, false, content)   weapon continuation (no bullet)
    ///
    /// ◦ (U+25E6) at any indent is always level 1 (weapon).
    /// </summary>
    private static (int Level, bool IsBullet, string Content)? ClassifyBulletLine(string rawLine)
    {
        // Use TrimStart() (all Unicode whitespace) rather than TrimStart(' ') so that
        // tabs, non-breaking spaces (U+00A0), and other space-like characters used for
        // indentation by some app versions are treated correctly as indent.
        var stripped = rawLine.TrimStart();
        int indent = rawLine.Length - stripped.Length;

        // ◦ (U+25E6) always means weapon/sub-item regardless of indent level.
        // iOS format uses ◦ at column 0; some iOS app versions use indented ◦.
        if (stripped.StartsWith('\u25E6')) return (1, true, stripped[1..].Trim());

        // iOS format: • bullet at column 0 = model
        if (indent == 0)
        {
            if (stripped.StartsWith('\u2022')) return (0, true, stripped[1..].Trim());
            return null;
        }

        // Android format: indented • bullet
        if (stripped.StartsWith('\u2022'))
        {
            var content = stripped[1..].Trim();
            return indent >= 4
                ? (1, true, content)   // [4sp]• = weapon
                : (0, true, content);  // [2sp]• = model or first weapon
        }

        // Android format: continuation weapon line (no bullet, indented ≥ 4)
        if (indent >= 4 && stripped.Length > 0)
            return (1, false, stripped);

        return null;
    }

    // ---------------------------------------------------------------------------
    // Unit item parsing
    // ---------------------------------------------------------------------------

    private static (IReadOnlyList<string> Enhancements, IReadOnlyList<ModelEntry> Models)
        ParseUnitItems(string unitName, List<(int Level, bool IsBullet, string Content)> items)
    {
        var enhancements = new List<string>();
        var filtered = new List<(int Level, bool IsBullet, string Content)>();

        foreach (var item in items)
        {
            var content = item.Content;

            // Enhancement lines (iOS: ◦ Enhancements: Name  /  Android: • Enhancement: Name)
            if (content.StartsWith("Enhancements:", StringComparison.OrdinalIgnoreCase))
            {
                enhancements.Add(content["Enhancements:".Length..].Trim());
                continue;
            }
            if (content.StartsWith("Enhancement:", StringComparison.OrdinalIgnoreCase))
            {
                enhancements.Add(content["Enhancement:".Length..].Trim());
                continue;
            }

            // Warlord designation marker — discard
            if (string.Equals(content, "Warlord", StringComparison.OrdinalIgnoreCase))
                continue;

            filtered.Add(item);
        }

        // Model mode: at least one level-1 item carries a bullet character.
        //   iOS:     ◦ items are always (Level=1, IsBullet=true)
        //   Android: [4sp]• items are (Level=1, IsBullet=true)
        //
        // If level-1 items only appear as continuation lines (IsBullet=false), this is a
        // single-model unit whose extra weapons are listed as bare indented lines.
        bool modelMode = filtered.Any(item => item.Level == 1 && item.IsBullet);

        var models = new List<ModelEntry>();

        if (modelMode)
        {
            // Level 0 = model entry; Level 1 = weapon of the current model
            string? currentModelName = null;
            int currentModelCount = 1;
            var currentWeapons = new List<WeaponEntry>();

            void FlushModel()
            {
                if (currentModelName != null)
                    models.Add(new ModelEntry(currentModelName, currentModelCount, currentWeapons.ToList()));
                currentWeapons.Clear();
            }

            foreach (var (level, _, content) in filtered)
            {
                if (level == 0)
                {
                    FlushModel();
                    (currentModelName, currentModelCount) = ParseCountPrefix(content);
                }
                else if (currentModelName != null)
                {
                    var (wName, wCount) = ParseCountPrefix(content);
                    currentWeapons.Add(new WeaponEntry(wName, wCount));
                }
            }
            FlushModel();
        }
        else
        {
            // All items (level 0 and level 1) are weapons of a single implicit model
            var weapons = new List<WeaponEntry>();
            foreach (var (_, _, content) in filtered)
            {
                var (wName, wCount) = ParseCountPrefix(content);
                weapons.Add(new WeaponEntry(wName, wCount));
            }
            models.Add(new ModelEntry(unitName, 1, weapons));
        }

        return (enhancements, models);
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

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
