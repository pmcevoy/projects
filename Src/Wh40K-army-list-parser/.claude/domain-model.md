# Domain Model & Army List Parser

## Core Domain Records

```csharp
record ArmyList(
    string Name,
    int Points,
    string GameSystem,
    string Faction,
    string Detachment,
    IReadOnlyList<UnitEntry> Units
);

record UnitEntry(
    string Name,
    int Points,
    string Category,                   // "CHARACTERS" | "BATTLELINE" | "DEDICATED TRANSPORTS" | "OTHER DATASHEETS"
    IReadOnlyList<string> Enhancements,
    IReadOnlyList<ModelEntry> Models
);

record ModelEntry(
    string Name,
    int Count,
    IReadOnlyList<WeaponEntry> Weapons
);

record WeaponEntry(string Name, int Count);
```

Fixture files: `tests/Fixtures/black-templars-sample.txt` (iOS), `tests/Fixtures/death-guard.txt` (Android).

---

## Army List Export Formats

Three format variants exist in the wild. The parser must handle all three.

### iOS current format
- `  •` (2 spaces + U+2022) = model entry or single-model weapon
- `     ◦` (5 spaces + U+25E6) = weapon belonging to the current model
- Enhancement line: `  ◦ Enhancements: <n>`
- Metadata order: game system / faction / detachment / force-size
- **Key:** `◦` (U+25E6) is always a weapon regardless of indent depth — handle it before indent-based branching in `ClassifyBulletLine`

### iOS legacy format
- `•` (U+2022) at column 0 = model entry
- `◦` (U+25E6) at column 0 = weapon or ability upgrade
- Enhancement line: `◦ Enhancements: <n>`
- Metadata order: game system / faction / detachment / force-size
- May no longer be produced by current app versions; fixture kept for regression

### Android format
- `  •` (2 spaces + bullet) = model in a squad, or first weapon of a single-model unit
- `    •` (4 spaces + bullet) = first weapon belonging to a squad model
- `    <text>` (4+ spaces, no bullet) = continuation weapons for the same model
- Enhancement line: `  • Enhancement: <n>` (singular, bullet line)
- Metadata order: faction / force-size / detachment (detachment appears **after** force-size line)

### Common properties
- Army name and total points on first line: `Iron Canticle (1970 Points)`
- `Points` vs `points` varies by faction — regex must use `RegexOptions.IgnoreCase`
- Sections delimited by ALL-CAPS headings: `CHARACTERS`, `BATTLELINE`, `DEDICATED TRANSPORTS`, `OTHER DATASHEETS`
- Each unit: `Assault Intercessor Squad (75 Points)`
- Count prefixes: `4x` precede model and weapon names
- Unit names may use U+2019 RIGHT SINGLE QUOTATION MARK (`'`) instead of ASCII apostrophe
- Items listed alongside weapons are not always weapons — ability upgrades (e.g. "Shield Dome") also appear as bullets; handle as ability/wargear entries rather than warning loudly
- The force-size line matches the points-header regex and must be consumed during metadata parsing; if faction field is still empty after parsing, fall back to game system field

### Parser implementation notes

`ClassifyBulletLine()` normalises all formats into `(int Level, bool IsBullet, string Content)`:
- Level 0 = model (or single-model unit item); Level 1 = weapon or continuation
- `IsBullet` = true only when `•` or `◦` is present on that line
- `◦` (U+25E6) is **always** Level 1 regardless of indent — check before indent-based `•` branching

**Model mode detection:** a unit is in model mode only when at least one Level-1 item has `IsBullet == true`. Bare continuation lines (Level 1, no bullet) do not trigger model mode.

**Android detachment scan:** after the force-size line break, if `detachment` is still empty, scan forward for the next non-empty, non-points-header line and treat it as the detachment.

---

## UnitProfile Schema

A single `UnitProfile` carries both offensive and defensive data. Both attacker and defender use the same type.

`AbilityProfile.Text` may contain multiple lines when sub-ability profiles are present. Sub-abilities are appended as `"• SubName: effect text"` lines, `\n`-separated from the parent intro text and from each other. This is a display-only concern; `AbilityProfile` requires no structural change.

