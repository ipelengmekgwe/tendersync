using TenderSync.Domain.Common;
using TenderSync.Domain.Enums;

namespace TenderSync.Domain.Applications;

/// <summary>A generated or attached document belonging to a bid application (Phase 2+).</summary>
public sealed class ApplicationDocument : Entity
{
    private ApplicationDocument()
    {
    }

    public Guid ApplicationId { get; private set; }

    public ApplicationDocumentKind Kind { get; private set; }

    public string Title { get; private set; } = null!;

    public string StoragePath { get; private set; } = null!;

    public int Version { get; private set; } = 1;

    public bool Generated { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public string? ApprovedBy { get; private set; }

    public static ApplicationDocument Create(Guid applicationId, ApplicationDocumentKind kind, string title, string storagePath, bool generated, int version = 1)
    {
        return new ApplicationDocument
        {
            ApplicationId = applicationId,
            Kind = kind,
            Title = Guard.NotEmpty(title, nameof(title)),
            StoragePath = Guard.NotEmpty(storagePath, nameof(storagePath)),
            Generated = generated,
            Version = Guard.InRange(version, 1, int.MaxValue, nameof(version)),
        };
    }

    public void Approve(string approvedBy, DateTimeOffset now)
    {
        ApprovedBy = Guard.NotEmpty(approvedBy, nameof(approvedBy));
        ApprovedAt = now;
    }
}
