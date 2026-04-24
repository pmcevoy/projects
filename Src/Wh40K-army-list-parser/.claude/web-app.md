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

`UnitProfile.LeadingAbilities` (`List<AbilityProfile>`) — populated at enrichment time by `Enricher.cs`, filtering abilities whose text starts with `"While this model is leading a unit"` into a separate list. Displayed in `_UnitCard.cshtml` as a collapsible "While Leading" section styled in amber (`#f1a94e`). Not consumed by the simulation engine.

## Ability Rendering — Sub-Abilities

`AbilityProfile.Text` may contain `\n`-separated lines where sub-ability lines start with `• ` (U+2022 + space). These must not be rendered as a raw concatenated string.

**Rendering rules in `_UnitCard.cshtml`** (applies to both `Model.Abilities` and `Model.LeadingAbilities`):

- If `ability.Text` contains no `•` character: render as `<strong>Name:</strong> text` on a single line (existing flat layout).
- If `ability.Text` contains `•` lines: split on `\n`, then for each line:
  - Lines **not** starting with `• `: render as a plain paragraph (intro text, e.g. "At the start of your Command phase, select one of the following.")
  - Lines starting with `• `: parse as `• SubName: effect text` — render `SubName` as a sub-header (`<span class="sub-ability-name">`) and `effect text` on the next line (`<span class="sub-ability-text">`), wrapped in `<div class="sub-ability">`.

**Parsing the sub-ability line:** find the first `:` after position 2; name = `line[2..colonIdx].Trim()`; text = `line[(colonIdx+1)..].Trim()`.

**CSS classes required:**

```css
.sub-ability { margin: 0.25rem 0; }
.sub-ability-name { display: block; font-weight: bold; }
.sub-ability-text { display: block; margin-left: 0.25rem; color: var(--text-dim); }
```
