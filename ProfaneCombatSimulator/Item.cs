using CombatSimulator.Models;

namespace CombatSimulator.Data;

// Represents one authoritative item imported from the balance spreadsheet.
public sealed class Item
{
    public required string Name { get; init; }
    public required EquipmentSlot Slot { get; init; }
    public string? AttackProfileName { get; set; }
    private readonly Dictionary<AttributeId, double> attributes = [];

    public IReadOnlyDictionary<AttributeId, double> Attributes => attributes;
    public bool IsBow => Name.Contains("Bow", StringComparison.OrdinalIgnoreCase);

    // Returns an attribute value or zero when the item does not provide it.
    public double GetAttribute(AttributeId attribute) =>
        attributes.TryGetValue(attribute, out double value) ? value : 0;

    // Adds one authoritative attribute and rejects duplicate spreadsheet rows.
    public void AddAttribute(AttributeId attribute, double value)
    {
        if (!attributes.TryAdd(attribute, value))
            throw new InvalidDataException($"Item '{Name}' defines '{attribute}' more than once.");
    }
}
