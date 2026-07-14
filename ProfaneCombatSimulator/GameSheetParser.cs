using System.Text.RegularExpressions;
using CombatSimulator.Models;

namespace CombatSimulator.Data;

// Converts the workbook's authoritative final-value sections into simulator game data.
public static partial class GameSheetParser
{
    // Imports starting stats, combat settings, items, and weapon attack profiles.
    public static GameData Parse(SpreadsheetWorkbook workbook)
    {
        WorksheetData caps = workbook.Sheet("Caps and Attribute distribution");
        WorksheetData combat = workbook.Sheet("Combat Information");
        ValidateAttackSpeedInformation(workbook.Sheet("Attack Speed Information"));

        CharacterStatsBuilder startingStats = new();
        startingStats.Set(AttributeId.AttackPower, FindMinimum(caps, AttributeId.AttackPower));
        startingStats.Set(AttributeId.MaxHealth, FindMinimum(caps, AttributeId.MaxHealth));
        startingStats.Set(AttributeId.WeaponDamage, FindMinimum(caps, AttributeId.WeaponDamage));
        startingStats.Set(AttributeId.Armor, FindMinimum(caps, AttributeId.Armor));
        startingStats.Set(AttributeId.CriticalChance, FindMinimum(caps, AttributeId.CriticalChance));
        startingStats.Set(AttributeId.CriticalDamage, FindMinimum(caps, AttributeId.CriticalDamage));
        startingStats.Set(AttributeId.HealthRegen, FindMinimum(caps, AttributeId.HealthRegen));
        startingStats.Set(AttributeId.LifeSteal, FindMinimum(caps, AttributeId.LifeSteal));
        startingStats.Set(AttributeId.MagicPower, FindMinimum(caps, AttributeId.MagicPower));
        startingStats.Set(AttributeId.MagicResist, FindMinimum(caps, AttributeId.MagicResist));
        startingStats.Set(AttributeId.MaxMana, FindMinimum(caps, AttributeId.MaxMana));
        startingStats.Set(AttributeId.ManaRegen, FindMinimum(caps, AttributeId.ManaRegen));
        startingStats.Set(AttributeId.CooldownReduction, FindMinimum(caps, AttributeId.CooldownReduction));
        startingStats.Set(AttributeId.ManaEfficiency, FindMinimum(caps, AttributeId.ManaEfficiency));

        CombatConfig config = new()
        {
            AttackPowerMultiplier = FindSetting(combat, "AttackPowerMultiplier"),
            PhysicalArmorConstant = FindSetting(combat, "Physical Armor Constant"),
            MagicalArmorConstant = FindSetting(combat, "Magical Armor Constant"),
            SkillSlots = (int)FindSetting(combat, "Skill slots")
        };

        IReadOnlyDictionary<string, WeaponAttackProfile> attackProfiles =
            ParseAttackProfiles(workbook.Sheet("Weapon Attack Profile Informati"));

        List<Item> items = [];
        items.AddRange(ParseArmor(workbook.Sheet("Sets (armors) lists and attribu")));
        items.AddRange(ParseAccessories(workbook.Sheet("Accessories lists and attribute")));
        items.AddRange(ParseWeapons(workbook.Sheet("Weapons lists and attributes")));
        IReadOnlyList<SkillDefinition> skills = ParseSkills(workbook.Sheet("Skill information"));

        GameData gameData = new()
        {
            StartingStats = startingStats.Build(),
            CombatConfig = config,
            Items = items,
            AttackProfiles = attackProfiles,
            Skills = skills
        };

        GameDataValidator.Validate(gameData);
        return gameData;
    }

