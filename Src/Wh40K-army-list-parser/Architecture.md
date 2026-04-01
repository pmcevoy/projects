# Architecture — Wh40kArmyEnricher

## Overview

A .NET 8 CLI tool that parses Warhammer 40K army list exports, resolves units/weapons against BSData catalogues, and produces enriched YAML profiles for Monte Carlo simulation. See CLAUDE.md for the full specification.

---

## Solution Structure

```
Wh40kArmyEnricher.sln
├── src/
│   ├── Wh40kArmyEnricher.Contracts/       # Output records shared with Monte Carlo project
│   ├── Wh40kArmyEnricher.Core/            # Parsing, BSData resolution, enrichment logic
│   └── Wh40kArmyEnricher.Cli/             # CLI entry point and subcommands
└── tests/
    └── Wh40kArmyEnricher.Tests/           # Unit, resolver, and integration tests
```

Full annotated file tree with per-file comments in CLAUDE.md § Repository Layout.

---

## Project Responsibilities

### `Wh40kArmyEnricher.Contracts`

Defines the YAML output schema shared between this project and the Monte Carlo simulation project. Both projects reference this assembly and use `YamlDotNet` with `CamelCaseNamingConvention` to serialise/deserialise it.

Key types:

| Type | Purpose |
|------|---------|
| `UnitProfile` | Complete unit record — identity, keywords, abilities, enhancements, offensive and defensive stats |
| `ModelProfile` | One distinct model type within a unit (name, count, weapon list) |
| `WeaponProfile` | A weapon with a list of `WeaponVariantProfile` (handles multi-mode weapons) |
| `WeaponVariantProfile` | Single weapon mode: attacks, skill, strength, AP, damage, `WeaponAbilities` |
| `WeaponAbilities` | Weapon keywords: torrent, blast, melta, rapidFire, sustainedHits, lethalHits, devastatingWounds, twinLinked, anti |
| `RerollOptions` | Unit-level re-roll auras (set per simulation run, not per weapon) |
| `AbilityProfile` | Special rule: name + text from BSData |
| `ScalarValue` | Union of integer or string (for variable attacks/damage values like `D6`) |
| `Pairing` | One attacker/defender pairing with a `simulationId` |
| `PairingFile` | Full `matchup` output: both army names, UTC timestamp, simulation defaults, list of pairings |

Schema design decisions (AP sign convention, sentinel values, reroll placement, etc.) are documented in CLAUDE.md § Output Profiles Schema.

---

### `Wh40kArmyEnricher.Core`

The core library. Contains all parsing, resolution, and enrichment logic.

#### `Parser/ArmyListParser.cs`

Parses the plain-text export from the Warhammer app into an `ArmyList` record. Handles both the iOS and Android export formats. For full format specifications see CLAUDE.md § Data Sources.

**Parsing implementation:**

`ClassifyBulletLine()` normalises both formats into `(int Level, bool IsBullet, string Content)`:
- Level 0 = model (or single-model unit item); Level 1 = weapon or continuation
- `IsBullet` = `true` only when a `•` character is present; bare Android continuation lines have `IsBullet = false`

Model mode detection:
- A unit is in **model mode** (distinct sub-models) when ≥ 1 Level-1 item has `IsBullet = true`
- Single-model units with only continuation weapon lines are in **weapon mode** — the unit name is also the model name

Android detachment scan: after the force-size metadata break, if `detachment` is still empty, scan forward for the next non-empty, non-points-header line.

Output domain model:
```
ArmyList
└── UnitEntry (name, points, category, enhancements)
    └── ModelEntry (name, count)
        └── WeaponEntry (name, count)
```

#### `BsData/ICatalogueFetcher.cs` & `CatalogueFetcher.cs`

Abstraction and implementation for downloading and caching BSData files.

- Downloads from `https://raw.githubusercontent.com/BSData/wh40k-10e/main/{filename}`
- Caches to `~/.wh40k-enricher/cache/` (configurable)
- Re-uses cached files on subsequent runs; only re-downloads with `--refresh-cache`
- Registered via `IHttpClientFactory` with a named `"bsdata"` client
- Sets `User-Agent` header (required by the GitHub API)

#### `BsData/CatalogueParser.cs`

Parses BSData `.cat` (plain XML) and `.catz` (raw deflate-compressed XML) files into `CatalogueEntry` objects. XML namespace: `http://www.battlescribe.net/schema/catalogueSchema`.

