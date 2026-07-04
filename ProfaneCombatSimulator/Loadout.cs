using CombatSimulator.Models;

namespace CombatSimulator.Analysis;

// Stores one valid equipment combination and the character attributes it produces.
public sealed class Loadout
{
    public required CharacterStats Stats { get; init; }
    public required IReadOnlyList<Data.Item> Items { get; init; }

    public string Description => string.Join(", ", Items.Select(item => item.Name));
}
