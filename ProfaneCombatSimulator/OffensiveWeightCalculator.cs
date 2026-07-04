using CombatSimulator.Models;

namespace CombatSimulator.Analysis;

// Calculates offensive weights relative to Attack Power and their item-budget contributions.
public static class OffensiveWeightCalculator
{
    public static double CalculateWeaponDamageWeight(CombatConfig config)
    {
        ValidateMultipliers(config);
        return config.WeaponDamageMultiplier / config.AttackPowerMultiplier;
    }

    public static double CalculateWeaponDamageBudget(
        double equippedWeaponDamage,
        CombatConfig config)
    {
        return equippedWeaponDamage * CalculateWeaponDamageWeight(config);
    }

    public static OffensiveAttributeWeightResult ValidateWeaponDamageWeight(
        CombatConfig config)
    {
        double weaponDamageWeight = CalculateWeaponDamageWeight(config);
        double weaponDamageContribution = config.WeaponDamageMultiplier;
        double attackPowerContribution = weaponDamageWeight * config.AttackPowerMultiplier;
        int failures = Math.Abs(weaponDamageContribution - attackPowerContribution) < 0.0000001 ? 0 : 1;

        return new OffensiveAttributeWeightResult
        {
            Attribute = OffensiveAttribute.WeaponDamage,
            AttackPowerBasedWeight = weaponDamageWeight,
            ValidationSamples = 1,
            ValidationFailures = failures
        };
    }

    private static void ValidateMultipliers(CombatConfig config)
    {
        if (config.AttackPowerMultiplier <= 0)
            throw new InvalidOperationException("Attack Power must have a positive damage multiplier.");
        if (config.WeaponDamageMultiplier <= 0)
            throw new InvalidOperationException("Weapon Damage must have a positive damage multiplier.");
    }
}
