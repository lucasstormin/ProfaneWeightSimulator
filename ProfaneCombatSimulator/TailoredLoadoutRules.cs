using CombatSimulator.Data;

namespace CombatSimulator.Analysis;

// Defines the curated equipment exclusions used by Tailored simulation mode.
public static class TailoredLoadoutRules
{
    // Checks whether an item remains available in the selected Tailored pool.
    public static bool IsEligible(Item item, TailoredLoadoutSettings settings)
    {
        if (item.ArmorSetName is not null && settings.ExcludedArmorSets.Contains(item.ArmorSetName))
            return false;

        if (item.Slot is EquipmentSlot.OneHandedWeapon or EquipmentSlot.TwoHandedWeapon)
            return !IsExcludedWeapon(item, settings);

        return true;
    }

    // Checks whether a main-hand weapon is excluded from Tailored simulations.
    public static bool IsExcludedWeapon(Item weapon, TailoredLoadoutSettings settings)
    {
        return settings.ExcludedWeaponNames.Contains(weapon.Name) ||
            (settings.ExcludeImprovisedWeapons &&
                weapon.Name.StartsWith("Improvised", StringComparison.OrdinalIgnoreCase)) ||
            (settings.ExcludeNecrosisWeapons &&
                weapon.Name.StartsWith("Necrosis", StringComparison.OrdinalIgnoreCase)) ||
            (settings.ExcludeStaffWeapons &&
                weapon.Name.Contains("Staff", StringComparison.OrdinalIgnoreCase));
    }
}
