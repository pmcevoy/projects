# WH40K Combat Simulator

A Monte Carlo combat simulator for Warhammer 40,000 10th Edition. Simulates the full attack sequence
and outputs probability distribution graphs to the console.

## Project Structure

```
wh40k-sim/
├── Models/         — AttackerProfile, DefenderProfile, WeaponProfile, SimulationConfig
├── Simulation/     — CombatSimulator, DiceRoller, AbilityProcessor
├── Output/         — ResultsFormatter, AsciiChart, CsvExporter
├── Tests/          — xUnit test project
└── profiles/       — YAML input files (examples included)
```

## Tech Stack

- **Language:** C# / .NET 8
- **YAML parsing:** YamlDotNet
- **Terminal output & charts:** Spectre.Console
- **Tests:** xUnit

## Build & Run

```bash
# Run a simulation
dotnet run -- --config profiles/example.yaml

# Run with CSV export
dotnet run -- --config profiles/example.yaml --csv

# Run tests
dotnet test
```

## Full Specification

All combat rules, the complete attack sequence, weapon abilities, profile schema, and output
requirements are documented in:

```
.claude/rules/combat-rules.md
```

Read that file before generating any simulation or model code.

## Key Conventions

- Dice expressions (e.g. `D3`, `2D6`, `D6+2`) must be supported wherever attacks or damage are specified
- All special rules are opt-in flags in the YAML profile — absence means the rule does not apply
- Simulation run count is controlled by a top-level `simulationRuns` key in the YAML (default: 100,000)
- Natural 1s always fail hit and wound rolls; natural 6s always succeed — apply these before modifiers
- Modifier cap of +1/-1 applies to hit and wound rolls after all modifiers are summed
