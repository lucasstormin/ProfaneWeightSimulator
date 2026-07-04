using CombatSimulator.Combat;
using CombatSimulator.Data;
using CombatSimulator.Models;

namespace CombatSimulator.Analysis;

// Samples legal matchups, derives contextual weights, and validates them in timed fights.
public static class TimeBasedAnalysisRunner
{
    private const double AttackSpeedUnit = 0.01;

    // Runs weight sampling and deterministic combat validation with one reproducible seed.
    public static SimulationAnalysisResult Analyze(GameData gameData, int fights, int seed)
    {
        if (fights <= 0)
            throw new ArgumentOutOfRangeException(nameof(fights));

        LoadoutGenerator generator = new(gameData, seed);
        double[] healthWeights = new double[fights];
        double[] weaponDamageWeights = new double[fights];
        double[] attackSpeedWeights = new double[fights];
        AttackSpeedDiagnosticEntry[] attackSpeedDiagnostics = new AttackSpeedDiagnosticEntry[fights];
        WeightedLoadout?[] minimums = new WeightedLoadout?[3];
        WeightedLoadout?[] maximums = new WeightedLoadout?[3];

        int stalemates = 0;
        int draws = 0;
        int completedFights = 0;
        double completedDurationTotal = 0;
        int outcomeAgreements = 0;

        for (int index = 0; index < fights; index++)
        {
            Loadout playerA = generator.Generate();
            Loadout playerB = generator.Generate();

            (double health, double weaponDamage, double attackSpeed) =
                CalculateWeights(playerA, gameData.CombatConfig);
            healthWeights[index] = health;
            weaponDamageWeights[index] = weaponDamage;
            attackSpeedWeights[index] = attackSpeed;
            double cycleDamage = playerA.AttackProfile.Steps.Sum(step =>
                DamageCalculator.CalculateRawDamage(playerA.Stats, step, gameData.CombatConfig));
            attackSpeedDiagnostics[index] = new AttackSpeedDiagnosticEntry
            {
                Weight = attackSpeed,
                CycleDamage = cycleDamage,
                Loadout = playerA
            };
            UpdateExtremes(minimums, maximums, playerA, [health, weaponDamage, attackSpeed]);

            TimedCombatResult baseFight =
                TimeBasedCombatSimulator.Simulate(playerA, playerB, gameData.CombatConfig);
            if (baseFight.Outcome == CombatOutcome.Stalemate)
            {
                stalemates++;
            }
            else
            {
                completedFights++;
                completedDurationTotal += baseFight.Duration;
                if (baseFight.Outcome == CombatOutcome.Draw)
                    draws++;
            }

            Loadout attackSpeedBuffed = WithAddedStat(playerA, AttributeId.AttackSpeed, AttackSpeedUnit);
            Loadout attackPowerBuffed = WithAddedStat(playerA, AttributeId.AttackPower, attackSpeed);
            CombatOutcome attackSpeedOutcome = TimeBasedCombatSimulator
                .Simulate(attackSpeedBuffed, playerB, gameData.CombatConfig)
                .Outcome;
            CombatOutcome attackPowerOutcome = TimeBasedCombatSimulator
                .Simulate(attackPowerBuffed, playerB, gameData.CombatConfig)
                .Outcome;
            if (attackSpeedOutcome == attackPowerOutcome)
                outcomeAgreements++;
        }

        return new SimulationAnalysisResult
        {
            SimulatedFights = fights,
            CompletedFights = completedFights,
            Stalemates = stalemates,
            Draws = draws,
            AverageCompletedFightDuration = completedFights == 0 ? 0 : completedDurationTotal / completedFights,
            AttackSpeedValidationComparisons = fights,
            AttackSpeedOutcomeAgreements = outcomeAgreements,
            AttackSpeedDiagnostics = attackSpeedDiagnostics,
            Health = CreateDistribution(
                AttributeId.MaxHealth,
                "Health",
                "1 Health",
                healthWeights,
                minimums[0]!,
                maximums[0]!),
            WeaponDamage = CreateDistribution(
                AttributeId.WeaponDamage,
                "Weapon Damage",
                "1 Weapon Damage",
                weaponDamageWeights,
                minimums[1]!,
                maximums[1]!),
            AttackSpeed = CreateDistribution(
                AttributeId.AttackSpeed,
                "Attack Speed (1%)",
                "1 percentage point",
                attackSpeedWeights,
                minimums[2]!,
                maximums[2]!)
        };
    }

