using Spectre.Console;
using Wh40kSim.Models;

namespace Wh40kSim.Output;

public static class ResultsFormatter
{
    public static void Render(SimulationResult result, SimulationConfig config)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"[bold]--- Results: {config.Attacker.Name} vs {config.Defender.Name} ({result.Runs:N0} runs) ---[/]");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine($"Mean damage  : [yellow]{result.Mean:F2}[/]");
        AnsiConsole.MarkupLine($"Median       : [yellow]{result.Median:F1}[/]");
        AnsiConsole.MarkupLine($"Std deviation: [yellow]{result.StdDeviation:F2}[/]");
        AnsiConsole.MarkupLine($"Min          : [yellow]{result.Min}[/]");
        AnsiConsole.MarkupLine($"Max          : [yellow]{result.Max}[/]");
        AnsiConsole.WriteLine();

        var table = new Table()
            .AddColumn("Damage")
            .AddColumn(new TableColumn("Probability").RightAligned())
            .AddColumn(new TableColumn("Cumulative").RightAligned());

        for (int d = result.Min; d <= result.Max; d++)
        {
            double prob = result.ProbabilityDistribution.GetValueOrDefault(d, 0);
            double cumul = result.CumulativeDistribution.GetValueOrDefault(d, 0);
            table.AddRow(d.ToString(), prob.ToString("P2"), cumul.ToString("P2"));
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }
}
