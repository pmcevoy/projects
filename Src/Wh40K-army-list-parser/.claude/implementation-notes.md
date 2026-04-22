# Implementation Notes & Gotchas

Defensive knowledge accumulated during development. Import this file when debugging or working on the relevant subsystem.

---

## AP Sign Convention

`WeaponVariantProfile.Ap` in Contracts is stored as a **negative integer** (e.g. AP-2 → `-2`). `SimulationAdapter` negates it when building `SimWeaponProfile.Ap` because the simulator's `AbilityProcessor.EffectiveSave` does `save + ap` (expects a positive value). Do not change this without updating both sides.

---

## Session JSON Serialisation

- `ScalarValueJsonConverter` is mandatory — without it `DiceExpression` fields serialise to `{}` and `DiceExpression.Parse` throws on deserialisation.
- `PropertyNamingPolicy = CamelCase` must **not** be set on `SessionJson.Options` — it causes all `WeaponAbilities` booleans to deserialise as `false`.
- The `data-unit` attribute on `ArmyView.cshtml` uses a separate `camelCaseJson` variable for JavaScript. This is independent of session serialisation.

---

## BSData XML Parsing

- All `typeName` comparisons (e.g. `"Ranged Weapons"`, `"Unit"`) **must use `StringComparison.OrdinalIgnoreCase`** — case variation has been observed in the wild and silently drops profiles if compared with `==`.
- `.catz` files are raw deflate compressed — use `DeflateStream`. **Not** `ZLibStream` or `GZipStream`.
- `<selectionEntryGroups>` must be traversed recursively. Double nesting exists in practice (e.g. Repulsor Executioner → Wargear → Turret Weapon → Heavy Laser Destroyer). Stop at depth 6.
- `_globalProfiles` must be retained as a field on `CatalogueStore` after `InitialiseAsync` completes — `RefreshCataloguesAsync` needs it. Do not scope it to `InitialiseAsync` only.
- GitHub raw URL: spaces in filenames must be `%20` — use `Uri.EscapeDataString(filename)`.
- Set a `User-Agent` header on all GitHub API requests — the API rejects requests without one.
- **Do not** use the GitHub Commits API for staleness checking — aggressively rate-limited.

---

## Single-Model Unit Ability Upgrades

For single-model units (`type="model"`, e.g. Impulsor), `unitEntry.Statline` is non-null, so `defenderStatline` is pre-initialised before the model loop. A null-check guard (`if defenderStatline == null`) skips the update entirely, losing ability upgrades (e.g. Shield Dome → 5+ invuln) applied inside the loop. Use a `defenderStatlineSet` boolean flag instead.

---

## Army List Parser — iOS Current Format

`◦` (U+25E6) is **always** a weapon regardless of indent depth. Check for it before the indent-based `•` branching in `ClassifyBulletLine`. Failing to do this causes weapons to be misclassified as model entries on deeply indented lines.

---

## Android Detachment Field

The detachment line appears **after** the force-size line in Android exports (unlike iOS where it appears before). After consuming the force-size line, if `detachment` is still empty, scan forward for the next non-empty, non-points-header line.

---

## Fuzzy Name Matching

Log fuzzy matches at `Information` level for scores ≥ 90, `Warning` level for scores 85–89. Include input name, matched BSData name, and score in all cases. Consider writing a `resolution_report.json` alongside the main output for review.

---

## Non-Weapon Army List Entries

Ability upgrades (e.g. "Shield Dome", "Icon of Despair") appear alongside weapons in the army export using the same bullet characters. If an army list item resolves to an entry with no weapon profiles, treat it silently — do not emit a warning. The ability-only check must match **either** the catalogue entry name **or** any ability profile name within that entry (e.g. entry `"Icon of Despair"` contains profile `"Icon of Despair (Aura)"`).

---

## Static Classes and ILogger

Static classes cannot be used as type parameters for `ILogger<T>`. Use `ILoggerFactory.CreateLogger("Name")` for loggers inside static classes.

---

## Cover and SimDefenderProfile.Save

Cover in 40K adds +1 to the defender's armour save roll (i.e. the die result is easier to meet the threshold). In `SimulationAdapter`, this is implemented by **subtracting 1 from `SimDefenderProfile.Save`** before the run, which lowers the required roll from `effectiveSave = save + ap`.

The spec document says "adding 1 to SimDefenderProfile.Save" which is physically backwards given the `effectiveSave = save + ap` convention (a higher Save value means a harder save). Do **not** add 1; subtract 1.

Cover does not affect invulnerable saves.

---

## name_overrides.json

Must be present in the **current working directory** when the web app starts. Example entry: `{ "Deathshroud Champion": "Deathshroud Terminator Champion" }`. The file is optional — if absent, resolution proceeds without overrides.

---

## Multi-Profile Weapon Variant Labels

BSData prefixes variant profiles with `➤ ` followed by the weapon entry name and ` - variantname`. Strip `➤ ` and the weapon entry name prefix to derive the variant label. Example: `"➤ Hellforged weapons - strike"` → `"strike"`.
