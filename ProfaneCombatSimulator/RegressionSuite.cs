using CombatSimulator.Analysis;
using CombatSimulator.Combat;
using CombatSimulator.Data;
using CombatSimulator.Models;

namespace CombatSimulator.Validation;

// Runs dependency-free regression checks for formulas, equipment rules, timing, and determinism.
public static class RegressionSuite
{
    // Executes every regression check and throws immediately on the first failure.
    public static void Run()
    {
        RunCheck("Typed attributes reject duplicates", TestDuplicateAttribute);
        RunCheck("Profile multipliers affect only Weapon Damage", TestDamageCalculation);
        RunCheck("Two-handed loadouts exclude off-hands and bows", TestTwoHandedLoadout);
        RunCheck("Random mode permits mixed armor sets", TestRandomArmorGeneration);
        RunCheck("Closed-set mode is complete and evenly distributed", TestClosedSetGeneration);
        RunCheck("Sheet exclusions apply to sampled loadouts", TestSheetExclusionGeneration);
        RunCheck("Incomplete armor sets are rejected", TestIncompleteArmorSet);
        RunCheck("Attack Speed scales contact timing", TestAttackSpeedTiming);
        RunCheck("Combo order repeats 1-2-3", TestComboOrder);
        RunCheck("Simultaneous lethal contacts draw", TestSimultaneousDraw);
        RunCheck("Time limit produces a stalemate", TestStalemate);
        RunCheck("Health regeneration ticks after damage and cannot revive", TestHealthRegeneration);
        RunCheck("Life Steal resolves after simultaneous overkill damage", TestLifeSteal);
        RunCheck("Skill ignore checkbox accepts numeric sheet values", TestSkillIgnoreCheckbox);
        RunCheck("Critical Damage reports unavailable without enough Critical Chance", TestCriticalDamageWithoutEligibleSamples);
        RunCheck("Skill output uses Magic Power as its base", TestSkillOutputAnalysis);
        RunCheck("Profile-aware weights use one-percent Attack Speed units", TestContextualWeights);
        RunCheck("Seeded time-based analysis is deterministic", TestDeterministicAnalysis);
        Console.WriteLine("All regression checks passed.");
    }

    // Confirms duplicate spreadsheet attributes cannot silently overwrite one another.
    private static void TestDuplicateAttribute()
    {
        Item item = NewItem("Test", EquipmentSlot.Helmet, (AttributeId.MaxHealth, 10));
        AssertThrows<InvalidDataException>(() => item.AddAttribute(AttributeId.MaxHealth, 20));
    }

    // Confirms AP is constant per hit while only Weapon Damage uses the step multiplier.
    private static void TestDamageCalculation()
    {
        CharacterStats stats = BuildStats(attackPower: 10, health: 1000, weaponDamage: 100);
        AttackStep step = NewStep(1, anticipation: 0.5, contact: 0.1, recovery: 0.4, multiplier: 1.2);

        AssertClose(125, DamageCalculator.CalculateRawDamage(stats, step, DefaultConfig()));
        AssertClose(125, DamageCalculator.CalculateDamage(stats, step, DefaultConfig()));
        AssertClose(83, DamageCalculator.CalculateDamage(stats, step, DefaultConfig(), 100));
        AssertClose(167, DamageCalculator.CalculateDamage(stats, step, DefaultConfig(), -100));
        AssertClose(0, DamageCalculator.CalculateDamage(stats, step, DefaultConfig(), 1_000_000_000));
        CharacterStats penetratingStats = BuildStats(
            attackPower: 10,
            health: 1000,
            weaponDamage: 100,
            armorPenetration: 0.10);
        AssertClose(90, DamageCalculator.CalculateEffectiveArmor(100, 0.10));
        AssertClose(86, DamageCalculator.CalculateDamage(
            penetratingStats,
            step,
            DefaultConfig(),
            100));
        CharacterStats criticalStats = BuildStats(
            attackPower: 10,
            health: 1000,
            weaponDamage: 100,
            criticalChance: 0.10);
        AssertClose(187, DamageCalculator.CalculateDamage(
            criticalStats, step, DefaultConfig(), criticalRoll: 1000));
        AssertClose(125, DamageCalculator.CalculateDamage(
            criticalStats, step, DefaultConfig(), criticalRoll: 1001));
        CharacterStats strongerCriticalStats = BuildStats(
            attackPower: 10,
            health: 1000,
            weaponDamage: 100,
            criticalChance: 0.10,
            criticalDamage: 0.50);
        AssertClose(250, DamageCalculator.CalculateDamage(
            strongerCriticalStats, step, DefaultConfig(), criticalRoll: 1000));
    }

    // Confirms full loadouts obey two-handed rules and never select excluded bows.
    private static void TestTwoHandedLoadout()
    {
        GameData gameData = CreateTestGameData();
        Loadout loadout = new LoadoutGenerator(gameData, 7).Generate();

        if (loadout.Items.Any(item => item.Slot == EquipmentSlot.OffHand))
            throw new Exception("A two-handed loadout equipped an off-hand.");
        if (loadout.Items.Any(item => item.IsBow))
            throw new Exception("A bow entered the time-based loadout pool.");
        if (loadout.Items.Count(item => item.Slot == EquipmentSlot.Ring) != 2)
            throw new Exception("The loadout did not equip two rings.");
    }

