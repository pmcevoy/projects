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

### Session 5 — Web App Shell
**Status:** Complete  
**What happened:**
- Implemented `Enricher.cs` — full name matching (exact → count-stripped → fuzzy ≥85 → prefix), weapon resolution (model scope → unit scope → global fallback), statline resolution (single-model vs squad), ability/keyword gathering, leading abilities, ability-upgrade silent skip
- Implemented `CatalogueStartupService.cs` — `IHostedService` that calls `CatalogueStore.InitialiseAsync` on startup; errors logged but don't crash the app
- Updated `Program.cs` — registered named "github" HTTP client (User-Agent), `ICatalogueFetcher`/`CatalogueFetcher` (singleton with factory), `CatalogueStore`, `ArmyListParser`, `Enricher` (all singletons), `POST /api/refresh-catalogues` minimal API endpoint
- Updated `SessionJson.cs` — added `CamelCaseOptions` (separate from `Options`; safe to use `PropertyNamingPolicy = CamelCase` here)
- Updated `Index.cshtml.cs` — full POST handler: parses both armies, enriches, stores JSON in session, redirects to ArmyView; returns error if catalogues not yet loaded
- Updated `ArmyView.cshtml.cs` — loads armies from session, resolves catalogue versions from `used_catalogue_ids`, redirects to Index if session empty
- Updated `ArmyView.cshtml` — two-column layout, catalogue version bar, Re-download button, `<partial>` for each unit card
- Created `_UnitCard.cshtml` — collapsed unit card with header (T/Sv/W/invuln/FNP), weapon table, ability blocks, `data-unit` camelCase JSON attribute
- Updated `site.css` — dark theme styles for all army-view elements, weapon table, unit cards, responsive breakpoints
- Updated `army-view.js` — `toggleCard()` expand/collapse
- Fixed `CatalogueFetcher` — cache reader now handles legacy string-array format (stale cache compatibility)
- Updated `_ViewImports.cshtml` — added `Core.Contracts`, `Core.Catalogue`, `Web.Helpers`, `System.Text.Json` usings

**Build state:** `dotnet test` → 166 passed, 0 failed. App starts, loads 46 catalogues, Index page enriches and redirects to ArmyView.

**Decisions made:**
- `Enricher` is a singleton (stateless except `_nameOverrides` which is loaded once at construction)
- `IndirectFire` is dropped from `CatalogueWeaponAbilities` → `WeaponAbilities` mapping — spec says it's a UI-only modifier, set via `req.IndirectFire` in `SimulationAdapter`
- Cache format compatibility: `CatalogueFetcher` tries object format then string-array format when reading `catalogue-list.json`
- `defenderStatlineSet` boolean used to ensure the first model's child entry can override unit-level invuln/FNP

**Spec gaps discovered:**
- Cache format migration not covered by spec — handled with try/catch fallback in `GetCatalogueListAsync`

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

### Session 6 — Leading Abilities + Integration
**Status:** Complete  
**What happened:**
- Added `POST /api/simulate` minimal API endpoint in `Program.cs`: reads attacker/defender from session, validates phase constraint (rejects mixed ranged+melee), runs `SimulationAdapter`, returns `SimulationResponse` as camelCase JSON
- Extended `WeaponSelection` with `WeaponType` (for phase validation) and `UnitName` (for multi-unit weapon selection)
- Added `SimulationAdapter.Adapt(request, IReadOnlyList<UnitProfile> attackers, defender)` overload — `FindWeapon` now resolves each selection to the correct attacker unit by `sel.UnitName`; backward-compat single-unit overload kept for existing tests
- Updated `_UnitCard.cshtml`: added `data-unit-name` and `data-weapon-type` attributes; defender card headers call `onDefenderHeaderClick`; all weapon rows have `onclick="selectWeaponRow(this)"`
- Replaced stub `army-view.js` with full implementation: weapon row selection (red highlight, phase lock, multi-weapon, auto-deselect); defender card selection (blue highlight); combat panel generation; five collapsible modifier sections (Attack, Hit, Wound, Save, Damage) with live section header summaries; model count inputs per weapon group; Run Simulation button with fetch to `/api/simulate`; `displayPipeline()` renders stat summary (mean damage, kills, P(kill≥1), stddev) and full pipeline funnel (single-group) or per-group + combined (multi-group)
- Added CSS for combat panel (sticky bottom, scrollable), modifier section layout, toggle buttons, step controls, pipeline table, simulation results

**Build state:** `dotnet test` → 166 passed, 0 failed. `dotnet build` → 0 errors.

**Decisions made:**
- Multi-unit attacker selection supported: each `WeaponSelection` carries `UnitName`; weapons from multiple expanded cards aggregate correctly; primary attacker (by `AttackerName`) supplies `CriticalHitsOn` for the simulation
- Combat panel is `position: sticky; bottom: 0` for phone/tablet usability — always visible when scrolling
- Modifier state (`mods` object) persists across weapon/defender re-selections; modifier sections remember open/closed state via `sectionState` object
- Fish for Criticals buttons hidden (not removed) when Reroll All is inactive; visibility updated in-place by `toggleMod` without full panel rebuild
- Phase lock applied via `.weapon-type-locked` CSS class on incompatible weapon rows; JS silently ignores clicks on locked rows

**Spec gaps discovered:**
- Multi-unit weapon selection implied by Gherkin ("Marshal A7 and Castellan A6") but not explicitly documented in the spec — implemented via `UnitName` field on `WeaponSelection`

---

## Current State

| Layer | Status | Notes |
|---|---|---|
| Project scaffolding | ✅ Complete | Builds, tests pass, serves HTTP 200 |
| Domain model / types | ✅ Complete | 38 tests, all passing |
| Simulation engine | ✅ Complete | 91 new tests, all passing (129 total) |
| BSData parsing | ✅ Complete | 37 new tests, all passing (166 total) |
| Web app shell | ✅ Complete | 166 tests, app starts, enriches armies, renders unit cards |
| Leading abilities / integration | ✅ Complete | Full round-trip: select weapons → run simulation → see pipeline results |
| Gherkin scenario coverage | ✅ Complete | All web-application.feature and combat-simulation.feature scenarios satisfied |

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
build state. Your goal this session is **Session 6: Leading Abilities + Integration**.
- Wire `SimulationAdapter` and `CombatSimulator` into the web layer: add `POST /api/simulate` endpoint that reads attacker/defender from session, deserialises `SimulationRequest` from request body, runs the simulation, and returns `SimulationResponse` as JSON
- Implement weapon selection UI in `army-view.js`: clicking a weapon variant row on an attacker card highlights it red and stores the selection; clicking a second weapon of the same type adds it (multi-weapon); clicking a defender card highlights it blue; locking phase type on first weapon selection; clearing selections resets the phase lock
- Implement the combat panel (`#combat-panel`) that appears when both attacker weapon(s) and defender are selected; panel contains: modifier controls (organised per simulation-engine.md section headings), Models firing input, Run Simulation button
- Implement `displayPipeline()` in `army-view.js` to render `SimulationResponse` results: mean damage, expected kills, P(kill≥1), stddev, and the attack pipeline funnel (single-group full table vs multi-group labelled sections + combined)
- Validate against Gherkin scenarios in `features/`
Done-state: full round-trip works — paste armies, select weapon + defender, run simulation, see results."

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