    // Imports armor-piece attributes until the intentionally excluded artifact section.
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
                bool excludeFromSimulation = IsTrue(sheet.Text(row, 12));
                currentItems =
                [
                    NewItem(sheet.Text(row, 5), EquipmentSlot.Helmet, itemFamily, excludeFromSimulation),
                    NewItem(sheet.Text(row, 6), EquipmentSlot.Chest, itemFamily, excludeFromSimulation),
                    NewItem(sheet.Text(row, 7), EquipmentSlot.Gloves, itemFamily, excludeFromSimulation),
                    NewItem(sheet.Text(row, 8), EquipmentSlot.Leggings, itemFamily, excludeFromSimulation),
                    NewItem(sheet.Text(row, 9), EquipmentSlot.Greaves, itemFamily, excludeFromSimulation)
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
                    AddImportedAttribute(currentItems[index], attribute, value.Value);
            }
        }

        return items;
    }

    // Imports rings and amulets while preserving their authoritative final values.
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
                currentItem = NewItem(firstColumn, section.Value, excludeFromSimulation: IsTrue(sheet.Text(row, 5)));
                items.Add(currentItem);
                continue;
            }

            if (currentItem is null || string.IsNullOrWhiteSpace(firstColumn))
                continue;

            double? value = sheet.Number(row, 4);
            if (value.HasValue)
                AddImportedAttribute(currentItem, ParseAttribute(firstColumn), value.Value);
        }

        return items;
    }

    // Imports main-hand and off-hand items plus each main-hand's profile mapping.
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
                currentItem = NewItem(firstColumn, section.Value, excludeFromSimulation: IsTrue(sheet.Text(row, 6)));
                TryAssignAttackProfile(currentItem, sheet.Text(row, 5));
                items.Add(currentItem);
                continue;
            }

            if (currentItem is null)
                continue;

            TryAssignAttackProfile(currentItem, sheet.Text(row, 5));
            if (string.IsNullOrWhiteSpace(firstColumn))
                continue;

            double? value = sheet.Number(row, 4);
            if (value.HasValue)
                AddImportedAttribute(currentItem, ParseAttribute(firstColumn), value.Value);
        }

        return items;
    }

    // Imports three ordered attacks for every profile listed in the profile table.
    private static IReadOnlyDictionary<string, WeaponAttackProfile> ParseAttackProfiles(WorksheetData sheet)
    {
        Dictionary<string, List<AttackStep>> groupedSteps = new(StringComparer.OrdinalIgnoreCase);

        for (int row = 3; row <= sheet.MaximumRow; row++)
        {
            string? label = sheet.Text(row, 1)?.Trim();
            Match match = AttackProfileRowRegex().Match(label ?? string.Empty);
            if (!match.Success)
                continue;

            string profileName = NormalizeProfileName(match.Groups["profile"].Value);
            int sequence = int.Parse(match.Groups["sequence"].Value);
            AttackStep step = new()
            {
                Sequence = sequence,
                Anticipation = RequiredNumber(sheet, row, 2, "Anticipation"),
                Contact = RequiredNumber(sheet, row, 3, "Contact"),
                Recovery = RequiredNumber(sheet, row, 4, "Recovery"),
                WeaponDamageMultiplier = RequiredNumber(sheet, row, 5, "Weapon Damage Multiplier")
            };

            if (!groupedSteps.TryGetValue(profileName, out List<AttackStep>? steps))
            {
                steps = [];
                groupedSteps.Add(profileName, steps);
            }
            steps.Add(step);
        }

        return groupedSteps.ToDictionary(
            entry => entry.Key,
            entry => new WeaponAttackProfile
            {
                Name = entry.Key,
                Steps = entry.Value.OrderBy(step => step.Sequence).ToArray()
            },
            StringComparer.OrdinalIgnoreCase);
    }

    // Imports usable damage skills while preserving physical/magical scaling metadata.
    private static IReadOnlyList<SkillDefinition> ParseSkills(WorksheetData sheet)
    {
        List<SkillDefinition> skills = [];
        for (int row = 3; row <= sheet.MaximumRow; row++)
        {
            string? name = sheet.Text(row, 1)?.Trim();
            if (string.IsNullOrWhiteSpace(name) || IsIgnoredSkill(sheet.Text(row, 7)))
                continue;

            double? damage = sheet.Number(row, 2);
            double attackScaling = OptionalNumber(sheet, row, 3);
            double magicScaling = OptionalNumber(sheet, row, 4);
            double? manaCost = sheet.Number(row, 5);
            double? cooldown = sheet.Number(row, 6);
            if (!damage.HasValue || !manaCost.HasValue || !cooldown.HasValue)
                continue;

            skills.Add(new SkillDefinition
            {
                Name = name,
                Damage = damage.Value,
                AttackScaling = attackScaling,
                MagicScaling = magicScaling,
                ManaCost = manaCost.Value,
                Cooldown = cooldown.Value
            });
        }

        return skills;
    }

    // Applies spreadsheet rounding metadata before storing an item attribute.
    private static void AddImportedAttribute(Item item, AttributeId attribute, double value)
    {
        item.AddAttribute(attribute, AttributeCatalog.NormalizeImportedValue(attribute, value));
    }

    // Assigns a non-header profile name found anywhere in the current weapon block.
    private static void TryAssignAttackProfile(Item item, string? profileName)
    {
        string? normalized = profileName is null ? null : NormalizeProfileName(profileName);
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Equals("Profile", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (item.AttackProfileName is not null &&
            !item.AttackProfileName.Equals(normalized, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Weapon '{item.Name}' maps to more than one attack profile.");
        }

        item.AttackProfileName = normalized;
    }

    // Creates an item and rejects unnamed spreadsheet blocks.
    private static Item NewItem(
        string? name,
        EquipmentSlot slot,
        string? armorSetName = null,
        bool excludeFromSimulation = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException($"An item in slot '{slot}' has no name.");
        return new Item
        {
            Name = name.Trim(),
            Slot = slot,
            ArmorSetName = string.IsNullOrWhiteSpace(armorSetName) ? null : armorSetName.Trim(),
            ExcludeFromSimulation = excludeFromSimulation
        };
    }

    // Finds an attribute's starting value in the caps table.
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

    // Finds a required numeric combat setting by its spreadsheet label.
    private static double FindSetting(WorksheetData sheet, string settingName)
    {
        for (int row = 1; row <= sheet.MaximumRow; row++)
        {
            string? label = sheet.Text(row, 1)?.Trim();
            if (label is not null &&
                (string.Equals(label, settingName, StringComparison.OrdinalIgnoreCase) ||
                label.StartsWith($"{settingName} ", StringComparison.OrdinalIgnoreCase)))
            {
                return sheet.Number(row, 2) ?? throw new InvalidDataException($"'{settingName}' has no numeric value.");
            }
        }

        throw new InvalidDataException($"Combat setting '{settingName}' was not found.");
    }

    // Reads optional numeric cells, treating nonnumeric placeholders as zero.
    private static double OptionalNumber(WorksheetData sheet, int row, int column) =>
        sheet.Number(row, column) ?? 0;

    // Reads the spreadsheet's checkbox export for skills excluded from simulation.
    private static bool IsIgnoredSkill(string? value) =>
        bool.TryParse(value, out bool ignored) && ignored;

    // Reads spreadsheet checkbox exports that mark entries unavailable for simulation sampling.
    private static bool IsTrue(string? value)
    {
        if (bool.TryParse(value, out bool result))
            return result;
        return string.Equals(value?.Trim(), "1", StringComparison.OrdinalIgnoreCase);
    }

    // Reads a required numeric profile cell and reports its exact location when missing.
    private static double RequiredNumber(WorksheetData sheet, int row, int column, string label)
    {
        return sheet.Number(row, column)
            ?? throw new InvalidDataException($"Profile row {row} has no numeric {label} value.");
    }

    // Confirms the authoritative rules tab still describes duration scaling and unrounded bonuses.
    private static void ValidateAttackSpeedInformation(WorksheetData sheet)
    {
        List<string> cells = [];
        for (int row = 1; row <= sheet.MaximumRow; row++)
        {
            for (int column = 1; column <= 10; column++)
            {
                if (sheet.Text(row, column) is { } value)
                    cells.Add(value);
            }
        }

        string rules = string.Join(' ', cells);
        if (!rules.Contains("Attack Duration", StringComparison.OrdinalIgnoreCase) ||
            !rules.Contains("Attack Speed Bonus", StringComparison.OrdinalIgnoreCase) ||
            !rules.Contains("not rounded", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The Attack Speed Information tab no longer contains the expected duration formula and rounding rule.");
        }
    }

    // Converts a decorated spreadsheet label into a typed attribute identifier.
    private static AttributeId ParseAttribute(string value)
    {
        string label = NormalizeAttributeName(value);
        if (AttributeCatalog.TryParse(label, out AttributeId attribute))
            return attribute;

        throw new InvalidDataException($"Unknown spreadsheet attribute '{label}'. Add it to AttributeCatalog before importing it.");
    }

    // Removes color markers while preserving the authoritative attribute label.
    private static string NormalizeAttributeName(string value)
    {
        return AttributeDecorationRegex().Replace(value, string.Empty).Trim();
    }

    // Removes explanatory parenthetical suffixes so profile mappings use stable canonical names.
    private static string NormalizeProfileName(string value)
    {
        return ProfileSuffixRegex().Replace(value.Trim(), string.Empty).Trim();
    }

    // Matches visual rarity markers that are not part of an attribute's name.
    [GeneratedRegex("[🔵🟣🟢]")]
    private static partial Regex AttributeDecorationRegex();

    // Extracts a canonical profile name and ordered attack number from a row label.
    [GeneratedRegex("^(?<profile>.+) Profile: Attack (?<sequence>[123])$")]
    private static partial Regex AttackProfileRowRegex();

    // Removes explanatory aliases appended to a canonical profile mapping.
    [GeneratedRegex("\\s*\\([^)]*\\)$")]
    private static partial Regex ProfileSuffixRegex();
}
