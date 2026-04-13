namespace Wh40kArmyEnricher.Core.Simulation;

public sealed record SimulationResult
{
    public required IReadOnlyList<int> RawResults { get; init; }
    public required int Runs { get; init; }
    public required double Mean { get; init; }
    public required double Median { get; init; }
    public required double StdDeviation { get; init; }
    public required int Min { get; init; }
    public required int Max { get; init; }
    public required IReadOnlyDictionary<int, double> ProbabilityDistribution { get; init; }
    public required IReadOnlyDictionary<int, double> CumulativeDistribution { get; init; }

    public static SimulationResult Compute(IReadOnlyList<int> rawResults)
    {
        int n = rawResults.Count;
        double mean = rawResults.Average();
        int min = rawResults.Min();
        int max = rawResults.Max();

        var sorted = rawResults.Order().ToList();
        double median = n % 2 == 1
            ? sorted[n / 2]
            : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;

        double variance = rawResults.Sum(x => (x - mean) * (x - mean)) / n;
        double stdDev = Math.Sqrt(variance);

        var dist = rawResults
            .GroupBy(x => x)
            .ToDictionary(g => g.Key, g => (double)g.Count() / n);

        var cumul = new Dictionary<int, double>();
        double running = 0;
        for (int d = min; d <= max; d++)
        {
            running += dist.GetValueOrDefault(d, 0);
            cumul[d] = running;
        }

        return new SimulationResult
        {
            RawResults = rawResults,
            Runs = n,
            Mean = mean,
            Median = median,
            StdDeviation = stdDev,
            Min = min,
            Max = max,
            ProbabilityDistribution = dist,
            CumulativeDistribution = cumul,
        };
    }
}
