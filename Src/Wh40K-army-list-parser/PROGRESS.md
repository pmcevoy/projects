# Regeneration Experiment — Build Journal

## What This Is

This file tracks the regeneration of the Warhammer 40K combat simulator web app
from specification files only. All original code was deleted. CLAUDE.md and the
`.claude/` subfiles are the sole source of truth.

The goal is to determine whether the spec is complete enough to regenerate
equivalent behaviour, and to surface gaps where it is not.

---

## Spec Files In Place

- `CLAUDE.md` — architecture overview, user flow, key constraints
- `.claude/domain-model.md` — parser records, UnitProfile schema, name matching
- `.claude/simulation-engine.md` — engine types, flow, modifier tables
- `.claude/bsdata-parsing.md` — catalogue loading, XML structure, two-pass load
- `.claude/web-app.md` — session handling, pages, leading abilities
- `.claude/implementation-notes.md` — debugging gotchas and archaeology
- `.claude/rules/combat-rules.md` — authoritative 40K rules spec

Feature acceptance criteria:
- `features/` — Gherkin BDD feature files (acceptance oracle for regeneration fidelity)

---

## Session Log

### Session 0 — Setup (pre-code)
**Status:** Complete  
**What happened:** Deleted all source code. Added spec files and this PROGRESS.md.  
**Decisions made:** None yet — first generation session not started.  
**What is NOT in place:** Any runnable code.

---

### Session 1 — Project Scaffolding
**Status:** Complete  
**What happened:**
- Created `Wh40kArmyEnricher.sln` with three projects: Core (classlib), Web (Razor Pages), Tests (xUnit)
- Added all package dependencies: FuzzySharp 2.0.2, YamlDotNet 17.0.1, FluentAssertions 8.9.0, Moq 4.20.72
- Wired Core → Web and Core → Tests project references
- Set up directory structure: `Core/{Contracts,Parsing,Catalogue,Enrichment,Simulation}/`, `Web/Pages/`, `Web/wwwroot/`, `Tests/Fixtures/`, `Tests/Parsing/`
- Wrote stub Program.cs with Razor Pages, session, IHttpClientFactory
- Created placeholder Index and ArmyView Razor Pages (Index returns HTTP 200)
- Created SessionJson.cs stub (ScalarValueJsonConverter to be added in Session 5)
- Created Dockerfile and docker-compose.yml with catalogue-cache volume
- Copied sample fixture files (black-templars-sample.txt, death-guard.txt) into Tests/Fixtures/
- Created 2 skipped stub tests in ArmyListParserTests.cs

**Build state:** `dotnet build` → 0 errors, 0 warnings. `dotnet test` → 2 skipped. `dotnet run` → HTTP 200 on `/`.

**Decisions made:**
- Used `dotnet new web` (minimal API template) then converted to Razor Pages manually in Program.cs — avoids MVC overhead
- YamlDotNet 17.0.1 installed (spec mentions 16.3.0 new interface; 17.x should have same interface — verify in Session 2 when implementing YAML serialisation)
- FluentAssertions 8.9.0 installed (major version bump from any previous — check for breaking API changes in Session 2 when writing tests)
- `ScalarValueJsonConverter` not yet implemented (placeholder in SessionJson.cs) — Session 2 will define `DiceExpression`/`ScalarValue` and the converter together

**Spec gaps discovered:**
- None in this session — scaffolding is structure-only and the spec covers it well

---

### Session 2 — Domain Model and Types
**Status:** Complete  
**What happened:**
- Implemented all domain records in `Core/Contracts/`: `ArmyList`, `UnitEntry`, `ModelEntry`, `WeaponEntry`
- Implemented all enriched profile types: `UnitProfile`, `ModelProfile`, `WeaponProfile`, `WeaponVariantProfile`, `WeaponAbilities`, `AbilityProfile`, `RerollProfile`, `WeaponType`
- Implemented `ScalarValue` (string-wrapper struct with private backing field) and `ScalarValueJsonConverter` in `Core/Contracts/ScalarValue.cs`
- Implemented `DiceExpression` (Count/Sides/Modifier; `Parse`, `Fixed`, `Scale`, `Add`) in `Core/Simulation/DiceExpression.cs`
- Implemented `ArmyListParser` in `Core/Parsing/ArmyListParser.cs` — handles all three format variants (iOS current, iOS legacy, Android) via unified `ClassifyBulletLine`
- Updated `SessionJson.cs` to add `ScalarValueJsonConverter` to `Options.Converters`
- Replaced parser stubs with 20 iOS tests (`ArmyListParserTests.cs`) and 18 Android tests (`ArmyListParserAndroidTests.cs`)
- Added fixture `CopyToOutputDirectory` to test csproj