    // Derives smooth marginal weights from one profile's complete three-hit damage cycle.
    public static (double Health, double WeaponDamage, double AttackSpeed) CalculateWeights(
        Loadout loadout,
        CombatConfig config)
    {
        double attackPowerCycleContribution =
            loadout.AttackProfile.Steps.Count * config.AttackPowerMultiplier;
        if (attackPowerCycleContribution <= 0 || loadout.Stats.MaxHealth <= 0)
            throw new InvalidOperationException("Loadout stats cannot produce AP-based weights.");

        double cycleDamage = loadout.AttackProfile.Steps.Sum(step =>
            DamageCalculator.CalculateRawDamage(loadout.Stats, step, config));
        double multiplierTotal = loadout.AttackProfile.Steps.Sum(step => step.WeaponDamageMultiplier);
        double speedFactor = 1 + loadout.Stats[AttributeId.AttackSpeed];
        if (speedFactor <= 0)
            throw new InvalidOperationException("Attack Speed must keep the duration divisor positive.");

        return (
            cycleDamage / (loadout.Stats.MaxHealth * attackPowerCycleContribution),
            multiplierTotal / attackPowerCycleContribution,
            cycleDamage * AttackSpeedUnit / (speedFactor * attackPowerCycleContribution));
    }

    // Copies a loadout while changing one immutable character stat for paired validation.
    private static Loadout WithAddedStat(Loadout source, AttributeId attribute, double amount)
    {
        return new Loadout
        {
            Stats = source.Stats.WithAdded(attribute, amount),
            Items = source.Items,
            AttackProfile = source.AttackProfile
        };
    }

    // Retains only the loadouts producing each attribute's observed minimum and maximum.
    private static void UpdateExtremes(
        WeightedLoadout?[] minimums,
        WeightedLoadout?[] maximums,
        Loadout loadout,
        double[] weights)
    {
        for (int index = 0; index < weights.Length; index++)
        {
            if (minimums[index] is null || weights[index] < minimums[index]!.Weight)
                minimums[index] = new WeightedLoadout { Loadout = loadout, Weight = weights[index] };
            if (maximums[index] is null || weights[index] > maximums[index]!.Weight)
                maximums[index] = new WeightedLoadout { Loadout = loadout, Weight = weights[index] };
        }
    }

    // Sorts sampled values and calculates robust distribution diagnostics.
    private static AttributeWeightDistributionResult CreateDistribution(
        AttributeId attribute,
        string displayName,
        string unitLabel,
        double[] weights,
        WeightedLoadout minimum,
        WeightedLoadout maximum)
    {
        Array.Sort(weights);
        double mean = weights.Average();
        double variance = weights.Sum(weight => Math.Pow(weight - mean, 2)) / weights.Length;
        double median = Percentile(weights, 0.50);

        return new AttributeWeightDistributionResult
        {
            Attribute = attribute,
            DisplayName = displayName,
            UnitLabel = unitLabel,
            RecommendedWeight = median,
            MeanWeight = mean,
            MedianWeight = median,
            StandardDeviation = Math.Sqrt(variance),
            MinimumWeight = weights[0],
            FifthPercentile = Percentile(weights, 0.05),
            NinetyFifthPercentile = Percentile(weights, 0.95),
            MaximumWeight = weights[^1],
            MinimumLoadout = minimum,
            MaximumLoadout = maximum
        };
    }

    // Interpolates one percentile from an already sorted sample array.
    private static double Percentile(double[] sortedValues, double percentile)
    {
        double position = (sortedValues.Length - 1) * percentile;
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        if (lower == upper)
            return sortedValues[lower];

        double fraction = position - lower;
        return sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * fraction;
    }
}
