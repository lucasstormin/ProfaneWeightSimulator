namespace CombatSimulator.Data;

// Stores worksheets extracted from an XLSX file and resolves them by their stable names.
public sealed class SpreadsheetWorkbook
{
    private readonly Dictionary<string, WorksheetData> sheets;

    // Builds a case-insensitive index because sheet names are external data.
    public SpreadsheetWorkbook(IEnumerable<WorksheetData> sheets)
    {
        this.sheets = sheets.ToDictionary(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase);
    }

    // Resolves a required worksheet or reports a clear import error.
    public WorksheetData Sheet(string name)
    {
        if (sheets.TryGetValue(name, out WorksheetData? sheet))
            return sheet;

        throw new InvalidDataException($"The spreadsheet is missing the required '{name}' tab.");
    }
}
