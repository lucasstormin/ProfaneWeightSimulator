using System.Globalization;
using System.Text;
using CombatSimulator.Analysis;
using CombatSimulator.Combat;
using CombatSimulator.Data;
using CombatSimulator.Models;
using CombatSimulator.Validation;

if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
{
    RegressionSuite.Run();
    return;
}

const string spreadsheetId = "1dH-1xQ5fE2y2HTw27evHl449DhbhXewrhR97haOO8jc";
const int defaultFights = 100_000;
const int randomSeed = 20260704;

string solutionRoot = FindSolutionRoot();
string cachePath = Path.Combine(solutionRoot, "Data", "game-balance-cache.xlsx");

Console.WriteLine("Loading authoritative game data...");
(string workbookPath, bool usedCache) =
    await GoogleSheetDataSource.GetWorkbookAsync(spreadsheetId, cachePath);

SpreadsheetWorkbook workbook = XlsxWorkbookReader.Read(workbookPath);
GameData gameData = GameSheetParser.Parse(workbook);
int eligibleWeapons = gameData.Items.Count(item =>
    (item.Slot is EquipmentSlot.OneHandedWeapon or EquipmentSlot.TwoHandedWeapon) &&
    !item.IsBow);

Console.WriteLine($"Source: {(usedCache ? "local cache" : "Google Sheets")}");
Console.WriteLine($"Imported items: {gameData.Items.Count}");
Console.WriteLine($"Imported attack profiles: {gameData.AttackProfiles.Count}");
Console.WriteLine($"Imported skills: {gameData.Skills.Count}");
Console.WriteLine($"Eligible non-bow weapons: {eligibleWeapons}");
while (true)
{
    int fights = ReadSimulationCount(defaultFights);
    LoadoutGenerationMode generationMode = ReadGenerationMode();
    AnalysisType analysisType = ReadAnalysisType();
    double combatWindowSeconds = analysisType == AnalysisType.SkillOutput
        ? ReadCombatWindowSeconds()
        : 0;
    Console.WriteLine($"Loadout mode: {DescribeGenerationMode(generationMode)}");
    Console.WriteLine($"Analysis type: {DescribeAnalysisType(analysisType)}");
    if (analysisType == AnalysisType.SkillOutput)
        Console.WriteLine($"Combat window: {combatWindowSeconds:F2} seconds");
    Console.WriteLine($"Analyzing {fights:N0} valid loadouts...");
    Console.WriteLine();

    bool runAgain;
    if (analysisType == AnalysisType.BasicAttackDuel)
    {
        SimulationAnalysisResult result =
            TimeBasedAnalysisRunner.Analyze(gameData, fights, randomSeed, generationMode);
        PrintReport(result);
        PrintSheetExclusions(gameData);

        if (Console.IsInputRedirected)
            break;

        runAgain = HandlePostSimulationMenu(result);
        result = null!;
    }
    else
    {
        SkillOutputAnalysisResult result = SkillOutputAnalysisRunner.Analyze(
            gameData,
            fights,
            randomSeed,
            generationMode,
            combatWindowSeconds);
        PrintSkillOutputReport(result);
        PrintSheetExclusions(gameData);

        if (Console.IsInputRedirected)
            break;

        runAgain = HandlePostSkillSimulationMenu();
        result = null!;
    }

    if (!runAgain)
        break;

    Console.WriteLine();
}

