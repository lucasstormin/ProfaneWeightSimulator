using CombatSimulator.Models;

namespace CombatSimulator.Data;

// Rejects incomplete or contradictory imported game data before analysis begins.
public static class GameDataValidator
{
    private static readonly EquipmentSlot[] RequiredSlots =
    [
        EquipmentSlot.Helmet,
        EquipmentSlot.Chest,
        EquipmentSlot.Gloves,
        EquipmentSlot.Leggings,
        EquipmentSlot.Greaves,
        EquipmentSlot.OffHand,
        EquipmentSlot.Amulet,
        EquipmentSlot.Ring
    ];

    // Validates all imported values and cross-sheet relationships used by simulations.
    public static void Validate(GameData gameData)
    {
        if (gameData.StartingStats.MaxHealth <= 0)
            throw new InvalidDataException("Starting Health must be positive.");
        if (gameData.CombatConfig.AttackPowerMultiplier <= 0)
            throw new InvalidDataException("Attack Power multiplier must be positive.");
        if (gameData.Items.Count == 0)
            throw new InvalidDataException("The spreadsheet contains no importable items.");

        string[] duplicateNames = gameData.Items
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateNames.Length > 0)
            throw new InvalidDataException($"Duplicate item names found: {string.Join(", ", duplicateNames)}.");

        foreach (EquipmentSlot slot in RequiredSlots)
        {
            if (!gameData.Items.Any(item => item.Slot == slot))
                throw new InvalidDataException($"No items were imported for required slot '{slot}'.");
        }

        Item[] weapons = gameData.Items
            .Where(item => item.Slot is EquipmentSlot.OneHandedWeapon or EquipmentSlot.TwoHandedWeapon)
            .ToArray();
        if (weapons.Length == 0)
            throw new InvalidDataException("No main-hand weapons were imported.");
        if (weapons.Any(weapon => weapon.GetAttribute(AttributeId.WeaponDamage) <= 0))
            throw new InvalidDataException("Every main-hand weapon must define positive Weapon Damage.");

        Item[] eligibleWeapons = weapons.Where(weapon => !weapon.IsBow).ToArray();
        if (eligibleWeapons.Length == 0)
            throw new InvalidDataException("No eligible non-bow weapons were imported.");

        foreach (Item weapon in eligibleWeapons)
        {
            if (string.IsNullOrWhiteSpace(weapon.AttackProfileName))
                throw new InvalidDataException($"Weapon '{weapon.Name}' has no attack profile mapping.");
            if (!gameData.AttackProfiles.ContainsKey(weapon.AttackProfileName))
                throw new InvalidDataException($"Weapon '{weapon.Name}' references unknown profile '{weapon.AttackProfileName}'.");
        }

        foreach (WeaponAttackProfile profile in gameData.AttackProfiles.Values)
        {
            if (profile.Steps.Count != 3 || !profile.Steps.Select(step => step.Sequence).SequenceEqual([1, 2, 3]))
                throw new InvalidDataException($"Profile '{profile.Name}' must contain attacks 1, 2, and 3 exactly once.");
            if (profile.Steps.Any(step =>
                step.Anticipation <= 0 || step.Contact <= 0 || step.Recovery <= 0 ||
                step.WeaponDamageMultiplier <= 0 || !double.IsFinite(step.TotalDuration)))
            {
                throw new InvalidDataException($"Profile '{profile.Name}' contains invalid timing or multiplier values.");
            }
        }

        foreach (Item item in gameData.Items)
        {
            foreach ((AttributeId attribute, double value) in item.Attributes)
            {
                if (!double.IsFinite(value))
                    throw new InvalidDataException($"Item '{item.Name}' has a non-finite '{attribute}' value.");
            }
        }
    }
}
