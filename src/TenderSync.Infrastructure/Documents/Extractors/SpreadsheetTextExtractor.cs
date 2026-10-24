using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using ExcelDataReader;
using TenderSync.Application.Abstractions.Documents;

namespace TenderSync.Infrastructure.Documents.Extractors;

/// <summary>
/// Spreadsheets: one page per sheet (PLAN.md §7.3), rows as tab-separated cell text with a "# Sheet:" header line.
/// Open XML workbooks go through ClosedXML; legacy .xls (BIFF) through ExcelDataReader.
/// </summary>
public sealed class SpreadsheetTextExtractor : ITextExtractor
{
    private const int MaxRowsPerSheet = 20_000;
    private const int MaxColumns = 256;

    static SpreadsheetTextExtractor()
    {
        // ExcelDataReader needs the legacy code pages for .xls text.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public IReadOnlyCollection<string> Formats { get; } = ["xlsx", "xlsm", "xls"];

    public Task<ExtractedDocument> ExtractAsync(Stream content, string format, CancellationToken cancellationToken)
    {
        try
        {
            var pages = format == "xls" ? ReadBinary(content, cancellationToken) : ReadOpenXml(content, cancellationToken);
            return Task.FromResult(new ExtractedDocument(pages));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new TextExtractionException($"Spreadsheet could not be read: {ex.Message}", ex);
        }
    }

    private static List<ExtractedPage> ReadOpenXml(Stream content, CancellationToken cancellationToken)
    {
        using var workbook = new XLWorkbook(content);
        var pages = new List<ExtractedPage>();
        foreach (var sheet in workbook.Worksheets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = new StringBuilder().Append("# Sheet: ").Append(sheet.Name).Append('\n');
            var used = sheet.RangeUsed();
            if (used is not null)
            {
                var rows = 0;
                foreach (var row in used.RowsUsed())
                {
                    if (++rows > MaxRowsPerSheet)
                    {
                        text.Append("[truncated]\n");
                        break;
                    }

                    var cells = row.CellsUsed().Take(MaxColumns).Select(c => c.GetFormattedString().Trim()).Where(s => s.Length > 0);
                    var line = string.Join('\t', cells);
                    if (line.Length > 0)
                    {
                        text.Append(line).Append('\n');
                    }
                }
            }

            pages.Add(new ExtractedPage(pages.Count + 1, text.ToString()));
        }

        return pages;
    }

    private static List<ExtractedPage> ReadBinary(Stream content, CancellationToken cancellationToken)
    {
        using var reader = ExcelReaderFactory.CreateBinaryReader(content);
        var pages = new List<ExtractedPage>();
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = new StringBuilder().Append("# Sheet: ").Append(reader.Name).Append('\n');
            var rows = 0;
            while (reader.Read())
            {
                if (++rows > MaxRowsPerSheet)
                {
                    text.Append("[truncated]\n");
                    break;
                }

                var cells = new List<string>();
                for (var i = 0; i < Math.Min(reader.FieldCount, MaxColumns); i++)
                {
                    var value = reader.GetValue(i);
                    var cell = value switch
                    {
                        null => string.Empty,
                        DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
                        _ => value.ToString() ?? string.Empty,
                    };
                    if (cell.Trim().Length > 0)
                    {
                        cells.Add(cell.Trim());
                    }
                }

                if (cells.Count > 0)
                {
                    text.Append(string.Join('\t', cells)).Append('\n');
                }
            }

            pages.Add(new ExtractedPage(pages.Count + 1, text.ToString()));
        }
        while (reader.NextResult());

        return pages;
    }
}
