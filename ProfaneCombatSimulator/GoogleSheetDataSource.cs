namespace CombatSimulator.Data;

// Downloads a read-only Google Sheets XLSX export and falls back to the last good cache.
public static class GoogleSheetDataSource
{
    private const int MaximumWorkbookBytes = 25 * 1024 * 1024;
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(30) };

    // Refreshes and validates the workbook atomically, retaining a safe offline fallback.
    public static async Task<(string Path, bool UsedCache)> GetWorkbookAsync(
        string spreadsheetId,
        string cachePath)
    {
        string? directory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string temporaryPath = cachePath + ".download";

        try
        {
            string url = $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/export?format=xlsx";
            using HttpResponseMessage response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength > MaximumWorkbookBytes)
                throw new InvalidDataException("The downloaded workbook exceeds the 25 MB safety limit.");

            byte[] content = await response.Content.ReadAsByteArrayAsync();
            if (content.Length > MaximumWorkbookBytes)
                throw new InvalidDataException("The downloaded workbook exceeds the 25 MB safety limit.");

            if (content.Length < 4 || content[0] != 'P' || content[1] != 'K')
                throw new InvalidDataException("Google did not return a valid XLSX workbook.");

            await File.WriteAllBytesAsync(temporaryPath, content);

            // Validate the workbook and required game sections before replacing
            // the last known-good cache with newly downloaded data.
            SpreadsheetWorkbook downloadedWorkbook = XlsxWorkbookReader.Read(temporaryPath);
            _ = GameSheetParser.Parse(downloadedWorkbook);

            File.Move(temporaryPath, cachePath, overwrite: true);
            return (cachePath, false);
        }
        catch (Exception exception) when (
            File.Exists(cachePath) &&
            exception is HttpRequestException or TaskCanceledException or InvalidDataException or IOException)
        {
            Console.WriteLine($"Sheet refresh failed; using the last cached workbook. ({exception.Message})");
            return (cachePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
