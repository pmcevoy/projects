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
│   │   │   ├── CatalogueStore.cs           # Holds loaded catalogues, resolves catalogueLinks
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
- **Target framework:** `net9.0`
- **Nullable reference types:** enabled on all projects
- **Implicit usings:** enabled

---

## NuGet Dependencies

### `Wh40kArmyEnricher.Cli`
- `System.CommandLine` (2.0.0-beta or later) — subcommand CLI parsing

### `Wh40kArmyEnricher.Core`
- `YamlDotNet` — YAML serialisation of output profiles (use the serialiser/deserialiser API with a `NamingConvention` of `CamelCaseNamingConvention` to match the schema below)
- `FuzzySharp` — fuzzy name matching (token-sort ratio) for resolving display names to BSData entries
- No third-party XML library needed — use `System.Xml.Linq` (LINQ to XML / `XDocument`) which handles the namespace-qualified BSData schema cleanly

### `Wh40kArmyEnricher.Tests`
- `xunit` + `xunit.runner.visualstudio`
- `FluentAssertions`
- `Moq` (for mocking `HttpClient` / `ICatalogueFetcher` in unit tests)

### HTTP / Caching
Use `IHttpClientFactory` with a named client. Cache downloaded `.cat` files to disk under a configurable path (default `~/.wh40k-enricher/cache/`). Key cache entries by `{filename}:{git-sha}` — check staleness via the GitHub Commits API before downloading.

---

## Data Sources

### 1. Army List Text Export (Input)

The Warhammer app exports a structured plain-text format. Key properties:
- Army name and total points on the first line: `Iron Canticle (1970 Points)`
- Faction metadata block (game system, chapter, detachment type, force size) before the first section heading
- Sections delimited by ALL-CAPS category headings: `CHARACTERS`, `BATTLELINE`, `DEDICATED TRANSPORTS`, `OTHER DATASHEETS`
- Each unit begins with its name and points cost: `Assault Intercessor Squad (75 Points)`
- Models and weapons indented with `•` (U+2022) and `◦` (U+25E6) bullets
- Enhancements listed as `◦ Enhancements: <name>` under the unit they apply to
- Count prefixes like `4x` precede model and weapon names

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

**File naming convention:**
- Faction files: e.g. `Imperium - Black Templars.cat`, `Chaos - Death Guard.cat`
- Shared parent library: `Imperium - Space Marines.cat` (Black Templars inherits from this)
- Game system root: `Warhammer 40,000.gst`

**File format:** UTF-8 XML using namespace `http://www.battlescribe.net/schema/catalogueSchema`.
The `.catz` variant is zlib-compressed (raw deflate, no header); decompress with `DeflateStream` before parsing:

```csharp
using var deflate = new DeflateStream(rawStream, CompressionMode.Decompress);
var doc = await XDocument.LoadAsync(deflate, LoadOptions.None, ct);
```

**Fetching strategy:**
- Download on demand via GitHub raw URL:
  `https://raw.githubusercontent.com/BSData/wh40k-10e/main/{Uri.EscapeDataString(filename)}`
- Persist to `~/.wh40k-enricher/cache/{filename}` (directories created on first use)
- Check staleness using GitHub Commits API:
  `GET https://api.github.com/repos/BSData/wh40k-10e/commits?path={filename}&per_page=1`
  Compare the returned SHA against a `.sha` sidecar file written alongside the cached file
- CLI flag `--refresh-cache` bypasses the staleness check and forces re-download

**XML namespace — declare once and reuse:**

```csharp
private static readonly XNamespace Ns =
    "http://www.battlescribe.net/schema/catalogueSchema";
```

**Key XML elements to extract:**

