using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Abstractions.Security;
using TenderSync.Application.Abstractions.Storage;
using TenderSync.Application.Common.Auditing;
using TenderSync.Application.Common.Exceptions;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;
using TenderSync.Domain.Companies;
using TenderSync.Domain.Enums;

namespace TenderSync.Application.Features.Companies.Commands.UploadCompanyDocument;

/// <summary>
/// POST /company-profiles/{id}/documents (Reviewer). The content is encrypted while it streams into the document store
/// (PLAN.md §12: AES-256-GCM, data key wrapped by the master key); the plaintext hash and size are recorded on the row.
/// </summary>
[Authorize(Roles.Reviewer)]
public sealed record UploadCompanyDocumentCommand(
    Guid CompanyProfileId,
    CompanyDocumentType Type,
    string Title,
    string? FileName,
    string? ContentType,
    long? DeclaredLength,
    DateTimeOffset? ExpiresAt,
    [property: Sensitive] Stream Content) : ICommand<CompanyDocumentDto>, IAuditedEntityReference
{
    public string AuditEntityType => nameof(CompanyProfile);

    public string? AuditEntityId => CompanyProfileId.ToString();
}

public sealed class UploadCompanyDocumentCommandValidator : AbstractValidator<UploadCompanyDocumentCommand>
{
    public UploadCompanyDocumentCommandValidator(IOptions<CompanyDocumentOptions> options)
    {
        RuleFor(c => c.Title).NotEmpty().MaximumLength(512);
        RuleFor(c => c.FileName).MaximumLength(260);
        RuleFor(c => c.ContentType).MaximumLength(128);
        RuleFor(c => c.Content).NotNull();
        RuleFor(c => c.DeclaredLength).Must(length => length is null || length <= options.Value.MaxBytes)
            .WithMessage(_ => $"Company documents are limited to {options.Value.MaxBytes / (1024 * 1024)} MB.");
    }
}

public sealed class UploadCompanyDocumentHandler(IAppDbContext db, IDocumentStore store, IEncryptionService encryption, IOptions<CompanyDocumentOptions> options, IDateTimeProvider clock)
    : IRequestHandler<UploadCompanyDocumentCommand, CompanyDocumentDto>
{
    public async Task<CompanyDocumentDto> Handle(UploadCompanyDocumentCommand request, CancellationToken cancellationToken)
    {
        var profile = await db.CompanyProfiles.GetByIdAsync(request.CompanyProfileId, cancellationToken)
            ?? throw new NotFoundException(nameof(CompanyProfile), request.CompanyProfileId);

        var documentId = Guid.CreateVersion7();
        var path = CompanyDocumentOptions.StoragePath(profile.Id, documentId);
        var limit = options.Value.MaxBytes;

        await using var cipher = await encryption.EncryptAsync(new LimitedStream(request.Content, limit), cancellationToken);
        var storedPath = await store.SaveAsync(path, cipher, "application/octet-stream", cancellationToken);
        if (!cipher.Completed)
        {
            await store.DeleteAsync(storedPath, cancellationToken);
            throw new InvalidOperationException("The upload stream ended before encryption completed.");
        }

        if (cipher.PlaintextLength == 0)
        {
            await store.DeleteAsync(storedPath, cancellationToken);
            throw new ConflictException("The uploaded file is empty.");
        }

        var document = CompanyDocument.Create(profile.Id, request.Type, request.Title, storedPath, cipher.WrappedDataKey, cipher.PlaintextSha256, cipher.PlaintextLength, ContentTypeFor(request), request.ExpiresAt);
        db.CompanyDocuments.Add(document);
        return CompanyDocumentDto.From(document, encryption.ActiveKeyId, clock.UtcNow);
    }

    private static string? ContentTypeFor(UploadCompanyDocumentCommand request)
    {
        if (!string.IsNullOrWhiteSpace(request.ContentType) && !request.ContentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return request.ContentType;
        }

        var format = Documents.DocumentFormats.Normalise(null, request.FileName);
        return format is null ? request.ContentType : Documents.DocumentFormats.ContentTypeFor(format);
    }

    /// <summary>Throws once more than the limit has been read, so an oversized upload never reaches the store in full.</summary>
    private sealed class LimitedStream(Stream inner, long limit) : Stream
    {
        private long _read;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => _read; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Account(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => Account(await inner.ReadAsync(buffer, cancellationToken));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Account(int read)
        {
            _read += read;
            return _read > limit ? throw new ConflictException($"Company documents are limited to {limit / (1024 * 1024)} MB.") : read;
        }
    }
}
