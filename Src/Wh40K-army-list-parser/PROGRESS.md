# Work Journal

## Active Work

Sub-ability rendering in unit cards — spec written, implementation pending.

---

## Recently Completed

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

"Read CLAUDE.md and all files in .claude/. Then read PROGRESS.md for current state.
The active task is implementing sub-ability rendering in `_UnitCard.cshtml`.
The spec is fully written in .claude/web-app.md under 'Ability Rendering — Sub-Abilities'.
Read that section before touching any code. The changes required are:
1. Update both ability loops in `_UnitCard.cshtml` (Abilities and While Leading) to
   split ability.Text on newline, detect lines starting with U+2022, and render them
   as .sub-ability divs with .sub-ability-name / .sub-ability-text spans.
2. Add the three CSS rules (.sub-ability, .sub-ability-name, .sub-ability-text) to site.css.
When done, update PROGRESS.md and rewrite this Next Session Prompt."