```xml
<!-- Unit/model/upgrade entry -->
<selectionEntry id="abc-123" name="Assault Intercessors" type="unit">
  <profiles>
    <!-- Model statline -->
    <profile name="Assault Intercessors" typeName="Unit">
      <characteristics>
        <characteristic name="M">3"</characteristic>
        <characteristic name="T">4</characteristic>
        <characteristic name="Sv">3+</characteristic>
        <characteristic name="W">2</characteristic>
        <characteristic name="Ld">6+</characteristic>
        <characteristic name="OC">2</characteristic>
      </characteristics>
    </profile>
    <!-- Melee weapon profile -->
    <profile name="Astartes chainsword" typeName="Melee Weapons">
      <characteristics>
        <characteristic name="Range">Melee</characteristic>
        <characteristic name="A">4</characteristic>
        <characteristic name="WS">3+</characteristic>
        <characteristic name="S">4</characteristic>
        <characteristic name="AP">-1</characteristic>
        <characteristic name="D">1</characteristic>
        <characteristic name="Keywords">-</characteristic>
      </characteristics>
    </profile>
    <!-- Ranged weapon profile -->
    <profile name="Heavy bolt pistol" typeName="Ranged Weapons">
      <characteristics>
        <characteristic name="Range">18"</characteristic>
        <characteristic name="A">1</characteristic>
        <characteristic name="BS">3+</characteristic>
        <characteristic name="S">4</characteristic>
        <characteristic name="AP">-1</characteristic>
        <characteristic name="D">1</characteristic>
        <characteristic name="Keywords">Pistol</characteristic>
      </characteristics>
    </profile>
  </profiles>
  <!-- Child model entries and upgrade options nested here -->
  <selectionEntries>
    <selectionEntry name="Assault Intercessor Sergeant" type="model"> ... </selectionEntry>
  </selectionEntries>
</selectionEntry>
```

**Profile `typeName` values in 10th edition BSData:**
- `"Unit"` — model statline: M, T, Sv, W, Ld, OC
- `"Ranged Weapons"` — Range, A, BS, S, AP, D, Keywords
- `"Melee Weapons"` — Range (always "Melee"), A, WS, S, AP, D, Keywords
- `"Abilities"` — free-text special rules; capture name + text for reference

**Catalogue inheritance — critical for Black Templars:**
Black Templars entries inherit from `Imperium - Space Marines.cat`. The resolver must load parent catalogues transitively by following `<catalogueLink>` elements:

```xml
<catalogueLinks>
  <catalogueLink id="..." name="Space Marines" targetId="..." type="catalogue"/>
</catalogueLinks>
```

Map `targetId` to the correct `.cat` filename using the `Warhammer 40,000.gst` game system file, which enumerates all catalogues. Load `Warhammer 40,000.gst` first on startup to build this map.

---

## Name Matching Strategy

Army list display names are generally identical to BSData `name` attributes but edge cases exist (pluralisation, punctuation differences, etc.).

Resolution order:
1. **Exact match** — `string.Equals(a, b, StringComparison.OrdinalIgnoreCase)` after trimming whitespace
2. **Count-stripped match** — strip leading `\d+x\s+` prefix with a regex, then exact match
3. **Fuzzy match** — use `FuzzySharp.Fuzz.TokenSortRatio(a, b)` with a threshold of 85. Log every fuzzy match at `Warning` level: input name, matched BSData name, score
4. **Manual override** — load `name_overrides.json` from the working directory at startup; maps `"display name" -> "BSData selectionEntry name"` and takes precedence over all automatic matching

Matching scope:
- **Unit names:** search `selectionEntry[@type='unit']` across the faction catalogue and all linked parent catalogues
- **Model names:** search within the matched unit's `selectionEntries` children first, then fall back to top-level `selectionEntry[@type='model']` entries
- **Weapon names:** search `profile[@typeName='Ranged Weapons' or @typeName='Melee Weapons']` within the matched unit/model scope first, then globally across loaded catalogues

---

## Output Profiles Schema

Output is YAML. Use `YamlDotNet` with `CamelCaseNamingConvention` so C# property names like `InvulnerableSave` serialise as `invulnerableSave`.

### Design Decisions (read before touching the schema)