```yaml
name: "Crusader Squad"
faction: "Black Templars"
modelCount: 20
keywords: [INFANTRY, CORE, ADEPTUS ASTARTES]
abilities:
  - name: "Righteous Zeal"
    text: "..."
enhancements: []

# Offensive stats
rerolls:
  hitRerollOnes: false
  hitRerollAll: false
  woundRerollOnes: false
  woundRerollAll: false
criticalHitsOn: 6
models:
  - modelName: "Sword Brother"
    count: 1
    weapons:
      - weaponName: "Hellforged weapons"
        type: Melee
        range: 0
        profiles:
          - variant: strike
            attacks: 4
            skill: 3
            strength: 8
            ap: -2               # Negative integer matching game value
            damage: 2
            abilities:
              torrent: false
              blast: false
              melta: 0           # 0 = not present; int = bonus damage within half range
              rapidFire: 0       # 0 = not present; int = bonus attacks at half range
              sustainedHits: 0   # 0 = not present; int = bonus hits on Critical Hit
              lethalHits: false
              devastatingWounds: false
              twinLinked: false
              anti: {}           # keyword -> criticalWoundThreshold map

# Defensive stats
toughness: 4
save: 3                          # Raw integer; implies 3+
invulnerableSave: null           # null if absent; int if present (e.g. 4 = 4++)
wounds: 2
feelNoPain: null                 # null if absent; int if present (e.g. 5 = 5+++)
```

### Schema conventions (do not change without updating both sides)

- `ap` is a **negative integer** matching game value (AP-2 → `-2`). This convention applies throughout — `SimulationAdapter` passes it unchanged into `SimWeaponProfile`.
- `skill`, `save`, `invulnerableSave`, `feelNoPain`, `criticalHitsOn` are raw integers (e.g. `3` means hits on 3+); the `+` suffix is implied.
- `weapons` is always a list, even for single-weapon models.
- `rapidFire` and `melta` use `0` as the sentinel for "not present" — treat `0` as absent, not as a valid ability value.
- `range` is stored in inches as a plain integer; melee weapons use `range: 0`.
- `anti` is a map of `keyword -> criticalWoundThreshold`.
- `rerolls` live at the unit level, not per-weapon (models army/detachment auras; per-weapon rerolls are out of scope for v1).
- `withinHalfRange` is a simulation parameter, not a weapon property.

---

## Name Matching Strategy

All string comparisons use `StringComparison.OrdinalIgnoreCase`.

Resolution order:
1. **Manual override** — `name_overrides.json` in the working directory at startup; maps display name → BSData entry name; takes precedence over all automatic matching
2. **Exact match** — after trimming whitespace
3. **Count-stripped match** — strip leading `\d+x\s+` prefix, then exact match
4. **Fuzzy match** — `FuzzySharp.Fuzz.TokenSortRatio(a, b)` with threshold 85; log at `Information` (≥ 90) or `Warning` (< 90) with input name, matched name, and score
5. **Prefix match** (model resolution only) — match any candidate whose name starts with the display name followed by a non-alphanumeric character (handles loadout variants like `"Initiate w/Bolt Rifle"` for army list name `"Initiate"`)

**Scopes:**
- Unit names: search across all loaded catalogues; include `type='unit'` and `type='model'` entries; search recursively
- Model names: within matched unit's child entries first, then fall back to global `type='model'` entries
- Weapon names: within model/unit scope first, then globally; if profile-name search fails, find entry by name and return all its weapon profiles as variants
- Non-weapon entries (ability upgrades): if an army list item resolves to an entry with no weapon profiles, treat it silently — check both entry name and any ability profile name within that entry

---

## Testing

- **Parser unit tests:** `ArmyListParserTests.cs` (iOS / Black Templars) and `ArmyListParserAndroidTests.cs` (Android / Death Guard) — assert section categorisation, model counts, weapon names, enhancements
- **Catalogue parser unit tests:** XML fixture snippets in `tests/Fixtures/`; mock `ICatalogueFetcher` with Moq; no live network calls
- **Resolver unit tests:** exact, count-stripped, fuzzy at threshold boundary, override file, not-found
- **Integration test:** full enrichment pipeline against Black Templars export; assert `Assault Intercessor` has `T=4, Sv=3+, W=2`; assert `Astartes chainsword` has `AP=-1`
- **Snapshot tests:** serialise enriched army to YAML; compare against committed `.yaml` fixture; fail build on schema drift
