namespace TenderSync.Application.Features.Documents;

/// <summary>
/// File-format helpers shared by download and extraction: the declared format from the feed, the format sniffed from the
/// first bytes of the body, the extension used in the store path and the content type served back by the API.
/// </summary>
public static class DocumentFormats
{
    public const string Pdf = "pdf";
    public const string Zip = "zip";
    public const string Html = "html";

    private static readonly HashSet<string> OfficeOpenXml = new(StringComparer.Ordinal) { "docx", "xlsx", "pptx", "docm", "xlsm", "pptm" };

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.Ordinal)
    {
        ["pdf"] = "application/pdf",
        ["docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ["docm"] = "application/vnd.ms-word.document.macroEnabled.12",
        ["doc"] = "application/msword",
        ["xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ["xlsm"] = "application/vnd.ms-excel.sheet.macroEnabled.12",
        ["xls"] = "application/vnd.ms-excel",
        ["pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ["ppt"] = "application/vnd.ms-powerpoint",
        ["zip"] = "application/zip",
        ["txt"] = "text/plain",
        ["csv"] = "text/csv",
        ["rtf"] = "application/rtf",
        ["jpg"] = "image/jpeg",
        ["jpeg"] = "image/jpeg",
        ["png"] = "image/png",
        ["tif"] = "image/tiff",
        ["tiff"] = "image/tiff",
        ["mp3"] = "audio/mpeg",
        ["html"] = "text/html",
    };

    private static readonly Dictionary<string, string> ExtensionsByContentType = ContentTypes
        .GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Lower-case extension without a dot. Accepts "pdf", ".PDF", "application/pdf" or null, falling back to the extension
    /// of <paramref name="fileName"/> or the <c>blobName</c> query parameter of <paramref name="url"/>.
    /// </summary>
    public static string? Normalise(string? declared, string? fileName = null, Uri? url = null)
    {
        var format = Clean(declared);
        if (format is not null && format.Contains('/', StringComparison.Ordinal))
        {
            format = ExtensionsByContentType.GetValueOrDefault(format);
        }

        format ??= Clean(Path.GetExtension(fileName));
        if (format is null && url is not null)
        {
            var query = url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            var blob = query.Select(q => q.Split('=', 2)).FirstOrDefault(kv => kv.Length == 2 && kv[0].Equals("blobName", StringComparison.OrdinalIgnoreCase))?[1];
            format = Clean(Path.GetExtension(Uri.UnescapeDataString(blob ?? string.Empty)));
            format ??= Clean(Path.GetExtension(url.AbsolutePath));
        }

        return format;
    }

    /// <summary>
    /// Detects the real format from magic bytes: %PDF, PK (Office Open XML or zip), OLE2 (xls/doc), HTML. Office Open XML
    /// and zip share a signature, so the declared format wins when it is one of those; otherwise the sniffed format wins.
    /// Returns the declared format when nothing is recognised.
    /// </summary>
    public static string? Sniff(ReadOnlySpan<byte> head, string? declared)
    {
        if (head.Length >= 5 && head[0] == (byte)'%' && head[1] == (byte)'P' && head[2] == (byte)'D' && head[3] == (byte)'F')
        {
            return Pdf;
        }

        if (head.Length >= 4 && head[0] == (byte)'P' && head[1] == (byte)'K' && head[2] is 3 or 5 or 7 && head[3] is 4 or 6 or 8)
        {
            return declared is not null && OfficeOpenXml.Contains(declared) ? declared : Zip;
        }

        if (head.Length >= 8 && head[0] == 0xD0 && head[1] == 0xCF && head[2] == 0x11 && head[3] == 0xE0)
        {
            return declared is "doc" or "ppt" or "msg" ? declared : "xls";
        }

        if (LooksLikeHtml(head))
        {
            return Html;
        }

        return declared;
    }

    public static string ContentTypeFor(string? format) =>
        format is not null && ContentTypes.TryGetValue(format, out var type) ? type : "application/octet-stream";

    /// <summary>Store path from PLAN.md §7.3: tenders/{ocid}/{documentId}.{ext}. The ocid is sanitised to a safe segment.</summary>
    public static string StoragePath(string ocid, Guid documentId, string? format)
    {
        var safeOcid = new string(ocid.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
        return format is null ? $"tenders/{safeOcid}/{documentId:D}" : $"tenders/{safeOcid}/{documentId:D}.{format}";
    }

    private static bool LooksLikeHtml(ReadOnlySpan<byte> head)
    {
        var length = Math.Min(head.Length, 512);
        Span<char> chars = stackalloc char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = (char)head[i];
        }

        var text = chars.TrimStart([' ', '\t', '\r', '\n', '﻿']);
        return text.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("<head", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim().TrimStart('.').ToLowerInvariant();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
