# CLAUDE.md — wh40k-army-enricher

## Documentation Maintenance

**After every implementation change — feature, bug fix, or design decision — update this file to keep it in sync.** Document the behaviour, gotchas, and design decisions introduced by the change. Do not let CLAUDE.md drift from the actual implementation.

## Project Overview

A .NET solution consisting of:

1. **Web application** (`Wh40kArmyEnricher.Web`) — live-game tool; paste two army lists, view enriched unit cards side-by-side, select an attacker weapon and a defender unit, configure combat options, and run a Monte Carlo simulation on the server — results appear inline without page reload
2. **Simulation engine** (in `Wh40kArmyEnricher.Core/Simulation/`) — ported from the now-retired `wh40k-sim` project; full 4-step 40K attack sequence (hit → wound → save → damage) with all weapon abilities

Sample input data lives in `./data` folder

---

## Language & Target Framework

- **Language:** C# 12
- **Target framework:** `net8.0`
- **Nullable reference types:** enabled on all projects
- **Implicit usings:** enabled

---

## NuGet Dependencies

### `Wh40kArmyEnricher.Core`
- `FuzzySharp` — fuzzy name matching (token-sort ratio) for resolving display names to BSData entries
- No third-party XML library needed — use `System.Xml.Linq` (LINQ to XML / `XDocument`) which handles the namespace-qualified BSData schema cleanly

### `Wh40kArmyEnricher.Tests`
- `xunit` + `xunit.runner.visualstudio`
- `FluentAssertions`
- `Moq` (for mocking `HttpClient` / `ICatalogueFetcher` in unit tests)

### HTTP / Caching
Use `IHttpClientFactory` with a named client. Cache downloaded `.cat` files to disk under a configurable path (default `~/.wh40k-enricher/cache/`). On each run, use the cached file if it exists; only re-download when `--refresh-cache` is passed. **Do not** use the GitHub Commits API for staleness checking — it is aggressively rate-limited even for unauthenticated reads.

The GitHub Contents API listing (`GET https://api.github.com/repos/BSData/wh40k-10e/contents/`) is also rate-limited; cache the resulting filename list to `~/.wh40k-enricher/cache/catalogue-list.json` and only re-fetch with `--refresh-cache`.

---

## Data Sources

### 1. Army List Text Export (Input)

The Warhammer app exports a structured plain-text format. Three format variants exist:

#### iOS current format (clipboard export — confirmed via diagnostic)
- `  •` (2 spaces + U+2022) = model entry in a squad, or single-model weapon
- `     ◦` (5 spaces + U+25E6) = weapon belonging to the current model
- Enhancement line: `  ◦ Enhancements: <name>`
- Metadata order: game system / faction / detachment / force-size (same as iOS legacy)
- **Key implementation note:** `◦` (U+25E6) is always a weapon regardless of indent depth — handle it before the indent-based branching in `ClassifyBulletLine`.

#### iOS legacy format (reference: `black-templars-sample.txt`)
- `•` (U+2022) at column 0 = model entry in a squad
- `◦` (U+25E6) at column 0 = weapon or ability upgrade belonging to the current model
- Enhancement line: `◦ Enhancements: <name>`
- Metadata order: game system / faction / detachment / force-size
- Note: this format may no longer be produced by current app versions but the fixture is kept for regression coverage.

#### Android format (reference: `death-guard.txt`)
- `  •` (2 spaces + bullet) = model in a squad, **or** first weapon of a single-model unit
- `    •` (4 spaces + bullet) = first weapon belonging to a squad model
- `    <text>` (4+ spaces, no bullet) = continuation weapons (additional weapons for the same model)
- Enhancement line: `  • Enhancement: <name>` (singular, on a bullet line)
- Metadata order: faction / force-size / detachment (detachment appears **after** the force-size line)

#### Common properties (both formats)
- Army name and total points on the first line: `Iron Canticle (1970 Points)`
- Faction metadata block before the first section heading. The number of metadata lines varies by faction:
  - Sub-factions (e.g. Black Templars): 3 lines — game system / faction / detachment type, preceded by a force-size line (`Incursion (1000 Points)`)
  - Standalone factions (e.g. Death Guard): 1 line — faction name only
  - **The force-size line matches the points-header regex** (`\d+ Points`) and must be consumed during metadata parsing, not treated as a unit header. If the faction field is still empty after parsing, fall back to the game system field.
- Points values in the header use `Points` (capital P) for some factions and `points` (lower case) for others — the regex must use `RegexOptions.IgnoreCase`
- Sections delimited by ALL-CAPS category headings: `CHARACTERS`, `BATTLELINE`, `DEDICATED TRANSPORTS`, `OTHER DATASHEETS`
- Each unit begins with its name and points cost: `Assault Intercessor Squad (75 Points)`
- **Items listed alongside weapons are not always weapons** — ability upgrades such as "Shield Dome" also appear as bullets. These will fail weapon resolution; handle them as ability/wargear entries rather than warning loudly
- Count prefixes like `4x` precede model and weapon names
- Unit names may use U+2019 RIGHT SINGLE QUOTATION MARK (`'`) rather than ASCII apostrophe (`'`) — e.g. "Emperor's Champion"

#### Parser implementation notes
- `ClassifyBulletLine()` normalises all formats into a `(int Level, bool IsBullet, string Content)` tuple
  - Level 0 = model (or single-model unit item); Level 1 = weapon or continuation
  - `IsBullet` is `true` only when a bullet character (`•` or `◦`) is present on that line (not for bare continuation lines)
  - `◦` (U+25E6) is **always** Level 1 regardless of indent — check for it before the indent-based `•` branching
- **Model mode detection:** a unit is in model mode (has distinct sub-models) only when at least one Level-1 item has `IsBullet == true`. Bare continuation lines (Level 1, no bullet) do not trigger model mode.
- **Android detachment scan:** after the force-size line break, if `detachment` is still empty, scan forward for the next non-empty, non-points-header line and treat it as the detachment.

Parsed domain model — use C# `record` types:

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
    string Category,                   // "CHARACTERS" | "BATTLELINE" | etc.
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

See `tests/Fixtures/black-templars-sample.txt` for the iOS reference export and `tests/Fixtures/death-guard.txt` for the Android reference export.

### 2. BattleScribe Data Files (`.cat` XML)

Source: `https://github.com/BSData/wh40k-10e`

