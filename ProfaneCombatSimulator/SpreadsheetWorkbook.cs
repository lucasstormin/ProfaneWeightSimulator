namespace CombatSimulator.Data;

// Stores worksheets extracted from an XLSX file and resolves them by their stable names.
public sealed class SpreadsheetWorkbook
{
    private readonly Dictionary<string, WorksheetData> sheets;

    public SpreadsheetWorkbook(IEnumerable<WorksheetData> sheets)
    {
        this.sheets = sheets.ToDictionary(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase);
    }

    public WorksheetData Sheet(string name)
    {
        if (sheets.TryGetValue(name, out WorksheetData? sheet))
            return sheet;

        throw new InvalidDataException($"The spreadsheet is missing the required '{name}' tab.");
    }
}
