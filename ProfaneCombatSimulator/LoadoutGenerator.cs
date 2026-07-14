using CombatSimulator.Data;
using CombatSimulator.Models;

namespace CombatSimulator.Analysis;

// Generates reproducible, valid full loadouts from the items imported from the sheet.
public sealed class LoadoutGenerator
{
    private readonly CharacterStats startingStats;
    private readonly IReadOnlyDictionary<string, WeaponAttackProfile> attackProfiles;
    private readonly Dictionary<EquipmentSlot, Item[]> itemsBySlot;
    private readonly Dictionary<string, Dictionary<EquipmentSlot, Item>> armorSets;
    private readonly Item[] weapons;
    private readonly Random random;
    private readonly LoadoutGenerationMode mode;
    private readonly string[] closedSetSchedule;
    private int generatedLoadouts;

    // Indexes eligible equipment and initializes the reproducible random sequence.
    public LoadoutGenerator(GameData gameData, int seed)
        : this(gameData, seed, LoadoutGenerationMode.RandomPieces, 0, null)
    {
    }

    // Configures random or evenly scheduled closed-set generation for a known run size.
    public LoadoutGenerator(
        GameData gameData,
        int seed,
        LoadoutGenerationMode mode,
        int expectedLoadouts,
        TailoredLoadoutSettings? tailoredSettings = null)
    {
        startingStats = gameData.StartingStats;
        attackProfiles = gameData.AttackProfiles;
        this.mode = mode;
        random = new Random(seed);
        TailoredLoadoutSettings selectedTailoredSettings =
            tailoredSettings ?? TailoredLoadoutSettings.CreateDefault();
        Item[] eligibleItems = mode == LoadoutGenerationMode.Tailored
            ? gameData.Items
                .Where(item => TailoredLoadoutRules.IsEligible(item, selectedTailoredSettings))
                .ToArray()
            : gameData.Items.ToArray();

        itemsBySlot = eligibleItems
            .GroupBy(item => item.Slot)
            .ToDictionary(group => group.Key, group => group.ToArray());
        weapons = eligibleItems
            .Where(item =>
                (item.Slot is EquipmentSlot.OneHandedWeapon or EquipmentSlot.TwoHandedWeapon) &&
                !item.IsBow)
            .ToArray();
        armorSets = gameData.Items
            .Where(item => item.ArmorSetName is not null)
            .GroupBy(item => item.ArmorSetName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(item => item.Slot),
                StringComparer.OrdinalIgnoreCase);
        closedSetSchedule = mode == LoadoutGenerationMode.ClosedArmorSet
            ? CreateClosedSetSchedule(armorSets.Keys, expectedLoadouts, seed)
            : [];

        foreach (EquipmentSlot slot in new[]
        {
            EquipmentSlot.Helmet, EquipmentSlot.Chest, EquipmentSlot.Gloves,
            EquipmentSlot.Leggings, EquipmentSlot.Greaves, EquipmentSlot.OffHand,
            EquipmentSlot.Amulet, EquipmentSlot.Ring
        })
        {
            if (!itemsBySlot.ContainsKey(slot))
                throw new InvalidDataException($"No items were imported for required slot '{slot}'.");
        }
        if (weapons.Length == 0)
            throw new InvalidDataException("No eligible main-hand weapons are available for the selected loadout mode.");
    }

    // Produces one fully equipped legal loadout and resolves its weapon profile.
    public Loadout Generate()
    {
        if (mode == LoadoutGenerationMode.ClosedArmorSet && generatedLoadouts >= closedSetSchedule.Length)
            throw new InvalidOperationException("Closed-set generation exceeded its scheduled loadout count.");

        Item[] equipped = new Item[10];
        int count = 0;
        if (mode == LoadoutGenerationMode.ClosedArmorSet)
        {
            Dictionary<EquipmentSlot, Item> set = armorSets[closedSetSchedule[generatedLoadouts]];
            equipped[count++] = set[EquipmentSlot.Helmet];
            equipped[count++] = set[EquipmentSlot.Chest];
            equipped[count++] = set[EquipmentSlot.Gloves];
            equipped[count++] = set[EquipmentSlot.Leggings];
            equipped[count++] = set[EquipmentSlot.Greaves];
        }
        else
        {
            equipped[count++] = Pick(EquipmentSlot.Helmet);
            equipped[count++] = Pick(EquipmentSlot.Chest);
            equipped[count++] = Pick(EquipmentSlot.Gloves);
            equipped[count++] = Pick(EquipmentSlot.Leggings);
            equipped[count++] = Pick(EquipmentSlot.Greaves);
        }
        generatedLoadouts++;

        Item weapon = weapons[random.Next(weapons.Length)];
        equipped[count++] = weapon;
        if (weapon.Slot == EquipmentSlot.OneHandedWeapon)
            equipped[count++] = Pick(EquipmentSlot.OffHand);

        equipped[count++] = Pick(EquipmentSlot.Amulet);
        equipped[count++] = Pick(EquipmentSlot.Ring);
        equipped[count++] = Pick(EquipmentSlot.Ring);

        if (count != equipped.Length)
            Array.Resize(ref equipped, count);

        CharacterStatsBuilder stats = new(startingStats);
        foreach (Item item in equipped)
        {
            foreach ((AttributeId attribute, double value) in item.Attributes)
            {
                if (attribute != AttributeId.WeaponDamage)
                    stats.Add(attribute, value);
            }
        }

        stats.Set(AttributeId.WeaponDamage, weapon.GetAttribute(AttributeId.WeaponDamage));
        WeaponAttackProfile profile = attackProfiles[weapon.AttackProfileName!];
        return new Loadout { Stats = stats.Build(), Items = equipped, AttackProfile = profile };
    }

    // Selects one uniformly random item from a required equipment slot.
    private Item Pick(EquipmentSlot slot)
    {
        Item[] candidates = itemsBySlot[slot];
        return candidates[random.Next(candidates.Length)];
    }

    // Builds an evenly distributed, reproducibly shuffled sequence of armor-set names.
    private static string[] CreateClosedSetSchedule(
        IEnumerable<string> availableSets,
        int expectedLoadouts,
        int seed)
    {
        if (expectedLoadouts <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedLoadouts));

        string[] setNames = availableSets.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (setNames.Length == 0)
            throw new InvalidDataException("No complete armor sets are available for closed-set generation.");

        Random scheduleRandom = new(seed ^ 0x4F1BBCDC);
        Shuffle(setNames, scheduleRandom);
        List<string> schedule = new(expectedLoadouts);
        int baseUses = expectedLoadouts / setNames.Length;
        int remainder = expectedLoadouts % setNames.Length;
        for (int setIndex = 0; setIndex < setNames.Length; setIndex++)
        {
            int uses = baseUses + (setIndex < remainder ? 1 : 0);
            for (int use = 0; use < uses; use++)
                schedule.Add(setNames[setIndex]);
        }

        string[] result = schedule.ToArray();
        Shuffle(result, scheduleRandom);
        return result;
    }

    // Applies an unbiased in-place Fisher-Yates shuffle with the supplied deterministic source.
    private static void Shuffle<T>(T[] values, Random source)
    {
        for (int index = values.Length - 1; index > 0; index--)
        {
            int swapIndex = source.Next(index + 1);
            (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
        }
    }
}
