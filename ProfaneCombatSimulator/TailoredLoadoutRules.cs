using CombatSimulator.Data;

namespace CombatSimulator.Analysis;

// Defines the curated equipment exclusions used by Tailored simulation mode.
public static class TailoredLoadoutRules
{
    public static readonly string[] ExcludedArmorSets =
        ["Silk", "Web", "Linen", "Hide", "Iron"];

    private static readonly HashSet<string> ExcludedArmorSetLookup = new(
        ExcludedArmorSets,
        StringComparer.OrdinalIgnoreCase);

    // Checks whether an item remains available in the curated Tailored pool.
    public static bool IsEligible(Item item)
    {
        if (item.ArmorSetName is not null && ExcludedArmorSetLookup.Contains(item.ArmorSetName))
            return false;

        if (item.Slot is EquipmentSlot.OneHandedWeapon or EquipmentSlot.TwoHandedWeapon)
            return !IsExcludedWeapon(item);

        return true;
    }

    // Checks whether a main-hand weapon is excluded from Tailored simulations.
    public static bool IsExcludedWeapon(Item weapon)
    {
        return weapon.Name.StartsWith("Improvised", StringComparison.OrdinalIgnoreCase) ||
            weapon.Name.Equals("Repair Hammer", StringComparison.OrdinalIgnoreCase) ||
            weapon.Name.Equals("Spiked Club", StringComparison.OrdinalIgnoreCase) ||
            weapon.Name.StartsWith("Necrosis", StringComparison.OrdinalIgnoreCase) ||
            weapon.Name.Contains("Staff", StringComparison.OrdinalIgnoreCase);
    }
}