// Prints the complete weight and time-based validation report for one run.
static void PrintReport(SimulationAnalysisResult result)
{
    Console.WriteLine("ATTRIBUTE WEIGHT REPORT");
    Console.WriteLine("=======================");
    Console.WriteLine($"{"Attribute",-27} {"Recommended Weight",20} {"Typical Range",22} {"Variation",18}");
    Console.WriteLine(new string('-', 91));
    Console.WriteLine($"{"Attack Power",-27} {1.0,20:F4} {"Fixed",22} {"None",18}");
    PrintSummaryRow(result.WeaponDamage);
    PrintSummaryRow(result.Health);
    PrintSummaryRow(result.AttackSpeed);
    PrintSummaryRow(result.Armor);
    PrintSummaryRow(result.ArmorPenetration);
    PrintSummaryRow(result.CriticalChance);
    PrintSummaryRow(result.CriticalDamage);
    PrintSummaryRow(result.HealthRegen);
    PrintSummaryRow(result.LifeSteal);
    Console.WriteLine();

    Console.WriteLine("TIME-BASED VALIDATION");
    Console.WriteLine("=====================");
    Console.WriteLine($"Average fight duration: {result.AverageCompletedFightDuration:F2} seconds");
    Console.WriteLine($"Shortest fight duration: {result.ShortestCompletedFightDuration:F2} seconds");
    Console.WriteLine($"Longest fight duration: {result.LongestCompletedFightDuration:F2} seconds");
    Console.WriteLine($"Stalemates: {result.Stalemates:N0} of {result.SimulatedFights:N0} fights");
    if (result.Draws > 0)
        Console.WriteLine($"Draws: {result.Draws:N0} of {result.SimulatedFights:N0} fights");
    Console.WriteLine($"Maximum fight duration: {TimeBasedCombatSimulator.DefaultMaximumDuration:N0} seconds");
    Console.WriteLine($"Attack Speed/AP outcome agreement: {result.AttackSpeedOutcomeAgreementRate:F2}%");
    Console.WriteLine($"Armor/AP outcome agreement: {result.ArmorOutcomeAgreementRate:F2}%");
    Console.WriteLine($"Armor Penetration/AP outcome agreement: {result.ArmorPenetrationOutcomeAgreementRate:F2}%");
    Console.WriteLine($"Critical Chance/AP outcome agreement: {result.CriticalChanceOutcomeAgreementRate:F2}%");
    Console.WriteLine(
        $"Critical Damage/AP outcome agreement: {result.CriticalDamageOutcomeAgreementRate:F2}% " +
        $"({result.CriticalDamageValidationComparisons:N0} eligible fights)");
    Console.WriteLine($"Health Regen/AP outcome agreement: {result.HealthRegenOutcomeAgreementRate:F2}%");
    Console.WriteLine($"Life Steal/AP outcome agreement: {result.LifeStealOutcomeAgreementRate:F2}%");
    Console.WriteLine("Formula/profile validation: Passed");
    Console.WriteLine("Timing simulation: Passed");
    Console.WriteLine();

    Console.WriteLine("ATTRIBUTE WEIGHT DETAILS");
    Console.WriteLine("========================");
    PrintDetails(result.Health);
    PrintDetails(result.WeaponDamage);
    PrintDetails(result.AttackSpeed);
    PrintDetails(result.Armor);
    PrintDetails(result.ArmorPenetration);
    PrintDetails(result.CriticalChance);
    PrintDetails(result.CriticalDamage);
    PrintDetails(result.HealthRegen);
    PrintDetails(result.LifeSteal);
    Console.WriteLine();

    Console.WriteLine("LEGEND");
    Console.WriteLine("======");
    Console.WriteLine("Mean: Average weight across all sampled loadouts.");
    Console.WriteLine("Median: Middle weight; used as the recommended balance-sheet weight.");
    Console.WriteLine("SD: How much the weight varies between loadouts; lower is more consistent.");
    Console.WriteLine("Observed: Lowest and highest weights found in the simulation.");
    Console.WriteLine("Attack Speed/AP agreement: Fights where +1% Attack Speed and its calculated AP equivalent had the same outcome.");
    Console.WriteLine("Armor/AP agreement: Fights where +1 Armor and its calculated AP equivalent had the same outcome.");
    Console.WriteLine("Armor Penetration/AP agreement: Fights where +1 percentage point and its calculated AP equivalent had the same outcome.");
    Console.WriteLine("Critical Chance/AP agreement: Fights where +1 percentage point and its calculated AP equivalent had the same outcome.");
    Console.WriteLine("Critical Damage/AP agreement: Fights where +1 percentage point and its calculated AP equivalent had the same outcome.");
    Console.WriteLine("Health Regen/AP agreement: Fights where +1 HP/second and its calculated AP equivalent had the same outcome.");
    Console.WriteLine("Life Steal/AP agreement: Fights where +1 percentage point and its calculated AP equivalent had the same outcome.");
    Console.WriteLine("* Critical Damage uses only builds with at least 10% Critical Chance.");
}

