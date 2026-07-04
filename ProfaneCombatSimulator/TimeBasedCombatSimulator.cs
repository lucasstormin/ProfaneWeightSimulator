using CombatSimulator.Analysis;
using CombatSimulator.Models;

namespace CombatSimulator.Combat;

// Resolves deterministic fights by processing weapon contacts on a shared timeline.
public static class TimeBasedCombatSimulator
{
    public const double DefaultMaximumDuration = 300;
    public const int DefaultMaximumEvents = 1_000_000;
    private const double TimeEpsilon = 0.000000001;

    // Simulates one fight while batching equal-time contacts before evaluating deaths.
    public static TimedCombatResult Simulate(
        Loadout playerA,
        Loadout playerB,
        CombatConfig config,
        double maximumDuration = DefaultMaximumDuration,
        int maximumEvents = DefaultMaximumEvents)
    {
        if (maximumDuration <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumDuration));
        if (maximumEvents <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumEvents));

        CombatantState stateA = new(playerA);
        CombatantState stateB = new(playerB);

        for (int eventCount = 0; eventCount < maximumEvents; eventCount++)
        {
            double eventTime = Math.Min(stateA.NextContactTime, stateB.NextContactTime);
            if (eventTime > maximumDuration + TimeEpsilon)
                return CreateStalemate(stateA, stateB, maximumDuration, CombatTerminationReason.TimeLimit);

            bool playerAHits = TimesMatch(stateA.NextContactTime, eventTime);
            bool playerBHits = TimesMatch(stateB.NextContactTime, eventTime);

            double damageToB = playerAHits
                ? DamageCalculator.CalculateDamage(stateA.Stats, stateA.CurrentStep, config)
                : 0;
            double damageToA = playerBHits
                ? DamageCalculator.CalculateDamage(stateB.Stats, stateB.CurrentStep, config)
                : 0;

            if (playerAHits)
            {
                stateB.ReceiveDamage(damageToB);
                stateA.AdvanceAfterContact();
            }
            if (playerBHits)
            {
                stateA.ReceiveDamage(damageToA);
                stateB.AdvanceAfterContact();
            }

            if (!stateA.IsAlive || !stateB.IsAlive)
                return CreateDeathResult(stateA, stateB, eventTime);
        }

        double duration = Math.Min(maximumDuration, Math.Min(stateA.NextContactTime, stateB.NextContactTime));
        return CreateStalemate(stateA, stateB, duration, CombatTerminationReason.EventLimit);
    }

    // Creates a death result, treating simultaneous deaths as a draw.
    private static TimedCombatResult CreateDeathResult(
        CombatantState stateA,
        CombatantState stateB,
        double duration)
    {
        CombatOutcome outcome = (!stateA.IsAlive, !stateB.IsAlive) switch
        {
            (true, true) => CombatOutcome.Draw,
            (false, true) => CombatOutcome.PlayerAWins,
            (true, false) => CombatOutcome.PlayerBWins,
            _ => throw new InvalidOperationException("Death result requested while both combatants are alive.")
        };

        return CreateResult(stateA, stateB, duration, outcome, CombatTerminationReason.Death);
    }

    // Creates a stalemate result for a time or event safety limit.
    private static TimedCombatResult CreateStalemate(
        CombatantState stateA,
        CombatantState stateB,
        double duration,
        CombatTerminationReason reason)
    {
        return CreateResult(stateA, stateB, duration, CombatOutcome.Stalemate, reason);
    }

    // Copies mutable combat state into an immutable result object.
    private static TimedCombatResult CreateResult(
        CombatantState stateA,
        CombatantState stateB,
        double duration,
        CombatOutcome outcome,
        CombatTerminationReason reason)
    {
        return new TimedCombatResult
        {
            Outcome = outcome,
            TerminationReason = reason,
            Duration = duration,
            PlayerAHits = stateA.HitsLanded,
            PlayerBHits = stateB.HitsLanded,
            PlayerARemainingHealth = stateA.CurrentHealth,
            PlayerBRemainingHealth = stateB.CurrentHealth
        };
    }

    // Treats sub-nanosecond differences as the same contact timestamp.
    private static bool TimesMatch(double left, double right) =>
        Math.Abs(left - right) <= TimeEpsilon;
}
