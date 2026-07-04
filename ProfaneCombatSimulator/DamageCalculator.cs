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

    // Applies the game's midpoint-to-even integer rounding to one contact's damage.
    public static double CalculateDamage(
        CharacterStats attacker,
        AttackStep attack,
        CombatConfig config)
    {
        return Math.Round(CalculateRawDamage(attacker, attack, config));
    }
}
