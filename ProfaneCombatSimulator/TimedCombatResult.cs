namespace CombatSimulator.Combat;

// Stores the outcome and timing diagnostics from one deterministic event-based fight.
public sealed class TimedCombatResult
{
    public required CombatOutcome Outcome { get; init; }
    public required CombatTerminationReason TerminationReason { get; init; }
    public required double Duration { get; init; }
    public required int PlayerAHits { get; init; }
    public required int PlayerBHits { get; init; }
    public required double PlayerARemainingHealth { get; init; }
    public required double PlayerBRemainingHealth { get; init; }
    public required double PlayerATotalHealing { get; init; }
    public required double PlayerBTotalHealing { get; init; }
    public required double PlayerAAdditionalRegenHealingOpportunity { get; init; }
    public required double PlayerBAdditionalRegenHealingOpportunity { get; init; }
    public required double PlayerAAdditionalLifeStealHealingOpportunity { get; init; }
    public required double PlayerBAdditionalLifeStealHealingOpportunity { get; init; }
}