**Fetching strategy — download everything:**
Rather than attempting to map faction names to filenames (which is fragile — catalogue link names frequently differ from actual filenames, e.g. a link named `"Chaos - Daemons Library"` maps to the file `Chaos - Chaos Daemons Library.cat`), the `CatalogueStore` downloads and parses **every `.cat` file** in the repository on first run:

1. Load `Warhammer 40,000.gst` (game system root)
2. Fetch the full file listing via `GET https://api.github.com/repos/BSData/wh40k-10e/contents/` — cache to `catalogue-list.json`
3. Download and parse each `.cat` file — cache each to `~/.wh40k-enricher/cache/{filename}`
4. All catalogues remain in memory for the lifetime of the run; no lazy loading

The ~46 catalogue files total ~35 MB. After the first run everything is cached on disk and subsequent runs read only from disk.

**File format:** UTF-8 XML using namespace `http://www.battlescribe.net/schema/catalogueSchema`.
The `.catz` variant is zlib-compressed (raw deflate, no header); decompress with `DeflateStream` before parsing:

```csharp
using var deflate = new DeflateStream(rawStream, CompressionMode.Decompress);
var doc = await XDocument.LoadAsync(deflate, LoadOptions.None, ct);
```

**XML namespace — declare once and reuse:**

```csharp
private static readonly XNamespace Ns =
    "http://www.battlescribe.net/schema/catalogueSchema";
```

**Key XML elements to extract:**

BSData 10e uses a more complex nesting than earlier editions. The important containers at the catalogue root level are:

- `<selectionEntries>` — force/army roster entries (rarely contain unit datasheets directly)
- `<sharedSelectionEntries>` — unit and model datasheets; **must be included in top-level entry search**
- `<sharedSelectionEntryGroups>` — wargear option groups; **must also be parsed** — entries within these groups include weapon and upgrade profiles

Within each `selectionEntry`, child entries live in:
- `<selectionEntries>` — direct child model/upgrade entries
- `<selectionEntryGroups>` — wargear option groups (e.g. weapon choices, equipment like Shield Dome); **must be traversed recursively** — groups can themselves contain nested `<selectionEntryGroups>` (double nesting observed in practice, e.g. Repulsor Executioner → Wargear → Turret Weapon → Heavy Laser Destroyer)
- `<entryLinks>` — references to `<sharedSelectionEntries>` by `targetId`; also appear at root catalogue level as faction-specific entry overrides

Parse `<selectionEntryGroups>` recursively (not just one level deep) to reach all nested entries. Stop at depth 6 to prevent pathological recursion.

```xml
<!-- Squad unit — statline is on child model entries, NOT the squad entry itself -->
<selectionEntry id="abc-123" name="Assault Intercessor Squad" type="unit">
  <profiles>
    <!-- Only ability profiles here, no Unit statline -->
    <profile name="Shock Assault" typeName="Abilities"> ... </profile>
  </profiles>
  <selectionEntryGroups>
    <selectionEntryGroup name="Assault Intercessors">
      <selectionEntries>
        <!-- The Unit statline lives on each model entry -->
        <selectionEntry name="Assault Intercessor" type="model">
          <profiles>
            <profile name="Assault Intercessor" typeName="Unit">
              <characteristics>
                <characteristic name="M">3"</characteristic>
                <characteristic name="T">4</characteristic>
                <characteristic name="Sv">3+</characteristic>
                <characteristic name="W">2</characteristic>
                <characteristic name="Ld">6+</characteristic>
                <characteristic name="OC">2</characteristic>
              </characteristics>
            </profile>
            <profile name="Astartes chainsword" typeName="Melee Weapons"> ... </profile>
          </profiles>
        </selectionEntry>
      </selectionEntries>
    </selectionEntryGroup>
    <selectionEntryGroup name="Wargear">
      <selectionEntries>
        <!-- Ability-only upgrades also appear here -->
        <selectionEntry name="Shield Dome" type="upgrade">
          <profiles>
            <profile name="Shield Dome" typeName="Abilities">
              <characteristics>
                <characteristic name="Description">The bearer has a 5+ invulnerable save.</characteristic>
              </characteristics>
            </profile>
          </profiles>
        </selectionEntry>
      </selectionEntries>
    </selectionEntryGroup>
  </selectionEntryGroups>
</selectionEntry>

<!-- Single-model datasheet — statline IS on the entry, type="model" not type="unit" -->
<selectionEntry id="def-456" name="Foetid Bloat-drone with heavy blight launcher" type="model">
  <profiles>
    <profile name="Foetid Bloat-drone with heavy blight launcher" typeName="Unit">
      <characteristics> ... </characteristics>
    </profile>
    <profile name="Heavy blight launcher" typeName="Ranged Weapons"> ... </profile>
  </profiles>
</selectionEntry>

<!-- Multi-profile weapon — profile names include a variant suffix -->
<selectionEntry name="Hellforged weapons" type="upgrade">
  <profiles>
    <profile name="➤ Hellforged weapons - strike" typeName="Melee Weapons"> ... </profile>
    <profile name="➤ Hellforged weapons - sweep" typeName="Melee Weapons"> ... </profile>
  </profiles>
</selectionEntry>
```

**Profile `typeName` values in 10th edition BSData:**
- `"Unit"` — model statline: M, T, Sv, W, Ld, OC
- `"Ranged Weapons"` — Range, A, BS, S, AP, D, Keywords
- `"Melee Weapons"` — Range (always "Melee"), A, WS, S, AP, D, Keywords
- `"Abilities"` — free-text special rules; capture name + text for reference

---

## Name Matching Strategy

Army list display names are generally identical to BSData `name` attributes but edge cases exist (pluralisation, punctuation differences, case differences, etc.). All string comparisons use `StringComparison.OrdinalIgnoreCase`.

Resolution order:
1. **Manual override** — load `name_overrides.json` from the working directory at startup; maps `"display name" -> "BSData selectionEntry name"` and takes precedence over all automatic matching
2. **Exact match** — `string.Equals(a, b, StringComparison.OrdinalIgnoreCase)` after trimming whitespace
3. **Count-stripped match** — strip leading `\d+x\s+` prefix with a regex, then exact match
4. **Fuzzy match** — use `FuzzySharp.Fuzz.TokenSortRatio(a, b)` with a threshold of 85. Log fuzzy matches at `Information` level if score ≥ 90, `Warning` level if score < 90: include input name, matched BSData name, and score
5. **Prefix match** (model resolution only) — BSData names loadout variants with a suffix, e.g. `"Initiate w/Bolt Rifle"`, `"Initiate w/Chainsword & Heavy Bolt Pistol"`. The army list uses only the base name `"Initiate"`. Match any candidate whose name starts with the display name followed by a non-alphanumeric character. Logged at `Debug` level. This is applied to both local (within-unit) and global model candidates, after the four steps above.

