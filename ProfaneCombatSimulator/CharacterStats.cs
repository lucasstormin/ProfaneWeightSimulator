namespace CombatSimulator.Models;

// Stores an immutable snapshot of all attributes produced by a character build.
public sealed class CharacterStats
{
    private readonly double[] values;

    // Takes a defensive copy so completed loadouts cannot be mutated later.
    internal CharacterStats(double[] values)
    {
        this.values = (double[])values.Clone();
    }

    public double this[AttributeId attribute] => values[(int)attribute];
    public double AttackPower => this[AttributeId.AttackPower];
    public double MaxHealth => this[AttributeId.MaxHealth];
    public double WeaponDamage => this[AttributeId.WeaponDamage];

    // Returns a new snapshot with one contextual validation bonus applied.
    public CharacterStats WithAdded(AttributeId attribute, double amount)
    {
        double[] changedValues = (double[])values.Clone();
        changedValues[(int)attribute] += amount;
        return new CharacterStats(changedValues);
    }

    // Supplies a defensive copy to builders that extend an existing snapshot.
    internal double[] CopyValues() => (double[])values.Clone();
}
