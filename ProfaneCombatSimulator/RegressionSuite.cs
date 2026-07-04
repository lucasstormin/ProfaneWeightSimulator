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
            BuildStats(attackPower: 10, health: 1000, weaponDamage: 100),
            profile);
        (double health, double weaponDamage, double attackSpeed) =
            TimeBasedAnalysisRunner.CalculateWeights(loadout, DefaultConfig());

        AssertClose(2, weaponDamage);
        AssertClose(0.21, health);
        AssertClose(2.1, attackSpeed);
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
            NewItem("Helmet", EquipmentSlot.Helmet, (AttributeId.MaxHealth, 10)),
            NewItem("Chest", EquipmentSlot.Chest, (AttributeId.MaxHealth, 20)),
            NewItem("Gloves", EquipmentSlot.Gloves, (AttributeId.AttackPower, 5)),
            NewItem("Leggings", EquipmentSlot.Leggings, (AttributeId.MaxHealth, 20)),
            NewItem("Greaves", EquipmentSlot.Greaves, (AttributeId.AttackPower, 5)),
            NewWeapon("Greatsword", EquipmentSlot.TwoHandedWeapon, 125, profile.Name),
            NewWeapon("Test Bow", EquipmentSlot.TwoHandedWeapon, 125, null),
            NewItem("Shield", EquipmentSlot.OffHand, (AttributeId.MaxHealth, 50)),
            NewItem("Amulet", EquipmentSlot.Amulet, (AttributeId.MaxHealth, 10)),
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
    private static CharacterStats BuildStats(double attackPower, double health, double weaponDamage)
    {
        CharacterStatsBuilder builder = new();
        builder.Set(AttributeId.AttackPower, attackPower);
        builder.Set(AttributeId.MaxHealth, health);
        builder.Set(AttributeId.WeaponDamage, weaponDamage);
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
        AttackPowerMultiplier = 0.5
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