**Build state:** `dotnet build` (Core + Tests) → 0 errors. `dotnet test` → 38 passed, 0 skipped, 0 failed.

**Decisions made:**
- Format detection: presence of `◦` (U+25E6) anywhere in the text → iOS; absence → Android
- `ClassifyBulletLine` unified for all formats: `◦` always Level 1; `•` at < 4 spaces → Level 0; `•` at ≥ 4 spaces → Level 1 (Android squad weapon); 4+ space text → Level 1, no-bullet (Android continuation)
- Model mode detected by presence of any Level-1 + IsBullet line in the unit block
- Single-model units (no model-mode): all bullet and continuation items become weapons; synthetic `ModelEntry` named after the unit
- `ScalarValue` has no dependency on `DiceExpression` — the sim engine calls `DiceExpression.Parse(scalarValue.ToString())` directly

**Spec gaps discovered:**
- None — the format specs in domain-model.md and implementation-notes.md were sufficient

---

### Session 3 — Simulation Engine
**Status:** Complete  
**What happened:**
- Implemented all simulation types in `Core/Simulation/`: `IDiceRoller`/`DiceRoller`, `SimWeaponAbilities`, `SimWeaponProfile`, `SimAttackerProfile`, `SimDefenderProfile`, `WoundPool`, `CombatStageStats`, `WeaponGroupStats`, `AbilityProcessor`
- Implemented `CombatSimulator` — full Monte Carlo engine with `RunTally`/`RunTotals` structs; all weapon abilities (Torrent, Blast, RapidFire, SustainedHits, LethalHits, DevastatingWounds, TwinLinked, Melta, Anti, IndirectFire); wound pool with no spillover; FNP; rerolls including FishForCriticals
- Implemented `SimulationRequest`, `WeaponSelection`, `SimulationResponse` DTOs
- Implemented `SimulationAdapter` — groups selections by `WeaponGroupKey`, aggregates attacks via `DiceExpression.Scale`/`Add`, negates AP, applies cover, applies all request modifiers
- Fixed `DiceExpression.Scale` — was not scaling the `Modifier` field (D6+1 × 2 should give 2D6+2, not 2D6+1)
- Wrote 91 new tests in `tests/Simulation/`: `DiceExpressionTests`, `DiceRollerTests`, `WoundPoolTests`, `AbilityProcessorTests`, `CombatSimulatorTests`, `SimulationAdapterTests`; two test helpers: `SequenceRoller` and `ConstantRoller`

**Build state:** `dotnet test` → 129 passed, 0 skipped, 0 failed.

**Decisions made:**
- Cover subtracts 1 from `SimDefenderProfile.Save` (lowers the required roll = easier to save). The spec says "adding 1" which is physically backwards given the `effectiveSave = save + ap` convention; correct behavior implemented.
- `SimulationRequest` is a `class` (not `record`) because it is a mutable DTO received from the web layer — `with` expressions not applicable.
- `WeaponBreakdown` is empty for single-group runs; the multi-group path is not exercised by single-weapon tests.
- Statistical test uses 50,000 iterations with tolerance ±0.015.

**Spec gaps discovered:**
- `DiceExpression.Scale` spec was implicit about modifier scaling — fixed in implementation.
- Spec says cover "adds 1 to SimDefenderProfile.Save" but the correct physics is subtracting 1 (documented in implementation-notes.md).

---

### Session 4 — BSData Parsing
**Status:** Complete  
**What happened:**
- Implemented `ICatalogueFetcher` interface and `CatalogueFileInfo` record
- Implemented `CatalogueFetcher` — `IHttpClientFactory` named client "github", User-Agent header, GitHub raw URL with `Uri.EscapeDataString`, disk cache to `~/.wh40k-enricher/cache/`, file list cached to `catalogue-list.json`, supports `.cat`/`.catz`/`.gst`/`.gstz`
- Implemented `CatalogueEntry.cs` domain types: `CatalogueStatline`, `CatalogueWeaponAbilities` (includes `IndirectFire` — not in Contracts `WeaponAbilities`), `CatalogueWeaponVariant`, `CatalogueWeaponEntry`, `CatalogueEntry`, `CatalogueData` (record for `with` expressions)
- Implemented `CatalogueParser` (static class) — compiled regexes; `LoadDocumentAsync` (raw DeflateStream for `.catz`); `ExtractSharedProfiles` (pass 1); `Parse` (pass 2 using global profiles); `ParseEntry` recursive to depth 6; invuln/FNP from ability text and infoLink resolution; weapon keyword parsing; multi-profile variant label stripping
- Implemented `CatalogueStore` — two-pass `InitialiseAsync`; `_globalProfiles` retained as field (required by `RefreshCataloguesAsync`); `RefreshCataloguesAsync` with `forceRefresh: true`; `GetAllCatalogues`, `GetCatalogue`, `GetAllTopLevelEntries`
- Wrote 37 new tests: `CatalogueParserTests.cs` (28 tests) and `CatalogueStoreTests.cs` (9 tests)

