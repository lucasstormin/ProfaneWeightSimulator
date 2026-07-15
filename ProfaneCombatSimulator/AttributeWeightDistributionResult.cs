using CombatSimulator.Models;

namespace CombatSimulator.Analysis;

// Summarizes one attribute's AP-based weight across sampled legal loadouts.
public sealed class AttributeWeightDistributionResult
{
    public bool IsAvailable { get; init; } = true;
    public required AttributeId Attribute { get; init; }
    public required string DisplayName { get; init; }
    public required string UnitLabel { get; init; }
    public string? UnavailableReason { get; init; }
    public required double RecommendedWeight { get; init; }
    public required double MeanWeight { get; init; }
    public required double MedianWeight { get; init; }
    public required double StandardDeviation { get; init; }
    public required double MinimumWeight { get; init; }
    public required double FifthPercentile { get; init; }
    public required double NinetyFifthPercentile { get; init; }
    public required double MaximumWeight { get; init; }
    public required WeightedLoadout? MinimumLoadout { get; init; }
    public required WeightedLoadout? MaximumLoadout { get; init; }
}
