# CLAUDE.md — wh40k-army-enricher

## Project Overview

A .NET CLI tool that:
1. Parses Warhammer 40,000 (10th Edition) army list text exports from the official Warhammer app
2. Resolves each unit, model, and weapon against the BattleScribe data files in the [BSData/wh40k-10e](https://github.com/BSData/wh40k-10e) GitHub repository
3. Enriches the list with full statline data (Movement, Toughness, Save, Wounds, Leadership, OC, etc.) and weapon profiles (Range, Attacks, Skill, Strength, AP, Damage, keywords)
4. Outputs structured **attacker** and **defender** profiles compatible with the separate Monte Carlo simulation project (also .NET/C#)
5. Supports pairwise matchup mode: given two army exports (e.g. Black Templars vs Death Guard), produce all desired attacker/defender pairings for simulation runs

The output YAML schema must be agreed with and kept in sync with the Monte Carlo simulation project.

Sample input data lives in `./data` folder

---

## Repository Layout

```
Wh40kArmyEnricher/
├── CLAUDE.md
├── README.md
├── Wh40kArmyEnricher.sln
├── src/
│   ├── Wh40kArmyEnricher.Cli/
│   │   ├── Wh40kArmyEnricher.Cli.csproj   # Entry point — System.CommandLine
│   │   ├── Program.cs
│   │   └── Commands/
│   │       ├── EnrichCommand.cs
│   │       └── MatchupCommand.cs
│   ├── Wh40kArmyEnricher.Core/
│   │   ├── Wh40kArmyEnricher.Core.csproj
│   │   ├── Parser/
│   │   │   └── ArmyListParser.cs           # Parses raw .txt export from the Warhammer app
│   │   ├── BsData/
│   │   │   ├── CatalogueFetcher.cs         # Downloads/caches .cat files from BSData GitHub
│   │   │   ├── CatalogueParser.cs          # Parses .cat XML into in-memory model
│   │   │   ├── CatalogueStore.cs           # Holds all loaded catalogues
│   │   │   └── NameResolver.cs             # Matches army list names -> BSData entries
│   │   ├── Enricher.cs                     # Assembles enriched unit/weapon profiles
│   │   └── Models/                         # All domain record types
│   │       ├── ArmyList.cs
│   │       ├── CatalogueEntry.cs
│   │       └── Profiles.cs                 # Output profile types + YAML serialisation attrs
│   └── Wh40kArmyEnricher.Contracts/
│       ├── Wh40kArmyEnricher.Contracts.csproj  # Shared with simulation project
│       └── SimulationProfiles.cs               # Profile records used by both projects
│                                               # Deserialised by sim project using YamlDotNet
└── tests/
    └── Wh40kArmyEnricher.Tests/
        ├── Wh40kArmyEnricher.Tests.csproj
        ├── Fixtures/
        │   ├── black-templars-sample.txt
        │   └── death-guard-sample.txt          # Obtain from your buddy
        ├── Parser/
        │   └── ArmyListParserTests.cs
        ├── BsData/
        │   ├── CatalogueParserTests.cs          # Uses saved .cat XML snippets, no live calls
        │   └── NameResolverTests.cs
        └── Integration/
            └── EnrichPipelineTests.cs
```

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

The Warhammer app exports a structured plain-text format. Key properties:
- Army name and total points on the first line: `Iron Canticle (1970 Points)`
- Faction metadata block before the first section heading. The number of metadata lines varies by faction:
  - Sub-factions (e.g. Black Templars): 3 lines — game system / faction / detachment type, preceded by a force-size line (`Incursion (1000 Points)`)
  - Standalone factions (e.g. Death Guard): 1 line — faction name only
  - **The force-size line matches the points-header regex** (`\d+ Points`) and must be consumed during metadata parsing, not treated as a unit header. If the faction field is still empty after parsing, fall back to the game system field.
- Points values in the header use `Points` (capital P) for some factions and `points` (lower case) for others — the regex must use `RegexOptions.IgnoreCase`
- Sections delimited by ALL-CAPS category headings: `CHARACTERS`, `BATTLELINE`, `DEDICATED TRANSPORTS`, `OTHER DATASHEETS`
- Each unit begins with its name and points cost: `Assault Intercessor Squad (75 Points)`
- Models are indented with `•` (U+2022); weapons/wargear with `◦` (U+25E6)
  - **`◦` items are not always weapons** — ability upgrades such as "Shield Dome" also appear as `◦` bullets. These will fail weapon resolution; handle them as ability/wargear entries rather than warning loudly
- Enhancements listed as `◦ Enhancements: <name>` under the unit they apply to
- Count prefixes like `4x` precede model and weapon names
- Unit names may use U+2019 RIGHT SINGLE QUOTATION MARK (`'`) rather than ASCII apostrophe (`'`) — e.g. "Emperor's Champion"

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

See `tests/Fixtures/black-templars-sample.txt` for the reference export.

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
- `<selectionEntryGroups>` — wargear option groups (e.g. weapon choices, equipment like Shield Dome); **must be traversed** to reach nested weapon/upgrade entries
- `<entryLinks>` — references to `<sharedSelectionEntries>` by `targetId`

Parse to a depth of at least 6 to cover the deepest nesting observed in practice.

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
                <characteristic name="Description">This model has a 4++ invulnerable save.</characteristic>
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
1. **Exact match** — `string.Equals(a, b, StringComparison.OrdinalIgnoreCase)` after trimming whitespace
2. **Count-stripped match** — strip leading `\d+x\s+` prefix with a regex, then exact match
3. **Fuzzy match** — use `FuzzySharp.Fuzz.TokenSortRatio(a, b)` with a threshold of 85. Log every fuzzy match at `Warning` level: input name, matched BSData name, score
4. **Manual override** — load `name_overrides.json` from the working directory at startup; maps `"display name" -> "BSData selectionEntry name"` and takes precedence over all automatic matching

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
- Non-weapon entries (ability upgrades like "Shield Dome") will appear in the army list alongside weapons. If an army list item resolves to an entry with no weapon profiles, treat it silently as a non-weapon entry rather than warning

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
- **Invulnerable saves and FNP from upgrades.** These can come from two sources: (1) ability text directly on the unit/model entry, parsed with regex `\d\+\+(?!\+)` for invuln and `\d\+\+\+` for FNP; (2) selected upgrade entries (e.g. Shield Dome) listed in the army export. After resolving weapons, scan any remaining army-list entries that resolve to ability-only catalogue entries and apply any invuln/FNP patterns found in their text to the model's statline.

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
- **Warn on fuzzy matches.** Log input name, matched BSData name, and similarity score at `Warning` level. Consider writing a `resolution_report.json` alongside the main output that lists every match decision for review.
- **Invulnerable saves and FNP.** Parse with regex `\d\+\+(?!\+)` for invuln and `\d\+\+\+` for FNP. Check both the unit/model entry's own ability text AND any selected ability upgrades listed in the army export (e.g. Shield Dome). Set to `null` when absent.
- **Multi-wound models.** Capture `W` per model type (`selectionEntry[@type='model']`), not per unit — essential for simulation accuracy when a unit contains models with different wound counts.
- **Weapons with multiple profiles.** Some weapons have multiple `<profile>` children (e.g. plasma supercharge, Hellforged weapons strike/sweep). Capture all variants in the `profiles` array with a `variant` label derived from the profile `name` attribute. Strip BSData's `➤ ` prefix and the weapon entry name from profile names to get a clean variant label.
- **Non-weapon `◦` entries.** The army export uses `◦` bullets for both weapons and ability upgrades (e.g. Shield Dome). When an entry cannot be resolved as a weapon, check whether it resolves to an ability-only catalogue entry. If so, apply any invuln/FNP it grants to the model statline and skip it silently — do not emit a warning.
- **Keywords.** Parse the `Keywords` characteristic as a comma-separated list, trimming whitespace and normalising `-` (no keywords) to an empty list. Keywords such as `Blast`, `Torrent`, `Pistol`, `Indirect Fire`, `Lethal Hits`, `Sustained Hits X`, `Devastating Wounds` directly affect simulation logic.
- **Unit abilities.** Capture all `profile[@typeName='Abilities']` entries by name and text even if the simulation does not yet consume them — they will be needed for future rule modelling.
- **`simulation_id` generation** (`matchup` output only). Prefixed with faction abbreviation (e.g. `bt_` for Black Templars, `dg_` for Death Guard), attacker and defender name slugs joined with `_vs_` (e.g. `bt_crusader_squad_vs_dg_plague_marines`). The `enrich` command does not use simulation IDs — it outputs a plain list of unit profiles.

---

## Testing

- **Parser unit tests:** exercise `ArmyListParser` against `black-templars-sample.txt`; assert section categorisation, model counts, weapon names, enhancements
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
