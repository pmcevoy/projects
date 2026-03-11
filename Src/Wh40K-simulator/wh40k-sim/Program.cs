using Spectre.Console;
using Wh40kSim;
using Wh40kSim.Output;
using Wh40kSim.Simulation;

string? configPath = null;
bool exportCsv = false;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--config" && i + 1 < args.Length)
        configPath = args[++i];
    else if (args[i] == "--csv")
        exportCsv = true;
}

if (configPath is null)
{
    AnsiConsole.MarkupLine("[red]Error:[/] --config <path> is required.");
    return 1;
}

if (!File.Exists(configPath))
{
    AnsiConsole.MarkupLine($"[red]Error:[/] Config file not found: {configPath}");
    return 1;
}

var config = YamlLoader.Load(configPath);
var dice = new DiceRoller();
var simulator = new CombatSimulator(dice);

IReadOnlyList<int> rawResults = [];
AnsiConsole.Status().Start(
    $"Running {config.SimulationRuns:N0} simulations...",
    _ => { rawResults = simulator.Run(config); });

var result = SimulationResult.Compute(rawResults);

ResultsFormatter.Render(result, config);
AsciiChart.Render(result);

if (exportCsv)
{
    CsvExporter.Export(result, configPath);
    string csvName = Path.GetFileNameWithoutExtension(configPath) + "-results.csv";
    AnsiConsole.MarkupLine($"[green]CSV exported:[/] {csvName}");
}

return 0;
