using System.Text.RegularExpressions;
using ProbHammer.Core.Contracts;

namespace ProbHammer.Core.Parsing;

public class ArmyListParser
{
    // Matches any line containing a points value in parentheses, e.g. "Iron Canticle (2035 Points)"
    private static readonly Regex PointsLineRegex =
        new(@"^.+\(\s*[\d,]+\s*[Pp]oints?\s*\)\s*$", RegexOptions.Compiled);

    // Captures the name and points number from a unit/army header line
    private static readonly Regex NamePointsRegex =
        new(@"^(.+?)\s*\(\s*([\d,]+)\s*[Pp]oints?\s*\)", RegexOptions.Compiled);

    private static readonly Regex CountPrefixRegex =
        new(@"^(\d+)x\s+(.+)$", RegexOptions.Compiled);

    private static readonly HashSet<string> SectionHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "CHARACTERS", "BATTLELINE", "DEDICATED TRANSPORTS", "OTHER DATASHEETS"
    };

    private const char Bullet = '•';   // •
    private const char SubBullet = '◦'; // ◦

    public ArmyList Parse(string text)
    {
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        // Android format has no ◦ characters; iOS (current and legacy) always use ◦ for weapons
        bool isAndroid = !text.Contains(SubBullet);
        return isAndroid ? ParseAndroid(lines) : ParseiOS(lines);
    }

    // ── iOS (current and legacy) ─────────────────────────────────────────────

    private ArmyList ParseiOS(List<string> lines)
    {
        int i = SkipBlanks(lines, 0);
        var (name, points) = ParseNamePoints(lines[i++]);

        // Collect non-blank metadata lines until the first section header
        var metaLines = new List<string>();
        i = SkipBlanks(lines, i);
        while (i < lines.Count && !SectionHeaders.Contains(lines[i].Trim()))
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
                metaLines.Add(lines[i].Trim());
            i++;
        }

        // iOS metadata order: GameSystem / Faction / Detachment / ForceSize(consumed)
        string gameSys = metaLines.Count > 0 ? metaLines[0] : "";
        string faction = metaLines.Count > 1 ? metaLines[1] : gameSys;
        string detachment = metaLines.Count > 2 ? metaLines[2] : "";

        var units = ParseUnitLines(lines, i, isAndroid: false);
        return new ArmyList(name, points, gameSys, faction, detachment, units);
    }

    // ── Android ──────────────────────────────────────────────────────────────

    private ArmyList ParseAndroid(List<string> lines)
    {
        int i = SkipBlanks(lines, 0);
        var (name, points) = ParseNamePoints(lines[i++]);

        var metaLines = new List<string>();
        i = SkipBlanks(lines, i);
        while (i < lines.Count && !SectionHeaders.Contains(lines[i].Trim()))
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
                metaLines.Add(lines[i].Trim());
            i++;
        }

        // Android metadata order: Faction / ForceSize / Detachment
        string faction = metaLines.Count > 0 ? metaLines[0] : "";
        int mi = 1;
        // Skip force-size line(s) that match the points pattern
        while (mi < metaLines.Count && PointsLineRegex.IsMatch(metaLines[mi]))
            mi++;
        string detachment = mi < metaLines.Count ? metaLines[mi] : "";

        var units = ParseUnitLines(lines, i, isAndroid: true);
        return new ArmyList(name, points, GameSystem: faction, faction, detachment, units);
    }

    // ── Shared unit-line parser ───────────────────────────────────────────────

    private List<UnitEntry> ParseUnitLines(List<string> lines, int startIdx, bool isAndroid)
    {
        var units = new List<UnitEntry>();
        string currentCategory = "";
        UnitBlock? current = null;

        void Flush()
        {
            if (current is null) return;
            units.Add(BuildUnit(current, isAndroid));
            current = null;
        }

        for (int i = startIdx; i < lines.Count; i++)
        {
            var raw = lines[i];
            var trimmed = raw.Trim();

            if (string.IsNullOrWhiteSpace(trimmed)) continue;
            if (trimmed.StartsWith("Exported with App Version", StringComparison.OrdinalIgnoreCase)) break;

            if (SectionHeaders.Contains(trimmed))
            {
                Flush();
                currentCategory = trimmed;
                continue;
            }

            // A unit header line: matches name+(points) but is NOT a bullet line
            if (!IsBulletLine(raw) && NamePointsRegex.IsMatch(trimmed))
            {
                Flush();
                var (unitName, unitPoints) = ParseNamePoints(trimmed);
                current = new UnitBlock(unitName, unitPoints, currentCategory);
                continue;
            }

            current?.Lines.Add(raw);
        }

        Flush();
        return units;
    }

    // ── Unit block → UnitEntry ───────────────────────────────────────────────

    private static UnitEntry BuildUnit(UnitBlock block, bool isAndroid)
    {
        var classified = block.Lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(ClassifyBulletLine)
            .Where(c => c.IsBullet || c.Level == 1)
            .ToList();

        // Model mode: any Level-1 line that carries its own bullet (◦ or 4-space •)
        bool modelMode = classified.Any(c => c.Level == 1 && c.IsBullet);

        var enhancements = new List<string>();
        var models = new List<ModelEntry>();

        if (modelMode)
        {
            string? currentModelName = null;
            int currentModelCount = 1;
            var currentWeapons = new List<WeaponEntry>();

            void FinalizeModel()
            {
                if (currentModelName is null) return;
                models.Add(new ModelEntry(currentModelName, currentModelCount, currentWeapons.ToArray()));
                currentModelName = null;
                currentWeapons = [];
            }

            foreach (var (level, isBullet, content) in classified)
            {
                if (IsEnhancement(content, out var enhName))
                {
                    enhancements.Add(enhName);
                    continue;
                }

                if (level == 0 && isBullet)
                {
                    FinalizeModel();
                    var (count, mName) = ParseCountedItem(content);
                    currentModelName = mName;
                    currentModelCount = count;
                }
                else if (level == 1)
                {
                    var (count, wName) = ParseCountedItem(content);
                    currentWeapons.Add(new WeaponEntry(wName, count));
                }
            }
            FinalizeModel();
        }
        else
        {
            // Single-model unit: every item (Level 0 and Level 1) is a weapon/wargear entry
            var weapons = new List<WeaponEntry>();
            foreach (var (_, _, content) in classified)
            {
                if (IsEnhancement(content, out var enhName))
                {
                    enhancements.Add(enhName);
                    continue;
                }
                var (count, wName) = ParseCountedItem(content);
                weapons.Add(new WeaponEntry(wName, count));
            }
            models.Add(new ModelEntry(block.Name, 1, weapons));
        }

        return new UnitEntry(block.Name, block.Points, block.Category, enhancements, models);
    }

    // ── ClassifyBulletLine ────────────────────────────────────────────────────

    /// <summary>
    /// Normalises any format line into (Level, IsBullet, Content).
    /// Level 0 = model or single-model item. Level 1 = weapon or continuation.
    /// ◦ is always Level 1 regardless of indent.
    /// </summary>
    private static (int Level, bool IsBullet, string Content) ClassifyBulletLine(string line)
    {
        // ◦ is always Level 1 (iOS weapon marker at any indent depth)
        int subIdx = line.IndexOf(SubBullet);
        if (subIdx >= 0)
        {
            var content = line[(subIdx + 1)..].TrimStart();
            return (1, true, content);
        }

        // Count leading spaces
        int spaces = 0;
        while (spaces < line.Length && line[spaces] == ' ') spaces++;

        if (spaces < line.Length && line[spaces] == Bullet)
        {
            var content = line[(spaces + 1)..].TrimStart();
            // • at 4+ spaces = Android squad-weapon marker (Level 1)
            int level = spaces >= 4 ? 1 : 0;
            return (level, true, content);
        }

        // No bullet — continuation line if sufficiently indented
        if (spaces >= 4 && spaces < line.Length)
        {
            return (1, false, line[spaces..]);
        }

        return (0, false, line.Trim());
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsBulletLine(string line) =>
        line.Contains(Bullet) || line.Contains(SubBullet);

    private static bool IsEnhancement(string content, out string name)
    {
        if (content.StartsWith("Enhancements:", StringComparison.OrdinalIgnoreCase))
        {
            name = content["Enhancements:".Length..].Trim();
            return true;
        }
        if (content.StartsWith("Enhancement:", StringComparison.OrdinalIgnoreCase))
        {
            name = content["Enhancement:".Length..].Trim();
            return true;
        }
        name = "";
        return false;
    }

    private static (string Name, int Points) ParseNamePoints(string line)
    {
        var m = NamePointsRegex.Match(line.Trim());
        if (!m.Success) throw new FormatException($"Expected name+(points) line, got: {line}");
        var pointsStr = m.Groups[2].Value.Replace(",", "");
        return (m.Groups[1].Value.Trim(), int.Parse(pointsStr));
    }

    private static (int Count, string Name) ParseCountedItem(string content)
    {
        var m = CountPrefixRegex.Match(content.Trim());
        return m.Success
            ? (int.Parse(m.Groups[1].Value), m.Groups[2].Value.Trim())
            : (1, content.Trim());
    }

    private static int SkipBlanks(List<string> lines, int i)
    {
        while (i < lines.Count && string.IsNullOrWhiteSpace(lines[i])) i++;
        return i;
    }

    // ── Inner types ───────────────────────────────────────────────────────────

    private sealed class UnitBlock(string name, int points, string category)
    {
        public string Name { get; } = name;
        public int Points { get; } = points;
        public string Category { get; } = category;
        public List<string> Lines { get; } = [];
    }
}