**Unit name matching scope:**
- Search across ALL loaded catalogues (no need to specify a faction catalogue — everything is pre-loaded)
- Include all `selectionEntry[@type='unit']` entries regardless of whether they have a direct statline (squad entries carry their statline on child model entries, not the squad entry itself)
- Also include `selectionEntry[@type='model']` entries that have their own statline (these are single-model datasheets — vehicles, characters, drones)
- Search recursively through all child entries, not just top-level entries

**Model name matching scope:**
- Search within the matched unit's child entries first, then fall back to global `type="model"` entries

**Weapon name matching scope:**
- Search weapon profile names within model/unit scope first, then globally
- **Fallback: search by entry name.** Some weapons have profiles named differently from the entry (e.g., "Hellforged weapons" entry has profiles named "➤ Hellforged weapons - strike" / "- sweep"). If profile-name search fails, find an entry whose name matches and return all its weapon profiles as variants
- Non-weapon entries (ability upgrades like "Shield Dome") will appear in the army list alongside weapons. If an army list item resolves to an entry with no weapon profiles, treat it silently as a non-weapon entry rather than warning. The ability-only check must match **either** the catalogue entry name **or** any ability profile name within that entry (e.g. entry `"Icon of Despair"` contains a profile named `"Icon of Despair (Aura)"` — the army list may reference either name)

---

## UnitProfile Schema

### Design Decisions (read before touching the schema)

- **`ap` is stored as a negative integer** matching the actual game value (e.g. AP -2 → `ap: -2`). The simulation must not negate it again.
- **`skill` is stored as a raw integer** (e.g. `3` means "hits on 3+"). The `+` suffix is implied; do not store it as a string. Same convention applies to `save`, `invulnerableSave`, `feelNoPain`, and `criticalHitsOn`.
- **`weapons` is always a list**, even for single-weapon models. Each weapon entry contains a `profiles` list to handle multi-mode weapons (e.g. plasma standard vs supercharge).
- **Multi-profile weapons** use a `variant` label derived from the profile name. BSData often prefixes variant profiles with `➤ ` followed by the weapon name and ` - variant`. Strip the `➤ ` and weapon name prefix to get the variant label (e.g. `"➤ Hellforged weapons - strike"` → `"strike"`).
- **`rerolls` live at the attacker unit level**, not per-weapon. This models army-wide or detachment auras. Per-weapon re-roll distinctions (e.g. from enhancements) are out of scope for v1 — note this as a known limitation.
- **`withinHalfRange`** is a simulation parameter, not a weapon property. It is defined at the top level of each pairing's simulation context, not inside the weapon block.
- **`rapidFire` and `melta`** store the bonus value as an integer. `0` means the keyword is not present. The simulation should treat `rapidFire: 0` as "not Rapid Fire" — i.e. 0 is the sentinel, not a valid ability value.
- **`range`** is stored in inches as a plain integer (`range: 12`). `"Melee"` weapons use `range: 0`. The simulation uses this to gate whether a ranged weapon can fire at all given the engagement scenario.
- **`anti`** is a map of `keyword -> criticalWoundThreshold`. If a target unit has any of the listed keywords, the attacker scores a Critical Wound on a roll of that value or higher (in addition to the normal Critical Wound rules).
- **`keywords` on the attacker** captures unit-level keywords (e.g. `INFANTRY`, `MOUNTED`, `CHARACTER`) that may interact with terrain, abilities, or opponent weapon keywords. These are sourced from the BSData `<categoryLink>` entries on the unit's `selectionEntry`.
- **Invulnerable saves and FNP from upgrades.** These can come from two sources: (1) ability text / infoLinks on the unit/model entry (see Key Behaviours → Invulnerable saves and FNP for the storage patterns and regexes); (2) selected upgrade entries (e.g. Shield Dome) listed in the army export — scan army-list entries that resolve to ability-only catalogue entries and apply any invuln/FNP from their `EntryInvulnerableSave` / `EntryFeelNoPain` (already parsed during catalogue load) or from scanning their ability text.

---

### Unit Profile (full schema)

A single `UnitProfile` record carries both offensive and defensive data. Both the attacker and defender use the same type; the web app's `/api/simulate` endpoint reads both from session and passes them to `SimulationAdapter`.

```yaml
name: "Crusader Squad"           # Unit display name from army list
faction: "Black Templars"
modelCount: 20                   # Total models in the unit
keywords:                        # Unit-level keywords from BSData categoryLinks
  - INFANTRY
  - CORE
  - ADEPTUS ASTARTES
abilities:                       # Unit special rules from BSData; not yet consumed by sim
  - name: "Righteous Zeal"
    text: "..."
enhancements: []                 # Enhancement names from army list

# --- Offensive stats ---
rerolls:                         # Army/detachment-level re-roll auras; set per simulation run
  hitRerollOnes: false
  hitRerollAll: false
  woundRerollOnes: false
  woundRerollAll: false
criticalHitsOn: 6                # Normally 6; some abilities lower this
models:                          # One entry per distinct model type in the unit
  - modelName: "Sword Brother"
    count: 1
    weapons:
      - weaponName: "Hellforged weapons"
        type: Melee
        range: 0
        profiles:
          - variant: strike      # Derived from "➤ Hellforged weapons - strike"
            attacks: 4
            skill: 3
            strength: 8
            ap: -2               # Negative integer matching game value
            damage: 2
            abilities:
              torrent: false
              blast: false
              melta: 0           # 0 = not present; integer = bonus damage within half range
              rapidFire: 0       # 0 = not present; integer = bonus attacks at half range
              sustainedHits: 0   # 0 = not present; integer = bonus hits on Critical Hit
              lethalHits: false
              devastatingWounds: false
              twinLinked: false
              anti: {}           # keyword -> criticalWoundThreshold map
          - variant: sweep       # Derived from "➤ Hellforged weapons - sweep"
            attacks: 8
            skill: 3
            strength: 6
            ap: -1
            damage: 1
            abilities:
              torrent: false
              blast: false
              melta: 0
              rapidFire: 0
              sustainedHits: 0
              lethalHits: false
              devastatingWounds: false
              twinLinked: false
              anti: {}
      - weaponName: "Pyre pistol"
        type: Ranged
        range: 12
        profiles:
          - variant: default
            attacks: "D6"        # Variable attacks stored as string when not a fixed integer
            skill: 3
            strength: 4
            ap: 0
            damage: 1
            abilities:
              torrent: true      # Torrent: auto-hits, skip hit roll entirely
              blast: false
              melta: 0
              rapidFire: 0
              sustainedHits: 0
              lethalHits: false
              devastatingWounds: false
              twinLinked: false
              anti: {}

# --- Defensive stats ---
toughness: 4
save: 3                          # Raw integer; implies 3+
invulnerableSave: null           # null if absent; integer if present (e.g. 4 = 4++)
wounds: 2                        # Wounds per model
feelNoPain: null                 # null if absent; integer if present (e.g. 5 = 5+++)
```