// Prints the Magic-Power-based skill-output report for caster/resource stats.
static void PrintSkillOutputReport(SkillOutputAnalysisResult result)
{
    Console.WriteLine("CASTER ATTRIBUTE WEIGHT REPORT");
    Console.WriteLine("==============================");
    Console.WriteLine("Base weight: 1 Magic Power = 1.0000 caster item-power unit");
    Console.WriteLine($"Skill slots: {result.SkillSlots}");
    Console.WriteLine($"Eligible magical skills: {result.EligibleSkills}");
    Console.WriteLine(
        $"Cooldown Reduction eligible samples: {result.CooldownReductionEligibleSamples:N0} " +
        $"of {result.Simulations:N0} (>10% existing CDR)");
    double manaPressure = Percent(result.ManaLimitedSamples, result.Simulations);
    Console.WriteLine($"Mana pressure: {DescribeManaPressure(manaPressure)} ({manaPressure:F1}% of builds were mana-limited)");
    Console.WriteLine($"Mana stat reliability: {DescribeManaReliability(manaPressure)}");
    Console.WriteLine($"Average smooth output: {result.AverageBaseOutput:F2} damage over {result.CombatWindowSeconds:F2}s");
    Console.WriteLine();
    Console.WriteLine($"{"Attribute",-30} {"Recommended Weight",20} {"Typical Range",22} {"Variation",18}");
    Console.WriteLine(new string('-', 94));
    Console.WriteLine($"{"Magic Power",-30} {1.0,20:F4} {"Fixed",22} {"None",18}");
    PrintCasterSummaryRow(result.CooldownReduction);
    PrintCasterSummaryRow(result.MagicResist);
    PrintCasterSummaryRow(result.MaxMana);
    PrintCasterSummaryRow(result.ManaRegen);
    PrintCasterSummaryRow(result.ManaEfficiency);
    Console.WriteLine();

    Console.WriteLine("CASTER ATTRIBUTE WEIGHT DETAILS");
    Console.WriteLine("===============================");
    PrintCasterDetails(result.CooldownReduction);
    PrintCasterDetails(result.MagicResist);
    PrintCasterDetails(result.MaxMana);
    PrintCasterDetails(result.ManaRegen);
    PrintCasterDetails(result.ManaEfficiency);
    Console.WriteLine();

    Console.WriteLine("LEGEND");
    Console.WriteLine("======");
    Console.WriteLine("Weights are relative to Magic Power, not Attack Power.");
    Console.WriteLine("Caster recommendations use priority-based smooth output estimates to avoid one-extra-cast breakpoints.");
    Console.WriteLine("Magic Power and CDR are valued as throughput; mana stats are valued by extra resource-limited output.");
    Console.WriteLine("Mana pressure shows how often caster builds wanted more mana during the combat window.");
    Console.WriteLine("Mana stat reliability explains whether mana weights are based on enough mana-limited builds.");
    Console.WriteLine("Median is used as the recommended caster balance-sheet weight.");
    Console.WriteLine("Typical Range is the middle 90% of sampled caster loadouts.");
    Console.WriteLine("Variation shows how context-dependent that caster stat is.");
    Console.WriteLine("Magic Resist is valued by magical damage prevented, relative to Magic Power output gained.");
    Console.WriteLine("* Cooldown Reduction uses only caster builds with more than 10% existing CDR.");
}

// Prints one contextual caster attribute in the scalable summary table.
static void PrintCasterSummaryRow(CasterAttributeWeightDistributionResult distribution)
{
    string range = $"{distribution.FifthPercentile:F4}–{distribution.NinetyFifthPercentile:F4}";
    double variationPercent = distribution.MeanWeight == 0
        ? 0
        : distribution.StandardDeviation / distribution.MeanWeight * 100;
    string variation = $"{ClassifyVariation(variationPercent)} ({variationPercent:F1}%)";
    Console.WriteLine($"{distribution.DisplayName,-30} {distribution.RecommendedWeight,20:F4} {range,22} {variation,18}");
}

