using CombatSimulator.Models;

namespace CombatSimulator.Data;

// Represents one authoritative item imported from the balance spreadsheet.
public sealed class Item
{
    public required string Name { get; init; }
    public required EquipmentSlot Slot { get; init; }
    private readonly Dictionary<AttributeId, double> attributes = [];

    public IReadOnlyDictionary<AttributeId, double> Attributes => attributes;

    public double GetAttribute(AttributeId attribute) =>
        attributes.TryGetValue(attribute, out double value) ? value : 0;

    public void AddAttribute(AttributeId attribute, double value)
    {
        if (!attributes.TryAdd(attribute, value))
            throw new InvalidDataException($"Item '{Name}' defines '{attribute}' more than once.");
    }
}
