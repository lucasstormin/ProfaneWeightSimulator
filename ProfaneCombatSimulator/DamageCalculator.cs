using CombatSimulator.Models;

namespace CombatSimulator.Combat;

// Calculates attack damage from character attributes and combat settings.
public static class DamageCalculator
{
    public const double BaseCriticalDamageBonus = 0.50;
    public const int MinimumChanceRoll = 1;
    public const int MaximumChanceRollExclusive = 10_001;

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
        double targetArmor = 0,
        int criticalRoll = MaximumChanceRollExclusive - 1)
    {
        int baseDamage = (int)CalculateRawDamage(attacker, attack, config);
        if (IsCriticalHit(attacker[AttributeId.CriticalChance], criticalRoll))
        {
            float criticalMultiplier = (float)(
                1 + BaseCriticalDamageBonus + attacker[AttributeId.CriticalDamage]);
            baseDamage = (int)(baseDamage * criticalMultiplier);
        }

        int effectiveArmor = CalculateEffectiveArmor(
            (int)targetArmor,
            attacker[AttributeId.ArmorPenetration]);
        float armorFactor = (float)config.PhysicalArmorConstant;
        float armor = effectiveArmor;
        float armorReduction = 1f -
            (armorFactor * armor / (1f + (armorFactor * Math.Abs(armor))));
        return (int)Math.Round(baseDamage * armorReduction);
    }

    // Applies the game's inclusive chance comparison to one roll from 1 through 10,000.
    public static bool IsCriticalHit(double criticalChance, int criticalRoll)
    {
        if (criticalRoll < MinimumChanceRoll || criticalRoll >= MaximumChanceRollExclusive)
            throw new ArgumentOutOfRangeException(nameof(criticalRoll));
        if (criticalChance <= 0)
            return false;

        float runtimeChance = (float)(criticalChance * 10_000);
        return runtimeChance >= criticalRoll;
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
