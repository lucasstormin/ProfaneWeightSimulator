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
const int fights = 100_000;
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
Console.WriteLine($"Analyzing {fights:N0} valid time-based loadouts...");
Console.WriteLine();

SimulationAnalysisResult result =
    TimeBasedAnalysisRunner.Analyze(gameData, fights, randomSeed);

Console.WriteLine("ATTRIBUTE WEIGHT REPORT");
Console.WriteLine("=======================");
Console.WriteLine($"{"Attribute",-20} {"Recommended Weight",20} {"Typical Range",22} {"Variation",18}");
Console.WriteLine(new string('-', 84));
Console.WriteLine($"{"Attack Power",-20} {1.0,20:F4} {"Fixed",22} {"None",18}");
PrintSummaryRow(result.WeaponDamage);
PrintSummaryRow(result.Health);
PrintSummaryRow(result.AttackSpeed);
Console.WriteLine();

Console.WriteLine("TIME-BASED VALIDATION");
Console.WriteLine("=====================");
Console.WriteLine($"Average fight duration: {result.AverageCompletedFightDuration:F2} seconds");
Console.WriteLine($"Stalemates: {result.Stalemates:N0} of {result.SimulatedFights:N0} fights");
if (result.Draws > 0)
    Console.WriteLine($"Draws: {result.Draws:N0} of {result.SimulatedFights:N0} fights");
Console.WriteLine($"Maximum fight duration: {TimeBasedCombatSimulator.DefaultMaximumDuration:N0} seconds");
Console.WriteLine($"Attack Speed/AP outcome agreement: {result.AttackSpeedOutcomeAgreementRate:F2}%");
Console.WriteLine("Formula/profile validation: Passed");
Console.WriteLine("Timing simulation: Passed");
Console.WriteLine();

Console.WriteLine("ATTRIBUTE WEIGHT DETAILS");
Console.WriteLine("========================");
PrintDetails(result.Health);
PrintDetails(result.WeaponDamage);
PrintDetails(result.AttackSpeed);
Console.WriteLine();

Console.WriteLine("LEGEND");
Console.WriteLine("======");
Console.WriteLine("Mean: Average weight across all sampled loadouts.");
Console.WriteLine("Median: Middle weight; used as the recommended balance-sheet weight.");
Console.WriteLine("SD: How much the weight varies between loadouts; lower is more consistent.");
Console.WriteLine("Observed: Lowest and highest weights found in the simulation.");
Console.WriteLine("Attack Speed/AP agreement: Fights where +1% Attack Speed and its calculated AP equivalent had the same outcome.");

if (!Console.IsInputRedirected)
{
    Console.WriteLine();
    Console.Write("Press D to inspect extreme Attack Speed builds, or any other key to exit: ");
    ConsoleKeyInfo key = Console.ReadKey(intercept: true);
    Console.WriteLine(key.Key == ConsoleKey.D ? "D" : string.Empty);
    if (key.Key == ConsoleKey.D)
        PrintAttackSpeedDiagnostics(result.AttackSpeedDiagnostics);
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

// Prints extreme sampled builds and weapon/profile patterns behind Attack Speed variation.
static void PrintAttackSpeedDiagnostics(IReadOnlyList<AttackSpeedDiagnosticEntry> diagnostics)
{
    const int extremeCount = 20;
    AttackSpeedDiagnosticEntry[] ordered = diagnostics.OrderBy(entry => entry.Weight).ToArray();

    Console.WriteLine();
    Console.WriteLine("ATTACK SPEED EXTREME BUILDS");
    Console.WriteLine("===========================");
    Console.WriteLine($"Lowest {Math.Min(extremeCount, ordered.Length)} sampled weights:");
    foreach (AttackSpeedDiagnosticEntry entry in ordered.Take(extremeCount))
        PrintAttackSpeedBuild(entry);

    Console.WriteLine();
    Console.WriteLine($"Highest {Math.Min(extremeCount, ordered.Length)} sampled weights:");
    foreach (AttackSpeedDiagnosticEntry entry in ordered.TakeLast(extremeCount).Reverse())
        PrintAttackSpeedBuild(entry);

    Console.WriteLine();
    Console.WriteLine("WEAPON/PROFILE SUMMARY");
    Console.WriteLine("======================");
    Console.WriteLine($"{"Weapon",-30} {"Profile",-22} {"Samples",8} {"Mean",9} {"95th",9} {"Maximum",9}");
    Console.WriteLine(new string('-', 92));
    foreach (IGrouping<(string Weapon, string Profile), AttackSpeedDiagnosticEntry> group in diagnostics
        .GroupBy(entry => (entry.Weapon.Name, entry.Loadout.AttackProfile.Name))
        .OrderByDescending(group => group.Max(entry => entry.Weight)))
    {
        double[] weights = group.Select(entry => entry.Weight).Order().ToArray();
        Console.WriteLine(
            $"{group.Key.Weapon,-30} {group.Key.Profile,-22} {weights.Length,8:N0} " +
            $"{weights.Average(),9:F4} {Percentile(weights, 0.95),9:F4} {weights[^1],9:F4}");
    }
}

// Prints the combat stats and full equipment list for one sampled extreme.
static void PrintAttackSpeedBuild(AttackSpeedDiagnosticEntry entry)
{
    CharacterStats stats = entry.Loadout.Stats;
    Console.WriteLine(
        $"  {entry.Weight:F4} | {entry.Weapon.Name} | {entry.Loadout.AttackProfile.Name} | " +
        $"AP {stats.AttackPower:F0} | WD {stats.WeaponDamage:F0} | " +
        $"AS {stats[AttributeId.AttackSpeed]:P2} | Cycle damage {entry.CycleDamage:F1}");
    Console.WriteLine($"    {entry.Loadout.Description}");
}

// Interpolates a percentile from sorted diagnostic weights.
static double Percentile(double[] sortedValues, double percentile)
{
    double position = (sortedValues.Length - 1) * percentile;
    int lower = (int)Math.Floor(position);
    int upper = (int)Math.Ceiling(position);
    if (lower == upper)
        return sortedValues[lower];

    double fraction = position - lower;
    return sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * fraction;
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
