namespace CombatSimulator.Models;

// Maps spreadsheet labels to typed attributes and owns attribute-level import rules.
public static class AttributeCatalog
{
    private static readonly IReadOnlyDictionary<string, AttributeId> Labels =
        new Dictionary<string, AttributeId>(StringComparer.OrdinalIgnoreCase)
        {
            ["Attack Power"] = AttributeId.AttackPower,
            ["Max HP"] = AttributeId.MaxHealth,
            ["Weapon Damage"] = AttributeId.WeaponDamage,
            ["Armor"] = AttributeId.Armor,
            ["HP5"] = AttributeId.HealthRegen,
            ["Magic Power"] = AttributeId.MagicPower,
            ["Magic Resist"] = AttributeId.MagicResist,
            ["Max Mana"] = AttributeId.MaxMana,
            ["MP5"] = AttributeId.ManaRegen,
            ["Cooldown Reduction"] = AttributeId.CooldownReduction,
            ["Casting Efficiency"] = AttributeId.CastingEfficiency,
            ["Mana Efficiency"] = AttributeId.ManaEfficiency,
            ["Perception"] = AttributeId.Perception,
            ["Stealth"] = AttributeId.Stealth,
            ["Move Speed"] = AttributeId.MoveSpeed,
            ["Critical Damage"] = AttributeId.CriticalDamage,
            ["Attack Speed"] = AttributeId.AttackSpeed,
            ["Critical Chance"] = AttributeId.CriticalChance,
            ["Armor Pen"] = AttributeId.ArmorPenetration,
            ["Life Steal"] = AttributeId.LifeSteal
        };

    private static readonly HashSet<AttributeId> WholeNumberAttributes =
    [
        AttributeId.AttackPower,
        AttributeId.MaxHealth,
        AttributeId.WeaponDamage,
        AttributeId.Armor,
        AttributeId.HealthRegen,
        AttributeId.MagicPower,
        AttributeId.MagicResist,
        AttributeId.MaxMana,
        AttributeId.ManaRegen,
        AttributeId.Perception,
        AttributeId.Stealth
    ];

    public static bool TryParse(string label, out AttributeId attribute) =>
        Labels.TryGetValue(label, out attribute);

    public static double NormalizeImportedValue(AttributeId attribute, double value) =>
        WholeNumberAttributes.Contains(attribute) ? Math.Round(value) : value;
}
