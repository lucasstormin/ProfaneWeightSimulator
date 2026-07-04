using CombatSimulator.Models;

namespace CombatSimulator.Data;

// Contains the combat settings, starting attributes, and items imported from the sheet.
public sealed class GameData
{
    public required CharacterStats StartingStats { get; init; }
    public required CombatConfig CombatConfig { get; init; }
    public required IReadOnlyList<Item> Items { get; init; }
    public required IReadOnlyDictionary<string, WeaponAttackProfile> AttackProfiles { get; init; }
}