---

## Key Behaviours & Rules

- **Never hard-code statlines.** All stat values must originate from BSData XML. If a unit cannot be resolved after fuzzy matching, emit a structured warning to stderr and skip that unit — do not guess or substitute default values.
- **Warn on fuzzy matches.** Log input name, matched BSData name, and similarity score. Use `Warning` level for scores 85–89 (needs human review); use `Information` level for scores ≥ 90 (near-exact, likely correct). Consider writing a `resolution_report.json` alongside the main output that lists every match decision for review.
- **Invulnerable saves and FNP.** BSData 10e does **not** use `4++`/`5+++` game shorthand in ability text. Two storage patterns exist:
  1. **Ability text** — `"N+ invulnerable save"` or `"N++ invulnerable save"` (regex `(\d)\+\+? invulnerable`, handles both forms) and `"Feel No Pain N+"` (regex `Feel No Pain (\d)\+`). The exact wording varies — e.g. Shield Dome reads `"The bearer has a 5+ invulnerable save."` Use the regex, do not match literal strings.
  2. **infoLinks** — `<infoLink name="Invulnerable Save" type="profile">` pointing to a shared profile whose Description is just `"4+"`, and `<infoLink name="Feel No Pain" type="rule">` with `<modifier type="append" value="5+" field="name"/>` encoding the threshold. These infoLinks can be **cross-catalogue** — e.g. a Black Templars unit's infoLink may target a shared profile defined in `Imperium - Space Marines.cat`. `CatalogueStore.InitialiseAsync` therefore does a **two-pass load**: pass 1 loads all XDocuments and merges their `sharedProfiles` into a global `Dictionary<string, XElement>`; pass 2 parses each document using that global map as a fallback so cross-catalogue profileLink and infoLink targets resolve correctly.
  Both patterns are extracted by `CatalogueParser.ExtractInvulnFnp()` and stored on `CatalogueEntry.EntryInvulnerableSave` / `EntryFeelNoPain` regardless of whether a statline is present. The `Enricher` applies unit-level values to child model statlines (squad-type unit entries have null statlines). Set to `null` when absent.
- **Single-model unit ability upgrades.** For single-model units (type `"model"`, e.g. Impulsor), `unitEntry.Statline` is non-null, so `defenderStatline` is pre-initialised before the model loop. A null-check guard (`if defenderStatline == null`) would skip the update entirely, losing ability upgrades (e.g. Shield Dome → 5+ invuln) applied inside the loop by `ApplySelectedAbilityUpgrades`. Use a `defenderStatlineSet` boolean flag instead so the first model's fully-enriched statline always overwrites `defenderStatline`.
- **Multi-wound models.** Capture `W` per model type (`selectionEntry[@type='model']`), not per unit — essential for simulation accuracy when a unit contains models with different wound counts.
- **Weapons with multiple profiles.** Some weapons have multiple `<profile>` children (e.g. plasma supercharge, Hellforged weapons strike/sweep). Capture all variants in the `profiles` array with a `variant` label derived from the profile `name` attribute. Strip BSData's `➤ ` prefix and the weapon entry name from profile names to get a clean variant label.
- **Non-weapon entries.** The army export uses bullet characters for both weapons and ability upgrades (e.g. Shield Dome). When an entry cannot be resolved as a weapon, check whether it resolves to a catalogue entry with no weapon profiles. If so, apply any invuln/FNP it grants to the model statline and skip it silently — do not emit a warning. The check must match either the entry name or any ability profile name within that entry (e.g. `"Icon of Despair"` entry contains profile `"Icon of Despair (Aura)"`).
- **Keywords.** Parse the `Keywords` characteristic as a comma-separated list, trimming whitespace and normalising `-` (no keywords) to an empty list. Keywords such as `Blast`, `Torrent`, `Pistol`, `Indirect Fire`, `Lethal Hits`, `Sustained Hits X`, `Devastating Wounds` directly affect simulation logic.
- **Unit abilities.** Capture all `profile[@typeName='Abilities']` entries by name and text even if the simulation does not yet consume them — they will be needed for future rule modelling.

---

## Testing

- **Parser unit tests:** exercise `ArmyListParser` against both fixture files:
  - `ArmyListParserTests.cs` — iOS format (Black Templars); assert section categorisation, model counts, weapon names, enhancements, total model counts per unit
  - `ArmyListParserAndroidTests.cs` — Android format (Death Guard); same assertions plus Android-specific metadata order and enhancement format
- **Catalogue parser unit tests:** use saved XML fixture snippets checked into `tests/Fixtures/`; do not make live network calls in unit tests; mock `ICatalogueFetcher` with Moq
- **Resolver unit tests:** test exact match, count-stripped match, fuzzy match at threshold boundary, override file resolution, and not-found behaviour
- **Integration test:** run the full enrichment pipeline against the sample Black Templars export with a live (or WireMock-recorded) BSData fetch; assert `Assault Intercessor` has `T=4`, `Sv=3+`, `W=2`; assert `Astartes chainsword` has `AP=-1`
- **Snapshot tests:** serialise a known enriched army to YAML and compare against a committed expected `.yaml` fixture file; fail the build on schema drift

---

## Development Notes

