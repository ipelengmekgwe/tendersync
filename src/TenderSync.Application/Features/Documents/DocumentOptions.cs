using System.ComponentModel.DataAnnotations;

namespace TenderSync.Application.Features.Documents;

/// <summary>Download and extraction limits from PLAN.md §7.3. Bound from the "Documents" section; validated on start.</summary>
public sealed class DocumentOptions
{
    public const string SectionName = "Documents";

    /// <summary>Files larger than this are marked Failed without being stored (50 MB).</summary>
    [Range(1024, 1024L * 1024 * 1024)]
    public long MaxDocumentBytes { get; set; } = 50L * 1024 * 1024;

    /// <summary>Formats never downloaded (audio and video); the document is marked Skipped.</summary>
    public string[] SkippedFormats { get; set; } = ["mp3", "mp4", "wav", "m4a", "aac", "ogg", "flac", "wma", "avi", "mov", "wmv", "mkv", "webm"];

    /// <summary>Hosts a document URL may point at. Empty allows any host. Feed URLs that fail this check are marked Skipped.</summary>
    public string[] AllowedDownloadHosts { get; set; } = ["www.etenders.gov.za", "etenders.gov.za"];

    [Range(1, 8)]
    public int MaxConcurrentDownloads { get; set; } = 2;

    [Range(0, 60_000)]
    public int DownloadSpacingMs { get; set; } = 500;

    [Range(1, 3600)]
    public int DownloadTimeoutSeconds { get; set; } = 300;

    /// <summary>A PDF averaging fewer characters per page than this is flagged needs_ocr (scanned).</summary>
    [Range(0, 10_000)]
    public int OcrMinCharsPerPage { get; set; } = 50;

    /// <summary>How deep nested zip archives are expanded (PLAN.md §7.3: 2).</summary>
    [Range(0, 5)]
    public int ZipMaxDepth { get; set; } = 2;

    /// <summary>Guard against zip bombs: total uncompressed bytes read from one archive.</summary>
    [Range(1024, 4L * 1024 * 1024 * 1024)]
    public long ZipMaxUncompressedBytes { get; set; } = 200L * 1024 * 1024;

    [Range(1, 10_000)]
    public int ZipMaxEntries { get; set; } = 500;
}
