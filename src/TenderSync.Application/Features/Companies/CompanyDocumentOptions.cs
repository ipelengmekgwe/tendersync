using System.ComponentModel.DataAnnotations;

namespace TenderSync.Application.Features.Companies;

/// <summary>Limits for the encrypted company document library, bound from "CompanyDocuments".</summary>
public sealed class CompanyDocumentOptions
{
    public const string SectionName = "CompanyDocuments";

    [Range(1024, 500L * 1024 * 1024)]
    public long MaxBytes { get; set; } = 25L * 1024 * 1024;

    /// <summary>Store path for a company document; the extension never reveals the type because the blob is ciphertext.</summary>
    public static string StoragePath(Guid companyProfileId, Guid documentId) => $"companies/{companyProfileId}/{documentId}.enc";
}
