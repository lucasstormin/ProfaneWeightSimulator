namespace CombatSimulator.Models;

// Defines one attack in a weapon's repeating three-step combo.
public sealed class AttackStep
{
    public required int Sequence { get; init; }
    public required double Anticipation { get; init; }
    public required double Contact { get; init; }
    public required double Recovery { get; init; }
    public required double WeaponDamageMultiplier { get; init; }

    public double TotalDuration => Anticipation + Contact + Recovery;
}