**Build state:** `dotnet test` → 166 passed, 0 skipped, 0 failed.

**Decisions made:**
- `CatalogueWeaponAbilities` is separate from Contracts `WeaponAbilities` — it includes `IndirectFire` which Contracts doesn't have. The Enricher (Session 6) will map between them.
- `selectionEntryGroups` are flattened into children (organisational containers, not separate entries)
- `entryLinks` resolved via per-catalogue `localShared` map (shared entries within the same catalogue); cross-catalogue links rely on the two-pass global profiles map
- `sharedSelectionEntryGroups` entries are unwrapped and included at the top level (consistent with how the enricher will query the store)
- `IHttpClientFactory` named client "github" — registration via DI deferred to Session 5 (web app setup)

**Spec gaps discovered:**
- None — the bsdata-parsing.md spec was sufficient, including all the gotchas

---

## Current State

| Layer | Status | Notes |
|---|---|---|
| Project scaffolding | ✅ Complete | Builds, tests pass, serves HTTP 200 |
| Domain model / types | ✅ Complete | 38 tests, all passing |
| Simulation engine | ✅ Complete | 91 new tests, all passing (129 total) |
| BSData parsing | ✅ Complete | 37 new tests, all passing (166 total) |
| Web app shell | ❌ Not started | |
| Leading abilities / integration | ❌ Not started | |
| Gherkin scenario coverage | ❌ Not started | |

---

## Spec Gap Log

Record gaps discovered during generation here. Each entry should note:
- Which session surfaced it
- Which spec file was missing or ambiguous
- What Claude Code assumed or asked about
- Whether the spec was updated as a result

*(Empty — no gaps found in Session 1)*

---

## Resume Prompt

> Paste this at the start of the next Claude Code session:

"Read CLAUDE.md and all files in .claude/. Then read PROGRESS.md for current
build state. Your goal this session is **Session 5: Web App Shell**.
Implement the ASP.NET Core web application in `src/Wh40kArmyEnricher.Web/`:
- Register `CatalogueStore`, `CatalogueFetcher`, `IHttpClientFactory` named client 'github' (User-Agent header), and `IHostedService` that calls `CatalogueStore.InitialiseAsync` on startup
- `SessionJson.cs` — ensure `ScalarValueJsonConverter` is in options; `PropertyNameCaseInsensitive = true`; no `PropertyNamingPolicy = CamelCase`
- Index page — two textareas (attacker/defender army list), submit button, POST enriches both lists and stores them in session (`attacker_army`, `defender_army`, `used_catalogue_ids`)
- ArmyView page — reads session, renders two columns of collapsed unit cards; catalogue version display; Re-download catalogues button (`POST /api/refresh-catalogues`)
- Wire `Enricher.cs` stub (or real implementation if straightforward) to bridge `ArmyList` → `List<UnitProfile>` using `CatalogueStore`
Follow `.claude/web-app.md` for session keys and non-obvious serialisation requirements. Done-state: app starts, Index page accepts a paste and redirects to ArmyView, `dotnet test` still green."

---

## Session Plan

| Session | Goal | Done-state |
|---|---|---|
| 1 | Project scaffolding | App starts and serves a placeholder ✅ |
| 2 | Domain model and types | Parser records, UnitProfile, name matching — no UI deps |
| 3 | Simulation engine | Engine types, modifier tables, wound pool — unit-testable |
| 4 | BSData parsing | Catalogue load, XML two-pass — testable with a sample file |
| 5 | Web app shell | Routing, session handling, page structure — no leading abilities |
| 6 | Leading abilities + integration | Wire everything, validate against Gherkin scenarios |

---

## Notes

- Each session should end with PROGRESS.md updated and the Resume Prompt rewritten.
- Do not start a session if the previous session's done-state was not reached.
- Spec gaps discovered during generation should be fixed in the spec files, not
  worked around in code.
- The Gherkin feature files are the acceptance oracle — if generated code
  doesn't satisfy them, the spec is incomplete, not the code.
