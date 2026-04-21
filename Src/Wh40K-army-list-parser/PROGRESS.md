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

## Current State

| Layer | Status | Notes |
|---|---|---|
| Project scaffolding | ❌ Not started | |
| Domain model / types | ❌ Not started | |
| Simulation engine | ❌ Not started | |
| BSData parsing | ❌ Not started | |
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

*(Empty — no sessions run yet)*

---

## Resume Prompt

> Paste this at the start of the next Claude Code session:

"Read CLAUDE.md and all files in .claude/. Then read PROGRESS.md for current
build state. Your goal this session is **Session 1: Project Scaffolding**. Create
the project directory structure, package.json, tsconfig (if applicable), any
config files, and entry point stubs — but no business logic. The app should
start and serve something (even a placeholder) by the end of this session.
When done, update PROGRESS.md: mark scaffolding complete, record any decisions
made or spec gaps discovered, and rewrite the Resume Prompt section for Session 2."

---

## Session Plan

| Session | Goal | Done-state |
|---|---|---|
| 1 | Project scaffolding | App starts and serves a placeholder |
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
