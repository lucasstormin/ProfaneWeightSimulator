namespace CombatSimulator.Models;

// Stores global combat rules and scaling values used by damage calculations.
public sealed class CombatConfig
{
    public required double AttackPowerMultiplier { get; init; }
    public required double WeaponDamageMultiplier { get; init; }
}
