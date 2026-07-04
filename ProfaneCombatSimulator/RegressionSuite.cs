using CombatSimulator.Analysis;
using CombatSimulator.Combat;
using CombatSimulator.Data;
using CombatSimulator.Models;

namespace CombatSimulator.Validation;

// Runs dependency-free regression checks for formulas, equipment rules, and determinism.
public static class RegressionSuite
{
    public static void Run()
    {
        RunCheck("Typed attributes reject duplicates", TestDuplicateAttribute);
        RunCheck("Damage exposes raw and rounded values", TestDamageCalculation);
        RunCheck("Weapon Damage weight follows multipliers", TestWeaponDamageWeight);
        RunCheck("Two-handed loadouts exclude off-hands", TestTwoHandedLoadout);
        RunCheck("Seeded analysis is deterministic", TestDeterministicAnalysis);
        Console.WriteLine("All regression checks passed.");
    }

    private static void TestDuplicateAttribute()
    {
        Item item = NewItem("Test", EquipmentSlot.Helmet, (AttributeId.MaxHealth, 10));
        AssertThrows<InvalidDataException>(() => item.AddAttribute(AttributeId.MaxHealth, 20));
    }

    private static void TestDamageCalculation()
    {
        CharacterStatsBuilder builder = new();
        builder.Set(AttributeId.AttackPower, 11);
        builder.Set(AttributeId.WeaponDamage, 10);
        CharacterStats stats = builder.Build();
        CombatConfig config = DefaultConfig();

        AssertClose(15.5, DamageCalculator.CalculateRawDamage(stats, config));
        AssertClose(16, DamageCalculator.CalculateDamage(stats, config));
    }

    private static void TestWeaponDamageWeight()
    {
        CombatConfig config = DefaultConfig();
        AssertClose(2, OffensiveWeightCalculator.CalculateWeaponDamageWeight(config));
        AssertClose(250, OffensiveWeightCalculator.CalculateWeaponDamageBudget(125, config));
        if (!OffensiveWeightCalculator.ValidateWeaponDamageWeight(config).ValidationPassed)
            throw new Exception("Weapon Damage formula validation failed.");
    }

    private static void TestTwoHandedLoadout()
    {
        GameData gameData = CreateTestGameData();
        Loadout loadout = new LoadoutGenerator(gameData, 7).Generate();

        if (loadout.Items.Any(item => item.Slot == EquipmentSlot.OffHand))
            throw new Exception("A two-handed loadout equipped an off-hand.");
        if (loadout.Items.Count(item => item.Slot == EquipmentSlot.Ring) != 2)
            throw new Exception("The loadout did not equip two rings.");
        AssertClose(125, loadout.Stats.WeaponDamage);
    }

    private static void TestDeterministicAnalysis()
    {
        GameData gameData = CreateTestGameData();
        WeightDistributionResult first = LoadoutWeightAnalyzer.Analyze(gameData, 100, 42);
        WeightDistributionResult second = LoadoutWeightAnalyzer.Analyze(gameData, 100, 42);

        AssertClose(first.MedianHealthWeight, second.MedianHealthWeight);
        AssertClose(first.MinimumHealthWeight, second.MinimumHealthWeight);
        AssertClose(first.MaximumHealthWeight, second.MaximumHealthWeight);
    }

    private static GameData CreateTestGameData()
    {
        CharacterStatsBuilder startingStats = new();
        startingStats.Set(AttributeId.AttackPower, 10);
        startingStats.Set(AttributeId.MaxHealth, 1000);
        startingStats.Set(AttributeId.WeaponDamage, 10);

        List<Item> items =
        [
            NewItem("Helmet", EquipmentSlot.Helmet, (AttributeId.MaxHealth, 10)),
            NewItem("Chest", EquipmentSlot.Chest, (AttributeId.MaxHealth, 20)),
            NewItem("Gloves", EquipmentSlot.Gloves, (AttributeId.AttackPower, 5)),
            NewItem("Leggings", EquipmentSlot.Leggings, (AttributeId.MaxHealth, 20)),
            NewItem("Greaves", EquipmentSlot.Greaves, (AttributeId.AttackPower, 5)),
            NewItem("Greatsword", EquipmentSlot.TwoHandedWeapon, (AttributeId.WeaponDamage, 125)),
            NewItem("Shield", EquipmentSlot.OffHand, (AttributeId.MaxHealth, 50)),
            NewItem("Amulet", EquipmentSlot.Amulet, (AttributeId.MaxHealth, 10)),
            NewItem("Ring", EquipmentSlot.Ring, (AttributeId.AttackPower, 2))
        ];

        GameData gameData = new()
        {
            StartingStats = startingStats.Build(),
            CombatConfig = DefaultConfig(),
            Items = items
        };
        GameDataValidator.Validate(gameData);
        return gameData;
    }

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

    private static CombatConfig DefaultConfig() => new()
    {
        AttackPowerMultiplier = 0.5,
        WeaponDamageMultiplier = 1
    };

    private static void RunCheck(string name, Action check)
    {
        check();
        Console.WriteLine($"PASS: {name}");
    }

    private static void AssertClose(double expected, double actual)
    {
        if (Math.Abs(expected - actual) > 0.0000001)
            throw new Exception($"Expected {expected}, received {actual}.");
    }

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
