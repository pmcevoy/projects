# Architecture — Wh40kArmyEnricher

## Overview

A .NET 8 CLI tool that parses Warhammer 40,000 (10th Edition) army list text exports, resolves each unit and weapon against the [BSData/wh40k-10e](https://github.com/BSData/wh40k-10e) catalogue files, and enriches the list with full statlines and weapon profiles. Output is YAML compatible with a separate Monte Carlo simulation project.

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

**Key design decisions in the schema:**
- `ap` is a negative integer (e.g. `-2`) matching the actual game value.
- `save`, `skill`, `invulnerableSave`, `feelNoPain` are raw integers without the `+` suffix.
- `weapons` is always a list, even for single-weapon models.
- Rerolls live at the unit level, not per weapon.
- `range: 0` means melee; `range: N` means N inches.
- `rapidFire: 0` and `melta: 0` are sentinels meaning "not present".

---

### `Wh40kArmyEnricher.Core`

The core library. Contains all parsing, resolution, and enrichment logic.

#### `Parser/ArmyListParser.cs`

Parses the plain-text export from the Warhammer app into an `ArmyList` record.

Input format key points:
- Line 0: `Army Name (N Points)` — case-insensitive on "Points"
- Metadata block: game system, faction, detachment (1–4 lines; sub-factions have more)
- Force-size lines (`N Points`) appear in the metadata and are consumed, not treated as unit headers
- Sections: `CHARACTERS`, `BATTLELINE`, `DEDICATED TRANSPORTS`, `OTHER DATASHEETS`, etc.
- Unit: `Name (N Points)`
- `•` (U+2022) bullets: model entries or weapons
- `◦` (U+25E6) bullets: weapons or ability upgrades (e.g. Shield Dome)
- `4x` count prefixes on models and weapons
- U+2019 RIGHT SINGLE QUOTATION MARK in names like "Emperor's Champion"

Parsing state machine:
1. Extract army header
2. Consume metadata block (detect and skip force-size lines)
3. For each section → for each unit → collect bullet lines
4. Detect bullet mode:
   - **Model mode**: `•` introduces a model, `◦` are that model's weapons
   - **Weapon mode**: both `•` and `◦` are weapons of an implicit single model

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

Parses BSData `.cat` (plain XML) and `.catz` (raw deflate-compressed XML) files.

XML namespace: `http://www.battlescribe.net/schema/catalogueSchema`

Entry collection strategy — parses all of the following at every depth level:
- `<selectionEntries>` and `<sharedSelectionEntries>` — unit, model, upgrade entries
- `<sharedSelectionEntryGroups>` — wargear option groups (weapon choices, ability upgrades)
- `<entryLinks>` — faction-specific overrides pointing to entries in parent catalogues
- `<selectionEntryGroups>` nested recursively within entries (stops at depth 6)

Profile extraction per entry:
- `typeName="Unit"` → statline: M, T, Sv, W, Ld, OC
- `typeName="Ranged Weapons"` → Range, A, BS, S, AP, D, Keywords
- `typeName="Melee Weapons"` → Range (always "Melee"), A, WS, S, AP, D, Keywords
- `typeName="Abilities"` → Name + Description text
- `<categoryLink>` → unit keywords (INFANTRY, CORE, etc.)
- Ability text is scanned for `\d\+\+(?!\+)` (invuln) and `\d\+\+\+` (FNP) and applied to the statline

`.catz` decompression:
```csharp
using var deflate = new DeflateStream(rawStream, CompressionMode.Decompress);
var doc = await XDocument.LoadAsync(deflate, ...);
```

#### `BsData/CatalogueStore.cs`

Eagerly loads all BSData catalogues into memory on startup.

Initialization sequence:
1. Load game system root (`Warhammer 40,000.gst`)
2. Fetch file listing from GitHub Contents API → cache to `catalogue-list.json`
3. Download and parse all ~46 `.cat` files (~35 MB total)
4. Hold all entries in memory for the duration of the run

Public API:
- `InitialiseAsync()` — trigger eager load (idempotent)
- `GetAllEntries()` — all entries across all catalogues
- `GetAllEntriesOfType(type)` — filtered by entry type (`"unit"`, `"model"`, `"upgrade"`)

Individual catalogue failures are logged as warnings; loading continues.

#### `BsData/NameResolver.cs`

Matches army list display names to BSData catalogue entries using multi-pass resolution.

Resolution pipeline (applied in order):

| Step | Strategy |
|------|----------|
| 1 | **Manual override** — `name_overrides.json` in working directory |
| 2 | **Exact match** — case-insensitive `OrdinalIgnoreCase` |
| 3 | **Count-stripped match** — strip `\d+x\s+` prefix, then exact match |
| 4 | **Fuzzy match** — FuzzySharp `TokenSortRatio ≥ 85`; logged at Warning level |
| 5 | **Prefix match** — for model variants (e.g. "Initiate" matches "Initiate w/Bolt Rifle") |

Resolution scopes:
- **Unit**: all `type="unit"` entries + all `type="model"` entries with a statline, across all catalogues
- **Model**: child entries of the matched unit first (with prefix fallback); then global `type="model"` entries
- **Weapon**: profile-name search within unit/model scope first, then globally; fallback to entry-name search (for multi-profile weapons like "Hellforged weapons" whose profiles are named "- strike" / "- sweep")

Non-weapon entries (ability upgrades like Shield Dome) resolve to entries with no weapon profiles and are silently ignored.

"Could not resolve" warnings are only emitted after all fallbacks are exhausted.

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

---

### `Wh40kArmyEnricher.Cli`

#### `Program.cs`

DI container setup and CLI root command registration.

Services registered as singletons:
- `IHttpClientFactory` with named `"bsdata"` client (User-Agent set)
- `CatalogueParser`, `ICatalogueFetcher` → `CatalogueFetcher`, `NameResolver`, `Enricher`

`CatalogueStore` is **not** pre-registered — it is instantiated per command so the `--refresh-cache` flag can be passed at construction time.

#### `Commands/EnrichCommand.cs`

`enrich <army-list.txt> [--output <path>] [--refresh-cache] [--dry-run]`

Enriches a single army and writes a flat YAML list of `UnitProfile` objects — one per unit.

#### `Commands/MatchupCommand.cs`

`matchup <attacker.txt> <defender.txt> [--output <path>] [--refresh-cache] [--attacker-unit <name>] [--defender-unit <name>]`

Enriches two armies, applies optional unit name filters, and writes a `PairingFile` containing all attacker/defender combinations (Cartesian product of filtered units).

`simulationId` generation: `{factionAbbrev}_{unitSlug}_vs_{factionAbbrev}_{unitSlug}`
e.g. `bt_crusader_squad_vs_dg_plague_marines`

---

## Data Flow

### `enrich` command

```
Text file
  → ArmyListParser.Parse()          — produces ArmyList
  → CatalogueStore.InitialiseAsync() — downloads/caches ~46 .cat files
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
| Name overrides | `name_overrides.json` in working directory | _(none)_ |
| Fuzzy match threshold | Constant in `NameResolver` | `85` |
| Force cache refresh | `--refresh-cache` CLI flag | `false` |
| Logging level | DI setup in `Program.cs` | `Information` |

---

## Testing

| Category | Location | Approach |
|----------|----------|---------|
| Parser unit tests | `tests/.../Parser/` | Assert section categorisation, model counts, weapon names, enhancements against sample `.txt` fixture |
| Catalogue parser tests | `tests/.../BsData/CatalogueParserTests.cs` | XML snippet fixtures; no live HTTP calls; `ICatalogueFetcher` mocked with Moq |
| Resolver unit tests | `tests/.../BsData/NameResolverTests.cs` | Exact, count-stripped, fuzzy, prefix, override, not-found |
| Integration tests | `tests/.../Integration/EnrichPipelineTests.cs` | Full enrichment pipeline against sample Black Templars export with live (or recorded) BSData fetch |

---

## External Dependencies

| Package | Used in | Purpose |
|---------|---------|---------|
| `System.CommandLine` | Cli | CLI argument parsing and subcommands |
| `YamlDotNet` 16.x | Core, Contracts | YAML serialisation/deserialisation |
| `FuzzySharp` | Core | Token-sort-ratio fuzzy name matching |
| `System.Xml.Linq` | Core | LINQ to XML (`XDocument`) for BSData XML parsing |
| `xunit` + `FluentAssertions` + `Moq` | Tests | Test framework and assertions |
