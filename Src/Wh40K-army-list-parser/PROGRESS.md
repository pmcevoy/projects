# Work Journal

## Active Work

Investigating razor tag rendering bug.

---

## Recently Completed

- Regeneration v1 complete. Generated UI near-identical to original.
  No spec files were modified during generation — all gaps were minor.

---

## Known Issues

- Razor tag bug: tags are rendering incorrectly in [describe affected view].
  Not yet root-caused. Check `.claude/web-app.md` and `combat-rules.md`
  once fixed to see if a spec gap contributed.

---

## Spec Debt

Nothing outstanding.

> Keep this section honest. If you change code without updating the
> corresponding spec file, log it here immediately. A growing list
> means the spec is drifting from reality.

---

## Next Session Prompt

> Paste this at the start of the next Claude Code session:

"Read CLAUDE.md and all files in .claude/. Then read PROGRESS.md for
current state. The active task is investigating the razor tag rendering
bug. Root-cause the issue, implement a fix, and check whether the bug
reveals a gap in any spec file — if so, update the spec alongside the
code. When done, update PROGRESS.md: move the razor tag item from Known
Issues to Recently Completed, clear Active Work, note any spec files
updated, and rewrite this Next Session Prompt for whatever comes next."
