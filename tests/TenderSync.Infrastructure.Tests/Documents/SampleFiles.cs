using System.IO.Compression;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace TenderSync.Infrastructure.Tests.Documents;

/// <summary>Builds small real files in memory so the extractor tests need no binary fixtures in the repo.</summary>
public static class SampleFiles
{
    /// <summary>A PDF with one block of text per page; an empty string makes a blank (scanned-looking) page.</summary>
    public static byte[] Pdf(params string[] pageTexts)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var text in pageTexts)
        {
            var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
            var y = 800.0;
            foreach (var line in text.Split('\n'))
            {
                if (line.Length > 0)
                {
                    page.AddText(line, 11, new UglyToad.PdfPig.Core.PdfPoint(40, y), font);
                }

                y -= 16;
            }
        }

        return builder.Build();
    }

    /// <summary>A Word document; each argument is a paragraph, and "---" inserts an explicit page break.</summary>
    public static byte[] Docx(params string[] paragraphs)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            var body = new Body();
            foreach (var paragraph in paragraphs)
            {
                body.Append(paragraph == "---"
                    ? new Paragraph(new Run(new Break { Type = BreakValues.Page }))
                    : new Paragraph(new Run(new Text(paragraph) { Space = SpaceProcessingModeValues.Preserve })));
            }

            var table = new Table(new TableRow(new TableCell(new Paragraph(new Run(new Text("Item")))), new TableCell(new Paragraph(new Run(new Text("Qty"))))));
            body.Append(table);
            main.Document = new Document(body);
            main.Document.Save();
        }

        return stream.ToArray();
    }

    /// <summary>A workbook with the given sheets; each sheet is a list of rows of cell values.</summary>
    public static byte[] Xlsx(params (string Name, string[][] Rows)[] sheets)
    {
        using var workbook = new XLWorkbook();
        foreach (var (name, rows) in sheets)
        {
            var sheet = workbook.Worksheets.Add(name);
            for (var r = 0; r < rows.Length; r++)
            {
                for (var c = 0; c < rows[r].Length; c++)
                {
                    sheet.Cell(r + 1, c + 1).Value = rows[r][c];
                }
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static byte[] Zip(params (string Name, byte[] Bytes)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
                using var target = entry.Open();
                target.Write(bytes);
            }
        }

        return stream.ToArray();
    }
}
