# CLAUDE.md — wh40k-army-enricher

## Project Overview

A .NET solution consisting of:

1. **CLI tool** — parses Warhammer 40,000 (10th Edition) army list text exports from the official Warhammer app, resolves each unit/model/weapon against BattleScribe data files ([BSData/wh40k-10e](https://github.com/BSData/wh40k-10e)), enriches with full statlines, and outputs YAML pairings for offline simulation runs
2. **Web application** (`Wh40kArmyEnricher.Web`) — live-game tool; paste two army lists, view enriched unit cards side-by-side, select an attacker weapon and a defender unit, configure combat options, and run a Monte Carlo simulation on the server — results appear inline without page reload
3. **Simulation engine** (in `Wh40kArmyEnricher.Core/Simulation/`) — ported from the now-retired `wh40k-sim` project; full 4-step 40K attack sequence (hit → wound → save → damage) with all weapon abilities

Sample input data lives in `./data` folder

---

## Language & Target Framework

- **Language:** C# 12
- **Target framework:** `net8.0`
- **Nullable reference types:** enabled on all projects
- **Implicit usings:** enabled

---

## NuGet Dependencies

### `Wh40kArmyEnricher.Cli`
- `System.CommandLine` (2.0.0-beta or later) — subcommand CLI parsing

### `Wh40kArmyEnricher.Core`
- `YamlDotNet` 16.x — YAML serialisation of output profiles (use the serialiser/deserialiser API with a `NamingConvention` of `CamelCaseNamingConvention` to match the schema below)
  - **Important:** v16 `IYamlTypeConverter` uses `ReadYaml(IParser, Type, ObjectDeserializer)` / `WriteYaml(IEmitter, object?, Type, ObjectSerializer)` — the signatures differ from v15 and earlier
  - For custom scalar converters emitting double-quoted strings, `isQuotedImplicit` must be `true` and `tag` must be empty; setting both implicit flags to `false` with an empty tag throws at runtime
  - Call `.DisableAliases()` on `SerializerBuilder` — without this, YamlDotNet emits YAML anchor/alias symbols (`&o1`, `*o3`) when it detects shared object references (e.g. default `RerollOptions` instances)
  - Multi-line ability text uses `|` (literal block scalar) style, not `>` (folded). Folded style doubles newlines in the file. Implement a `LiteralBlockScalarEmitter : ChainedEventEmitter` that sets `ScalarStyle.Literal` for any string value containing `\n`, and register it with `.WithEventEmitter(next => new LiteralBlockScalarEmitter(next))`
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

The Warhammer app exports a structured plain-text format. Two distinct format variants exist depending on the platform:

#### iOS format (reference: `black-templars-sample.txt`)
- `•` (U+2022) at column 0 = model entry in a squad
- `◦` (U+25E6) at column 0 = weapon or ability upgrade belonging to the current model
- Enhancement line: `◦ Enhancements: <name>`
- Metadata order: game system / faction / detachment / force-size

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
- `ClassifyBulletLine()` normalises both formats into a `(int Level, bool IsBullet, string Content)` tuple
  - Level 0 = model (or single-model unit item); Level 1 = weapon or continuation
  - `IsBullet` is `true` only when a `•` character is present on that line (not for bare continuation lines)
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

## Output Profiles Schema

Output is YAML. Use `YamlDotNet` with `CamelCaseNamingConvention` so C# property names like `InvulnerableSave` serialise as `invulnerableSave`.

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

A single `UnitProfile` record carries both offensive and defensive data. The `Pairing.Attacker` / `Pairing.Defender` field names encode the role; the profile type itself does not differ between roles. This eliminates the duplication of identity fields (name, faction, keywords, abilities) that would arise from separate attacker/defender types.

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

### `enrich` command output

The `enrich` command outputs a flat list of `UnitProfile` objects — one per unit in the army. Each unit appears exactly once with all its data.

```yaml
- name: "Assault Intercessor Squad"
  faction: "Black Templars"
  modelCount: 5
  # ... full UnitProfile as above
- name: "Crusader Squad"
  faction: "Black Templars"
  # ...
```

### Pairing File (`matchup` command output)

One YAML file per matchup containing all requested pairings. Both `attacker` and `defender` are full `UnitProfile` objects. The simulation project reads this file to enumerate and execute runs.

```yaml
attackerArmy: "Iron Canticle (Black Templars)"
defenderArmy: "Plague Horde (Death Guard)"
generatedUtc: "2025-03-13T12:00:00Z"
simulationDefaults:
  withinHalfRange: false           # Applied to all pairings unless overridden
  runs: 10000                      # Default Monte Carlo iteration count
pairings:
  - simulationId: "bt_crusader_squad_vs_dg_plague_marines"
    attacker:
      # full UnitProfile (offensive + defensive data for the attacking unit)
    defender:
      # full UnitProfile (offensive + defensive data for the defending unit)
```

The Monte Carlo simulation project should deserialise the pairing file using matching C# record types defined in `Wh40kArmyEnricher.Contracts`, using `YamlDotNet` with the same `CamelCaseNamingConvention`.

---

## CLI Interface

Built with `System.CommandLine`. Two subcommands:

```
# Enrich a single army list — outputs a flat list of UnitProfile objects (one per unit)
army-enricher enrich <army-list.txt> [--output <path>] [--refresh-cache] [--dry-run]

# Enrich two armies and generate all pairings
army-enricher matchup <attacker.txt> <defender.txt> [--output <path>] [--refresh-cache]

# Selective pairings by unit name filter (repeatable)
army-enricher matchup attacker.txt defender.txt \
  --attacker-unit "Crusader Squad" \
  --defender-unit "Plague Marines" \
  --output selective.json
```

`--dry-run` runs the full parse and resolution pipeline but writes no output files; useful for auditing unresolved names before committing to a run.

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
- **`simulation_id` generation** (`matchup` output only). Prefixed with faction abbreviation (e.g. `bt_` for Black Templars, `dg_` for Death Guard), attacker and defender name slugs joined with `_vs_` (e.g. `bt_crusader_squad_vs_dg_plague_marines`). The `enrich` command does not use simulation IDs — it outputs a plain list of unit profiles.

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
- Keep the record types in `Wh40kArmyEnricher.Contracts` in sync with the Monte Carlo simulation project's deserialisation expectations. Both projects use `YamlDotNet` with `CamelCaseNamingConvention`. If both projects live in separate solutions, publish `Contracts` as a local NuGet package or use a git submodule
- Static classes cannot be used as type parameters for `ILogger<T>` — use `ILoggerFactory.CreateLogger("Name")` for loggers inside static command classes
- All `typeName` comparisons (e.g. `"Ranged Weapons"`, `"Melee Weapons"`, `"Unit"`) **must use `StringComparison.OrdinalIgnoreCase`** — case variation has been observed in the wild and silently drops profiles if compared with `==`
- `name_overrides.json` must be present in the **current working directory** when `army-enricher.exe` is invoked — not relative to the executable. Example entry: `{ "Deathshroud Champion": "Deathshroud Terminator Champion" }`. The file is optional; if absent, resolution proceeds without overrides.
- `dotnet test` only builds the test project and its transitive dependencies — it does **not** build the CLI project. Use `dotnet build Wh40kArmyEnricher.sln` to update `army-enricher.exe`.

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

All simulation modifiers are set explicitly by the user — ability text is not auto-parsed into rerolls:
- **Hit rerolls**: None / Reroll 1s / Reroll All
- **Wound rerolls**: None / Reroll 1s / Reroll All
- **Within Half Range** — enables Melta bonus damage and Rapid Fire extra attacks
- **In Cover** — adds +1 to defender's armour save before simulation
- **Crit on 5+** — lowers `CriticalHitsOn` to 5 for abilities like Oath of Moment
- **Models firing** — pre-filled from the weapon's model count; can be overridden (e.g. only 3 of 5 models in range)

### AP sign convention

`WeaponVariantProfile.Ap` in Contracts is stored as a **negative integer** (e.g. AP-2 → `-2`). `SimulationAdapter` negates it when building `SimWeaponProfile.Ap` because the simulator's `AbilityProcessor.EffectiveSave` does `save + ap` (expects a positive value). Do not change this without updating both sides.

---

## Simulation Engine (`Wh40kArmyEnricher.Core/Simulation/`)

Ported from the retired `wh40k-sim` standalone project. The combat rules spec lives in `.claude/rules/combat-rules.md`.

### Key types

| Type | Purpose |
|---|---|
| `DiceExpression` | Parses `"D6"`, `"2D3+1"`, fixed integers; `Count=0` means fixed value in `Modifier` |
| `IDiceRoller` / `DiceRoller` | Abstracts randomness; injectable for deterministic testing |
| `SimAttackerProfile` | Name, model count, single `SimWeaponProfile`, rerolls, `CriticalHitsOn` |
| `SimDefenderProfile` | Name, model count, T, Sv, invuln, W, FNP, keywords |
| `CombatSimulator` | Runs N iterations; returns `IReadOnlyList<int>` (damage per run) |
| `SimulationResult` | Computed statistics: mean, median, stddev, min, max, probability/cumulative distributions |
| `AbilityProcessor` | Pure static helpers: `WoundThreshold(S,T)`, `EffectiveSave(defender, ap)` |

### Simulation flow

Per run: resolve attack count (base × models + Blast bonus + Rapid Fire if half range) → for each attack: hit roll (skip if Torrent) → Sustained Hits bonus attacks → wound roll (skip if Lethal Hit) → save roll (skip if Devastating Wounds) → damage + FNP. Each die may only be rerolled once. Natural 1 always fails, natural 6 (or lower if `CriticalHitsOn` is reduced) always succeeds.

### `SimulationAdapter` (in Web project)

Bridges `UnitProfile` (Contracts) → `SimulationConfig` (Core.Simulation):
- Finds the selected weapon variant across all selected attacker unit models
- Negates AP: `simAp = -contractsAp`
- Parses `ScalarValue` attacks/damage via `DiceExpression.Parse`
- Applies cover by adding 1 to `SimDefenderProfile.Save` before the run
- Returns `SimulationResponse` with mean damage, expected kills (`mean / woundsPerModel`), P(kill ≥ 1), stddev

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

### `Pairing` changes

- `Attacker` changes from `UnitProfile` to `AttachedUnit`.
- `Defender` remains `UnitProfile` — defenders are always plain units, never attached.
- `simulationId` encodes leader names: `bt_sword_brethren_led_by_marshal_castellan_vs_dg_plague_marines`.
- A baseline pairing (0 leaders) is always included alongside leader combinations.

### `matchup` command changes

- **Attacker side**: enumerate all `AttachedUnit` combinations from the attacking army (0-leader baseline + all valid 1-leader + all valid 2-leader combinations).
- **Defender side**: if `--defender-unit` flag is present, use those units; otherwise present an interactive numbered list of defender units and prompt for selection.
- `--defender-unit` remains repeatable and optional.

### Retinue models

When a CHARACTER unit contains retinue models (e.g. Chaplain Grimaldus + Cenobyte Servitors), the entire `UnitProfile` (with all its `ModelEntry` rows) attaches as a single leader. The retinue's models participate in the combined unit's attack pool and the CHARACTER model's buffs apply to the bodyguard. No special handling is needed — the existing multi-model `UnitProfile` structure already captures this correctly.
