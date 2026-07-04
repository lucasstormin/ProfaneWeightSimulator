using System.Text.RegularExpressions;
using CombatSimulator.Models;

namespace CombatSimulator.Data;

// Converts the workbook's authoritative final-value sections into simulator game data.
public static partial class GameSheetParser
{
    public static GameData Parse(SpreadsheetWorkbook workbook)
    {
        WorksheetData caps = workbook.Sheet("Caps and Attribute distribution");
        WorksheetData combat = workbook.Sheet("Combat Information");

        CharacterStatsBuilder startingStats = new();
        startingStats.Set(AttributeId.AttackPower, FindMinimum(caps, AttributeId.AttackPower));
        startingStats.Set(AttributeId.MaxHealth, FindMinimum(caps, AttributeId.MaxHealth));
        startingStats.Set(AttributeId.WeaponDamage, FindMinimum(caps, AttributeId.WeaponDamage));

        CombatConfig config = new()
        {
            AttackPowerMultiplier = FindSetting(combat, "AttackPowerMultiplier"),
            WeaponDamageMultiplier = FindSetting(combat, "WeaponDamageMultiplier")
        };

        List<Item> items = [];
        items.AddRange(ParseArmor(workbook.Sheet("Sets (armors) lists and attribu")));
        items.AddRange(ParseAccessories(workbook.Sheet("Accessories lists and attribute")));
        items.AddRange(ParseWeapons(workbook.Sheet("Weapons lists and attributes")));

        GameData gameData = new()
        {
            StartingStats = startingStats.Build(),
            CombatConfig = config,
            Items = items
        };

        GameDataValidator.Validate(gameData);
        return gameData;
    }

    private static IEnumerable<Item> ParseArmor(WorksheetData sheet)
    {
        List<Item> items = [];
        Item[]? currentItems = null;

        for (int row = 1; row <= sheet.MaximumRow; row++)
        {
            string? firstColumn = sheet.Text(row, 1);
            if (string.Equals(firstColumn?.Trim(), "Artifacts", StringComparison.OrdinalIgnoreCase))
                break;

            string? itemFamily = sheet.Text(row, 4);
            if (firstColumn?.Contains("Valor do cap", StringComparison.OrdinalIgnoreCase) == true &&
                !string.IsNullOrWhiteSpace(itemFamily))
            {
                currentItems =
                [
                    NewItem(sheet.Text(row, 5), EquipmentSlot.Helmet),
                    NewItem(sheet.Text(row, 6), EquipmentSlot.Chest),
                    NewItem(sheet.Text(row, 7), EquipmentSlot.Gloves),
                    NewItem(sheet.Text(row, 8), EquipmentSlot.Leggings),
                    NewItem(sheet.Text(row, 9), EquipmentSlot.Greaves)
                ];
                items.AddRange(currentItems);
                continue;
            }

            if (currentItems is null || string.IsNullOrWhiteSpace(itemFamily))
                continue;

            AttributeId attribute = ParseAttribute(itemFamily);
            for (int index = 0; index < currentItems.Length; index++)
            {
                double? value = sheet.Number(row, 5 + index);
                if (value.HasValue)
                    currentItems[index].AddAttribute(
                        attribute,
                        AttributeCatalog.NormalizeImportedValue(attribute, value.Value));
            }
        }

        return items;
    }

    private static IEnumerable<Item> ParseAccessories(WorksheetData sheet)
    {
        List<Item> items = [];
        EquipmentSlot? section = null;
        Item? currentItem = null;

        for (int row = 1; row <= sheet.MaximumRow; row++)
        {
            string? firstColumn = sheet.Text(row, 1)?.Trim();
            if (string.Equals(firstColumn, "Rings", StringComparison.OrdinalIgnoreCase))
            {
                section = EquipmentSlot.Ring;
                currentItem = null;
                continue;
            }
            if (string.Equals(firstColumn, "Amulets", StringComparison.OrdinalIgnoreCase))
            {
                section = EquipmentSlot.Amulet;
                currentItem = null;
                continue;
            }

            if (section.HasValue &&
                sheet.Text(row, 2)?.Contains("Rela", StringComparison.OrdinalIgnoreCase) == true &&
                !string.IsNullOrWhiteSpace(firstColumn))
            {
                currentItem = NewItem(firstColumn, section.Value);
                items.Add(currentItem);
                continue;
            }

            if (currentItem is null || string.IsNullOrWhiteSpace(firstColumn))
                continue;

            double? value = sheet.Number(row, 4);
            if (value.HasValue)
            {
                AttributeId attribute = ParseAttribute(firstColumn);
                currentItem.AddAttribute(
                    attribute,
                    AttributeCatalog.NormalizeImportedValue(attribute, value.Value));
            }
        }

        return items;
    }