- **`ap` is stored as a negative integer** matching the actual game value (e.g. AP -2 → `ap: -2`). The simulation must not negate it again.
- **`skill` is stored as a raw integer** (e.g. `3` means "hits on 3+"). The `+` suffix is implied; do not store it as a string. Same convention applies to `save`, `invulnerableSave`, `feelNoPain`, and `criticalHitsOn`.
- **`weapons` is always a list**, even for single-weapon models. Each weapon entry contains a `profiles` list to handle multi-mode weapons (e.g. plasma standard vs supercharge).
- **`rerolls` live at the attacker unit level**, not per-weapon. This models army-wide or detachment auras. Per-weapon re-roll distinctions (e.g. from enhancements) are out of scope for v1 — note this as a known limitation.
- **`withinHalfRange`** is a simulation parameter, not a weapon property. It is defined at the top level of each pairing's simulation context, not inside the weapon block.
- **`rapidFire` and `melta`** store the bonus value as an integer. `0` means the keyword is not present. The simulation should treat `rapidFire: 0` as "not Rapid Fire" — i.e. 0 is the sentinel, not a valid ability value.
- **`range`** is stored in inches as a plain integer (`range: 12`). `"Melee"` weapons use `range: 0`. The simulation uses this to gate whether a ranged weapon can fire at all given the engagement scenario.
- **`anti`** is a map of `keyword -> criticalWoundThreshold`. If a target unit has any of the listed keywords, the attacker scores a Critical Wound on a roll of that value or higher (in addition to the normal Critical Wound rules).
- **`keywords` on the attacker** captures unit-level keywords (e.g. `INFANTRY`, `MOUNTED`, `CHARACTER`) that may interact with terrain, abilities, or opponent weapon keywords. These are sourced from the BSData `<categoryLink>` entries on the unit's `selectionEntry`.

---

### Attacker Profile

```yaml
attacker:
  name: "Crusader Squad"           # Unit display name from army list
  faction: "Black Templars"
  modelCount: 20                   # Total models in the unit
  keywords:                        # Unit-level keywords from BSData categoryLinks
    - INFANTRY
    - CORE
    - ADEPTUS ASTARTES
  rerolls:                         # Army/detachment-level re-roll auras; set per simulation run
    hitRerollOnes: false
    hitRerollAll: false
    woundRerollOnes: false
    woundRerollAll: false
  criticalHitsOn: 6                # Normally 6; some abilities lower this (e.g. Sustained Hits)
  models:                          # One entry per distinct model type in the unit
    - modelName: "Sword Brother"
      count: 1
      weapons:
        - weaponName: "Master-crafted power weapon"
          type: Melee               # Melee | Ranged
          range: 0                  # 0 for melee
          profiles:
            - variant: default      # Use "default" when weapon has only one mode
              attacks: 4
              skill: 3              # Raw integer; implies 3+
              strength: 5
              ap: -2                # Negative integer matching game value
              damage: 2
              abilities:
                torrent: false
                blast: false
                melta: 0            # 0 = not Melta; integer = bonus damage within half range
                rapidFire: 0        # 0 = not Rapid Fire; integer = bonus attacks at half range
                sustainedHits: 0    # 0 = not Sustained Hits; integer = bonus hits on Critical Hit
                lethalHits: false
                devastatingWounds: false
                anti: {}            # Empty map = no Anti keyword ability
        - weaponName: "Pyre pistol"
          type: Ranged
          range: 12
          profiles:
            - variant: default
              attacks: "D6"         # Variable attacks stored as string when not a fixed integer
              skill: 3
              strength: 4
              ap: 0
              damage: 1
              abilities:
                torrent: true       # Torrent: auto-hits, skip hit roll entirely
                blast: false
                melta: 0
                rapidFire: 0
                sustainedHits: 0
                lethalHits: false
                devastatingWounds: false
                anti: {}
    - modelName: "Initiate"
      count: 11
      weapons:
        - weaponName: "Astartes chainsword"
          type: Melee
          range: 0
          profiles:
            - variant: default
              attacks: 4
              skill: 3
              strength: 4
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
                anti: {}
        - weaponName: "Bolt pistol"
          type: Ranged
          range: 12
          profiles:
            - variant: default
              attacks: 1
              skill: 3
              strength: 4
              ap: 0
              damage: 1
              abilities:
                torrent: false
                blast: false
                melta: 0
                rapidFire: 0
                sustainedHits: 0
                lethalHits: false
                devastatingWounds: false
                anti: {}
  abilities:                        # Unit special rules from BSData; not yet consumed by sim
    - name: "Righteous Zeal"
      text: "..."
  enhancements: []                  # Enhancement names from army list
```

