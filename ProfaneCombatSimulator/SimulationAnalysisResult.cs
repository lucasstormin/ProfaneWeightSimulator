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
    public required AttributeWeightDistributionResult Health { get; init; }
    public required AttributeWeightDistributionResult WeaponDamage { get; init; }
    public required AttributeWeightDistributionResult AttackSpeed { get; init; }
    public required IReadOnlyList<AttackSpeedDiagnosticEntry> AttackSpeedDiagnostics { get; init; }

    public double AttackSpeedOutcomeAgreementRate =>
        AttackSpeedValidationComparisons == 0
            ? 0
            : (double)AttackSpeedOutcomeAgreements / AttackSpeedValidationComparisons * 100;
}
