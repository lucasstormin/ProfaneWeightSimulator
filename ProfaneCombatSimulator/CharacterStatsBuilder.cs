namespace CombatSimulator.Models;

// Efficiently accumulates typed attribute values before producing immutable character stats.
public sealed class CharacterStatsBuilder
{
    private readonly double[] values;

    public CharacterStatsBuilder()
    {
        values = new double[Enum.GetValues<AttributeId>().Length];
    }

    public CharacterStatsBuilder(CharacterStats source)
    {
        values = source.CopyValues();
    }

    public void Add(AttributeId attribute, double amount) => values[(int)attribute] += amount;

    public void Set(AttributeId attribute, double value) => values[(int)attribute] = value;

    public CharacterStats Build() => new(values);
}
