namespace CombatSimulator.Analysis;

// Stores an offensive attribute's AP-based weight and its sampled validation outcome.
public sealed class OffensiveAttributeWeightResult
{
    public required OffensiveAttribute Attribute { get; init; }
    public required double AttackPowerBasedWeight { get; init; }
    public required int ValidationSamples { get; init; }
    public required int ValidationFailures { get; init; }

    public bool ValidationPassed => ValidationFailures == 0;
}
