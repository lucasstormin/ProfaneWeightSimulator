using System.Globalization;

namespace CombatSimulator.Data;

// Provides simple row-and-column access to cached values read from one XLSX worksheet.
public sealed class WorksheetData
{
    private readonly Dictionary<(int Row, int Column), string> cells;

    // Indexes cached cells and records the final populated row for bounded scans.
    public WorksheetData(string name, Dictionary<(int Row, int Column), string> cells)
    {
        Name = name;
        this.cells = cells;
        MaximumRow = cells.Count == 0 ? 0 : cells.Keys.Max(key => key.Row);
    }

    public string Name { get; }
    public int MaximumRow { get; }

    // Reads one cached cell as text without inventing values for blank cells.
    public string? Text(int row, int column) =>
        cells.TryGetValue((row, column), out string? value) ? value : null;

    // Parses one cell using XLSX's invariant numeric representation.
    public double? Number(int row, int column)
    {
        string? value = Text(row, column);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            ? number
            : null;
    }
}
