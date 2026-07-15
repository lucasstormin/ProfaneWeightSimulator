using CombatSimulator.Data;
using CombatSimulator.Models;

namespace CombatSimulator.Analysis;

// Samples caster gear and skill bars, then derives Magic-Power-based resource weights.
public static class SkillOutputAnalysisRunner
{
    private const double CooldownReductionUnit = 0.01;
    private const double CooldownReductionMinimumExistingValue = 0.10;
    private const double ManaRegenUnit = 1;
    private const double ManaEfficiencyUnit = 0.01;

    // Runs one deterministic caster-output analysis for the selected item population.
    public static SkillOutputAnalysisResult Analyze(
        GameData gameData,
        int simulations,
        int seed,
        LoadoutGenerationMode mode,
        double combatWindowSeconds)
    {
        if (simulations <= 0)
            throw new ArgumentOutOfRangeException(nameof(simulations));
        if (combatWindowSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(combatWindowSeconds));

        SkillDefinition[] eligibleSkills = gameData.Skills
            .Where(skill => skill.IsMagicalOnly)
            .OrderBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (eligibleSkills.Length < gameData.CombatConfig.SkillSlots)
        {
            throw new InvalidDataException(
                $"At least {gameData.CombatConfig.SkillSlots} magical skills are required for caster analysis.");
        }

        LoadoutGenerator gearGenerator = new(
            gameData,
            seed,
            mode,
            checked(simulations * 2));
        Random skillRandom = new(seed ^ 0x5C1A7E);
        List<double> cooldownReductionWeights = new(simulations);
        double[] magicResistWeights = new double[simulations];
        double[] maxManaWeights = new double[simulations];
        double[] manaRegenWeights = new double[simulations];
        double[] manaEfficiencyWeights = new double[simulations];
        WeightedSkillLoadout? cooldownReductionMinimum = null;
        WeightedSkillLoadout? cooldownReductionMaximum = null;
        WeightedSkillLoadout?[] minimums = new WeightedSkillLoadout?[4];
        WeightedSkillLoadout?[] maximums = new WeightedSkillLoadout?[4];
        double totalBaseOutput = 0;
        int manaLimitedSamples = 0;

        for (int index = 0; index < simulations; index++)
        {
            Loadout gear = gearGenerator.Generate();
            Loadout target = gearGenerator.Generate();
            SkillLoadout skillLoadout = new()
            {
                Gear = gear,
                Skills = PickSkills(eligibleSkills, gameData.CombatConfig.SkillSlots, skillRandom)
            };

            double baseOutput = SkillOutputSimulator.EstimateSmoothOutput(
                skillLoadout.Stats,
                skillLoadout.Skills,
                gameData.CombatConfig,
                combatWindowSeconds,
                target.Stats[AttributeId.MagicResist]);
            double unlimitedManaOutput = SkillOutputSimulator.EstimateSmoothOutput(
                skillLoadout.Stats,
                skillLoadout.Skills,
                gameData.CombatConfig,
                combatWindowSeconds,
                target.Stats[AttributeId.MagicResist],
                enforceMana: false);
            totalBaseOutput += baseOutput;
            if (unlimitedManaOutput > baseOutput)
                manaLimitedSamples++;

            double magicPowerGain = CalculateOutputGain(
                skillLoadout,
                gameData,
                combatWindowSeconds,
                target.Stats[AttributeId.MagicResist],
                AttributeId.MagicPower,
                1,
                enforceMana: false);
            double safeMagicPowerGain = magicPowerGain > 0 ? magicPowerGain : 1;

            double cooldownReductionWeight = CalculateOutputGain(
                skillLoadout,
                gameData,
                combatWindowSeconds,
                target.Stats[AttributeId.MagicResist],
                AttributeId.CooldownReduction,
                CooldownReductionUnit,
                enforceMana: false) / safeMagicPowerGain;
            double magicResistWeight = CalculatePreventedOutput(
                skillLoadout,
                target,
                gameData,
                combatWindowSeconds,
                1,
                enforceMana: false) / safeMagicPowerGain;
            double maxManaWeight = CalculateOutputGain(
                skillLoadout,
                gameData,
                combatWindowSeconds,
                target.Stats[AttributeId.MagicResist],
                AttributeId.MaxMana,
                1) / safeMagicPowerGain;
            double manaRegenWeight = CalculateOutputGain(
                skillLoadout,
                gameData,
                combatWindowSeconds,
                target.Stats[AttributeId.MagicResist],
                AttributeId.ManaRegen,
                ManaRegenUnit) / safeMagicPowerGain;
            double manaEfficiencyWeight = CalculateOutputGain(
                skillLoadout,
                gameData,
                combatWindowSeconds,
                target.Stats[AttributeId.MagicResist],
                AttributeId.ManaEfficiency,
                ManaEfficiencyUnit) / safeMagicPowerGain;

            bool cooldownReductionEligible =
                skillLoadout.Stats[AttributeId.CooldownReduction] > CooldownReductionMinimumExistingValue;
            if (cooldownReductionEligible)
            {
                cooldownReductionWeights.Add(cooldownReductionWeight);
                if (cooldownReductionMinimum is null || cooldownReductionWeight < cooldownReductionMinimum.Weight)
                {
                    cooldownReductionMinimum = new WeightedSkillLoadout
                    {
                        Loadout = skillLoadout,
                        Weight = cooldownReductionWeight
                    };
                }
                if (cooldownReductionMaximum is null || cooldownReductionWeight > cooldownReductionMaximum.Weight)
                {
                    cooldownReductionMaximum = new WeightedSkillLoadout
                    {
                        Loadout = skillLoadout,
                        Weight = cooldownReductionWeight
                    };
                }
            }
            magicResistWeights[index] = magicResistWeight;
            maxManaWeights[index] = maxManaWeight;
            manaRegenWeights[index] = manaRegenWeight;
            manaEfficiencyWeights[index] = manaEfficiencyWeight;

            UpdateExtremes(
                minimums,
                maximums,
                skillLoadout,
                magicResistWeight,
                maxManaWeight,
                manaRegenWeight,
                manaEfficiencyWeight);
        }

        if (cooldownReductionWeights.Count == 0)
        {
            throw new InvalidDataException(
                "No caster samples had more than 10% Cooldown Reduction; increase simulations or use a gear pool with CDR.");
        }

        return new SkillOutputAnalysisResult
        {
            Simulations = simulations,
            CombatWindowSeconds = combatWindowSeconds,
            SkillSlots = gameData.CombatConfig.SkillSlots,
            EligibleSkills = eligibleSkills.Length,
            CooldownReductionEligibleSamples = cooldownReductionWeights.Count,
            ManaLimitedSamples = manaLimitedSamples,
            AverageBaseOutput = totalBaseOutput / simulations,
            CooldownReduction = CreateDistribution(
                AttributeId.CooldownReduction,
                "Cooldown Reduction (1%)",
                "1 percentage point",
                cooldownReductionWeights.ToArray(),
                cooldownReductionMinimum!,
                cooldownReductionMaximum!),
            MagicResist = CreateDistribution(
                AttributeId.MagicResist,
                "Magic Resist",
                "1 Magic Resist",
                magicResistWeights,
                minimums[0]!,
                maximums[0]!),
            MaxMana = CreateDistribution(
                AttributeId.MaxMana,
                "Max Mana",
                "1 Mana",
                maxManaWeights,
                minimums[1]!,
                maximums[1]!),
            ManaRegen = CreateDistribution(
                AttributeId.ManaRegen,
                "MP/s",
                "1 Mana/second",
                manaRegenWeights,
                minimums[2]!,
                maximums[2]!),
            ManaEfficiency = CreateDistribution(
                AttributeId.ManaEfficiency,
                "Mana Cost Efficiency (1%)",
                "1 percentage point",
                manaEfficiencyWeights,
                minimums[3]!,
                maximums[3]!)
        };
    }

