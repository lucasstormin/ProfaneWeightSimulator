using CombatSimulator.Combat;
using CombatSimulator.Data;
using CombatSimulator.Models;

namespace CombatSimulator.Analysis;

// Samples legal matchups, derives contextual weights, and validates them in timed fights.
public static class TimeBasedAnalysisRunner
{
    private const double AttackSpeedUnit = 0.01;
    private const double ArmorPenetrationUnit = 0.01;
    private const double CriticalChanceUnit = 0.01;
    private const double CriticalDamageUnit = 0.01;
    private const double HealthRegenUnit = 1;
    private const double LifeStealUnit = 0.01;
    public const double CriticalDamageMinimumChance = 0.10;
    private const int RetainedStrongestBuilds = 20;

    // Runs weight sampling and deterministic combat validation with one reproducible seed.
    public static SimulationAnalysisResult Analyze(
        GameData gameData,
        int fights,
        int seed,
        LoadoutGenerationMode mode = LoadoutGenerationMode.RandomPieces)
    {
        if (fights <= 0)
            throw new ArgumentOutOfRangeException(nameof(fights));

        LoadoutGenerator generator = new(gameData, seed, mode, checked(fights * 2));
        double[] healthWeights = new double[fights];
        double[] weaponDamageWeights = new double[fights];
        double[] attackSpeedWeights = new double[fights];
        double[] armorWeights = new double[fights];
        double[] armorPenetrationWeights = new double[fights];
        double[] criticalChanceWeights = new double[fights];
        List<double> criticalDamageWeights = new(fights);
        double[] healthRegenWeights = new double[fights];
        double[] lifeStealWeights = new double[fights];
        List<AttackSpeedDiagnosticEntry> strongestBuilds = new(RetainedStrongestBuilds);
        Dictionary<(string Weapon, string Profile), DpsAccumulator> dpsAccumulators = [];
        WeightedLoadout?[] minimums = new WeightedLoadout?[9];
        WeightedLoadout?[] maximums = new WeightedLoadout?[9];

        int stalemates = 0;
        int draws = 0;
        int completedFights = 0;
        double completedDurationTotal = 0;
        double shortestCompletedFight = double.PositiveInfinity;
        double longestCompletedFight = 0;
        FightDiagnosticEntry? shortestFightDiagnostic = null;
        FightDiagnosticEntry? longestFightDiagnostic = null;
        FightDiagnosticEntry? maximumHealthRegenDiagnostic = null;
        int outcomeAgreements = 0;
        int armorOutcomeAgreements = 0;
        int armorPenetrationOutcomeAgreements = 0;
        int criticalChanceOutcomeAgreements = 0;
        int criticalDamageOutcomeAgreements = 0;
        int healthRegenOutcomeAgreements = 0;
        int lifeStealOutcomeAgreements = 0;

        for (int index = 0; index < fights; index++)
        {
            Loadout playerA = generator.Generate();
            Loadout playerB = generator.Generate();

            int combatSeed = unchecked(seed * 397 ^ index * 1_000_003);
            (
                double health,
                double weaponDamage,
                double attackSpeed,
                double armor,
                double armorPenetration,
                double criticalChance,
                double criticalDamage) =
                CalculateWeights(
                    playerA,
                    gameData.CombatConfig,
                    playerB.Stats[AttributeId.Armor],
                    playerB.Stats[AttributeId.ArmorPenetration]);
            healthWeights[index] = health;
            weaponDamageWeights[index] = weaponDamage;
            attackSpeedWeights[index] = attackSpeed;
            armorWeights[index] = armor;
            armorPenetrationWeights[index] = armorPenetration;
            criticalChanceWeights[index] = criticalChance;
            bool criticalDamageEligible =
                playerA.Stats[AttributeId.CriticalChance] >= CriticalDamageMinimumChance;
            if (criticalDamageEligible)
            {
                criticalDamageWeights.Add(criticalDamage);
                if (minimums[6] is null || criticalDamage < minimums[6]!.Weight)
                {
                    minimums[6] = new WeightedLoadout
                    {
                        Loadout = playerA,
                        Weight = criticalDamage
                    };
                }
                if (maximums[6] is null || criticalDamage > maximums[6]!.Weight)
                {
                    maximums[6] = new WeightedLoadout
                    {
                        Loadout = playerA,
                        Weight = criticalDamage
                    };
                }
            }
            double cycleDamage = playerA.AttackProfile.Steps.Sum(step =>
                DamageCalculator.CalculateRawDamage(playerA.Stats, step, gameData.CombatConfig));
            double baseCycleDuration = playerA.AttackProfile.Steps.Sum(step => step.TotalDuration);
            double speedFactor = 1 + playerA.Stats[AttributeId.AttackSpeed];
            double expectedCriticalMultiplier = CalculateExpectedCriticalMultiplier(playerA.Stats);
            AttackSpeedDiagnosticEntry diagnostic = new()
            {
                Weight = attackSpeed,
                CycleDamage = cycleDamage * expectedCriticalMultiplier,
                DamagePerSecond = cycleDamage * expectedCriticalMultiplier * speedFactor / baseCycleDuration,
                Loadout = playerA
            };
            RetainStrongestBuild(strongestBuilds, diagnostic);
            AddDpsSample(dpsAccumulators, diagnostic);
            UpdateExtremes(
                minimums,
                maximums,
                playerA,
                [
                    health,
                    weaponDamage,
                    attackSpeed,
                    armor,
                    armorPenetration,
                    criticalChance
                ]);

            TimedCombatResult baseFight =
                TimeBasedCombatSimulator.Simulate(
                    playerA,
                    playerB,
                    gameData.CombatConfig,
                    randomSeed: combatSeed);
            if (baseFight.Outcome == CombatOutcome.Stalemate)
            {
                stalemates++;
            }
            else
            {
                completedFights++;
                completedDurationTotal += baseFight.Duration;
                if (baseFight.Duration < shortestCompletedFight)
                {
                    shortestCompletedFight = baseFight.Duration;
                    shortestFightDiagnostic = new FightDiagnosticEntry
                    {
                        PlayerA = playerA,
                        PlayerB = playerB,
                        Fight = baseFight
                    };
                }
                if (baseFight.Duration > longestCompletedFight)
                {
                    longestCompletedFight = baseFight.Duration;
                    longestFightDiagnostic = new FightDiagnosticEntry
                    {
                        PlayerA = playerA,
                        PlayerB = playerB,
                        Fight = baseFight
                    };
                }
                if (baseFight.Outcome == CombatOutcome.Draw)
                    draws++;
            }

            Loadout healthRegenBuffed = WithAddedStat(
                playerA,
                AttributeId.HealthRegen,
                HealthRegenUnit);
            TimedCombatResult healthRegenFight = TimeBasedCombatSimulator.Simulate(
                healthRegenBuffed,
                playerB,
                gameData.CombatConfig,
                randomSeed: combatSeed);
            double healthRegenWeight =
                baseFight.PlayerAAdditionalRegenHealingOpportunity * health;
            healthRegenWeights[index] = healthRegenWeight;
            if (minimums[7] is null || healthRegenWeight < minimums[7]!.Weight)
                minimums[7] = new WeightedLoadout { Loadout = playerA, Weight = healthRegenWeight };
            if (maximums[7] is null || healthRegenWeight > maximums[7]!.Weight)
            {
                maximums[7] = new WeightedLoadout { Loadout = playerA, Weight = healthRegenWeight };
                maximumHealthRegenDiagnostic = new FightDiagnosticEntry
                {
                    PlayerA = playerA,
                    PlayerB = playerB,
                    Fight = baseFight,
                    HealthWeight = health,
                    HealthRegenWeight = healthRegenWeight
                };
            }

            Loadout healthRegenEquivalentAttackPower = WithAddedStat(
                playerA,
                AttributeId.AttackPower,
                healthRegenWeight);
            CombatOutcome healthRegenAttackPowerOutcome = TimeBasedCombatSimulator
                .Simulate(
                    healthRegenEquivalentAttackPower,
                    playerB,
                    gameData.CombatConfig,
                    randomSeed: combatSeed)
                .Outcome;
            if (healthRegenFight.Outcome == healthRegenAttackPowerOutcome)
                healthRegenOutcomeAgreements++;

            double lifeStealWeight =
                baseFight.PlayerAAdditionalLifeStealHealingOpportunity * health;
            lifeStealWeights[index] = lifeStealWeight;
            if (minimums[8] is null || lifeStealWeight < minimums[8]!.Weight)
                minimums[8] = new WeightedLoadout { Loadout = playerA, Weight = lifeStealWeight };
            if (maximums[8] is null || lifeStealWeight > maximums[8]!.Weight)
                maximums[8] = new WeightedLoadout { Loadout = playerA, Weight = lifeStealWeight };

            Loadout lifeStealBuffed = WithAddedStat(
                playerA,
                AttributeId.LifeSteal,
                LifeStealUnit);
            CombatOutcome lifeStealOutcome = TimeBasedCombatSimulator
                .Simulate(lifeStealBuffed, playerB, gameData.CombatConfig, randomSeed: combatSeed)
                .Outcome;
            Loadout lifeStealEquivalentAttackPower = WithAddedStat(
                playerA,
                AttributeId.AttackPower,
                lifeStealWeight);
            CombatOutcome lifeStealAttackPowerOutcome = TimeBasedCombatSimulator
                .Simulate(
                    lifeStealEquivalentAttackPower,
                    playerB,
                    gameData.CombatConfig,
                    randomSeed: combatSeed)
                .Outcome;
            if (lifeStealOutcome == lifeStealAttackPowerOutcome)
                lifeStealOutcomeAgreements++;

            Loadout attackSpeedBuffed = WithAddedStat(playerA, AttributeId.AttackSpeed, AttackSpeedUnit);
            Loadout attackPowerBuffed = WithAddedStat(playerA, AttributeId.AttackPower, attackSpeed);
            CombatOutcome attackSpeedOutcome = TimeBasedCombatSimulator
                .Simulate(attackSpeedBuffed, playerB, gameData.CombatConfig, randomSeed: combatSeed)
                .Outcome;
            CombatOutcome attackPowerOutcome = TimeBasedCombatSimulator
                .Simulate(attackPowerBuffed, playerB, gameData.CombatConfig, randomSeed: combatSeed)
                .Outcome;
            if (attackSpeedOutcome == attackPowerOutcome)
                outcomeAgreements++;

            Loadout armorBuffed = WithAddedStat(playerA, AttributeId.Armor, 1);
            Loadout armorEquivalentAttackPower = WithAddedStat(playerA, AttributeId.AttackPower, armor);
            CombatOutcome armorOutcome = TimeBasedCombatSimulator
                .Simulate(armorBuffed, playerB, gameData.CombatConfig, randomSeed: combatSeed)
                .Outcome;
            CombatOutcome armorAttackPowerOutcome = TimeBasedCombatSimulator
                .Simulate(armorEquivalentAttackPower, playerB, gameData.CombatConfig, randomSeed: combatSeed)
                .Outcome;
            if (armorOutcome == armorAttackPowerOutcome)
                armorOutcomeAgreements++;

            Loadout armorPenetrationBuffed = WithAddedStat(
                playerA,
                AttributeId.ArmorPenetration,
                ArmorPenetrationUnit);
            Loadout armorPenetrationEquivalentAttackPower = WithAddedStat(
                playerA,
                AttributeId.AttackPower,
                armorPenetration);
            CombatOutcome armorPenetrationOutcome = TimeBasedCombatSimulator
                .Simulate(armorPenetrationBuffed, playerB, gameData.CombatConfig, randomSeed: combatSeed)
                .Outcome;
            CombatOutcome armorPenetrationAttackPowerOutcome = TimeBasedCombatSimulator
                .Simulate(
                    armorPenetrationEquivalentAttackPower,
                    playerB,
                    gameData.CombatConfig,
                    randomSeed: combatSeed)
                .Outcome;
            if (armorPenetrationOutcome == armorPenetrationAttackPowerOutcome)
                armorPenetrationOutcomeAgreements++;

            Loadout criticalChanceBuffed = WithAddedStat(
                playerA,
                AttributeId.CriticalChance,
                CriticalChanceUnit);
            Loadout criticalChanceEquivalentAttackPower = WithAddedStat(
                playerA,
                AttributeId.AttackPower,
                criticalChance);
            CombatOutcome criticalChanceOutcome = TimeBasedCombatSimulator
                .Simulate(criticalChanceBuffed, playerB, gameData.CombatConfig, randomSeed: combatSeed)
                .Outcome;
            CombatOutcome criticalChanceAttackPowerOutcome = TimeBasedCombatSimulator
                .Simulate(
                    criticalChanceEquivalentAttackPower,
                    playerB,
                    gameData.CombatConfig,
                    randomSeed: combatSeed)
                .Outcome;
            if (criticalChanceOutcome == criticalChanceAttackPowerOutcome)
                criticalChanceOutcomeAgreements++;

            if (criticalDamageEligible)
            {
                Loadout criticalDamageBuffed = WithAddedStat(
                    playerA,
                    AttributeId.CriticalDamage,
                    CriticalDamageUnit);
                Loadout criticalDamageEquivalentAttackPower = WithAddedStat(
                    playerA,
                    AttributeId.AttackPower,
                    criticalDamage);
                CombatOutcome criticalDamageOutcome = TimeBasedCombatSimulator
                    .Simulate(
                        criticalDamageBuffed,
                        playerB,
                        gameData.CombatConfig,
                        randomSeed: combatSeed)
                    .Outcome;
                CombatOutcome criticalDamageAttackPowerOutcome = TimeBasedCombatSimulator
                    .Simulate(
                        criticalDamageEquivalentAttackPower,
                        playerB,
                        gameData.CombatConfig,
                        randomSeed: combatSeed)
                    .Outcome;
                if (criticalDamageOutcome == criticalDamageAttackPowerOutcome)
                    criticalDamageOutcomeAgreements++;
            }
        }

        if (criticalDamageWeights.Count == 0)
        {
            throw new InvalidOperationException(
                "No sampled builds met the 10% Critical Chance requirement for Critical Damage weighting.");
        }

        return new SimulationAnalysisResult
        {
            SimulatedFights = fights,
            CompletedFights = completedFights,
            Stalemates = stalemates,
            Draws = draws,
            AverageCompletedFightDuration = completedFights == 0 ? 0 : completedDurationTotal / completedFights,
            ShortestCompletedFightDuration = completedFights == 0 ? 0 : shortestCompletedFight,
            LongestCompletedFightDuration = completedFights == 0 ? 0 : longestCompletedFight,
            AttackSpeedValidationComparisons = fights,
            AttackSpeedOutcomeAgreements = outcomeAgreements,
            ArmorValidationComparisons = fights,
            ArmorOutcomeAgreements = armorOutcomeAgreements,
            ArmorPenetrationValidationComparisons = fights,
            ArmorPenetrationOutcomeAgreements = armorPenetrationOutcomeAgreements,
            CriticalChanceValidationComparisons = fights,
            CriticalChanceOutcomeAgreements = criticalChanceOutcomeAgreements,
            CriticalDamageValidationComparisons = criticalDamageWeights.Count,
            CriticalDamageOutcomeAgreements = criticalDamageOutcomeAgreements,
            HealthRegenValidationComparisons = fights,
            HealthRegenOutcomeAgreements = healthRegenOutcomeAgreements,
            LifeStealValidationComparisons = fights,
            LifeStealOutcomeAgreements = lifeStealOutcomeAgreements,
            StrongestAttackSpeedBuilds = strongestBuilds
                .OrderByDescending(entry => entry.DamagePerSecond)
                .ToArray(),
            WeaponProfileDpsSummaries = CreateDpsSummaries(dpsAccumulators),
            ShortestFight = shortestFightDiagnostic,
            LongestFight = longestFightDiagnostic,
            MaximumHealthRegenWeightFight = maximumHealthRegenDiagnostic!,
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
                maximums[2]!),
            Armor = CreateDistribution(
                AttributeId.Armor,
                "Armor",
                "1 Armor",
                armorWeights,
                minimums[3]!,
                maximums[3]!),
            ArmorPenetration = CreateDistribution(
                AttributeId.ArmorPenetration,
                "Armor Penetration (1%)",
                "1 percentage point",
                armorPenetrationWeights,
                minimums[4]!,
                maximums[4]!),
            CriticalChance = CreateDistribution(
                AttributeId.CriticalChance,
                "Critical Chance (1%)",
                "1 percentage point",
                criticalChanceWeights,
                minimums[5]!,
                maximums[5]!),
            CriticalDamage = CreateDistribution(
                AttributeId.CriticalDamage,
                "Critical Damage (1%)*",
                "1 percentage point",
                criticalDamageWeights.ToArray(),
                minimums[6]!,
                maximums[6]!),
            HealthRegen = CreateDistribution(
                AttributeId.HealthRegen,
                "Health Regen (1 HP/s)",
                "1 Health per second",
                healthRegenWeights,
                minimums[7]!,
                maximums[7]!),
            LifeSteal = CreateDistribution(
                AttributeId.LifeSteal,
                "Life Steal (1%)",
                "1 percentage point",
                lifeStealWeights,
                minimums[8]!,
                maximums[8]!)
        };
    }

    // Retains only the highest-DPS builds needed by the interactive diagnostic.
    private static void RetainStrongestBuild(
        List<AttackSpeedDiagnosticEntry> strongestBuilds,
        AttackSpeedDiagnosticEntry candidate)
    {
        if (strongestBuilds.Count < RetainedStrongestBuilds)
        {
            strongestBuilds.Add(candidate);
            return;
        }

        int weakestIndex = 0;
        for (int index = 1; index < strongestBuilds.Count; index++)
        {
            if (strongestBuilds[index].DamagePerSecond < strongestBuilds[weakestIndex].DamagePerSecond)
                weakestIndex = index;
        }

        if (candidate.DamagePerSecond > strongestBuilds[weakestIndex].DamagePerSecond)
            strongestBuilds[weakestIndex] = candidate;
    }

    // Adds one compact DPS value to its weapon/profile aggregate.
    private static void AddDpsSample(
        Dictionary<(string Weapon, string Profile), DpsAccumulator> accumulators,
        AttackSpeedDiagnosticEntry diagnostic)
    {
        var key = (diagnostic.Weapon.Name, diagnostic.Loadout.AttackProfile.Name);
        if (!accumulators.TryGetValue(key, out DpsAccumulator? accumulator))
        {
            accumulator = new DpsAccumulator();
            accumulators.Add(key, accumulator);
        }

        accumulator.Add(diagnostic.DamagePerSecond, diagnostic.Weight);
    }

    // Finalizes exact mean, percentile, maximum, and contextual weight diagnostics.
    private static IReadOnlyList<WeaponProfileDpsSummary> CreateDpsSummaries(
        Dictionary<(string Weapon, string Profile), DpsAccumulator> accumulators)
    {
        return accumulators
            .Select(entry => entry.Value.CreateSummary(entry.Key.Weapon, entry.Key.Profile))
            .OrderByDescending(summary => summary.MaximumDamagePerSecond)
            .ToArray();
    }

    // Derives smooth marginal weights from one profile's complete three-hit damage cycle.
    public static (
        double Health,
        double WeaponDamage,
        double AttackSpeed,
        double Armor,
        double ArmorPenetration,
        double CriticalChance,
        double CriticalDamage) CalculateWeights(
        Loadout loadout,
        CombatConfig config,
        double? targetArmor = null,
        double incomingArmorPenetration = 0)
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
        double armorMarginalFraction = CalculateArmorMarginalFraction(
            loadout.Stats[AttributeId.Armor],
            config.PhysicalArmorConstant,
            incomingArmorPenetration);
        double armorPenetrationWeight = CalculateArmorPenetrationWeight(
            cycleDamage,
            attackPowerCycleContribution,
            targetArmor ?? loadout.Stats[AttributeId.Armor],
            loadout.Stats[AttributeId.ArmorPenetration],
            config.PhysicalArmorConstant);
        double expectedCriticalMultiplier = CalculateExpectedCriticalMultiplier(loadout.Stats);
        double criticalBonus = DamageCalculator.BaseCriticalDamageBonus +
            loadout.Stats[AttributeId.CriticalDamage];
        double effectiveCriticalChance = Math.Min(loadout.Stats[AttributeId.CriticalChance], 1);
        double criticalChanceIncrease = Math.Min(
            loadout.Stats[AttributeId.CriticalChance] + CriticalChanceUnit,
            1) - effectiveCriticalChance;
        double criticalChanceWeight = cycleDamage * criticalBonus * criticalChanceIncrease /
            (attackPowerCycleContribution * expectedCriticalMultiplier);
        double criticalDamageWeight = cycleDamage * effectiveCriticalChance *
            CriticalDamageUnit /
            (attackPowerCycleContribution * expectedCriticalMultiplier);

        return (
            cycleDamage / (loadout.Stats.MaxHealth * attackPowerCycleContribution),
            multiplierTotal / attackPowerCycleContribution,
            cycleDamage * AttackSpeedUnit / (speedFactor * attackPowerCycleContribution),
            cycleDamage * armorMarginalFraction / attackPowerCycleContribution,
            armorPenetrationWeight,
            criticalChanceWeight,
            criticalDamageWeight);
    }

    // Calculates the smooth expected damage multiplier from chance and critical bonus.
    private static double CalculateExpectedCriticalMultiplier(CharacterStats stats)
    {
        double criticalChance = stats[AttributeId.CriticalChance];
        if (criticalChance < 0)
            throw new InvalidOperationException("Critical Chance cannot be negative.");

        double effectiveChance = Math.Min(criticalChance, 1);
        double criticalBonus = DamageCalculator.BaseCriticalDamageBonus +
            stats[AttributeId.CriticalDamage];
        return 1 + effectiveChance * criticalBonus;
    }

    // Converts a smooth one-percentage-point penetration gain into equivalent Attack Power.
    private static double CalculateArmorPenetrationWeight(
        double cycleDamage,
        double attackPowerCycleContribution,
        double targetArmor,
        double currentPenetration,
        double armorConstant)
    {
        if (currentPenetration < 0 || currentPenetration + ArmorPenetrationUnit >= 1)
            throw new InvalidOperationException("Armor Penetration must remain between 0% and 99%.");

        double currentEffectiveArmor = currentPenetration > 0
            ? targetArmor * (1 - currentPenetration)
            : targetArmor;
        double increasedEffectiveArmor = targetArmor *
            (1 - (currentPenetration + ArmorPenetrationUnit));
        double currentMultiplier = CalculatePhysicalDamageMultiplier(
            currentEffectiveArmor,
            armorConstant);
        double increasedMultiplier = CalculatePhysicalDamageMultiplier(
            increasedEffectiveArmor,
            armorConstant);

        return cycleDamage * (increasedMultiplier - currentMultiplier) /
            (attackPowerCycleContribution * currentMultiplier);
    }

    // Evaluates the continuous physical-damage multiplier used for smooth weighting.
    private static double CalculatePhysicalDamageMultiplier(double armor, double armorConstant)
    {
        return 1 -
            (armorConstant * armor / (1 + armorConstant * Math.Abs(armor)));
    }

    // Calculates the fractional physical effective-Health gain from one additional Armor.
    private static double CalculateArmorMarginalFraction(
        double armor,
        double armorConstant,
        double incomingArmorPenetration)
    {
        if (incomingArmorPenetration < 0 || incomingArmorPenetration >= 1)
            throw new InvalidOperationException("Incoming Armor Penetration must remain between 0% and 99%.");

        double retainedArmorFraction = incomingArmorPenetration > 0
            ? 1 - incomingArmorPenetration
            : 1;
        double effectiveArmor = armor * retainedArmorFraction;
        if (effectiveArmor >= 0)
        {
            return retainedArmorFraction * armorConstant /
                (1 + armorConstant * effectiveArmor);
        }

        return retainedArmorFraction *
            ((2 * armorConstant / (1 - 2 * armorConstant * effectiveArmor)) -
            (armorConstant / (1 - armorConstant * effectiveArmor)));
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

    // Accumulates compact DPS samples without retaining their complete loadouts.
    private sealed class DpsAccumulator
    {
        private readonly List<double> values = [];
        private double maximum = double.NegativeInfinity;
        private double attackSpeedWeightAtMaximum;

        // Records one DPS sample and the Attack Speed weight attached to a new maximum.
        public void Add(double damagePerSecond, double attackSpeedWeight)
        {
            values.Add(damagePerSecond);
            if (damagePerSecond > maximum)
            {
                maximum = damagePerSecond;
                attackSpeedWeightAtMaximum = attackSpeedWeight;
            }
        }

        // Sorts retained numeric values and produces the immutable report summary.
        public WeaponProfileDpsSummary CreateSummary(string weaponName, string profileName)
        {
            double[] sortedValues = values.Order().ToArray();
            return new WeaponProfileDpsSummary
            {
                WeaponName = weaponName,
                ProfileName = profileName,
                Samples = sortedValues.Length,
                MeanDamagePerSecond = sortedValues.Average(),
                NinetyFifthPercentileDamagePerSecond = Percentile(sortedValues, 0.95),
                MaximumDamagePerSecond = sortedValues[^1],
                AttackSpeedWeightAtMaximum = attackSpeedWeightAtMaximum
            };
        }
    }
}
