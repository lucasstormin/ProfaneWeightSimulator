using CombatSimulator.Models;

namespace CombatSimulator.Combat;

// Calculates attack damage from character attributes and combat settings.
public static class DamageCalculator
{
    public static double CalculateRawDamage(CharacterStats attacker, CombatConfig config)
    {
        return (attacker.AttackPower * config.AttackPowerMultiplier) +
            (attacker.WeaponDamage * config.WeaponDamageMultiplier);
    }

    public static double CalculateDamage(CharacterStats attacker, CombatConfig config)
    {
        return Math.Round(CalculateRawDamage(attacker, config));
    }
}
