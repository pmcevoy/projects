using Spectre.Console;

namespace Wh40kSim.Output;

public static class AsciiChart
{
    public static void Render(SimulationResult result)
    {
        var chart = new BarChart()
            .Width(78)
            .Label("[bold]Damage Distribution (%)[/]");

        int range = result.Max - result.Min;

        for (int d = result.Min; d <= result.Max; d++)
        {
            double prob = result.ProbabilityDistribution.GetValueOrDefault(d, 0);

            // Filter low-probability values when range is large to keep chart readable
            if (range > 30 && prob < 0.005)
                continue;

            chart.AddItem(d.ToString(), Math.Round(prob * 100, 2), Color.Aqua);
        }

        AnsiConsole.Write(chart);
        AnsiConsole.WriteLine();
    }
}