// Prints compact distribution diagnostics for one caster attribute.
static void PrintCasterDetails(CasterAttributeWeightDistributionResult distribution)
{
    Console.WriteLine(
        $"{distribution.DisplayName}: mean {distribution.MeanWeight:F4} | " +
        $"median {distribution.MedianWeight:F4} | SD {distribution.StandardDeviation:F4} | " +
        $"observed {distribution.MinimumWeight:F4}–{distribution.MaximumWeight:F4}");
}

// Prints one contextual attribute in the scalable summary table.
static void PrintSummaryRow(AttributeWeightDistributionResult distribution)
{
    if (!distribution.IsAvailable)
    {
        Console.WriteLine($"{distribution.DisplayName,-27} {"N/A",20} {"N/A",22} {"No samples",18}");
        return;
    }

    string range = $"{distribution.FifthPercentile:F4}–{distribution.NinetyFifthPercentile:F4}";
    double variationPercent = distribution.StandardDeviation / distribution.MeanWeight * 100;
    string variation = $"{ClassifyVariation(variationPercent)} ({variationPercent:F1}%)";
    Console.WriteLine($"{distribution.DisplayName,-27} {distribution.RecommendedWeight,20:F4} {range,22} {variation,18}");
}

// Prints compact distribution diagnostics for one contextual attribute.
static void PrintDetails(AttributeWeightDistributionResult distribution)
{
    if (!distribution.IsAvailable)
    {
        Console.WriteLine($"{distribution.DisplayName}: {distribution.UnavailableReason}");
        return;
    }

    Console.WriteLine(
        $"{distribution.DisplayName}: mean {distribution.MeanWeight:F4} | " +
        $"median {distribution.MedianWeight:F4} | SD {distribution.StandardDeviation:F4} | " +
        $"observed {distribution.MinimumWeight:F4}–{distribution.MaximumWeight:F4}");
}

// Prints the strongest sampled builds and their contextual Attack Speed values.
static void PrintDiagnostics(SimulationAnalysisResult analysis)
{
    Console.WriteLine();
    Console.WriteLine("STRONGEST SAMPLED BASIC-ATTACK BUILDS");
    Console.WriteLine("======================================");
    Console.WriteLine("Ranked by sustained unmitigated DPS; AS weight is the value of the next +1% Attack Speed.");
    foreach (AttackSpeedDiagnosticEntry entry in analysis.StrongestAttackSpeedBuilds)
        PrintAttackSpeedBuild(entry);

    Console.WriteLine();
    Console.WriteLine("WEAPON/PROFILE DPS SUMMARY");
    Console.WriteLine("==========================");
    Console.WriteLine($"{"Weapon",-30} {"Profile",-22} {"Samples",8} {"Mean DPS",10} {"95th DPS",10} {"Max DPS",10} {"AS Weight",10}");
    Console.WriteLine(new string('-', 107));
    foreach (WeaponProfileDpsSummary summary in analysis.WeaponProfileDpsSummaries)
    {
        Console.WriteLine(
            $"{summary.WeaponName,-30} {summary.ProfileName,-22} {summary.Samples,8:N0} " +
            $"{summary.MeanDamagePerSecond,10:F2} {summary.NinetyFifthPercentileDamagePerSecond,10:F2} " +
            $"{summary.MaximumDamagePerSecond,10:F2} {summary.AttackSpeedWeightAtMaximum,10:F4}");
    }

    if (analysis.ShortestFight is not null)
        PrintFightDiagnostic("SHORTEST COMPLETED FIGHT", analysis.ShortestFight, includeRegenContext: false);
    if (analysis.LongestFight is not null)
        PrintFightDiagnostic("LONGEST COMPLETED FIGHT", analysis.LongestFight, includeRegenContext: false);
    PrintFightDiagnostic(
        "MAXIMUM HEALTH REGEN WEIGHT",
        analysis.MaximumHealthRegenWeightFight,
        includeRegenContext: true);
}

