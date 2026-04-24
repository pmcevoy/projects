# Web Application

## Purpose

ASP.NET Core Razor Pages application. Live-game tool for phone/tablet use at the Warhammer 40K table.

## Pages

- `/` (Index) — paste two army list exports, submit to enrich
- `/ArmyView` — side-by-side unit cards, weapon selection, simulation panel

## Session Storage

Enriched armies stored in session as JSON using `SessionJson.Options` (`Helpers/SessionJson.cs`).

**Two non-obvious requirements:**
- `ScalarValueJsonConverter` is mandatory — `ScalarValue` has only private backing fields and serialises to `{}` by default; without it every `Attacks`/`Damage` value comes back as empty string and `DiceExpression.Parse` throws.
- `PropertyNameCaseInsensitive = true` — session data uses PascalCase. **Do NOT** set `PropertyNamingPolicy = CamelCase` on `SessionJson.Options` — that causes `WeaponAbilities` booleans (`LethalHits`, `DevastatingWounds`, etc.) to deserialise as `false`.

The `data-unit` HTML attribute in `ArmyView.cshtml` uses a **separate** `camelCaseJson` variable so JavaScript receives camelCase property names — independent of session serialisation.

**Session keys:**

| Key | Content |
|---|---|
| `attacker_army` | JSON-serialised `List<UnitProfile>` for the attacker |
| `defender_army` | JSON-serialised `List<UnitProfile>` for the defender |
| `used_catalogue_ids` | JSON-serialised `List<string>` of BSData catalogue IDs touched during enrichment |

## Leading Abilities

`UnitProfile.LeadingAbilities` (`List<AbilityProfile>`) — populated at enrichment time by `Enricher.cs`, filtering abilities whose text starts with `"While this model is leading a unit"` into a separate list. Displayed in `_UnitCard.cshtml` as a collapsible "While Leading" section styled in amber. Not consumed by the simulation engine.

## Ability Rendering — Sub-Abilities

`AbilityProfile.Text` may contain `\n`-separated lines where sub-ability lines start with `• ` (U+2022 + space). These must not be rendered as a raw concatenated string.

**Rendering rules in `_UnitCard.cshtml`** (applies to both `Model.Abilities` and `Model.LeadingAbilities`):

- If `ability.Text` contains no `•` character: render as `<strong>Name:</strong> text` on a single line (existing flat layout).
- If `ability.Text` contains `•` lines: split on `\n`, then for each line:
  - Lines **not** starting with `• `: render as a plain paragraph (intro text, e.g. "At the start of your Command phase, select one of the following.")
  - Lines starting with `• `: parse as `• SubName: effect text` — render `SubName` as a bold sub-header and `effect text` below it in dim text, indented slightly.

**Parsing the sub-ability line:** find the first `:` after position 2; name = `line[2..colonIdx].Trim()`; text = `line[(colonIdx+1)..].Trim()`.

---

## Layout & UI Design

Visual tokens (colours, typography, spacing intent) are in `@.claude/design-tokens.md`.

---

### Index Page

Centred, max-width container. Two side-by-side text areas for pasting army lists, a submit button below.

```
┌─────────────────────────────────────────────────┐
│  Wh40K Combat Simulator                         │
│  Paste two army list exports below...           │
│                                                 │
│  ┌───────────────────┐  ┌───────────────────┐  │
│  │ Attacker Army List│  │ Defender Army List │  │
│  │                   │  │                   │  │
│  │  (large textarea) │  │  (large textarea) │  │
│  │                   │  │                   │  │
│  └───────────────────┘  └───────────────────┘  │
│                                                 │
│  [error message if present]                     │
│                                   [ Enrich ]    │
└─────────────────────────────────────────────────┘
```

The textareas use monospace font — users are pasting app exports which are plain text with whitespace structure.

---

### Army View Page

Three stacked zones: a thin catalogue info bar at top, the main two-column unit list, and a sticky combat panel that appears at the bottom when units are selected.

```
┌──────────────────────────────────────────────────────────────┐
│ [badge: Space Marines rev 42] [badge: Chaos rev 17]  [↺ Re-download] │
├─────────────────────────────┬────────────────────────────────┤
│  Attacker                   │  Defender                      │
│  ┌─────────────────────┐    │  ┌─────────────────────┐       │
│  │ Unit Name  T4 Sv3+ W2 ▶ │    │ Unit Name  T5 Sv4+ W3 ▶│       │
│  └─────────────────────┘    │  └─────────────────────┘       │
│  ┌─────────────────────┐    │  ┌─────────────────────┐       │
│  │ Unit Name  T4 Sv3+ W2 ▶ │    │ Unit Name ...        ▶│       │
│  │ ─────────────────── │    │  └─────────────────────┘       │
│  │ (expanded body)     │    │                                │
│  └─────────────────────┘    │                                │
├──────────────────────────────────────────────────────────────┤
│ COMBAT PANEL (sticky, appears when attacker+defender chosen) │
└──────────────────────────────────────────────────────────────┘
```

---

### Unit Card

Cards default to collapsed — only the header is visible. Clicking the header toggles the body open. The header is a single dense line showing name and defensive stats; the body contains weapons and abilities.