Entry collection and profile extraction follow the BSData XML structure specified in CLAUDE.md § BattleScribe Data Files. Key implementation notes:
- All `typeName` comparisons use `StringComparison.OrdinalIgnoreCase` (case variation observed in the wild)
- `<selectionEntryGroups>` are traversed recursively to depth 6
- Ability text is scanned with `\d\+\+(?!\+)` (invuln) and `\d\+\+\+` (FNP) and applied to the statline
- `.catz` files are decompressed with `DeflateStream` (raw deflate, not ZLibStream/GZipStream)

#### `BsData/CatalogueStore.cs`

Eagerly loads all BSData catalogues into memory on startup.

Initialization sequence (download-everything strategy — rationale in CLAUDE.md § BattleScribe Data Files):
1. Load game system root (`Warhammer 40,000.gst`)
2. Fetch file listing from GitHub Contents API → cache to `catalogue-list.json`
3. Download and parse all `.cat` files
4. Hold all entries in memory for the duration of the run

Public API:
- `InitialiseAsync()` — trigger eager load (idempotent)
- `GetAllEntries()` — all entries across all catalogues
- `GetAllEntriesOfType(type)` — filtered by entry type (`"unit"`, `"model"`, `"upgrade"`)

Individual catalogue failures are logged as warnings; loading continues.

#### `BsData/NameResolver.cs`

Matches army list display names to BSData catalogue entries using multi-pass resolution.

Implements a 5-step resolution pipeline: override → exact → count-stripped → fuzzy → prefix. Full algorithm and thresholds in CLAUDE.md § Name Matching Strategy.

Searches are scoped to unit → model → weapon with global fallback; ability-only entries are silently filtered. "Could not resolve" warnings are only emitted after all fallbacks are exhausted.

#### `Enricher.cs`

Main orchestrator. Converts a parsed `ArmyList` + loaded `CatalogueStore` into a list of `EnrichedUnit` records.

Per-unit enrichment:
1. Resolve unit name → `CatalogueEntry`
2. For each model:
   - Resolve model name (prefix match for loadout variants)
   - Use model's own statline if available; otherwise fall back to the unit statline
   - Scan selected ability upgrades from the army list for invuln/FNP patterns
   - For each weapon: resolve profiles, extract variant labels, parse weapon abilities
3. Aggregate into `UnitProfile`

Weapon ability parsing from the `Keywords` characteristic:
- Boolean flags: `Torrent`, `Blast`, `Lethal Hits`, `Devastating Wounds`, `Twin-linked`
- Integer values (`0` = not present): `Rapid Fire N`, `Sustained Hits N`, `Melta N`
- Map: `Anti-KEYWORD N+` → `{ keyword: criticalWoundThreshold }`

Multi-profile weapon variant label extraction:
- BSData format: `"➤ Hellforged weapons - strike"`
- Strip `"➤ "` prefix and `"Hellforged weapons - "` → variant = `"strike"`

#### `YamlSerialiser.cs`