- Use `XDocument` / LINQ to XML throughout — it handles the BSData XML namespace cleanly with the `Ns + "elementName"` pattern and is more readable than `XmlDocument` for the nested query patterns needed here
- Declare the BSData XML namespace constant once in `CatalogueParser.cs` and reference it everywhere; do not scatter the namespace string literal
- `CatalogueStore` eagerly loads all catalogues on startup — no lazy loading or `catalogueLink` traversal needed. The `LoadCatalogueAsync(filename)` method is retained for API compatibility but is a no-op after initialisation
- GitHub raw URL pattern: `https://raw.githubusercontent.com/BSData/wh40k-10e/main/{Uri.EscapeDataString(filename)}` — note spaces in filenames like `Imperium - Black Templars.cat` must be encoded as `%20`
- `.catz` files are raw deflate compressed (no zlib header). Use `new DeflateStream(stream, CompressionMode.Decompress)` — do **not** use `ZLibStream` or `GZipStream`
- Register `HttpClient` via `IHttpClientFactory` in DI; set a `User-Agent` header identifying this tool — the GitHub API rejects requests without one
- Static classes cannot be used as type parameters for `ILogger<T>` — use `ILoggerFactory.CreateLogger("Name")` for loggers inside static classes
- All `typeName` comparisons (e.g. `"Ranged Weapons"`, `"Melee Weapons"`, `"Unit"`) **must use `StringComparison.OrdinalIgnoreCase`** — case variation has been observed in the wild and silently drops profiles if compared with `==`
- `name_overrides.json` must be present in the **current working directory** when the web app is started. Example entry: `{ "Deathshroud Champion": "Deathshroud Terminator Champion" }`. The file is optional; if absent, resolution proceeds without overrides.

---

## Web Application (`Wh40kArmyEnricher.Web`)

### Purpose

A live-game tool designed for use on a phone or tablet at the table. The user pastes two army exports, the server enriches them, and the resulting page lets them click through weapons and run instant Monte Carlo simulations to get expected damage / expected kills.

### Running locally (Docker)

```bash
docker compose up --build        # first run: downloads ~35 MB BSData cache
docker compose up                # subsequent runs: cache is on the volume, starts fast
# browse to http://localhost:8080
```

`appsettings.json` key: `Enricher:CachePath` — set to `/root/.wh40k-enricher/cache` in the container (mounted volume). Override in `appsettings.Development.json` to a local Windows path for non-Docker dev.

### User flow

1. **Index page** — paste attacker and defender army list text, submit
2. Server enriches both lists (BSData catalogue lookup) and stores `List<UnitProfile>` in ASP.NET Core session
3. **ArmyView page** — two columns of collapsed unit cards; click a card header to expand it
4. In an expanded attacker card, **click a weapon variant row** to select it (highlights red; auto-selects the unit)
5. Click a defender unit card to select it (highlights blue)
6. The **combat panel** appears at the bottom; configure options and click **Run Simulation**
7. Results display inline: mean damage, expected kills, P(kill ≥ 1 model), std deviation

### Session storage and JSON

Enriched armies are stored in session as JSON using `SessionJson.Options` (`Helpers/SessionJson.cs`). Two non-obvious requirements:
- **`ScalarValueJsonConverter`** is mandatory — `ScalarValue` has only private backing fields and serialises to `{}` by default; without the converter every `Attacks`/`Damage` value comes back as empty string and `DiceExpression.Parse` throws.
- **`PropertyNameCaseInsensitive = true`** — session data uses PascalCase (no naming policy); case-insensitive matching ensures round-trips work correctly. Do NOT set `PropertyNamingPolicy = CamelCase` on `SessionJson.Options` — that causes `WeaponAbilities` booleans (`LethalHits`, `DevastatingWounds`, etc.) to deserialise as `false` because the default STJ matcher is case-sensitive.
- The `data-unit` HTML attribute in `ArmyView.cshtml` uses a **separate** `camelCaseJson` variable so the JavaScript receives camelCase property names — this is independent of session serialisation.

### Combat options (user-controlled)

All simulation modifiers are set explicitly by the user — ability text is not auto-parsed. The combat panel is organised into five collapsible modifier sections plus a top-level "Models firing" control.

**Models firing** — pre-filled from the weapon's model count; can be overridden (e.g. only 3 of 5 models in range). Hidden when multiple weapons are selected.

Each accordion section header shows a live one-line summary of its active modifiers (e.g. `½ Range · RR All · Crit 5+`) so the user can see at a glance what is set without expanding the section.

---

#### Attack Modifiers

| Control | Effect |
|---|---|
| **+1/-1 Attack** | Adds or subtracts 1 from the total attack count for each selected weapon group, applied after Blast and Rapid Fire adjustments. |
| **Blast** | Override toggle — enables the Blast keyword on selected weapons that do not already have it. Weapons that already have Blast are unaffected. Blast adds 1 attack per 5 defender models (rounded down). |
| **Reroll attack dice** | For variable-attack weapons (e.g. D6 attacks), reroll the attack-count dice once per model group if the result is below the expected average (average = sides/2 rounded down + 0.5 — i.e. reroll 1–3 on D6, reroll 1 on D3). Applied independently per model contribution before aggregation. |

---

#### Hit Modifiers

| Control | Effect |
|---|---|
| **Within half range** | Enables Rapid Fire and Melta bonuses for weapons that have them. Lives in this section because it gates weapon abilities that affect the hit-through-damage pipeline. |
| **+1/-1 Hit** | Roll modifier — added to the raw dice result after rolling, capped at a net total of +1/-1 (bonuses cancel penalties; cannot exceed ±1). Natural 1 still always fails; natural 6 always hits. |
| **+1/-1 BS/WS** | Characteristic modifier — changes the effective BS/WS target number by ±1 step (e.g. BS 4+ → 3+). Tracked separately from the roll modifier; the two stack independently (e.g. +1 Hit roll AND -1 BS can combine to the equivalent of a +2 shift, unlike two roll modifiers which cap at +1). |
| **Reroll 1s** | Reroll hit rolls of 1 once. Mutually exclusive with Reroll All. |
| **Reroll All** | Reroll all failed hit rolls once. Mutually exclusive with Reroll 1s. |
| **Fish for Criticals** | Sub-option of Reroll All only. Instead of rerolling failures, reroll any result below `criticalHitsOn` (i.e. reroll successful non-critical hits as well, accepting only critical results). Only available when Reroll All is selected. |
| **Indirect Fire** | Weapon hits on a flat 4+ regardless of BS. The -1 to hit from Indirect Fire is already baked in (no further modifier needed). Torrent overrides this if both are active. |
| **Crit Hit on 5+** | Lowers `CriticalHitsOn` to 5, so natural 5 or 6 counts as a Critical Hit. |
| **Sustained Hits 1** | Override toggle — grants Sustained Hits 1 to selected weapons that do not already have it. Weapons with Sustained Hits already are unaffected. |
| **Lethal Hits** | Override toggle — grants Lethal Hits to selected weapons that do not already have it. Typically applied via a stratagem aura. |

