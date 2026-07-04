using System.IO.Compression;
using System.Xml.Linq;

namespace CombatSimulator.Data;

// Reads cached cell values from XLSX files without requiring external spreadsheet packages.
public static class XlsxWorkbookReader
{
    private const int MaximumCellsPerWorksheet = 1_000_000;
    private static readonly XNamespace SpreadsheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    // Opens the XLSX package and materializes every worksheet needed by the importer.
    public static SpreadsheetWorkbook Read(string path)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        IReadOnlyList<string> sharedStrings = ReadSharedStrings(archive);
        Dictionary<string, string> relationships = ReadWorkbookRelationships(archive);
        XDocument workbookDocument = LoadXml(archive, "xl/workbook.xml");

        List<WorksheetData> worksheets = [];
        foreach (XElement sheet in workbookDocument.Descendants(SpreadsheetNamespace + "sheet"))
        {
            string name = (string?)sheet.Attribute("name")
                ?? throw new InvalidDataException("A worksheet has no name.");
            string relationshipId = (string?)sheet.Attribute(RelationshipNamespace + "id")
                ?? throw new InvalidDataException($"Worksheet '{name}' has no relationship.");

            if (!relationships.TryGetValue(relationshipId, out string? target))
                throw new InvalidDataException($"Worksheet relationship '{relationshipId}' was not found.");

            string entryPath = target.StartsWith('/')
                ? target.TrimStart('/')
                : $"xl/{target.TrimStart('/')}";
            worksheets.Add(ReadWorksheet(archive, entryPath, name, sharedStrings));
        }

        return new SpreadsheetWorkbook(worksheets);
    }

    // Decodes cached and inline cell values while enforcing a defensive size limit.
    private static WorksheetData ReadWorksheet(
        ZipArchive archive,
        string entryPath,
        string name,
        IReadOnlyList<string> sharedStrings)
    {
        XDocument document = LoadXml(archive, entryPath);
        Dictionary<(int Row, int Column), string> cells = [];

        foreach (XElement cell in document.Descendants(SpreadsheetNamespace + "c"))
        {
            string? reference = (string?)cell.Attribute("r");
            if (string.IsNullOrWhiteSpace(reference))
                continue;

            (int row, int column) = ParseCellReference(reference);
            string type = (string?)cell.Attribute("t") ?? string.Empty;
            string? rawValue = cell.Element(SpreadsheetNamespace + "v")?.Value;
            string? value = type switch
            {
                "s" when int.TryParse(rawValue, out int index) && index < sharedStrings.Count => sharedStrings[index],
                "inlineStr" => string.Concat(cell.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value)),
                "str" => rawValue,
                _ => rawValue
            };

            if (value is not null)
            {
                if (cells.Count >= MaximumCellsPerWorksheet)
                    throw new InvalidDataException($"Worksheet '{name}' exceeds the one-million-cell safety limit.");
                cells[(row, column)] = value;
            }
        }

        return new WorksheetData(name, cells);
    }

    // Reads the XLSX string table used by indexed text cells.
    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        ZipArchiveEntry? entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
            return [];

        using Stream stream = entry.Open();
        XDocument document = XDocument.Load(stream);
        return document
            .Descendants(SpreadsheetNamespace + "si")
            .Select(item => string.Concat(item.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value)))
            .ToArray();
    }

    // Maps workbook relationship IDs to their worksheet package paths.
    private static Dictionary<string, string> ReadWorkbookRelationships(ZipArchive archive)
    {
        XNamespace packageRelationships =
            "http://schemas.openxmlformats.org/package/2006/relationships";
        XDocument document = LoadXml(archive, "xl/_rels/workbook.xml.rels");

        return document.Descendants(packageRelationships + "Relationship")
            .Where(relationship => ((string?)relationship.Attribute("Type"))?.EndsWith("/worksheet") == true)
            .ToDictionary(
                relationship => (string)relationship.Attribute("Id")!,
                relationship => (string)relationship.Attribute("Target")!);
    }

    // Loads one required XML entry and fails clearly when the package is incomplete.
    private static XDocument LoadXml(ZipArchive archive, string path)
    {
        ZipArchiveEntry entry = archive.GetEntry(path)
            ?? throw new InvalidDataException($"XLSX entry '{path}' was not found.");
        using Stream stream = entry.Open();
        return XDocument.Load(stream);
    }

    // Converts an A1-style XLSX reference into one-based row and column indexes.
    private static (int Row, int Column) ParseCellReference(string reference)
    {
        int column = 0;
        int position = 0;
        while (position < reference.Length && char.IsLetter(reference[position]))
        {
            column = column * 26 + char.ToUpperInvariant(reference[position]) - 'A' + 1;
            position++;
        }

        if (position == 0 || position == reference.Length ||
            !int.TryParse(reference[position..], out int row) || row <= 0)
        {
            throw new InvalidDataException($"Invalid XLSX cell reference '{reference}'.");
        }
        return (row, column);
    }
}
