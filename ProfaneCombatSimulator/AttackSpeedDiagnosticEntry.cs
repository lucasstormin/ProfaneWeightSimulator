using CombatSimulator.Data;

namespace CombatSimulator.Analysis;

// Captures the build context behind one sampled Attack Speed weight.
public sealed class AttackSpeedDiagnosticEntry
{
    public required double Weight { get; init; }
    public required double CycleDamage { get; init; }
    public required Loadout Loadout { get; init; }

    public Item Weapon => Loadout.Items.Single(item =>
        item.Slot is EquipmentSlot.OneHandedWeapon or EquipmentSlot.TwoHandedWeapon);
}