### Defender Profile

```yaml
defender:
  name: "Plague Marines"
  faction: "Death Guard"
  modelCount: 7
  toughness: 5
  save: 3                           # Raw integer; implies 3+
  invulnerableSave: null            # null if no invuln; integer if present (e.g. 4 = 4++)
  wounds: 2                         # Wounds per model
  feelNoPain: null                  # null if none; integer if present (e.g. 5 = 5+++)
  keywords:
    - INFANTRY
    - CHAOS
    - NURGLE
    - HERETIC ASTARTES
  abilities:
    - name: "Plague-ridden"
      text: "..."
```

### Pairing File (matchup output)

One YAML file per matchup containing all requested pairings. The simulation project reads this file to enumerate and execute runs.

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
      # full attacker profile as above, inlined
    defender:
      # full defender profile as above, inlined
  - simulationId: "bt_assault_intercessors_vs_dg_plague_marines"
    attacker:
      # ...
    defender:
      # ...
```

The Monte Carlo simulation project should deserialise the pairing file using matching C# record types defined in `Wh40kArmyEnricher.Contracts`, using `YamlDotNet` with the same `CamelCaseNamingConvention`.

---

## CLI Interface

Built with `System.CommandLine`. Two subcommands:

```
# Enrich a single army list and write profiles
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
- **Invulnerable saves and FNP.** These appear as separate `profile` entries (typeName `"Abilities"`) or embedded in ability text. Parse them with a regex (`\d\+\+` for invuln, `\d\+\+\+` for FNP) where possible and populate `inv_sv` / `fnp` fields; set to `null` when absent.
- **Multi-wound models.** Capture `W` per model type (`selectionEntry[@type='model']`), not per unit — essential for simulation accuracy when a unit contains models with different wound counts.
- **Weapons with multiple profiles.** Some weapons have multiple `<profile>` children (e.g. plasma supercharge). Capture all variants in the `profiles` array with a `variant` label derived from the profile `name` attribute.
- **Keywords.** Parse the `Keywords` characteristic as a comma-separated list, trimming whitespace and normalising `-` (no keywords) to an empty list. Keywords such as `Blast`, `Torrent`, `Pistol`, `Indirect Fire`, `Lethal Hits`, `Sustained Hits X`, `Devastating Wounds` directly affect simulation logic. Store as a YAML sequence on the weapon's abilities block per the schema above.
- **Unit abilities.** Capture all `profile[@typeName='Abilities']` entries by name and text even if the simulation does not yet consume them — they will be needed for future rule modelling.
- **`simulation_id` generation.** Derive from attacker and defender unit names: lowercase, spaces to underscores, non-alphanumeric characters stripped, prefixed with faction abbreviation (e.g. `bt_` for Black Templars, `dg_` for Death Guard).

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
- `CatalogueStore` should maintain a `Dictionary<string, XDocument>` keyed by catalogue ID and resolve `catalogueLink` references lazily on first access
- The `Warhammer 40,000.gst` game system file defines `profileType` elements naming all characteristics — parse this on startup to validate that expected characteristic names exist in the loaded data
- GitHub raw URL pattern: `https://raw.githubusercontent.com/BSData/wh40k-10e/main/{Uri.EscapeDataString(filename)}` — note spaces in filenames like `Imperium - Black Templars.cat` must be encoded as `%20`
- `.catz` files are raw deflate compressed (no zlib header). Use `new DeflateStream(stream, CompressionMode.Decompress)` — do **not** use `ZLibStream` or `GZipStream`
- Register `HttpClient` via `IHttpClientFactory` in DI; set a `User-Agent` header identifying this tool — the GitHub API rejects requests without one
- Keep the record types in `Wh40kArmyEnricher.Contracts` in sync with the Monte Carlo simulation project's deserialisation expectations. Both projects use `YamlDotNet` with `CamelCaseNamingConvention`. If both projects live in separate solutions, publish `Contracts` as a local NuGet package or use a git submodule