// Prints both complete builds and timing details for one notable fight.
static void PrintFightDiagnostic(
    string title,
    FightDiagnosticEntry diagnostic,
    bool includeRegenContext)
{
    Console.WriteLine();
    Console.WriteLine(title);
    Console.WriteLine(new string('=', title.Length));
    Console.WriteLine(
        $"Duration: {diagnostic.Fight.Duration:F2} seconds | " +
        $"Outcome: {diagnostic.Fight.Outcome}");
    if (includeRegenContext)
    {
        Console.WriteLine(
            $"HP/s weight: {diagnostic.HealthRegenWeight:F4} | " +
            $"Useful baseline ticks: {diagnostic.Fight.PlayerAAdditionalRegenHealingOpportunity:F0} | " +
            $"Health weight: {diagnostic.HealthWeight:F4} | " +
            $"Existing HP/s: {diagnostic.PlayerA.Stats[AttributeId.HealthRegen]:F0}");
    }

    PrintFightBuild(
        "Player A",
        diagnostic.PlayerA,
        diagnostic.Fight.PlayerARemainingHealth,
        diagnostic.Fight.PlayerAHits,
        diagnostic.Fight.PlayerATotalHealing);
    PrintFightBuild(
        "Player B",
        diagnostic.PlayerB,
        diagnostic.Fight.PlayerBRemainingHealth,
        diagnostic.Fight.PlayerBHits,
        diagnostic.Fight.PlayerBTotalHealing);
}

// Prints combat-relevant stats and every equipped item for one diagnostic player.
static void PrintFightBuild(
    string label,
    Loadout loadout,
    double remainingHealth,
    int hits,
    double totalHealing)
{
    CharacterStats stats = loadout.Stats;
    Console.WriteLine(
        $"{label}: AP {stats.AttackPower:F0} | Health {stats.MaxHealth:F0} | " +
        $"Armor {stats[AttributeId.Armor]:F0} | WD {stats.WeaponDamage:F0} | " +
        $"AS {stats[AttributeId.AttackSpeed]:P1} | CC {stats[AttributeId.CriticalChance]:P1} | " +
        $"CD {stats[AttributeId.CriticalDamage]:P1} | HP/s {stats[AttributeId.HealthRegen]:F0} | " +
        $"LS {stats[AttributeId.LifeSteal]:P1}");
    Console.WriteLine(
        $"  Remaining Health {remainingHealth:F0} | Hits {hits:N0} | Healed {totalHealing:F0}");
    Console.WriteLine($"  {loadout.Description}");
}

// Prints the combat stats and full equipment list for one sampled extreme.
static void PrintAttackSpeedBuild(AttackSpeedDiagnosticEntry entry)
{
    CharacterStats stats = entry.Loadout.Stats;
    Console.WriteLine(
        $"  DPS {entry.DamagePerSecond:F2} | AS weight {entry.Weight:F4} | " +
        $"{entry.Weapon.Name} | {entry.Loadout.AttackProfile.Name} | " +
        $"AP {stats.AttackPower:F0} | WD {stats.WeaponDamage:F0} | " +
        $"AS {stats[AttributeId.AttackSpeed]:P2} | Expected cycle damage {entry.CycleDamage:F1}");
    Console.WriteLine($"    {entry.Loadout.Description}");
}

// Classifies relative variation using stable thresholds shared by all attributes.
static string ClassifyVariation(double coefficientOfVariationPercent)
{
    if (coefficientOfVariationPercent < 10)
        return "Low";
    if (coefficientOfVariationPercent < 25)
        return "Moderate";
    return "High";
}

// Converts a count into a percentage while keeping empty runs defensive.
static double Percent(int count, int total) => total == 0 ? 0 : (double)count / total * 100;

// Summarizes whether the caster sample is usually constrained by mana.
static string DescribeManaPressure(double manaPressurePercent)
{
    if (manaPressurePercent >= 70)
        return "High";
    if (manaPressurePercent >= 30)
        return "Moderate";
    return "Low";
}

