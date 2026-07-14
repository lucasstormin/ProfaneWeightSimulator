using CombatSimulator.Models;

namespace CombatSimulator.Analysis;

// Summarizes one caster attribute's Magic-Power-based weight across sampled skill loadouts.
public sealed class CasterAttributeWeightDistributionResult
{
    public required AttributeId Attribute { get; init; }
    public required string DisplayName { get; init; }
    public required string UnitLabel { get; init; }
    public required double RecommendedWeight { get; init; }
    public required double MeanWeight { get; init; }
    public required double MedianWeight { get; init; }
    public required double StandardDeviation { get; init; }
    public required double MinimumWeight { get; init; }
    public required double FifthPercentile { get; init; }
    public required double NinetyFifthPercentile { get; init; }
    public required double MaximumWeight { get; init; }
    public required WeightedSkillLoadout MinimumLoadout { get; init; }
    public required WeightedSkillLoadout MaximumLoadout { get; init; }
}
