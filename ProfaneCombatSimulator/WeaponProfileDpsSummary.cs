namespace CombatSimulator.Analysis;

// Stores finalized DPS diagnostics for one weapon and attack-profile combination.
public sealed class WeaponProfileDpsSummary
{
    public required string WeaponName { get; init; }
    public required string ProfileName { get; init; }
    public required int Samples { get; init; }
    public required double MeanDamagePerSecond { get; init; }
    public required double NinetyFifthPercentileDamagePerSecond { get; init; }
    public required double MaximumDamagePerSecond { get; init; }
    public required double AttackSpeedWeightAtMaximum { get; init; }
}