    // Calculates how much one stat increment changes total skill output.
    private static double CalculateOutputGain(
        SkillLoadout loadout,
        GameData gameData,
        double combatWindowSeconds,
        double targetMagicResist,
        AttributeId attribute,
        double amount,
        bool enforceMana = true)
    {
        double baseOutput = SkillOutputSimulator.EstimateSmoothOutput(
            loadout.Stats,
            loadout.Skills,
            gameData.CombatConfig,
            combatWindowSeconds,
            targetMagicResist,
            enforceMana);
        double increasedOutput = SkillOutputSimulator.EstimateSmoothOutput(
            loadout.Stats.WithAdded(attribute, amount),
            loadout.Skills,
            gameData.CombatConfig,
            combatWindowSeconds,
            targetMagicResist,
            enforceMana);
        return Math.Max(0, increasedOutput - baseOutput);
    }

    // Calculates how much magical damage one target Magic Resist increment prevents.
    private static double CalculatePreventedOutput(
        SkillLoadout attacker,
        Loadout target,
        GameData gameData,
        double combatWindowSeconds,
        double magicResistAmount,
        bool enforceMana = true)
    {
        double baseOutput = SkillOutputSimulator.EstimateSmoothOutput(
            attacker.Stats,
            attacker.Skills,
            gameData.CombatConfig,
            combatWindowSeconds,
            target.Stats[AttributeId.MagicResist],
            enforceMana);
        double reducedOutput = SkillOutputSimulator.EstimateSmoothOutput(
            attacker.Stats,
            attacker.Skills,
            gameData.CombatConfig,
            combatWindowSeconds,
            target.Stats.WithAdded(AttributeId.MagicResist, magicResistAmount)[AttributeId.MagicResist],
            enforceMana);
        return Math.Max(0, baseOutput - reducedOutput);
    }

