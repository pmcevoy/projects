# Work Journal

## Active Work

Nothing in progress.

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
Sub-ability parsing is done. The next logical area to explore is verifying the
sub-ability parsing works against real BSData catalogues — consider running the app
against a real army list and checking that abilities like 'Lord of the Death Guard'
display their sub-options correctly in the unit card. Alternatively, pick up any
other backlog item."
