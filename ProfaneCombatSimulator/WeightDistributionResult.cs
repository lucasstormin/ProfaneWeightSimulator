namespace CombatSimulator.Analysis;

// Summarizes Health weights observed across many valid equipment combinations.
public sealed class WeightDistributionResult
{
    public required int Samples { get; init; }
    public required double RecommendedHealthWeight { get; init; }
    public required double MeanHealthWeight { get; init; }
    public required double MedianHealthWeight { get; init; }
    public required double StandardDeviation { get; init; }
    public required double MinimumHealthWeight { get; init; }
    public required double FifthPercentile { get; init; }
    public required double NinetyFifthPercentile { get; init; }
    public required double MaximumHealthWeight { get; init; }
    public required WeightedLoadout MinimumLoadout { get; init; }
    public required WeightedLoadout MaximumLoadout { get; init; }
}
