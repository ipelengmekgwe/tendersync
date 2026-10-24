using System.ComponentModel.DataAnnotations;

namespace TenderSync.Infrastructure.Storage;

/// <summary>ADR-013: filesystem locally and on the VPS, Azure Blob on Azure. Bound from "DocumentStore".</summary>
public sealed class DocumentStoreOptions
{
    public const string SectionName = "DocumentStore";
    public const string FileSystemProvider = "FileSystem";
    public const string AzureBlobProvider = "AzureBlob";

    [Required]
    [RegularExpression("^(FileSystem|AzureBlob)$", ErrorMessage = "DocumentStore:Provider must be FileSystem or AzureBlob.")]
    public string Provider { get; set; } = FileSystemProvider;

    public FileSystemOptions FileSystem { get; set; } = new();

    public AzureBlobOptions AzureBlob { get; set; } = new();

    public sealed class FileSystemOptions
    {
        /// <summary>Root folder. Empty means %LOCALAPPDATA%/TenderSync/documents (or the XDG equivalent) so the API and Worker share one folder in development.</summary>
        public string RootPath { get; set; } = string.Empty;

        public string ResolveRoot() => string.IsNullOrWhiteSpace(RootPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TenderSync", "documents")
            : Path.GetFullPath(RootPath);
    }

    public sealed class AzureBlobOptions
    {
        /// <summary>Connection string (Azurite locally, a Key Vault reference in Azure). Managed identity is a later addition.</summary>
        public string ConnectionString { get; set; } = string.Empty;

        public string ContainerName { get; set; } = "documents";
    }
}
