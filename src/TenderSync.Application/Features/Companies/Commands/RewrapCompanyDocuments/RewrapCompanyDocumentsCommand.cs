using MediatR;
using Microsoft.Extensions.Logging;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Abstractions.Security;
using TenderSync.Application.Common.Messaging;
using TenderSync.Application.Security;

namespace TenderSync.Application.Features.Companies.Commands.RewrapCompanyDocuments;

/// <summary>
/// POST /admin/encryption/rewrap (Admin): the master-key rotation step from docs/runbooks/key-rotation.md. Every company
/// document whose data key is wrapped by a key other than the active one is unwrapped with that key and wrapped again with
/// the active key. The ciphertext is untouched, so the operation is quick and safe to repeat.
/// </summary>
[Authorize(Roles.Admin)]
public sealed record RewrapCompanyDocumentsCommand : ICommand<RewrapResult>;

public sealed record RewrapResult(string ActiveKeyId, int Total, int Rewrapped, int AlreadyCurrent, IReadOnlyList<string> Errors);

public sealed class RewrapCompanyDocumentsHandler(IAppDbContext db, IEncryptionService encryption, ILogger<RewrapCompanyDocumentsHandler> logger) : IRequestHandler<RewrapCompanyDocumentsCommand, RewrapResult>
{
    public async Task<RewrapResult> Handle(RewrapCompanyDocumentsCommand request, CancellationToken cancellationToken)
    {
        var documents = await db.CompanyProfiles.ListAllDocumentsAsync(cancellationToken);
        var rewrapped = 0;
        var current = 0;
        var errors = new List<string>();
        foreach (var document in documents)
        {
            if (string.Equals(encryption.KeyIdOf(document.EncryptedDataKey), encryption.ActiveKeyId, StringComparison.Ordinal))
            {
                current++;
                continue;
            }

            try
            {
                document.RewrapDataKey(await encryption.RewrapAsync(document.EncryptedDataKey, cancellationToken));
                rewrapped++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"{document.Id}: {ex.Message}");
                logger.LogError(ex, "Could not re-wrap company document {DocumentId}", document.Id);
            }
        }

        logger.LogInformation("Key rotation: {Rewrapped} re-wrapped, {Current} already on {KeyId}, {Errors} error(s)", rewrapped, current, encryption.ActiveKeyId, errors.Count);
        return new RewrapResult(encryption.ActiveKeyId, documents.Count, rewrapped, current, errors);
    }
}