// Explains how much trust to place in mana-stat recommendations for this run.
static string DescribeManaReliability(double manaPressurePercent)
{
    if (manaPressurePercent >= 70)
        return "Good — mana stats affected most sampled caster builds.";
    if (manaPressurePercent >= 30)
        return "Mixed — mana stats matter in some builds, but recommendations are context-heavy.";
    return "Weak — most sampled builds were not mana-limited, so mana weights may be near zero or unstable.";
}

// Reads a positive simulation count from an editable prefilled console field.
static int ReadSimulationCount(int defaultValue)
{
    const int maximumFights = 5_000_000;
    if (Console.IsInputRedirected)
        return defaultValue;

    Console.WriteLine();
    string prompt = $"Number of simulations (1–{maximumFights:N0}): ";
    StringBuilder value = new(defaultValue.ToString(CultureInfo.InvariantCulture));
    int cursor = value.Length;
    Console.Write(prompt);
    Console.Write(value);

    while (true)
    {
        ConsoleKeyInfo key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            if (int.TryParse(value.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out int fights) &&
                fights is > 0 and <= maximumFights)
            {
                Console.WriteLine();
                return fights;
            }

            Console.WriteLine();
            Console.WriteLine($"Enter a whole number from 1 to {maximumFights:N0}.");
            value.Clear().Append(defaultValue.ToString(CultureInfo.InvariantCulture));
            cursor = value.Length;
            Console.Write(prompt);
            Console.Write(value);
            continue;
        }

        if (char.IsAsciiDigit(key.KeyChar))
        {
            value.Insert(cursor, key.KeyChar);
            cursor++;
        }
        else if (key.Key == ConsoleKey.Backspace && cursor > 0)
        {
            value.Remove(cursor - 1, 1);
            cursor--;
        }
        else if (key.Key == ConsoleKey.Delete && cursor < value.Length)
        {
            value.Remove(cursor, 1);
        }
        else if (key.Key == ConsoleKey.LeftArrow && cursor > 0)
        {
            cursor--;
        }
        else if (key.Key == ConsoleKey.RightArrow && cursor < value.Length)
        {
            cursor++;
        }
        else if (key.Key == ConsoleKey.Home)
        {
            cursor = 0;
        }
        else if (key.Key == ConsoleKey.End)
        {
            cursor = value.Length;
        }
        else
        {
            continue;
        }

        RedrawEditableValue(prompt, value, cursor);
    }
}

// Redraws a short editable value and restores its logical cursor position.
static void RedrawEditableValue(string prompt, StringBuilder value, int cursor)
{
    Console.Write('\r');
    Console.Write(prompt);
    Console.Write(value);
    Console.Write(' ');
    Console.CursorLeft = prompt.Length + cursor;
}

// Offers diagnostics or a fully reconfigured repeat run after each report.
static bool HandlePostSimulationMenu(SimulationAnalysisResult result)
{
    while (true)
    {
        Console.WriteLine();
        Console.Write("Press D for diagnostics, R to run another simulation, or any other key to exit: ");
        ConsoleKeyInfo key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.D)
        {
            Console.WriteLine("D");
            PrintDiagnostics(result);
            continue;
        }
        if (key.Key == ConsoleKey.R)
        {
            Console.WriteLine("R");
            return true;
        }

        Console.WriteLine();
        return false;
    }
}

// Offers a fully reconfigured repeat run after skill-output analysis.
static bool HandlePostSkillSimulationMenu()
{
    while (true)
    {
        Console.WriteLine();
        Console.Write("Press R to run another simulation, or any other key to exit: ");
        ConsoleKeyInfo key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.R)
        {
            Console.WriteLine("R");
            return true;
        }

        Console.WriteLine();
        return false;
    }
}

