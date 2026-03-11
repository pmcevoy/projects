namespace Wh40kSim.Output;

public static class CsvExporter
{
    public static void Export(SimulationResult result, string configFilePath)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(configFilePath))!;
        string name = Path.GetFileNameWithoutExtension(configFilePath);
        string outputPath = Path.Combine(dir, $"{name}-results.csv");

        using var writer = new StreamWriter(outputPath);
        writer.WriteLine("Damage,Probability,CumulativeProbability");

        for (int d = result.Min; d <= result.Max; d++)
        {
            double prob = result.ProbabilityDistribution.GetValueOrDefault(d, 0);
            double cumul = result.CumulativeDistribution.GetValueOrDefault(d, 0);
            writer.WriteLine($"{d},{prob:F6},{cumul:F6}");
        }
    }
}