    private static IEnumerable<Item> ParseWeapons(WorksheetData sheet)
    {
        List<Item> items = [];
        EquipmentSlot? section = null;
        Item? currentItem = null;

        for (int row = 1; row <= sheet.MaximumRow; row++)
        {
            string? firstColumn = sheet.Text(row, 1)?.Trim();
            string? secondColumn = sheet.Text(row, 2)?.Trim();

            section = secondColumn switch
            {
                "1 Handed" => EquipmentSlot.OneHandedWeapon,
                "2 Handed" => EquipmentSlot.TwoHandedWeapon,
                "Offhand" => EquipmentSlot.OffHand,
                _ => section
            };

            if (section.HasValue &&
                secondColumn?.Contains("Rela", StringComparison.OrdinalIgnoreCase) == true &&
                !string.IsNullOrWhiteSpace(firstColumn))
            {
                currentItem = NewItem(firstColumn, section.Value);
                items.Add(currentItem);
                continue;
            }

            if (currentItem is null || string.IsNullOrWhiteSpace(firstColumn))
                continue;

            double? value = sheet.Number(row, 4);
            if (value.HasValue)
            {
                AttributeId attribute = ParseAttribute(firstColumn);
                currentItem.AddAttribute(
                    attribute,
                    AttributeCatalog.NormalizeImportedValue(attribute, value.Value));
            }
        }

        return items;
    }

    private static Item NewItem(string? name, EquipmentSlot slot)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException($"An item in slot '{slot}' has no name.");
        return new Item { Name = name.Trim(), Slot = slot };
    }

    private static double FindMinimum(WorksheetData sheet, AttributeId targetAttribute)
    {
        for (int row = 1; row <= sheet.MaximumRow; row++)
        {
            string label = NormalizeAttributeName(sheet.Text(row, 1) ?? string.Empty);
            if (AttributeCatalog.TryParse(label, out AttributeId attribute) && attribute == targetAttribute)
            {
                return sheet.Number(row, 2)
                    ?? throw new InvalidDataException($"'{targetAttribute}' has no minimum value.");
            }
        }

        throw new InvalidDataException($"Attribute '{targetAttribute}' was not found in the caps tab.");
    }

    private static double FindSetting(WorksheetData sheet, string settingName)
    {
        for (int row = 1; row <= sheet.MaximumRow; row++)
        {
            if (string.Equals(sheet.Text(row, 1)?.Trim(), settingName, StringComparison.OrdinalIgnoreCase))
                return sheet.Number(row, 2) ?? throw new InvalidDataException($"'{settingName}' has no value.");
        }

        throw new InvalidDataException($"Combat setting '{settingName}' was not found.");
    }

    private static AttributeId ParseAttribute(string value)
    {
        string label = NormalizeAttributeName(value);
        if (AttributeCatalog.TryParse(label, out AttributeId attribute))
            return attribute;

        throw new InvalidDataException($"Unknown spreadsheet attribute '{label}'. Add it to AttributeCatalog before importing it.");
    }

    private static string NormalizeAttributeName(string value)
    {
        string withoutSymbols = AttributeDecorationRegex().Replace(value, string.Empty).Trim();
        return withoutSymbols;
    }

    [GeneratedRegex("[🔵🟣🟢]")]
    private static partial Regex AttributeDecorationRegex();
}
