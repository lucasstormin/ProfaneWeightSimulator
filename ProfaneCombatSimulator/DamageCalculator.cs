using CombatSimulator.Models;

namespace CombatSimulator.Combat;

// Calculates attack damage from character attributes and combat settings.
public static class DamageCalculator
{
    // Calculates unrounded damage for one profile-specific combo attack.
    public static double CalculateRawDamage(
        CharacterStats attacker,
        AttackStep attack,
        CombatConfig config)
    {
        return (attacker.AttackPower * config.AttackPowerMultiplier) +
            (attacker.WeaponDamage * attack.WeaponDamageMultiplier);
    }

    // Applies physical Armor with game-accurate float math before midpoint-to-even rounding.
    public static double CalculateDamage(
        CharacterStats attacker,
        AttackStep attack,
        CombatConfig config,
        double targetArmor = 0)
    {
        int effectiveArmor = CalculateEffectiveArmor(
            (int)targetArmor,
            attacker[AttributeId.ArmorPenetration]);
        float armorFactor = (float)config.PhysicalArmorConstant;
        float armor = effectiveArmor;
        float armorReduction = 1f -
            (armorFactor * armor / (1f + (armorFactor * Math.Abs(armor))));
        float baseDamage = (float)CalculateRawDamage(attacker, attack, config);
        return (int)Math.Round(baseDamage * armorReduction);
    }

    // Reproduces the game's positive percentage penetration and integer Armor truncation.
    public static int CalculateEffectiveArmor(int targetArmor, double armorPenetration)
    {
        float penetration = (float)armorPenetration;
        if (penetration > 0)
            targetArmor = (int)(targetArmor * (1f - penetration));
        return targetArmor;
    }
}
