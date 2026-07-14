namespace CombatSimulator.Analysis;

// Contains the Magic-Power-based caster weights and output diagnostics for one run.
public sealed class SkillOutputAnalysisResult
{
    public required int Simulations { get; init; }
    public required double CombatWindowSeconds { get; init; }
    public required int SkillSlots { get; init; }
    public required int EligibleSkills { get; init; }
    public required int CooldownReductionEligibleSamples { get; init; }
    public required double AverageBaseOutput { get; init; }
    public required CasterAttributeWeightDistributionResult CooldownReduction { get; init; }
    public required CasterAttributeWeightDistributionResult MagicResist { get; init; }
    public required CasterAttributeWeightDistributionResult MaxMana { get; init; }
    public required CasterAttributeWeightDistributionResult ManaRegen { get; init; }
    public required CasterAttributeWeightDistributionResult ManaEfficiency { get; init; }
}
