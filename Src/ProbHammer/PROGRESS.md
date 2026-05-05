# Work Journal

## Active Work

Nothing in progress.

---

## Recently Completed

- AP sign convention unified across the sim engine. `SimWeaponProfile.Ap` is now a negative
  integer matching the game value (AP-2 → -2). `AbilityProcessor.EffectiveSave` and
  `CombatSimulator` both use `save - ap`. `SimulationAdapter` passes AP through unchanged
  (removed the negation and `Math.Max(0,...)` guard). All tests updated; 2 new Gherkin tests
  added for the AP-2 → 5+ save boundary. 172 tests, all passing.
- Sub-ability rendering implemented in `_UnitCard.cshtml`.
  Both Abilities and While Leading loops now detect `•` in `ability.Text`,
  split on `\n`, and render sub-ability lines as `.sub-ability` divs with
  `.sub-ability-name` / `.sub-ability-text` spans. Flat abilities unchanged.
  Three CSS rules added to `site.css`. Build: 0 errors, 0 warnings.
- Sub-ability profile parsing implemented in `CatalogueParser.ParseProfiles`.
  Two-pass approach: Pass 1 collects standard types; Pass 2 collects non-standard
  typeNames as sub-ability groups, merging into matching parent abilities or creating
  new ones. 4 new tests added; 170 total, all passing.
- Regeneration v1 complete. Generated UI near-identical to original.
  No spec files were modified during generation — all gaps were minor.
- Sub-ability spec written and merged into `.claude/` docs.

---

## Known Issues

Nothing outstanding.

---

## Spec Debt

Nothing outstanding.

> Keep this section honest. If you change code without updating the
> corresponding spec file, log it here immediately. A growing list
> means the spec is drifting from reality.

---

## Next Session Prompt

> Paste this at the start of the next Claude Code session:

"Read CLAUDE.md and all files in .claude/. Then read PROGRESS.md for current state."
