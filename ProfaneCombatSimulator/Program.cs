using CombatSimulator.Analysis;
using CombatSimulator.Data;
using CombatSimulator.Models;
using CombatSimulator.Validation;

if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
{
    RegressionSuite.Run();
    return;
}

const string spreadsheetId = "1dH-1xQ5fE2y2HTw27evHl449DhbhXewrhR97haOO8jc";
const int samples = 100_000;
const int randomSeed = 20260704;

string solutionRoot = FindSolutionRoot();
string cachePath = Path.Combine(solutionRoot, "Data", "game-balance-cache.xlsx");

Console.WriteLine("Loading authoritative game data...");
(string workbookPath, bool usedCache) =
    await GoogleSheetDataSource.GetWorkbookAsync(spreadsheetId, cachePath);

SpreadsheetWorkbook workbook = XlsxWorkbookReader.Read(workbookPath);
GameData gameData = GameSheetParser.Parse(workbook);

Console.WriteLine($"Source: {(usedCache ? "local cache" : "Google Sheets")}");
Console.WriteLine($"Imported items: {gameData.Items.Count}");
Console.WriteLine($"Analyzing {samples:N0} valid full loadouts...");
Console.WriteLine();

WeightDistributionResult result =
    LoadoutWeightAnalyzer.Analyze(gameData, samples, randomSeed);
OffensiveAttributeWeightResult weaponDamageWeight =
    OffensiveWeightCalculator.ValidateWeaponDamageWeight(gameData.CombatConfig);

Console.WriteLine("ATTRIBUTE WEIGHT REPORT");
Console.WriteLine("=======================");
Console.WriteLine($"{"Attribute",-18} {"Recommended Weight",20} {"Typical Range",22} {"Variation",16}");
Console.WriteLine(new string('-', 80));
Console.WriteLine($"{"Attack Power",-18} {1.0,20:F4} {"Fixed",22} {"None",16}");
Console.WriteLine($"{"Weapon Damage",-18} {weaponDamageWeight.AttackPowerBasedWeight,20:F4} {"Fixed",22} {"None",16}");

string healthRange = $"{result.FifthPercentile:F4}–{result.NinetyFifthPercentile:F4}";
double healthVariationPercent = result.StandardDeviation / result.MeanHealthWeight * 100;
string healthVariation = $"{ClassifyVariation(healthVariationPercent)} ({healthVariationPercent:F1}%)";
Console.WriteLine($"{"Health",-18} {result.RecommendedHealthWeight,20:F4} {healthRange,22} {healthVariation,16}");
Console.WriteLine();
Console.WriteLine("HEALTH WEIGHT DETAILS");
Console.WriteLine("=====================");
Console.WriteLine($"Mean:              {result.MeanHealthWeight:F4}");
Console.WriteLine($"Median:            {result.MedianHealthWeight:F4}");
Console.WriteLine($"Standard deviation:{result.StandardDeviation,10:F4}");
Console.WriteLine($"Typical 90% range: {result.FifthPercentile:F4} to {result.NinetyFifthPercentile:F4}");
Console.WriteLine($"Observed range:    {result.MinimumHealthWeight:F4} to {result.MaximumHealthWeight:F4}");
Console.WriteLine();
PrintExtreme("Lowest-weight loadout", result.MinimumLoadout);
Console.WriteLine();
PrintExtreme("Highest-weight loadout", result.MaximumLoadout);
Console.WriteLine();
Console.WriteLine("Notes: caps are not enforced; artifacts are excluded; duplicate rings and mixed armor are allowed.");
Console.WriteLine("Two-handed weapons correctly exclude off-hands. Other attributes are imported but do not affect this milestone.");
Console.WriteLine($"Weapon Damage formula validation: {(weaponDamageWeight.ValidationPassed ? "passed" : "FAILED")}.");

static void PrintExtreme(string title, WeightedLoadout weightedLoadout)
{
    CharacterStats character = weightedLoadout.Loadout.Stats;
    Console.WriteLine($"{title}: {weightedLoadout.HealthWeight:F4}");
    Console.WriteLine($"  {character.AttackPower:N0} AP | {character.MaxHealth:N0} Health | {character.WeaponDamage:N0} Weapon Damage");
    Console.WriteLine($"  {weightedLoadout.Loadout.Description}");
}

static string ClassifyVariation(double coefficientOfVariationPercent)
{
    if (coefficientOfVariationPercent < 10)
        return "Low";
    if (coefficientOfVariationPercent < 25)
        return "Moderate";
    return "High";
}

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