---

#### Wound Modifiers

| Control | Effect |
|---|---|
| **+1/-1 Wound** | Roll modifier — added to the raw wound dice result, capped at net ±1. Natural 1 still always fails; natural 6 always wounds. Critical wound threshold checks use the raw unmodified die. |
| **+1/-1 Strength** | Adjusts the attacker's effective Strength by ±1 for the wound table lookup (`S vs T`). |
| **+1/-1 Toughness** | Adjusts the defender's effective Toughness by ±1 for the wound table lookup. |
| **Reroll 1s** | Reroll wound rolls of 1 once. |
| **Reroll All** | Reroll all failed wound rolls once (also applies to Twin-Linked weapons). |
| **Fish for Criticals** | Sub-option of Wound Reroll All only. Reroll any wound result below `criticalWoundsOn`, accepting only critical wounds. |
| **Crit Wound on 5+** | Lowers the critical wound threshold to 5, so unmodified 5 or 6 scores a Critical Wound. Independent of Anti thresholds (both are tracked; the lower value wins). |
| **Devastating Wounds** | Override toggle — grants Devastating Wounds to selected weapons that do not already have it. |
| **Anti-X** | Override — applies an Anti keyword to all selected weapons. User configures: keyword type (one of `Infantry`, `Monster`, `Vehicle`, `Fly`, `Psyker`, `Character`, `Daemon`) and threshold (e.g. 4+). The Anti threshold combines with any existing Anti on the weapon; the lower threshold wins. Applied to the defender's keywords at simulation time. |

---

#### Save Modifiers

| Control | Effect |
|---|---|
| **Cover** | Adds +1 to the defender's armour save before simulation (e.g. Sv 3+ → 2+). Does not affect invulnerable saves. |
| **Ignores Cover** | Negates the Cover bonus if both are active (net zero effect on save). |
| **+1/-1 AP** | Adjusts the weapon's AP by ±1 (e.g. AP-1 → AP-2 with +1, or AP-2 → AP-1 with -1). Applied to the sim-side `Ap` value (stored as positive int in `SimWeaponProfile`). |

---

#### Damage Modifiers

| Control | Effect |
|---|---|
| **+1/-1 Damage** | Adds or subtracts 1 from each individual damage roll after rolling (applied per wound, before FNP). Damage is clamped to a minimum of 1 — a failed save always deals at least 1 damage. |
| **Reroll damage dice** | For variable-damage weapons (e.g. D3 damage, D6 damage), reroll the damage dice once if the result is below the expected average (reroll 1–3 on D6, reroll 1 on D3). Applied per wound resolution. |
| **Feel No Pain** | Override — grants the defender a FNP save (4+++, 5+++, or 6+++) if they do not already have one. Defenders with a native FNP are unaffected. Lives in this section because FNP rolls are made after the damage value is determined, not at the save step. |

---

#### Implementation notes for modifier stacking

- **Roll modifier cap:** the +1/-1 Hit and +1/-1 Wound roll modifiers are each capped at net ±1 before being applied to the threshold comparison. The BS/WS characteristic modifier is separate and uncapped.
- **Ability overrides (Blast, Sustained, Lethals, Dev Wounds, Anti):** the override simply ORs the flag / merges the value into `SimWeaponAbilities` before the run. Weapons that already have the ability are unaffected — the sim engine already handles them correctly.
- **Fish for Criticals** replaces the normal reroll condition: instead of `raw < threshold`, the reroll triggers when `raw < criticalHitsOn` (hits) or `raw < criticalWoundsOn` (wounds).
- **Ignores Cover + Cover:** both flags flow through to `SimulationAdapter`; if both are set, the cover bonus is not applied (they cancel).
- **FNP override:** applied in `SimulationAdapter` via `defender.FeelNoPain ?? (request.FnpOverride > 0 ? request.FnpOverride : null)` — the native value always wins; the override only fills in when `FeelNoPain` is null.
- **Blast and Rapid Fire** are fully simulated in `CombatSimulator.SimulateOneRun`: Blast adds `defender.Models / 5` attacks; Rapid Fire adds `RapidFire` attacks when `WithinHalfRange` is true. Both applied after the attack dice roll and before the `AttackModifier` offset.
- **`AttackModifier`** is a field on `SimWeaponProfile` (not encoded in the `DiceExpression`) so it is applied after Blast/Rapid Fire: `attacks = Math.Max(0, attacks + weapon.AttackModifier)`.
- **`RollWithReroll(DiceExpression)`** on `IDiceRoller` — rolls each die individually and rerolls once if ≤ `sides/2` (D6 threshold: 3; D3 threshold: 1). Used for both attack-dice and damage-dice reroll controls. Fixed expressions pass through unchanged.
- **`IndirectFire`** on `SimWeaponAbilities` — when true, `RollHit` uses a fixed skill target of 4 instead of `weapon.Skill`. Torrent still takes priority (hit roll is skipped entirely before `RollHit` is called).

### AP sign convention

`WeaponVariantProfile.Ap` in Contracts is stored as a **negative integer** (e.g. AP-2 → `-2`). `SimulationAdapter` negates it when building `SimWeaponProfile.Ap` because the simulator's `AbilityProcessor.EffectiveSave` does `save + ap` (expects a positive value). Do not change this without updating both sides.

---

## Simulation Engine (`Wh40kArmyEnricher.Core/Simulation/`)

Ported from the retired `wh40k-sim` standalone project. The combat rules spec lives in `.claude/rules/combat-rules.md`.

### Key types