**Collapsed:**
```
┌─ Unit Name ──────────────── T4  Sv3+  4++  W2  FNP5+++ ▶ ─┐
```

**Expanded (attacker side):**
```
┌─ Unit Name ──────────────── T4  Sv3+  4++  W2 ──────────── ▼ ─┐
│ INFANTRY, CORE, ADEPTUS ASTARTES                               │
│                                                                │
│ 4× Sword Brother                                               │
│ ┌──────────────────────┬─────┬───┬──────┬───┬─────┬───┬──────┐│
│ │ Weapon               │ Rng │ A │ BS/WS│ S │ AP  │ D │ Abil ││
│ ├──────────────────────┼─────┼───┼──────┼───┼─────┼───┼──────┤│
│ │ Master-crafted sword │Melee│ 4 │  3+  │ 5 │ -3  │ 2 │      ││  ← clickable row
│ │ Bolt pistol          │ 12" │ 1 │  3+  │ 4 │ 0   │ 1 │      ││  ← clickable row
│ └──────────────────────┴─────┴───┴──────┴───┴─────┴───┴──────┘│
│                                                                │
│ ▸ Abilities                                                    │
│ ▸ While Leading                                                │
└────────────────────────────────────────────────────────────────┘
```

Stat line: invulnerable save is in sky blue, FNP in green — visually distinct from the white armour save.

The weapon table is the main interaction surface on the attacker side. Each row is clickable; clicking it selects that weapon (highlights in a red tint). Clicking again deselects. Multiple rows can be selected. Once the first weapon is selected, its phase (Ranged/Melee) is locked — rows of the opposite type fade out and become unclickable.

On the defender side, clicking the card header selects it as the defender (blue left-border highlight). Clicking again deselects.

Abilities and "While Leading" are collapsed `<details>` sections. "While Leading" section header is amber to draw the eye — these abilities only apply when the unit is attached to another.

---

### Combat Panel

Appears at the bottom of the screen — sticky, scrollable if tall — once both an attacker weapon and a defender unit are selected. Rebuilt on every selection change; modifier values persist across rebuilds.

```
┌────────────────────────────────────────────────────┐
│  Sword Brethren  →  Death Guard                    │  ← panel header
│  ┌──────────────────────────────────────────────┐  │
│  │ Master-crafted sword (Sword Brother)  ×4 [4] │  │  ← weapon chip with model count
│  └──────────────────────────────────────────────┘  │
│                                                    │
│  ▶ Attack   [½ Range · RR All]                     │  ← collapsed modifier section
│  ▶ Hit      [active summary]                       │     summary shows active modifiers
│  ▶ Wound                                           │
│  ▶ Save                                            │
│  ▶ Damage                                          │
│                                                    │
│  Surviving defenders (0 = use profile): [  0  ]   │
│                                                    │
│                  [ Run Simulation ]                │
│                                                    │
│  ┌──────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐  │
│  │ 7.24 │ │   3.61   │ │  94.2%   │ │   2.1    │  │  ← stat tiles
│  │ Dmg  │ │ Exp.Kill │ │ P(≥1kil) │ │ Std Dev  │  │
│  └──────┘ └──────────┘ └──────────┘ └──────────┘  │
│                                                    │
│  Attack pipeline...                                │
└────────────────────────────────────────────────────┘
```

**Modifier sections** are collapsible `<details>` panels. Their open/closed state persists across panel rebuilds (stored in JS module state). Each header shows a live one-line summary of which modifiers are active in that section (amber text), so the user can see at a glance what's configured without opening every section.

**Weapon chips** show the weapon name, variant, model name, and an editable model count. The model count defaults to the number of models of that type in the unit.

**Run Simulation** posts to `/api/simulate` and injects results below the button without a page reload. The button disables during the request.

---

### Modifier Controls

Two control shapes are used throughout:

**Toggle button** — for on/off abilities (Blast, Lethal Hits, Cover, etc.). Inert/dim by default; filled blue when active.

**Step control** — for numeric modifiers (±Attacks, ±Hit, ±AP, etc.). Shows `−  +1  +` with − and + buttons either side. Hit and wound roll modifiers are capped at ±1 by the control itself.

One special case: "Fish for Crits" toggles (on hit and wound) are only shown when their parent "Reroll All" toggle is active.

---

### Attack Pipeline (Simulation Results)

Displayed below the Run button after a successful simulation. A funnel table showing average values at each stage of the attack sequence.

**Single weapon group:** full funnel with sub-rows for detail stages (crit hits, sustained bonus, lethal auto-wounds, save type breakdown, FNP saved), and a conversion-rate column showing the ratio to the previous stage.

**Multiple weapon groups:** one labelled full-funnel section per weapon group, then a compact combined summary (main stages only) at the bottom.

```
Stage           Avg    Rate
─────────────────────────────
Attacks         8.00
Hits            5.33   66.7%
  ↳ Crit hits   1.33
Wounds          2.67   50.0%
Failed saves    2.22   83.3%
  ↳ Armour sv   2.22
Dmg pre-FNP     4.44
──────────────────────────────
Final Damage    4.44          ← accent colour
```

The "Final Damage" row is styled in the accent colour to draw the eye to the headline result.
