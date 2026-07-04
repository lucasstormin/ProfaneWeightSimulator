namespace CombatSimulator.Models;

// Defines the ordered attack cycle shared by a category of weapons.
public sealed class WeaponAttackProfile
{
    public required string Name { get; init; }
    public required IReadOnlyList<AttackStep> Steps { get; init; }
}