| Type | Purpose |
|---|---|
| `DiceExpression` | Parses `"D6"`, `"2D3+1"`, fixed integers; `Count=0` means fixed value in `Modifier`; has `Scale(n)` and `Add(other)` for attack aggregation |
| `IDiceRoller` / `DiceRoller` | Abstracts randomness; injectable for deterministic testing; `RollWithReroll(expr)` rerolls each die independently if ≤ sides/2 |
| `SimAttackerProfile` | Name, Weapons, Rerolls, `CriticalHitsOn`, `CriticalWoundsOn` (default 6), `HitRollModifier`, `WoundRollModifier`, `FishForCriticalHits`, `FishForCriticalWounds` |
| `SimDefenderProfile` | Name, model count, T, Sv, invuln, W, FNP, keywords |
| `CombatSimulator` | Runs N iterations; returns `(IReadOnlyList<int> Damage, CombatStageStats Aggregate, IReadOnlyList<WeaponGroupStats> PerWeapon)` |
| `CombatStageStats` | Per-run averages for each pipeline stage and ability contribution |
| `WeaponGroupStats` | `{ WeaponName, CombatStageStats Stats }` — per-weapon breakdown entry |
| `SimulationResult` | Computed statistics: mean, median, stddev, min, max, probability/cumulative distributions |
| `AbilityProcessor` | Pure static helpers: `WoundThreshold(S,T)`, `EffectiveSave(defender, ap)` |

### Simulation flow

Per run: loop over each weapon in `Attacker.Weapons` — for each weapon, roll attack dice (with optional per-die reroll) → apply Blast bonus → apply Rapid Fire bonus → apply `AttackModifier` offset → for each attack: hit roll (skip if Torrent; use 4+ if Indirect Fire) → Sustained Hits bonus attacks → wound roll (skip if Lethal Hit) → save roll (skip if Devastating Wounds) → roll damage (with optional per-die reroll) → apply `DamageModifier` → FNP rolls. Each die may only be rerolled once. Natural 1 always fails, natural 6 (or lower if `CriticalHitsOn`/`CriticalWoundsOn` is reduced) always succeeds.

### Multi-weapon selection and attack aggregation

The simulation supports firing multiple weapons simultaneously (e.g. a Marshal, Castellan, and Sword Brethren all firing their Master-crafted Power Weapon in the same fight phase). The key design decisions:

**Weapon equality** — two weapon selections are considered the same weapon profile if their `(Type, Skill, Strength, Ap, Damage, Abilities)` match. Attacks are explicitly **excluded** from the equality key; they are the quantity being aggregated, not part of the weapon's identity. This means Marshal (7A), Castellan (6A), and 4× Sword Brother (3A) with identical MCPW stats all merge into one group: 7 + 6 + 12 = 25 total attacks.