    // Confirms unrestricted generation can combine armor pieces from different sets.
    private static void TestRandomArmorGeneration()
    {
        LoadoutGenerator generator = new(CreateTestGameData(), 17);
        bool foundMixedSet = Enumerable.Range(0, 100)
            .Select(_ => generator.Generate())
            .Any(loadout => loadout.Items
                .Where(item => item.ArmorSetName is not null)
                .Select(item => item.ArmorSetName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() > 1);
        if (!foundMixedSet)
            throw new Exception("Random generation did not produce any mixed-set armor loadout.");
    }

    // Confirms closed sets remain complete and remainder uses differ by no more than one.
    private static void TestClosedSetGeneration()
    {
        const int loadoutCount = 21;
        LoadoutGenerator first = new(
            CreateTestGameData(), 23, LoadoutGenerationMode.ClosedArmorSet, loadoutCount);
        LoadoutGenerator second = new(
            CreateTestGameData(), 23, LoadoutGenerationMode.ClosedArmorSet, loadoutCount);
        Loadout[] firstRun = Enumerable.Range(0, loadoutCount).Select(_ => first.Generate()).ToArray();
        Loadout[] secondRun = Enumerable.Range(0, loadoutCount).Select(_ => second.Generate()).ToArray();

        string[] firstSequence = firstRun.Select(GetOnlyArmorSet).ToArray();
        string[] secondSequence = secondRun.Select(GetOnlyArmorSet).ToArray();
        if (!firstSequence.SequenceEqual(secondSequence))
            throw new Exception("Closed-set scheduling is not reproducible.");

        int[] uses = firstSequence.GroupBy(name => name).Select(group => group.Count()).ToArray();
        if (uses.Max() - uses.Min() > 1)
            throw new Exception("Closed-set usage differs by more than one loadout.");
        if (firstRun.Any(loadout => loadout.Items.Count(item => item.ArmorSetName is not null) != 5))
            throw new Exception("A closed-set loadout did not equip exactly five armor pieces.");
        if (firstRun.Select(loadout => loadout.Items.Single(item =>
                item.Slot is EquipmentSlot.OneHandedWeapon or EquipmentSlot.TwoHandedWeapon).Name)
            .Distinct().Count() < 2)
        {
            throw new Exception("Closed-set generation did not vary weapons independently.");
        }
        if (firstRun.Select(loadout => loadout.Items.Single(item => item.Slot == EquipmentSlot.Amulet).Name)
            .Distinct().Count() < 2)
        {
            throw new Exception("Closed-set generation did not vary accessories independently.");
        }

        const int divisibleCount = 20;
        LoadoutGenerator divisible = new(
            CreateTestGameData(), 23, LoadoutGenerationMode.ClosedArmorSet, divisibleCount);
        int[] divisibleUses = Enumerable.Range(0, divisibleCount)
            .Select(_ => GetOnlyArmorSet(divisible.Generate()))
            .GroupBy(name => name)
            .Select(group => group.Count())
            .ToArray();
        if (divisibleUses.Any(usesPerSet => usesPerSet != 10))
            throw new Exception("Divisible closed-set usage was not exactly equal.");

    }

    // Returns the single armor-set identity required from a closed-set loadout.
    private static string GetOnlyArmorSet(Loadout loadout)
    {
        string[] sets = loadout.Items
            .Where(item => item.ArmorSetName is not null)
            .Select(item => item.ArmorSetName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sets.Length != 1)
            throw new Exception("A closed-set loadout mixed armor sets.");
        return sets[0];
    }

    // Confirms sheet-marked exclusions are removed from both random and closed-set generation.
    private static void TestSheetExclusionGeneration()
    {
        GameData gameData = CreateSheetExclusionTestGameData();
        LoadoutGenerator randomGenerator = new(gameData, 31);
        Loadout[] randomLoadouts = Enumerable.Range(0, 100)
            .Select(_ => randomGenerator.Generate())
            .ToArray();
        if (randomLoadouts.Any(loadout => loadout.Items.Any(item => item.ExcludeFromSimulation)))
            throw new Exception("Random generation equipped a sheet-excluded item.");

        const int closedLoadoutCount = 40;
        LoadoutGenerator closedGenerator = new(
            gameData,
            31,
            LoadoutGenerationMode.ClosedArmorSet,
            closedLoadoutCount);
        Loadout[] closedLoadouts = Enumerable.Range(0, closedLoadoutCount)
            .Select(_ => closedGenerator.Generate())
            .ToArray();
        if (closedLoadouts.Any(loadout => loadout.Items.Any(item => item.ExcludeFromSimulation)))
            throw new Exception("Closed-set generation equipped a sheet-excluded item.");
        if (closedLoadouts.Select(GetOnlyArmorSet)
            .Any(setName => string.Equals(setName, "Silk", StringComparison.OrdinalIgnoreCase)))
        {
            throw new Exception("Closed-set generation scheduled a sheet-excluded armor set.");
        }

        GameData noOffHands = CreateNoOffHandTestGameData(gameData);
        LoadoutGenerator noOffHandGenerator = new(noOffHands, 41);
        Loadout[] noOffHandLoadouts = Enumerable.Range(0, 100)
            .Select(_ => noOffHandGenerator.Generate())
            .ToArray();
        if (noOffHandLoadouts.Any(loadout => loadout.Items.Any(item => item.Slot == EquipmentSlot.OffHand)))
            throw new Exception("Generation equipped a sheet-excluded off-hand.");
        if (noOffHandLoadouts.Any(loadout => loadout.Items.Any(item => item.Slot == EquipmentSlot.OneHandedWeapon)))
            throw new Exception("Generation equipped a one-handed weapon when no off-hands were available.");
    }

    // Confirms malformed sheet data cannot silently produce partial closed sets.
    private static void TestIncompleteArmorSet()
    {
        GameData valid = CreateTestGameData();
        GameData incomplete = new()
        {
            StartingStats = valid.StartingStats,
            CombatConfig = valid.CombatConfig,
            AttackProfiles = valid.AttackProfiles,
            Items = valid.Items.Where(item => item.Name != "B Greaves").ToArray(),
            Skills = valid.Skills
        };
        AssertThrows<InvalidDataException>(() => GameDataValidator.Validate(incomplete));
    }

    // Confirms a 100% bonus halves the first Anticipation and contact timestamp.
    private static void TestAttackSpeedTiming()
    {
        GameData gameData = CreateTestGameData();
        Loadout baseLoadout = new LoadoutGenerator(gameData, 1).Generate();
        Loadout fastLoadout = WithAddedStat(baseLoadout, AttributeId.AttackSpeed, 1.0);

        CombatantState normal = new(baseLoadout);
        CombatantState fast = new(fastLoadout);
        AssertClose(0.5, normal.NextContactTime);
        AssertClose(0.25, fast.NextContactTime);
    }

    // Confirms advancing contacts cycles through attacks 1, 2, 3, then back to 1.
    private static void TestComboOrder()
    {
        Loadout loadout = new LoadoutGenerator(CreateTestGameData(), 1).Generate();
        CombatantState state = new(loadout);
        int[] observed = new int[4];
        for (int index = 0; index < observed.Length; index++)
        {
            observed[index] = state.CurrentStep.Sequence;
            state.AdvanceAfterContact();
        }

        if (!observed.SequenceEqual([1, 2, 3, 1]))
            throw new Exception("Combo order did not repeat 1-2-3-1.");
    }

    // Confirms equal-time lethal contacts are both applied before evaluating death.
    private static void TestSimultaneousDraw()
    {
        WeaponAttackProfile profile = CreateProfile();
        CharacterStats lethalStats = BuildStats(attackPower: 0, health: 10, weaponDamage: 100);
        Loadout playerA = NewLoadout("A", lethalStats, profile);
        Loadout playerB = NewLoadout("B", lethalStats, profile);

        TimedCombatResult result = TimeBasedCombatSimulator.Simulate(playerA, playerB, DefaultConfig());
        if (result.Outcome != CombatOutcome.Draw)
            throw new Exception("Simultaneous lethal contacts did not draw.");
        AssertClose(0.5, result.Duration);
    }

    // Confirms no-progress combat terminates at the configured duration limit.
    private static void TestStalemate()
    {
        WeaponAttackProfile profile = CreateProfile();
        CharacterStats harmless = BuildStats(attackPower: 0, health: 100, weaponDamage: 0);
        Loadout playerA = NewLoadout("A", harmless, profile);
        Loadout playerB = NewLoadout("B", harmless, profile);

        TimedCombatResult result = TimeBasedCombatSimulator.Simulate(
            playerA,
            playerB,
            DefaultConfig(),
            maximumDuration: 3);
        if (result.Outcome != CombatOutcome.Stalemate || result.TerminationReason != CombatTerminationReason.TimeLimit)
            throw new Exception("No-progress combat did not reach a time-limit stalemate.");
        AssertClose(3, result.Duration);
    }

    // Confirms one-second ceiling-rounded ticks follow damage and never revive lethal targets.
    private static void TestHealthRegeneration()
    {
        WeaponAttackProfile profile = CreateProfile();
        Loadout regenerating = NewLoadout(
            "Regenerating",
            BuildStats(0, 100, 0, healthRegen: 10.1),
            profile);
        Loadout damaging = NewLoadout("Damaging", BuildStats(0, 100, 20), profile);
        TimedCombatResult tickResult = TimeBasedCombatSimulator.Simulate(
            regenerating,
            damaging,
            DefaultConfig(),
            maximumDuration: 1);
        AssertClose(91, tickResult.PlayerARemainingHealth);
        AssertClose(11, tickResult.PlayerATotalHealing);
        AssertClose(1, tickResult.PlayerAAdditionalRegenHealingOpportunity);

        WeaponAttackProfile lethalAtOneSecond = new()
        {
            Name = "Lethal at one second",
            Steps =
            [
                NewStep(1, 1, 0.1, 0.4, 1),
                NewStep(2, 1, 0.1, 0.4, 1),
                NewStep(3, 1, 0.1, 0.4, 1)
            ]
        };
        Loadout doomed = NewLoadout(
            "Doomed",
            BuildStats(0, 50, 0, healthRegen: 100),
            lethalAtOneSecond);
        Loadout lethal = NewLoadout(
            "Lethal",
            BuildStats(0, 100, 100),
            lethalAtOneSecond);
        TimedCombatResult lethalResult = TimeBasedCombatSimulator.Simulate(
            doomed,
            lethal,
            DefaultConfig());
        if (lethalResult.Outcome != CombatOutcome.PlayerBWins)
            throw new Exception("Lethal damage did not end combat before regeneration.");
        AssertClose(0, lethalResult.PlayerATotalHealing);
        AssertClose(0, lethalResult.PlayerAAdditionalRegenHealingOpportunity);
    }

    // Confirms truncated overkill Life Steal can rescue both attackers before death checks.
    private static void TestLifeSteal()
    {
        WeaponAttackProfile profile = CreateProfile();
        CharacterStats stats = BuildStats(
            attackPower: 0,
            health: 100,
            weaponDamage: 150,
            lifeSteal: 0.50);
        Loadout playerA = NewLoadout("A", stats, profile);
        Loadout playerB = NewLoadout("B", stats, profile);
        TimedCombatResult result = TimeBasedCombatSimulator.Simulate(
            playerA,
            playerB,
            DefaultConfig(),
            maximumDuration: 0.5);

        if (result.Outcome != CombatOutcome.Stalemate)
            throw new Exception("Simultaneous Life Steal did not prevent lethal deaths.");
        AssertClose(25, result.PlayerARemainingHealth);
        AssertClose(75, result.PlayerATotalHealing);
        AssertClose(1, result.PlayerAAdditionalLifeStealHealingOpportunity);

        CombatantState truncationState = new(NewLoadout(
            "Truncation",
            BuildStats(0, 100, 0, lifeSteal: 0.10),
            profile));
        truncationState.ReceiveDamage(50);
        truncationState.ApplyLifeSteal(333);
        AssertClose(83, truncationState.CurrentHealth);
        AssertClose(33, truncationState.TotalHealing);
    }

    // Confirms Google Sheets numeric checkboxes are treated as ignored skills.
    private static void TestSkillIgnoreCheckbox()
    {
        System.Reflection.MethodInfo method = typeof(GameSheetParser).GetMethod(
            "IsIgnoredSkill",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new Exception("Could not find skill ignore parser.");
        if (!Equals(method.Invoke(null, ["1"]), true))
            throw new Exception("Numeric checked skill checkbox was not ignored.");
        if (!Equals(method.Invoke(null, ["TRUE"]), true))
            throw new Exception("Boolean checked skill checkbox was not ignored.");
        if (!Equals(method.Invoke(null, ["0"]), false))
            throw new Exception("Unchecked skill checkbox was ignored.");
    }

    // Confirms Critical Damage does not stop the run when no sampled build has enough Critical Chance.
    private static void TestCriticalDamageWithoutEligibleSamples()
    {
        GameData source = CreateTestGameData();
        CharacterStatsBuilder startingStats = new(source.StartingStats);
        startingStats.Set(AttributeId.CriticalChance, 0);
        GameData noCriticalChance = new()
        {
            StartingStats = startingStats.Build(),
            CombatConfig = source.CombatConfig,
            Items = source.Items
                .Select(RemoveCriticalChance)
                .ToArray(),
            AttackProfiles = source.AttackProfiles,
            Skills = source.Skills
        };

        SimulationAnalysisResult result = TimeBasedAnalysisRunner.Analyze(noCriticalChance, 20, 13);
        if (result.CriticalDamage.IsAvailable)
            throw new Exception("Critical Damage was available without eligible Critical Chance samples.");
        if (result.CriticalDamageValidationComparisons != 0)
            throw new Exception("Critical Damage retained comparisons without eligible samples.");
    }

    // Confirms caster output weights are reproducible and Magic Power increases skill damage.
    private static void TestSkillOutputAnalysis()
    {
        GameData gameData = CreateTestGameData();
        SkillOutputAnalysisResult first = SkillOutputAnalysisRunner.Analyze(
            gameData,
            50,
            99,
            LoadoutGenerationMode.RandomPieces,
            10);
        SkillOutputAnalysisResult second = SkillOutputAnalysisRunner.Analyze(
            gameData,
            50,
            99,
            LoadoutGenerationMode.RandomPieces,
            10);

        if (first.AverageBaseOutput <= 0)
            throw new Exception("Caster analysis produced no skill output.");
        if (first.CooldownReduction.MedianWeight <= 0)
            throw new Exception("Caster analysis did not value Cooldown Reduction.");
        if (first.ManaLimitedSamples < 0)
            throw new Exception("Caster mana diagnostics produced invalid sample counts.");
        AssertClose(first.CooldownReduction.MedianWeight, second.CooldownReduction.MedianWeight);
        AssertClose(first.MagicResist.MedianWeight, second.MagicResist.MedianWeight);
        AssertClose(first.MaxMana.MedianWeight, second.MaxMana.MedianWeight);
        AssertClose(first.ManaRegen.MedianWeight, second.ManaRegen.MedianWeight);
        AssertClose(first.ManaEfficiency.MedianWeight, second.ManaEfficiency.MedianWeight);
    }

    // Confirms profile-aware smooth weights and the one-percentage-point Attack Speed unit.
    private static void TestContextualWeights()
    {
        WeaponAttackProfile profile = CreateProfile();
        Loadout loadout = NewLoadout(
            "Test Weapon",
            BuildStats(attackPower: 10, health: 1000, weaponDamage: 100, armor: 100),
            profile);
        (
            double health,
            double weaponDamage,
            double attackSpeed,
            double armor,
            double armorPenetration,
            double criticalChance,
            double criticalDamage) =
            TimeBasedAnalysisRunner.CalculateWeights(loadout, DefaultConfig());

        AssertClose(2, weaponDamage);
        AssertClose(0.21, health);
        AssertClose(2.1, attackSpeed);
        AssertClose(0.7, armor);
        double currentMultiplier = 1.0 / 1.5;
        double increasedMultiplier = 1.0 / 1.495;
        double expectedArmorPenetration = 315 *
            (increasedMultiplier - currentMultiplier) / (1.5 * currentMultiplier);
        AssertClose(expectedArmorPenetration, armorPenetration);
        AssertClose(1.05, criticalChance);
        AssertClose(0, criticalDamage);
        (_, _, _, double armorAgainstPenetration, _, _, _) =
            TimeBasedAnalysisRunner.CalculateWeights(
                loadout,
                DefaultConfig(),
                targetArmor: 100,
                incomingArmorPenetration: 0.50);
        AssertClose(0.42, armorAgainstPenetration);
    }

    // Confirms identical seeds produce identical distributions and timing statistics.
    private static void TestDeterministicAnalysis()
    {
        GameData gameData = CreateTestGameData();
        SimulationAnalysisResult first = TimeBasedAnalysisRunner.Analyze(gameData, 100, 42);
        SimulationAnalysisResult second = TimeBasedAnalysisRunner.Analyze(gameData, 100, 42);

        AssertClose(first.Health.MedianWeight, second.Health.MedianWeight);
        AssertClose(first.AttackSpeed.MedianWeight, second.AttackSpeed.MedianWeight);
        AssertClose(first.ArmorPenetration.MedianWeight, second.ArmorPenetration.MedianWeight);
        AssertClose(first.CriticalChance.MedianWeight, second.CriticalChance.MedianWeight);
        AssertClose(first.CriticalDamage.MedianWeight, second.CriticalDamage.MedianWeight);
        AssertClose(first.HealthRegen.MedianWeight, second.HealthRegen.MedianWeight);
        AssertClose(first.LifeSteal.MedianWeight, second.LifeSteal.MedianWeight);
        AssertClose(first.AverageCompletedFightDuration, second.AverageCompletedFightDuration);
        AssertClose(first.ShortestCompletedFightDuration, second.ShortestCompletedFightDuration);
        AssertClose(first.LongestCompletedFightDuration, second.LongestCompletedFightDuration);
        AssertClose(first.ShortestFight!.Fight.Duration, first.ShortestCompletedFightDuration);
        AssertClose(first.LongestFight!.Fight.Duration, first.LongestCompletedFightDuration);
        AssertClose(
            first.MaximumHealthRegenWeightFight.HealthRegenWeight,
            first.HealthRegen.MaximumWeight);
        if (first.ShortestFight.PlayerA.Items.Count == 0 ||
            first.ShortestFight.PlayerB.Items.Count == 0 ||
            first.LongestFight.PlayerA.Items.Count == 0 ||
            first.LongestFight.PlayerB.Items.Count == 0)
        {
            throw new Exception("Fight diagnostics did not retain both complete builds.");
        }
        AssertClose(
            first.StrongestAttackSpeedBuilds.Max(entry => entry.DamagePerSecond),
            second.StrongestAttackSpeedBuilds.Max(entry => entry.DamagePerSecond));
        if (first.StrongestAttackSpeedBuilds.Any(entry => entry.DamagePerSecond <= 0))
            throw new Exception("A diagnostic build produced non-positive DPS.");
        if (first.StrongestAttackSpeedBuilds.Count != 20 ||
            first.WeaponProfileDpsSummaries.Sum(summary => summary.Samples) != 100)
        {
            throw new Exception("Compact diagnostics retained an unexpected sample count.");
        }
        if (first.CriticalDamageValidationComparisons != 100)
            throw new Exception("Eligible Critical Damage samples were not retained correctly.");
        if (first.Stalemates != second.Stalemates)
            throw new Exception("Seeded stalemate counts differ.");
        if (first.Draws != second.Draws)
            throw new Exception("Seeded draw counts differ.");
    }

    // Builds a minimal valid game dataset with one eligible weapon and one excluded bow.
    private static GameData CreateTestGameData()
    {
        CharacterStats startingStats = BuildStats(
            attackPower: 10,
            health: 1000,
            weaponDamage: 10,
            criticalChance: 0.10);
        WeaponAttackProfile profile = CreateProfile();
        List<Item> items =
        [
            NewArmor("A Helmet", EquipmentSlot.Helmet, "Set A", AttributeId.MaxHealth, 10),
            NewArmor("A Chest", EquipmentSlot.Chest, "Set A", AttributeId.MaxHealth, 20),
            NewArmor("A Gloves", EquipmentSlot.Gloves, "Set A", AttributeId.AttackPower, 5),
            NewArmor("A Leggings", EquipmentSlot.Leggings, "Set A", AttributeId.MaxHealth, 20),
            NewArmor("A Greaves", EquipmentSlot.Greaves, "Set A", AttributeId.AttackPower, 5),
            NewArmor("B Helmet", EquipmentSlot.Helmet, "Set B", AttributeId.AttackPower, 2),
            NewArmor("B Chest", EquipmentSlot.Chest, "Set B", AttributeId.MaxHealth, 15),
            NewArmor("B Gloves", EquipmentSlot.Gloves, "Set B", AttributeId.AttackPower, 4),
            NewArmor("B Leggings", EquipmentSlot.Leggings, "Set B", AttributeId.MaxHealth, 15),
            NewArmor("B Greaves", EquipmentSlot.Greaves, "Set B", AttributeId.AttackPower, 4),
            NewWeapon("Greatsword", EquipmentSlot.TwoHandedWeapon, 125, profile.Name),
            NewWeapon("Greataxe", EquipmentSlot.TwoHandedWeapon, 120, profile.Name),
            NewWeapon("Test Bow", EquipmentSlot.TwoHandedWeapon, 125, null),
            NewItem("Shield", EquipmentSlot.OffHand, (AttributeId.MaxHealth, 50)),
            NewItem("Amulet", EquipmentSlot.Amulet, (AttributeId.MaxHealth, 10)),
            NewItem("Power Amulet", EquipmentSlot.Amulet, (AttributeId.AttackPower, 3)),
            NewItem("Ring", EquipmentSlot.Ring, (AttributeId.AttackPower, 2))
        ];

        GameData gameData = new()
        {
            StartingStats = startingStats,
            CombatConfig = DefaultConfig(),
            Items = items,
            AttackProfiles = new Dictionary<string, WeaponAttackProfile>(StringComparer.OrdinalIgnoreCase)
            {
                [profile.Name] = profile
            },
            Skills = CreateTestSkills()
        };
        GameDataValidator.Validate(gameData);
        return gameData;
    }

    // Builds a fixture with sheet-excluded equipment alongside valid remaining choices.
    private static GameData CreateSheetExclusionTestGameData()
    {
        GameData baseData = CreateTestGameData();
        WeaponAttackProfile profile = baseData.AttackProfiles.Values.Single();
        List<Item> items = baseData.Items.ToList();
        items.AddRange(
        [
            NewArmor("Silk Helmet", EquipmentSlot.Helmet, "Silk", AttributeId.MaxHealth, 1, true),
            NewArmor("Silk Chest", EquipmentSlot.Chest, "Silk", AttributeId.MaxHealth, 1, true),
            NewArmor("Silk Gloves", EquipmentSlot.Gloves, "Silk", AttributeId.MaxHealth, 1, true),
            NewArmor("Silk Leggings", EquipmentSlot.Leggings, "Silk", AttributeId.MaxHealth, 1, true),
            NewArmor("Silk Greaves", EquipmentSlot.Greaves, "Silk", AttributeId.MaxHealth, 1, true),
            NewArmor("Web Helmet", EquipmentSlot.Helmet, "Web", AttributeId.MagicPower, 1),
            NewArmor("Web Chest", EquipmentSlot.Chest, "Web", AttributeId.MaxHealth, 1),
            NewArmor("Web Gloves", EquipmentSlot.Gloves, "Web", AttributeId.MaxHealth, 1),
            NewArmor("Web Leggings", EquipmentSlot.Leggings, "Web", AttributeId.MaxHealth, 1),
            NewArmor("Web Greaves", EquipmentSlot.Greaves, "Web", AttributeId.MaxHealth, 1),
            NewArmor("Mage Helmet", EquipmentSlot.Helmet, "Mage", AttributeId.MagicPower, 2),
            NewArmor("Mage Chest", EquipmentSlot.Chest, "Mage", AttributeId.MaxHealth, 2),
            NewArmor("Mage Gloves", EquipmentSlot.Gloves, "Mage", AttributeId.MaxHealth, 2),
            NewArmor("Mage Leggings", EquipmentSlot.Leggings, "Mage", AttributeId.MaxHealth, 2),
            NewArmor("Mage Greaves", EquipmentSlot.Greaves, "Mage", AttributeId.MaxHealth, 2),
            NewWeapon("Improvised Axe", EquipmentSlot.OneHandedWeapon, 50, profile.Name),
            NewWeapon("Mage Wand", EquipmentSlot.OneHandedWeapon, 50, profile.Name, (AttributeId.MagicPower, 3)),
            NewExcludedWeapon("Repair Hammer", EquipmentSlot.OneHandedWeapon, 50, profile.Name),
            NewExcludedWeapon("Spiked Club", EquipmentSlot.OneHandedWeapon, 50, profile.Name),
            NewWeapon("Necrosis Greatsword", EquipmentSlot.TwoHandedWeapon, 50, profile.Name),
            NewWeapon("Battle Staff", EquipmentSlot.TwoHandedWeapon, 50, profile.Name),
            NewItem("Mage Amulet", EquipmentSlot.Amulet, (AttributeId.MagicPower, 2)),
            NewItem("Mage Ring", EquipmentSlot.Ring, (AttributeId.MagicPower, 1))
        ]);

        GameData gameData = new()
        {
            StartingStats = baseData.StartingStats,
            CombatConfig = baseData.CombatConfig,
            Items = items,
            AttackProfiles = baseData.AttackProfiles,
            Skills = baseData.Skills
        };
        GameDataValidator.Validate(gameData);
        return gameData;
    }

    // Builds a fixture where all off-hands are sheet-excluded but two-handed weapons remain usable.
    private static GameData CreateNoOffHandTestGameData(GameData source)
    {
        List<Item> items = source.Items
            .Where(item => item.Slot != EquipmentSlot.OffHand)
            .ToList();
        Item excludedShield = new()
        {
            Name = "Excluded Shield",
            Slot = EquipmentSlot.OffHand,
            ExcludeFromSimulation = true
        };
        excludedShield.AddAttribute(AttributeId.MaxHealth, 50);
        items.Add(excludedShield);

        GameData gameData = new()
        {
            StartingStats = source.StartingStats,
            CombatConfig = source.CombatConfig,
            Items = items,
            AttackProfiles = source.AttackProfiles,
            Skills = source.Skills
        };
        GameDataValidator.Validate(gameData);
        return gameData;
    }

    // Copies one item while removing Critical Chance for eligibility edge-case tests.
    private static Item RemoveCriticalChance(Item source)
    {
        Item item = new()
        {
            Name = source.Name,
            Slot = source.Slot,
            AttackProfileName = source.AttackProfileName,
            ArmorSetName = source.ArmorSetName,
            ExcludeFromSimulation = source.ExcludeFromSimulation
        };
        foreach ((AttributeId attribute, double value) in source.Attributes)
        {
            if (attribute != AttributeId.CriticalChance)
                item.AddAttribute(attribute, value);
        }

        return item;
    }

    // Creates the deterministic three-hit profile shared by regression fixtures.
    private static WeaponAttackProfile CreateProfile()
    {
        return new WeaponAttackProfile
        {
            Name = "Test",
            Steps =
            [
                NewStep(1, 0.5, 0.1, 0.4, 1),
                NewStep(2, 0.5, 0.1, 0.4, 1),
                NewStep(3, 0.5, 0.1, 0.4, 1)
            ]
        };
    }

    // Creates enough magical damage skills for caster-analysis regression fixtures.
    private static IReadOnlyList<SkillDefinition> CreateTestSkills()
    {
        return
        [
            NewSkill("Spark", 100, 0, 0.5, 10, 1),
            NewSkill("Bolt", 120, 0, 0.6, 20, 2),
            NewSkill("Flame", 140, 0, 0.7, 30, 3),
            NewSkill("Frost", 160, 0, 0.8, 40, 4),
            NewSkill("Arcane", 180, 0, 0.9, 50, 5),
            NewSkill("Nova", 200, 0, 1.0, 60, 6),
            NewSkill("Meteor", 220, 0, 1.1, 70, 7)
        ];
    }

    // Creates one damage skill for regression fixtures.
    private static SkillDefinition NewSkill(
        string name,
        double damage,
        double attackScaling,
        double magicScaling,
        double manaCost,
        double cooldown)
    {
        return new SkillDefinition
        {
            Name = name,
            Damage = damage,
            AttackScaling = attackScaling,
            MagicScaling = magicScaling,
            ManaCost = manaCost,
            Cooldown = cooldown
        };
    }

    // Creates one attack step for a regression profile.
    private static AttackStep NewStep(
        int sequence,
        double anticipation,
        double contact,
        double recovery,
        double multiplier)
    {
        return new AttackStep
        {
            Sequence = sequence,
            Anticipation = anticipation,
            Contact = contact,
            Recovery = recovery,
            WeaponDamageMultiplier = multiplier
        };
    }

    // Creates immutable stats with the attributes needed by combat fixtures.
    private static CharacterStats BuildStats(
        double attackPower,
        double health,
        double weaponDamage,
        double armor = 0,
        double armorPenetration = 0,
        double criticalChance = 0,
        double criticalDamage = 0,
        double healthRegen = 0,
        double lifeSteal = 0,
        double magicPower = 100,
        double maxMana = 500,
        double manaRegen = 10,
        double cooldownReduction = 0.20,
        double manaEfficiency = 0)
    {
        CharacterStatsBuilder builder = new();
        builder.Set(AttributeId.AttackPower, attackPower);
        builder.Set(AttributeId.MaxHealth, health);
        builder.Set(AttributeId.WeaponDamage, weaponDamage);
        builder.Set(AttributeId.Armor, armor);
        builder.Set(AttributeId.ArmorPenetration, armorPenetration);
        builder.Set(AttributeId.CriticalChance, criticalChance);
        builder.Set(AttributeId.CriticalDamage, criticalDamage);
        builder.Set(AttributeId.HealthRegen, healthRegen);
        builder.Set(AttributeId.LifeSteal, lifeSteal);
        builder.Set(AttributeId.MagicPower, magicPower);
        builder.Set(AttributeId.MaxMana, maxMana);
        builder.Set(AttributeId.ManaRegen, manaRegen);
        builder.Set(AttributeId.CooldownReduction, cooldownReduction);
        builder.Set(AttributeId.ManaEfficiency, manaEfficiency);
        return builder.Build();
    }

    // Creates one ordinary item with typed attributes.
    private static Item NewItem(
        string name,
        EquipmentSlot slot,
        params (AttributeId Attribute, double Value)[] attributes)
    {
        Item item = new() { Name = name, Slot = slot };
        foreach ((AttributeId attribute, double value) in attributes)
            item.AddAttribute(attribute, value);
        return item;
    }

    // Creates one armor item carrying its authoritative set identity.
    private static Item NewArmor(
        string name,
        EquipmentSlot slot,
        string setName,
        AttributeId attribute,
        double value,
        bool excludeFromSimulation = false)
    {
        Item item = new()
        {
            Name = name,
            Slot = slot,
            ArmorSetName = setName,
            ExcludeFromSimulation = excludeFromSimulation
        };
        item.AddAttribute(attribute, value);
        return item;
    }

    // Creates a weapon item and assigns its optional attack profile.
    private static Item NewWeapon(
        string name,
        EquipmentSlot slot,
        double weaponDamage,
        string? profileName,
        params (AttributeId Attribute, double Value)[] extraAttributes)
    {
        Item item = NewItem(name, slot, (AttributeId.WeaponDamage, weaponDamage));
        foreach ((AttributeId attribute, double value) in extraAttributes)
            item.AddAttribute(attribute, value);
        item.AttackProfileName = profileName;
        return item;
    }

    // Creates a weapon marked unavailable for simulation fixtures.
    private static Item NewExcludedWeapon(
        string name,
        EquipmentSlot slot,
        double weaponDamage,
        string? profileName)
    {
        Item item = new()
        {
            Name = name,
            Slot = slot,
            ExcludeFromSimulation = true
        };
        item.AddAttribute(AttributeId.WeaponDamage, weaponDamage);
        item.AttackProfileName = profileName;
        return item;
    }

    // Wraps immutable stats and a profile in a minimal loadout.
    private static Loadout NewLoadout(string name, CharacterStats stats, WeaponAttackProfile profile)
    {
        return new Loadout
        {
            Stats = stats,
            Items = [NewWeapon(name, EquipmentSlot.TwoHandedWeapon, stats.WeaponDamage, profile.Name)],
            AttackProfile = profile
        };
    }

    // Copies a loadout while adding one stat for timing checks.
    private static Loadout WithAddedStat(Loadout source, AttributeId attribute, double amount)
    {
        return new Loadout
        {
            Stats = source.Stats.WithAdded(attribute, amount),
            Items = source.Items,
            AttackProfile = source.AttackProfile
        };
    }

    // Supplies the current AP scaling rule to regression fixtures.
    private static CombatConfig DefaultConfig() => new()
    {
        AttackPowerMultiplier = 0.5,
        PhysicalArmorConstant = 0.005,
        MagicalArmorConstant = 0.005,
        SkillSlots = 7
    };

    // Runs one named check and prints a compact success line.
    private static void RunCheck(string name, Action check)
    {
        check();
        Console.WriteLine($"PASS: {name}");
    }

    // Compares floating-point values with a strict deterministic tolerance.
    private static void AssertClose(double expected, double actual)
    {
        if (Math.Abs(expected - actual) > 0.0000001)
            throw new Exception($"Expected {expected}, received {actual}.");
    }

    // Confirms that an action throws the requested exception type.
    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new Exception($"Expected {typeof(TException).Name}.");
    }
}
