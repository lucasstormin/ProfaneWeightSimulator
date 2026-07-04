using CombatSimulator.Combat;
using CombatSimulator.Data;
using CombatSimulator.Models;

namespace CombatSimulator.Analysis;

// Calculates Attack-Power-based Health weights across reproducibly sampled loadouts.
public static class LoadoutWeightAnalyzer
{
    public static WeightDistributionResult Analyze(GameData gameData, int samples, int seed)
    {
        if (samples <= 0)
            throw new ArgumentOutOfRangeException(nameof(samples));

        LoadoutGenerator generator = new(gameData, seed);
        double[] weights = new double[samples];
        WeightedLoadout? minimum = null;
        WeightedLoadout? maximum = null;

        for (int index = 0; index < samples; index++)
        {
            Loadout loadout = generator.Generate();
            double healthWeight = CalculateHealthWeight(loadout.Stats, gameData.CombatConfig);
            weights[index] = healthWeight;

            if (minimum is null || healthWeight < minimum.HealthWeight)
                minimum = new WeightedLoadout { Loadout = loadout, HealthWeight = healthWeight };
            if (maximum is null || healthWeight > maximum.HealthWeight)
                maximum = new WeightedLoadout { Loadout = loadout, HealthWeight = healthWeight };
        }

        Array.Sort(weights);
        double mean = weights.Average();
        double variance = weights.Sum(weight => Math.Pow(weight - mean, 2)) / weights.Length;
        double median = Percentile(weights, 0.50);

        return new WeightDistributionResult
        {
            Samples = samples,
            RecommendedHealthWeight = median,
            MeanHealthWeight = mean,
            MedianHealthWeight = median,
            StandardDeviation = Math.Sqrt(variance),
            MinimumHealthWeight = weights[0],
            FifthPercentile = Percentile(weights, 0.05),
            NinetyFifthPercentile = Percentile(weights, 0.95),
            MaximumHealthWeight = weights[^1],
            MinimumLoadout = minimum!,
            MaximumLoadout = maximum!
        };
    }

    public static double CalculateHealthWeight(CharacterStats stats, CombatConfig config)
    {
        if (stats.MaxHealth <= 0)
            throw new InvalidOperationException("Health must be positive.");
        if (config.AttackPowerMultiplier <= 0)
            throw new InvalidOperationException("Attack Power must have a positive damage multiplier.");

        double damage = DamageCalculator.CalculateDamage(stats, config);
        return damage / (stats.MaxHealth * config.AttackPowerMultiplier);
    }

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
