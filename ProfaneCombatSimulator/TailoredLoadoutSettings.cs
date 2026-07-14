using CombatSimulator.Data;

namespace CombatSimulator.Analysis;

// Stores the user-selected exclusions that define one Tailored simulation run.
public sealed class TailoredLoadoutSettings
{
    public static readonly string[] DefaultArmorSets =
        ["Silk", "Web", "Linen", "Hide", "Iron"];

    public static readonly string[] DefaultWeaponNames =
        ["Repair Hammer", "Spiked Club"];

    public required IReadOnlySet<string> ExcludedArmorSets { get; init; }
    public required IReadOnlySet<string> ExcludedWeaponNames { get; init; }
    public required bool ExcludeImprovisedWeapons { get; init; }
    public required bool ExcludeNecrosisWeapons { get; init; }
    public required bool ExcludeStaffWeapons { get; init; }

    // Creates the original Tailored exclusion set requested for focused balance sampling.
    public static TailoredLoadoutSettings CreateDefault()
    {
        return new TailoredLoadoutSettings
        {
            ExcludedArmorSets = new HashSet<string>(DefaultArmorSets, StringComparer.OrdinalIgnoreCase),
            ExcludedWeaponNames = new HashSet<string>(DefaultWeaponNames, StringComparer.OrdinalIgnoreCase),
            ExcludeImprovisedWeapons = true,
            ExcludeNecrosisWeapons = true,
            ExcludeStaffWeapons = true
        };
    }

    // Parses comma-separated user exclusions and reports unknown terms instead of guessing.
    public static bool TryParse(
        string input,
        GameData gameData,
        out TailoredLoadoutSettings? settings,
        out string error)
    {
        HashSet<string> knownArmorSets = gameData.Items
            .Where(item => item.ArmorSetName is not null)
            .Select(item => item.ArmorSetName!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> knownWeaponNames = gameData.Items
            .Where(item => item.Slot is EquipmentSlot.OneHandedWeapon or EquipmentSlot.TwoHandedWeapon)
            .Select(item => item.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        HashSet<string> armorSets = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> weaponNames = new(StringComparer.OrdinalIgnoreCase);
        bool excludeImprovised = false;
        bool excludeNecrosis = false;
        bool excludeStaff = false;
        List<string> unknownTerms = [];

        foreach (string rawTerm in input.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string term = rawTerm.Trim();
            string normalized = NormalizeTerm(term);
            if (string.IsNullOrWhiteSpace(normalized))
                continue;

            if (normalized.Contains("improvised", StringComparison.OrdinalIgnoreCase))
            {
                excludeImprovised = true;
                continue;
            }
            if (normalized.Contains("necrosis", StringComparison.OrdinalIgnoreCase))
            {
                excludeNecrosis = true;
                continue;
            }
            if (normalized is "staff" or "staves" ||
                normalized.Contains("staff weapon", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("staff weapons", StringComparison.OrdinalIgnoreCase))
            {
                excludeStaff = true;
                continue;
            }

            string possibleArmorSet = RemoveTrailingWord(normalized, "set");
            if (TryFindKnownValue(possibleArmorSet, knownArmorSets, out string? armorSet))
            {
                armorSets.Add(armorSet!);
                continue;
            }
            if (TryFindKnownValue(normalized, knownWeaponNames, out string? weaponName))
            {
                weaponNames.Add(weaponName!);
                continue;
            }

            unknownTerms.Add(term);
        }

        if (unknownTerms.Count > 0)
        {
            settings = null;
            error = $"Unknown exclusion term(s): {string.Join(", ", unknownTerms)}.";
            return false;
        }

        settings = new TailoredLoadoutSettings
        {
            ExcludedArmorSets = armorSets,
            ExcludedWeaponNames = weaponNames,
            ExcludeImprovisedWeapons = excludeImprovised,
            ExcludeNecrosisWeapons = excludeNecrosis,
            ExcludeStaffWeapons = excludeStaff
        };
        error = string.Empty;
        return true;
    }

    // Normalizes spacing and common plural wording before matching user-entered terms.
    private static string NormalizeTerm(string term)
    {
        string normalized = string.Join(' ', term.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        normalized = RemoveTrailingWord(normalized, "weapons");
        normalized = RemoveTrailingWord(normalized, "weapon");
        return normalized.Trim();
    }

    // Removes a final helper word such as "set" without changing names that merely contain it.
    private static string RemoveTrailingWord(string value, string word)
    {
        return value.EndsWith($" {word}", StringComparison.OrdinalIgnoreCase)
            ? value[..^word.Length].Trim()
            : value;
    }

    // Finds the authoritative casing for a known armor set or weapon name.
    private static bool TryFindKnownValue(
        string requestedValue,
        IEnumerable<string> knownValues,
        out string? knownValue)
    {
        knownValue = knownValues.FirstOrDefault(value =>
            value.Equals(requestedValue, StringComparison.OrdinalIgnoreCase));
        return knownValue is not null;
    }
}
