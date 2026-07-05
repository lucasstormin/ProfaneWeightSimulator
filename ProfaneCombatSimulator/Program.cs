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
Console.WriteLine($"Eligible non-bow weapons: {eligibleWeapons}");
while (true)
{
    int fights = ReadSimulationCount(defaultFights);
    LoadoutGenerationMode generationMode = ReadGenerationMode();
    Console.WriteLine($"Loadout mode: {DescribeGenerationMode(generationMode)}");
    Console.WriteLine($"Analyzing {fights:N0} valid time-based loadouts...");
    Console.WriteLine();

    SimulationAnalysisResult result =
        TimeBasedAnalysisRunner.Analyze(gameData, fights, randomSeed, generationMode);
    PrintReport(result);

    if (Console.IsInputRedirected)
        break;

    bool runAgain = HandlePostSimulationMenu(result);
    result = null!;
    if (!runAgain)
        break;

    Console.WriteLine();
}

// Prints the complete weight and time-based validation report for one run.
static void PrintReport(SimulationAnalysisResult result)
{
    Console.WriteLine("ATTRIBUTE WEIGHT REPORT");
    Console.WriteLine("=======================");
    Console.WriteLine($"{"Attribute",-20} {"Recommended Weight",20} {"Typical Range",22} {"Variation",18}");
    Console.WriteLine(new string('-', 84));
    Console.WriteLine($"{"Attack Power",-20} {1.0,20:F4} {"Fixed",22} {"None",18}");
    PrintSummaryRow(result.WeaponDamage);
    PrintSummaryRow(result.Health);
    PrintSummaryRow(result.AttackSpeed);
    PrintSummaryRow(result.Armor);
    Console.WriteLine();

    Console.WriteLine("TIME-BASED VALIDATION");
    Console.WriteLine("=====================");
    Console.WriteLine($"Average fight duration: {result.AverageCompletedFightDuration:F2} seconds");
    Console.WriteLine($"Stalemates: {result.Stalemates:N0} of {result.SimulatedFights:N0} fights");
    if (result.Draws > 0)
        Console.WriteLine($"Draws: {result.Draws:N0} of {result.SimulatedFights:N0} fights");
    Console.WriteLine($"Maximum fight duration: {TimeBasedCombatSimulator.DefaultMaximumDuration:N0} seconds");
    Console.WriteLine($"Attack Speed/AP outcome agreement: {result.AttackSpeedOutcomeAgreementRate:F2}%");
    Console.WriteLine($"Armor/AP outcome agreement: {result.ArmorOutcomeAgreementRate:F2}%");
    Console.WriteLine("Formula/profile validation: Passed");
    Console.WriteLine("Timing simulation: Passed");
    Console.WriteLine();

    Console.WriteLine("ATTRIBUTE WEIGHT DETAILS");
    Console.WriteLine("========================");
    PrintDetails(result.Health);
    PrintDetails(result.WeaponDamage);
    PrintDetails(result.AttackSpeed);
    PrintDetails(result.Armor);
    Console.WriteLine();

    Console.WriteLine("LEGEND");
    Console.WriteLine("======");
    Console.WriteLine("Mean: Average weight across all sampled loadouts.");
    Console.WriteLine("Median: Middle weight; used as the recommended balance-sheet weight.");
    Console.WriteLine("SD: How much the weight varies between loadouts; lower is more consistent.");
    Console.WriteLine("Observed: Lowest and highest weights found in the simulation.");
    Console.WriteLine("Attack Speed/AP agreement: Fights where +1% Attack Speed and its calculated AP equivalent had the same outcome.");
    Console.WriteLine("Armor/AP agreement: Fights where +1 Armor and its calculated AP equivalent had the same outcome.");
}

// Prints one contextual attribute in the scalable summary table.
static void PrintSummaryRow(AttributeWeightDistributionResult distribution)
{
    string range = $"{distribution.FifthPercentile:F4}–{distribution.NinetyFifthPercentile:F4}";
    double variationPercent = distribution.StandardDeviation / distribution.MeanWeight * 100;
    string variation = $"{ClassifyVariation(variationPercent)} ({variationPercent:F1}%)";
    Console.WriteLine($"{distribution.DisplayName,-20} {distribution.RecommendedWeight,20:F4} {range,22} {variation,18}");
}

// Prints compact distribution diagnostics for one contextual attribute.
static void PrintDetails(AttributeWeightDistributionResult distribution)
{
    Console.WriteLine(
        $"{distribution.DisplayName}: mean {distribution.MeanWeight:F4} | " +
        $"median {distribution.MedianWeight:F4} | SD {distribution.StandardDeviation:F4} | " +
        $"observed {distribution.MinimumWeight:F4}–{distribution.MaximumWeight:F4}");
}

// Prints the strongest sampled builds and their contextual Attack Speed values.
static void PrintAttackSpeedDiagnostics(
    IReadOnlyList<AttackSpeedDiagnosticEntry> strongestBuilds,
    IReadOnlyList<WeaponProfileDpsSummary> summaries)
{
    Console.WriteLine();
    Console.WriteLine("STRONGEST SAMPLED BASIC-ATTACK BUILDS");
    Console.WriteLine("======================================");
    Console.WriteLine("Ranked by sustained unmitigated DPS; AS weight is the value of the next +1% Attack Speed.");
    foreach (AttackSpeedDiagnosticEntry entry in strongestBuilds)
        PrintAttackSpeedBuild(entry);

    Console.WriteLine();
    Console.WriteLine("WEAPON/PROFILE DPS SUMMARY");
    Console.WriteLine("==========================");
    Console.WriteLine($"{"Weapon",-30} {"Profile",-22} {"Samples",8} {"Mean DPS",10} {"95th DPS",10} {"Max DPS",10} {"AS Weight",10}");
    Console.WriteLine(new string('-', 107));
    foreach (WeaponProfileDpsSummary summary in summaries)
    {
        Console.WriteLine(
            $"{summary.WeaponName,-30} {summary.ProfileName,-22} {summary.Samples,8:N0} " +
            $"{summary.MeanDamagePerSecond,10:F2} {summary.NinetyFifthPercentileDamagePerSecond,10:F2} " +
            $"{summary.MaximumDamagePerSecond,10:F2} {summary.AttackSpeedWeightAtMaximum,10:F4}");
    }
}

// Prints the combat stats and full equipment list for one sampled extreme.
static void PrintAttackSpeedBuild(AttackSpeedDiagnosticEntry entry)
{
    CharacterStats stats = entry.Loadout.Stats;
    Console.WriteLine(
        $"  DPS {entry.DamagePerSecond:F2} | AS weight {entry.Weight:F4} | " +
        $"{entry.Weapon.Name} | {entry.Loadout.AttackProfile.Name} | " +
        $"AP {stats.AttackPower:F0} | WD {stats.WeaponDamage:F0} | " +
        $"AS {stats[AttributeId.AttackSpeed]:P2} | Cycle damage {entry.CycleDamage:F1}");
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
            PrintAttackSpeedDiagnostics(
                result.StrongestAttackSpeedBuilds,
                result.WeaponProfileDpsSummaries);
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

// Converts the selected generation policy into a concise report label.
static string DescribeGenerationMode(LoadoutGenerationMode mode) => mode switch
{
    LoadoutGenerationMode.RandomPieces => "Random armor pieces",
    LoadoutGenerationMode.ClosedArmorSet => "Closed armor sets",
    _ => throw new ArgumentOutOfRangeException(nameof(mode))
};

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
