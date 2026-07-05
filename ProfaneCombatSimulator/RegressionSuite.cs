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
        RunCheck("Incomplete armor sets are rejected", TestIncompleteArmorSet);
        RunCheck("Attack Speed scales contact timing", TestAttackSpeedTiming);
        RunCheck("Combo order repeats 1-2-3", TestComboOrder);
        RunCheck("Simultaneous lethal contacts draw", TestSimultaneousDraw);
        RunCheck("Time limit produces a stalemate", TestStalemate);
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

    // Confirms malformed sheet data cannot silently produce partial closed sets.
    private static void TestIncompleteArmorSet()
    {
        GameData valid = CreateTestGameData();
        GameData incomplete = new()
        {
            StartingStats = valid.StartingStats,
            CombatConfig = valid.CombatConfig,
            AttackProfiles = valid.AttackProfiles,
            Items = valid.Items.Where(item => item.Name != "B Greaves").ToArray()
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

    // Confirms profile-aware smooth weights and the one-percentage-point Attack Speed unit.
    private static void TestContextualWeights()
    {
        WeaponAttackProfile profile = CreateProfile();
        Loadout loadout = NewLoadout(
            "Test Weapon",
            BuildStats(attackPower: 10, health: 1000, weaponDamage: 100, armor: 100),
            profile);
        (double health, double weaponDamage, double attackSpeed, double armor) =
            TimeBasedAnalysisRunner.CalculateWeights(loadout, DefaultConfig());

        AssertClose(2, weaponDamage);
        AssertClose(0.21, health);
        AssertClose(2.1, attackSpeed);
        AssertClose(0.7, armor);
    }

    // Confirms identical seeds produce identical distributions and timing statistics.
    private static void TestDeterministicAnalysis()
    {
        GameData gameData = CreateTestGameData();
        SimulationAnalysisResult first = TimeBasedAnalysisRunner.Analyze(gameData, 100, 42);
        SimulationAnalysisResult second = TimeBasedAnalysisRunner.Analyze(gameData, 100, 42);

        AssertClose(first.Health.MedianWeight, second.Health.MedianWeight);
        AssertClose(first.AttackSpeed.MedianWeight, second.AttackSpeed.MedianWeight);
        AssertClose(first.AverageCompletedFightDuration, second.AverageCompletedFightDuration);
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
        if (first.Stalemates != second.Stalemates)
            throw new Exception("Seeded stalemate counts differ.");
        if (first.Draws != second.Draws)
            throw new Exception("Seeded draw counts differ.");
    }

    // Builds a minimal valid game dataset with one eligible weapon and one excluded bow.
    private static GameData CreateTestGameData()
    {
        CharacterStats startingStats = BuildStats(attackPower: 10, health: 1000, weaponDamage: 10);
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
            }
        };
        GameDataValidator.Validate(gameData);
        return gameData;
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
        double armor = 0)
    {
        CharacterStatsBuilder builder = new();
        builder.Set(AttributeId.AttackPower, attackPower);
        builder.Set(AttributeId.MaxHealth, health);
        builder.Set(AttributeId.WeaponDamage, weaponDamage);
        builder.Set(AttributeId.Armor, armor);
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
        double value)
    {
        Item item = new() { Name = name, Slot = slot, ArmorSetName = setName };
        item.AddAttribute(attribute, value);
        return item;
    }

    // Creates a weapon item and assigns its optional attack profile.
    private static Item NewWeapon(
        string name,
        EquipmentSlot slot,
        double weaponDamage,
        string? profileName)
    {
        Item item = NewItem(name, slot, (AttributeId.WeaponDamage, weaponDamage));
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
        PhysicalArmorConstant = 0.005
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
