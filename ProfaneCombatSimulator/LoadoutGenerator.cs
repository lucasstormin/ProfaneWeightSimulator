using CombatSimulator.Data;
using CombatSimulator.Models;

namespace CombatSimulator.Analysis;

// Generates reproducible, valid full loadouts from the items imported from the sheet.
public sealed class LoadoutGenerator
{
    private readonly CharacterStats startingStats;
    private readonly IReadOnlyDictionary<string, WeaponAttackProfile> attackProfiles;
    private readonly Dictionary<EquipmentSlot, Item[]> itemsBySlot;
    private readonly Item[] weapons;
    private readonly Random random;

    // Indexes eligible equipment and initializes the reproducible random sequence.
    public LoadoutGenerator(GameData gameData, int seed)
    {
        startingStats = gameData.StartingStats;
        attackProfiles = gameData.AttackProfiles;
        random = new Random(seed);
        itemsBySlot = gameData.Items
            .GroupBy(item => item.Slot)
            .ToDictionary(group => group.Key, group => group.ToArray());
        weapons = gameData.Items
            .Where(item =>
                (item.Slot is EquipmentSlot.OneHandedWeapon or EquipmentSlot.TwoHandedWeapon) &&
                !item.IsBow)
            .ToArray();

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
            throw new InvalidDataException("No main-hand weapons were imported.");
    }

    // Produces one fully equipped legal loadout and resolves its weapon profile.
    public Loadout Generate()
    {
        Item[] equipped = new Item[10];
        int count = 0;
        equipped[count++] = Pick(EquipmentSlot.Helmet);
        equipped[count++] = Pick(EquipmentSlot.Chest);
        equipped[count++] = Pick(EquipmentSlot.Gloves);
        equipped[count++] = Pick(EquipmentSlot.Leggings);
        equipped[count++] = Pick(EquipmentSlot.Greaves);

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
}