**Attack aggregation** — implemented in `SimulationAdapter.AggregateAttacks()` using `DiceExpression.Scale(n)` and `DiceExpression.Add(other)`:
- Fixed attacks: Σ(attacks × modelCount) → `DiceExpression.Fixed(total)` e.g. 7+6+12 = `Fixed(25)`
- Dice attacks: 3 models × D6 → `3D6` (correct distribution, not roll-once-multiply); D3+1 × 2 → `2D3+2`
- `DiceExpression.Add` requires same `Sides` when both have dice (throws for mixed D3/D6 — shouldn't occur in practice for the same named weapon)

**`SimAttackerProfile.Weapons`** — a list of `SimWeaponProfile`, one per distinct weapon group. Each profile's `Attacks` field already encodes the aggregated total. No separate model count field. The simulator loops over all weapons per run and sums damage.

**Phase constraint** — shooting and melee occur in different game phases; it is invalid to simulate both simultaneously. The UI enforces this: the first weapon selected locks in a type (`Melee` or `Ranged`); rows of the opposite type get `.weapon-type-locked` styling (35% opacity, `cursor: not-allowed`) and clicks on them are silently rejected. The constraint resets when all weapon selections are cleared.

**`SimulationRequest.WeaponSelections`** — replaces the old single `WeaponName`/`VariantName`/`ModelName` fields with `List<WeaponSelection>`. Each entry carries `{ WeaponName, VariantName, ModelName, ModelCount }`. For single-weapon selections the `ModelCount` may be overridden by the user via the "models firing" input; for multi-weapon it is taken directly from the unit profile. The adapter groups selections by equality key, aggregates attacks, and builds one `SimWeaponProfile` per group.

### Combat stage statistics (`CombatStageStats` and `WeaponGroupStats`)

`CombatSimulator.Run()` returns the raw per-run damage list, an aggregate `CombatStageStats`, and a per-weapon `IReadOnlyList<WeaponGroupStats>`. These are displayed in the web UI as an "Attack Pipeline" funnel.

**Implementation:** `CombatSimulator` uses two private structs:
- `RunTally` (int fields) — per-run counters, passed via `ref` through all internal methods, cleared between runs using a reused `RunTally[]` buffer (one slot per weapon, `Array.Clear` each run — no per-run heap allocations)
- `RunTotals` (long fields) — cross-run accumulators, one per weapon; aggregate is the element-wise sum of all weapon totals

**Pipeline fields tracked (per weapon, then summed for aggregate):**

| Field | What it counts |
|---|---|
| `AvgAttacks` | Total attack dice rolled (including Rapid Fire bonus, excluding SH bonus hits) |
| `AvgHits` | Attacks that hit (including SH bonus hits; Torrent auto-hits) |
| `AvgCritHits` | Hits that were natural-6 (or lower if `CriticalHitsOn` reduced) |
| `AvgSustainedHitsBonus` | Extra hits generated by Sustained Hits ability |
| `AvgWounds` | Successful wound rolls (including Lethal Hits auto-wounds) |
| `AvgCritWounds` | Wounds that scored a critical wound (natural 6, or lowered by Anti) |
| `AvgLethalHitsAutoWounds` | Wounds that bypassed the wound roll due to Lethal Hits |
| `AvgAntiCritWounds` | Wounds that became crit wounds *only* because Anti lowered the threshold below 6 (i.e. `raw < 6` but `raw >= criticalWoundsOn`) |
| `AvgFailedSaves` | Save rolls that were made and failed (excludes DevW bypasses) |
| `AvgDevastatingWoundsTriggers` | Wounds that bypassed the save roll via Devastating Wounds |
| `AvgArmourSaveRolls` | Save rolls made against the armour save (possibly AP-modified) |
| `AvgInvulnSaveRolls` | Save rolls made against the invulnerable save |
| `AvgDamageBeforeFnp` | Raw damage that reached the FNP step |
| `AvgFnpSaved` | Damage points negated by Feel No Pain rolls |

**Save type logic:** `RollSave` replicates `AbilityProcessor.EffectiveSave` inline: if `defender.InvulnerableSave.HasValue && invuln < armourSave`, invuln save is used.

**UI display:** `displayPipeline()` in `army-view.js` generates all pipeline HTML dynamically into `#pipeline-content`:
- **Single weapon group**: full funnel table with ability sub-rows; Final Damage at the bottom. Sub-rows hidden when value < 0.001.
- **Multiple weapon groups**: one labelled full-funnel section per group (`.pipeline-weapon-header`), followed by a compact "Combined" summary table (main stages only, no rates) ending with Final Damage.
- `SimulationResponse.WeaponBreakdown` is empty for single-group runs (use `StageStats` directly); populated only when multiple groups exist.

### `SimulationAdapter` (in Web project)

Bridges `UnitProfile` (Contracts) → `SimulationConfig` (Core.Simulation):
- Groups `WeaponSelections` by `WeaponGroupKey` (type + Skill + S + AP + D + Abilities; Anti normalised to sorted `"kw:val,..."` string for dictionary equality)
- Aggregates attacks per group via `DiceExpression.Scale` + `DiceExpression.Add`
- Negates AP: `simAp = -contractsAp`
- Applies cover by adding 1 to `SimDefenderProfile.Save` before the run
- Returns `SimulationResponse` with mean damage, expected kills, P(kill ≥ 1), stddev, `StageStats` (aggregate), and `WeaponBreakdown` (per-group, empty when single group)

---

## Planned: Leader Attachments & AttachedUnit (not yet implemented)

This section records agreed design decisions for the next major feature. **Do not implement until instructed.**

### Background

In Warhammer 40K 10th edition, CHARACTER units with the `Leader` ability can attach to eligible Bodyguard units. The Leader unit (which may include retinue models with different statlines, e.g. Chaplain Grimaldus + Cenobyte Servitors) physically joins the Bodyguard, combining into a single combat unit. Leaders provide conditional buffs to the Bodyguard unit.

### Layer separation

- **`UnitProfile`** — pure enriched output from the army list. One per unit listed in the export. Indivisible. No knowledge of attachments. Gains one new field: `leadingAbilities`.
- **`AttachedUnit`** — simulation-layer composition. Not produced by the enricher directly; constructed by `LeaderResolver` from enriched profiles. Used in pairing files.

### Changes to `UnitProfile`

Add `leadingAbilities: AbilityProfile[]` — populated at enrichment time by filtering the unit's abilities whose text **starts with** `"While this model is leading a unit"`. These abilities only apply when the unit is acting as a leader; they must not be applied when simulating the unit standalone. All other abilities remain in `abilities`.

### `AttachedUnit` type (in `Wh40kArmyEnricher.Contracts`)

```csharp
record AttachedUnit(
    UnitProfile Bodyguard,
    IReadOnlyList<UnitProfile> Leaders,    // 0–2; each may contain retinue models
    IReadOnlyList<string> EffectiveKeywords,  // union of all keywords
    RerollOptions EffectiveRerolls,           // merged from leaders' leadingAbilities
    int EffectiveCritHitsOn,                  // minimum across all leading ability grants
    IReadOnlyList<AbilityProfile> EffectiveAbilities, // see merge rules below
    IReadOnlyList<string> Notes               // warnings, assumptions, overrides
);
```

**Effective property merge rules:**
- `effectiveKeywords` — union of bodyguard + all leader keywords (a leader with `PSYKER` makes the whole unit targetable by Anti-Psyker weapons)
- `effectiveRerolls` — OR of all leaders' `leadingAbilities` re-roll grants
- `effectiveCritHitsOn` — minimum across grants; default 6
- `effectiveAbilities` — **unit-wide** abilities ("this unit" / "models in this unit") are unioned; **individual** abilities (see known list below) require all models to have them — attaching a leader without an individual ability removes it from the combined unit

**Known individual abilities (intersection semantics):**
`Stealth`, `Infiltrate`, `Scouts N"`, `Deep Strike`, `Lone Operative` — and others as discovered. Abilities not on this list are treated as unit-wide.

### Leader eligibility

- Only units with the `CHARACTER` keyword are candidate leaders.
- Leader eligibility is parsed from the `Leader` ability text: `"This model can be attached to the following units: X, Y, Z."` — extract the comma-separated unit list.
- **Primary leaders** have the standard pattern above.
- **Support leaders** (Lieutenants, Apothecaries, etc.) have text like `"...can be attached to a unit that already contains a Leader..."` — they can join a unit that already has a primary leader.
- Generally 1 leader per unit; exceptions exist. Do **not** reject primary+primary combinations outright — instead add a note to the pairing: `"Second leader eligibility unverified — confirm manually"`.

### `LeaderResolver` (new class in `Wh40kArmyEnricher.Core`)

Responsibilities:
1. Given a list of enriched `UnitProfile` objects (one army), identify all CHARACTER units.
2. Parse each character's `Leader` ability text to extract eligible bodyguard unit names.
3. Classify primary vs support leaders from ability text patterns.
4. For each non-character unit, enumerate all valid 0-leader, 1-leader, and 2-leader combinations (where eligible).
5. For each combination, compute the `AttachedUnit` effective properties and populate `Notes`.
6. Parse re-roll and crit-hit grants from `leadingAbilities` using ability text patterns:
   - `"re-roll hit rolls of 1"` → `hitRerollOnes: true`
   - `"re-roll hit rolls"` (all) → `hitRerollAll: true`
   - `"re-roll wound rolls of 1"` → `woundRerollOnes: true`
   - `"re-roll wound rolls"` (all) → `woundRerollAll: true`
   - `"Critical Hits are scored on a 5+"` → `criticalHitsOn: 5`

### Web app simulation changes

- The `/api/simulate` attacker payload will need to accept an `AttachedUnit` instead of a plain `UnitProfile`.
- A baseline run (0 leaders) is always included alongside leader combinations.
- Selecting leader combinations will be done in the web UI, not via command-line flags.

### Retinue models

When a CHARACTER unit contains retinue models (e.g. Chaplain Grimaldus + Cenobyte Servitors), the entire `UnitProfile` (with all its `ModelEntry` rows) attaches as a single leader. The retinue's models participate in the combined unit's attack pool and the CHARACTER model's buffs apply to the bodyguard. No special handling is needed — the existing multi-model `UnitProfile` structure already captures this correctly.
