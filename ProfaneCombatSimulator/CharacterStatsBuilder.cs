namespace CombatSimulator.Models;

// Efficiently accumulates typed attribute values before producing immutable character stats.
public sealed class CharacterStatsBuilder
{
    private readonly double[] values;

    // Starts an empty accumulator with one slot for every known attribute.
    public CharacterStatsBuilder()
    {
        values = new double[Enum.GetValues<AttributeId>().Length];
    }

    // Starts from an immutable snapshot without sharing its backing storage.
    public CharacterStatsBuilder(CharacterStats source)
    {
        values = source.CopyValues();
    }

    // Adds an item's contribution to one accumulated attribute.
    public void Add(AttributeId attribute, double amount) => values[(int)attribute] += amount;

    // Assigns an authoritative starting or replacement value.
    public void Set(AttributeId attribute, double value) => values[(int)attribute] = value;

    // Freezes the accumulated values into an immutable character snapshot.
    public CharacterStats Build() => new(values);
}