    // Selects a unique random skill bar from the eligible magical-skill pool.
    private static SkillDefinition[] PickSkills(
        SkillDefinition[] eligibleSkills,
        int skillSlots,
        Random random)
    {
        SkillDefinition[] shuffled = eligibleSkills.ToArray();
        for (int index = shuffled.Length - 1; index > 0; index--)
        {
            int swapIndex = random.Next(index + 1);
            (shuffled[index], shuffled[swapIndex]) = (shuffled[swapIndex], shuffled[index]);
        }

        return shuffled.Take(skillSlots).ToArray();
    }

    // Updates tracked minimum and maximum loadouts for all caster attributes.
    private static void UpdateExtremes(
        WeightedSkillLoadout?[] minimums,
        WeightedSkillLoadout?[] maximums,
        SkillLoadout loadout,
        params double[] weights)
    {
        for (int index = 0; index < weights.Length; index++)
        {
            if (minimums[index] is null || weights[index] < minimums[index]!.Weight)
                minimums[index] = new WeightedSkillLoadout { Loadout = loadout, Weight = weights[index] };
            if (maximums[index] is null || weights[index] > maximums[index]!.Weight)
                maximums[index] = new WeightedSkillLoadout { Loadout = loadout, Weight = weights[index] };
        }
    }

    // Creates the median-led distribution report used by caster attributes.
    private static CasterAttributeWeightDistributionResult CreateDistribution(
        AttributeId attribute,
        string displayName,
        string unitLabel,
        double[] weights,
        WeightedSkillLoadout minimum,
        WeightedSkillLoadout maximum)
    {
        double[] sorted = weights.Order().ToArray();
        double mean = weights.Average();
        double variance = weights.Sum(weight => Math.Pow(weight - mean, 2)) / weights.Length;
        double median = Percentile(sorted, 0.5);
        return new CasterAttributeWeightDistributionResult
        {
            Attribute = attribute,
            DisplayName = displayName,
            UnitLabel = unitLabel,
            RecommendedWeight = median,
            MeanWeight = mean,
            MedianWeight = median,
            StandardDeviation = Math.Sqrt(variance),
            MinimumWeight = sorted[0],
            FifthPercentile = Percentile(sorted, 0.05),
            NinetyFifthPercentile = Percentile(sorted, 0.95),
            MaximumWeight = sorted[^1],
            MinimumLoadout = minimum,
            MaximumLoadout = maximum
        };
    }

    // Returns an interpolated percentile from a sorted sample.
    private static double Percentile(double[] sorted, double percentile)
    {
        if (sorted.Length == 1)
            return sorted[0];
        double position = (sorted.Length - 1) * percentile;
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        if (lower == upper)
            return sorted[lower];
        double fraction = position - lower;
        return sorted[lower] + ((sorted[upper] - sorted[lower]) * fraction);
    }
}