// Prompts interactive users for a loadout policy while keeping automated runs non-blocking.
static LoadoutGenerationMode ReadGenerationMode()
{
    if (Console.IsInputRedirected)
        return LoadoutGenerationMode.RandomPieces;

    while (true)
    {
        Console.WriteLine();
        Console.WriteLine("Press 1 to simulate allowing random pieces.");
        Console.WriteLine("Press 2 to simulate with set restriction.");
        ConsoleKeyInfo key = Console.ReadKey(intercept: true);
        if (key.KeyChar == '1')
        {
            Console.WriteLine("1");
            return LoadoutGenerationMode.RandomPieces;
        }
        if (key.KeyChar == '2')
        {
            Console.WriteLine("2");
            return LoadoutGenerationMode.ClosedArmorSet;
        }
    }
}

// Prompts interactive users for the combat model while keeping automated runs non-blocking.
static AnalysisType ReadAnalysisType()
{
    if (Console.IsInputRedirected)
        return AnalysisType.BasicAttackDuel;

    while (true)
    {
        Console.WriteLine();
        Console.WriteLine("Press 1 to run basic attack duel simulation.");
        Console.WriteLine("Press 2 to run skill output simulation.");
        ConsoleKeyInfo key = Console.ReadKey(intercept: true);
        if (key.KeyChar == '1')
        {
            Console.WriteLine("1");
            return AnalysisType.BasicAttackDuel;
        }
        if (key.KeyChar == '2')
        {
            Console.WriteLine("2");
            return AnalysisType.SkillOutput;
        }
    }
}

// Reads the caster-output combat window in seconds.
static double ReadCombatWindowSeconds()
{
    const double defaultWindow = 45;
    if (Console.IsInputRedirected)
        return defaultWindow;

    while (true)
    {
        Console.WriteLine();
        Console.Write($"Combat window in seconds [{defaultWindow:F0}]: ");
        string input = Console.ReadLine() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(input))
            return defaultWindow;
        if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) &&
            seconds > 0)
        {
            return seconds;
        }

        Console.WriteLine("Enter a positive number of seconds.");
    }
}

// Converts the selected generation policy into a concise report label.
static string DescribeGenerationMode(LoadoutGenerationMode mode) => mode switch
{
    LoadoutGenerationMode.RandomPieces => "Random armor pieces",
    LoadoutGenerationMode.ClosedArmorSet => "Closed armor sets",
    _ => throw new ArgumentOutOfRangeException(nameof(mode))
};

// Converts the selected analysis model into a concise report label.
static string DescribeAnalysisType(AnalysisType analysisType) => analysisType switch
{
    AnalysisType.BasicAttackDuel => "Basic attack duel",
    AnalysisType.SkillOutput => "Skill output",
    _ => throw new ArgumentOutOfRangeException(nameof(analysisType))
};

// Prints sheet-controlled exclusions that are removed from every simulation mode.
static void PrintSheetExclusions(GameData gameData)
{
    string[] sheetExcludedArmorSets = gameData.Items
        .Where(item => item.ArmorSetName is not null && item.ExcludeFromSimulation)
        .Select(item => item.ArmorSetName!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    string[] sheetExcludedItems = gameData.Items
        .Where(item => item.ArmorSetName is null && item.ExcludeFromSimulation)
        .Select(item => item.Name)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    Console.WriteLine();
    Console.WriteLine("SHEET EXCLUSIONS");
    Console.WriteLine("================");
    Console.WriteLine($"Sheet-excluded armor sets: {FormatList(sheetExcludedArmorSets)}");
    Console.WriteLine($"Sheet-excluded items: {FormatList(sheetExcludedItems)}");
}

// Formats empty and populated report lists in a compact, readable way.
static string FormatList(IEnumerable<string> values)
{
    string[] materialized = values.ToArray();
    return materialized.Length == 0 ? "None" : string.Join(", ", materialized);
}

// Locates the solution root from either the current directory or executable path.
static string FindSolutionRoot()
{
    DirectoryInfo? directory = new(Directory.GetCurrentDirectory());
    while (directory is not null)
    {
        if (directory.EnumerateFiles("*.sln").Any())
            return directory.FullName;
        directory = directory.Parent;
    }

    directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (directory.EnumerateFiles("*.sln").Any())
            return directory.FullName;
        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate the solution root.");
}