Utility for consistent `YamlDotNet` serializer configuration:
- `CamelCaseNamingConvention` (C# `InvulnerableSave` → YAML `invulnerableSave`)
- `ScalarValueConverter` for integer/string union (`ScalarValue`)
- Null omission on serialisation
- `.DisableAliases()` — suppresses YAML anchor/alias symbols
- `LiteralBlockScalarEmitter` — custom `ChainedEventEmitter` that forces `|` (literal) block scalar style for multi-line strings

See CLAUDE.md § NuGet Dependencies for the YamlDotNet v16 API gotchas and rationale behind these choices.

---

### `Wh40kArmyEnricher.Cli`

#### `Program.cs`

DI container setup and CLI root command registration.

Services registered as singletons:
- `IHttpClientFactory` with named `"bsdata"` client (User-Agent set)
- `CatalogueParser`, `ICatalogueFetcher` → `CatalogueFetcher`, `NameResolver`, `Enricher`

`CatalogueStore` is **not** pre-registered — it is instantiated per command so the `--refresh-cache` flag can be passed at construction time.

#### `Commands/EnrichCommand.cs`

Enriches a single army and writes a flat YAML list of `UnitProfile` objects — one per unit.

#### `Commands/MatchupCommand.cs`

Enriches two armies, applies optional unit name filters, and writes a `PairingFile` containing all attacker/defender combinations (Cartesian product of filtered units).

`simulationId` format: `{factionAbbrev}_{unitSlug}_vs_{factionAbbrev}_{unitSlug}` (e.g. `bt_crusader_squad_vs_dg_plague_marines`)

Full CLI signatures in CLAUDE.md § CLI Interface.

---

## Data Flow

### `enrich` command

```
Text file
  → ArmyListParser.Parse()          — produces ArmyList
  → CatalogueStore.InitialiseAsync() — downloads/caches all .cat files
  → Enricher.Enrich(armyList)       — resolves units/models/weapons, builds UnitProfiles
  → YamlSerialiser.Serialise()      — serialises List<UnitProfile>
  → Output file
```

### `matchup` command

```
Two text files
  → ArmyListParser.Parse() × 2      — produces ArmyList × 2
  → CatalogueStore.InitialiseAsync() — shared catalogue store
  → Enricher.Enrich() × 2           — produces EnrichedUnit[] × 2
  → Apply unit name filters
  → Cartesian product → List<Pairing>
  → Wrap in PairingFile
  → YamlSerialiser.Serialise()
  → Output file
```

---

## Key Interfaces & Abstractions

| Interface | Concrete type | Purpose |
|-----------|---------------|---------|
| `ICatalogueFetcher` | `CatalogueFetcher` | HTTP fetch + disk cache; mockable in tests |
| `ILogger<T>` | Console provider | Structured logging throughout |
| `IHttpClientFactory` | DI container | Named HTTP clients with shared config |
| `IYamlTypeConverter` | `ScalarValueConverter` | Custom YAML serialisation for `ScalarValue` |

---

## Name Resolution Detail

```
Input: "Crusader Squad"

1. Manual override?       name_overrides.json → no match
2. Exact match?           "Crusader Squad" == "Crusader Squad" → match ✓

Input: "Initiate"

1. Manual override?       → no match
2. Exact match?           → no match ("Initiate w/Bolt Rifle" ≠ "Initiate")
3. Count-stripped match?  → no match
4. Fuzzy match?           → below threshold
5. Prefix match?          "Initiate w/Bolt Rifle".StartsWith("Initiate") + non-alphanumeric → match ✓
```

---

## Error Handling

| Layer | Behaviour |
|-------|-----------|
| Army list parser | Graceful fallback on missing/ambiguous metadata |
| Catalogue loading | Per-file failures logged as warnings; other catalogues continue to load |
| Unit resolution | Unresolved units logged as warnings; unit skipped in output |
| Model resolution | Warning only after all fallbacks (local exact → local fuzzy → local prefix → global exact → global fuzzy → global prefix) are exhausted |
| Weapon resolution | Ability-only entries (Shield Dome etc.) silently ignored; warning only for genuinely unresolvable entries |

---

## Configuration

| Setting | Mechanism | Default |
|---------|-----------|---------|
| Cache directory | `CatalogueFetcher` constructor parameter | `~/.wh40k-enricher/cache/` |
| Name overrides | `name_overrides.json` in **CLI working directory** | _(none — file is optional)_ |
| Fuzzy match threshold | Constant in `NameResolver` | `85` |
| Fuzzy match log level | Score-conditional in `NameResolver` | Info if ≥ 90, Warning if < 90 |
| Force cache refresh | `--refresh-cache` CLI flag | `false` |
| Logging level | DI setup in `Program.cs` | `Information` |

---

## Testing

Tests are organised into Parser (`ArmyListParserTests.cs` for iOS, `ArmyListParserAndroidTests.cs` for Android), BsData (`CatalogueParserTests.cs`, `NameResolverTests.cs`), and Integration (`EnrichPipelineTests.cs`). No live HTTP calls in unit tests — `ICatalogueFetcher` is mocked with Moq; XML fixture snippets are used for catalogue parser tests.

See CLAUDE.md § Testing for coverage requirements and specific assertions.

---

## External Dependencies

| Package | Used in | Purpose |
|---------|---------|---------|
| `System.CommandLine` | Cli | CLI argument parsing and subcommands |
| `YamlDotNet` 16.x | Core, Contracts | YAML serialisation/deserialisation |
| `FuzzySharp` | Core | Token-sort-ratio fuzzy name matching |
| `System.Xml.Linq` | Core | LINQ to XML (`XDocument`) for BSData XML parsing |
| `xunit` + `FluentAssertions` + `Moq` | Tests | Test framework and assertions |
