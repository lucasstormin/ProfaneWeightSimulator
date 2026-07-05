namespace CombatSimulator.Analysis;

// Combines contextual attribute weights with time-based fight validation statistics.
public sealed class SimulationAnalysisResult
{
    public required int SimulatedFights { get; init; }
    public required int CompletedFights { get; init; }
    public required int Stalemates { get; init; }
    public required int Draws { get; init; }
    public required double AverageCompletedFightDuration { get; init; }
    public required int AttackSpeedValidationComparisons { get; init; }
    public required int AttackSpeedOutcomeAgreements { get; init; }
    public required int ArmorValidationComparisons { get; init; }
    public required int ArmorOutcomeAgreements { get; init; }
    public required int ArmorPenetrationValidationComparisons { get; init; }
    public required int ArmorPenetrationOutcomeAgreements { get; init; }
    public required AttributeWeightDistributionResult Health { get; init; }
    public required AttributeWeightDistributionResult WeaponDamage { get; init; }
    public required AttributeWeightDistributionResult AttackSpeed { get; init; }
    public required AttributeWeightDistributionResult Armor { get; init; }
    public required AttributeWeightDistributionResult ArmorPenetration { get; init; }
    public required IReadOnlyList<AttackSpeedDiagnosticEntry> StrongestAttackSpeedBuilds { get; init; }
    public required IReadOnlyList<WeaponProfileDpsSummary> WeaponProfileDpsSummaries { get; init; }

    public double AttackSpeedOutcomeAgreementRate =>
        AttackSpeedValidationComparisons == 0
            ? 0
            : (double)AttackSpeedOutcomeAgreements / AttackSpeedValidationComparisons * 100;

    public double ArmorOutcomeAgreementRate =>
        ArmorValidationComparisons == 0
            ? 0
            : (double)ArmorOutcomeAgreements / ArmorValidationComparisons * 100;

    public double ArmorPenetrationOutcomeAgreementRate =>
        ArmorPenetrationValidationComparisons == 0
            ? 0
            : (double)ArmorPenetrationOutcomeAgreements /
                ArmorPenetrationValidationComparisons * 100;
}
