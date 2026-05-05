# Visual Design Specification

Dark tactical theme. Feels like a military operations display — dark backgrounds, muted text, red accent for action and selection. Designed for phone/tablet in low-light conditions at a gaming table.

No web fonts. System font throughout. Monospace for all game statistics.

---

## Colour Palette

Seven semantic colours cover the whole UI. Use named variables (`--bg`, `--accent`, etc.) — do not hardcode hex values in components.

| Name | Value | Semantic role |
|---|---|---|
| `--bg` | `#1a1a2e` | Page background |
| `--bg2` | `#16213e` | Card headers, panels, surfaces |
| `--bg3` | `#0f3460` | Hover state for headers, pill chips, modifier section headers |
| `--accent` | `#c0392b` | Primary action: selected attacker weapons, run button, headings |
| `--text` | `#e0e0e0` | Primary text |
| `--text-dim` | `#a0a0b0` | Labels, stat line, dim info, most table headers |
| `--amber` | `#f1a94e` | "While Leading" abilities, active modifier summaries, multi-weapon group headers |
| `--border` | `#2a2a4e` | All borders and dividers |

Three additional semantic colours that don't need to be variables (used in one place each):

- **Invulnerable save** — sky blue; distinguishes the invuln stat from the armour save in the unit header
- **Feel No Pain** — light green; same role for FNP stat
- **Selected defender** — a blue highlight (header tint + left border) when a defender card is chosen

---

## Typography

- **Font:** `system-ui, sans-serif` — no loading, no fallback issues
- **Base size:** 14px body
- **Line height:** 1.4
- **Monospace:** used for the stat line (T/Sv/W), all numeric weapon stats (A/S/AP/D), step-control values, and simulation results
- **Scale:** use relative sizing — section titles slightly larger than body, labels and secondary text slightly smaller. Avoid more than three distinct sizes on any one screen.

---

## Spacing & Shape

- Spacing is tight — this is a game tool, not a marketing page. Prefer compact padding over generous whitespace.
- Border radius is consistent: `4px` on cards and main containers, `3px` on chips/buttons/controls.
- All borders use `--border` colour. No drop shadows.

---

## Interaction States

Describe intent — the exact CSS values should follow from the palette above:

- **Hover (headers, rows):** surface lifts from `--bg2` to `--bg3`
- **Selected weapon (attacker):** row tinted with a low-opacity wash of `--accent`
- **Selected defender (unit card):** left border in a distinct blue; header background darkened
- **Locked weapon row (wrong phase):** faded to ~40% opacity, cursor becomes not-allowed
- **Active modifier toggle:** filled with a dim blue, brighter border — clearly "on" without being garish
- **Disabled button:** faded to ~60% opacity
- **Focused textarea:** thin `--accent` outline

---

## Responsive Behaviour

Single breakpoint at 700px:

- **Above 700px:** two-column army layout side by side; two-column army-input layout side by side
- **Below 700px:** everything stacks into a single column; catalogue bar wraps

No other breakpoints. The combat panel sticks to the bottom of the viewport at all sizes.
