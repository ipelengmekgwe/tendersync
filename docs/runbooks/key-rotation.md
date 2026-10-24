# Runbook: rotating the company-document master key

Company documents (CVs, certificates, tax PIN letters) are encrypted at rest with a random data key per file; the data key is wrapped by a master key and stored on the `company_documents` row as `encrypted_data_key` (`v1|{keyId}|{base64}`). Rotation replaces the wrapping only: no blob is re-encrypted, and documents stay readable throughout because every key named on a row stays configured until the re-wrap has run. See PLAN.md §12 and ADR-013.

## Environment provider (local, VPS, Container Apps secrets)

Keys live under `Encryption:Keys:{id}` (base64, 32 bytes) with `Encryption:ActiveKeyId` naming the one new uploads use. A lone `TENDERSYNC_MASTER_KEY` variable is read as key id `env`.

1. **Generate** a new key and give it a new id (the id is authenticated into every wrap, so never reuse one):

   ```bash
   openssl rand -base64 32
   ```

2. **Configure both keys** on the API and the Worker: keep the old entry, add the new one, and point `Encryption:ActiveKeyId` at the new id. Restart both hosts. New uploads now use the new key; old documents still open because their key id is still configured.

   ```bash
   dotnet user-secrets set "Encryption:Keys:k2" "<new base64 key>" --project src/TenderSync.Api
   ```

   ```bash
   dotnet user-secrets set "Encryption:ActiveKeyId" "k2" --project src/TenderSync.Api
   ```

   Repeat for `src/TenderSync.Worker` (user-secrets id `tendersync-worker`) or the deployment's secret store.

3. **Re-wrap** every document under the new key (Admin token; safe to repeat, idempotent):

   ```bash
   curl -X POST http://localhost:5000/api/v1/admin/encryption/rewrap -H "Authorization: Bearer <admin token>"
   ```

   The response lists the active key id, how many rows were re-wrapped, how many were already current and any errors. `GET /api/v1/company-profiles/{id}/documents` shows `masterKeyId` per document; every row must show the new id before the next step.

4. **Retire** the old key: remove `Encryption:Keys:{old}` from every host and restart. A row still wrapped with it would now fail to open with "Master key '{old}' is not configured", which is why step 3 must report zero errors first.

5. **Record** the rotation date and key id in the deployment log. Old keys are not kept anywhere else.

## Key Vault provider (Azure)

`Encryption:Provider=KeyVault` with `Encryption:KeyVault:KeyUri=https://{vault}.vault.azure.net/keys/{name}`. The key id on each row is the versioned key URI, so:

1. Create a new key version in Key Vault (`az keyvault key rotate` or the rotation policy). Older versions stay enabled.
2. Restart the hosts; `ActiveKeyId` resolves to the new version. New uploads wrap with it.
3. Run the re-wrap endpoint as above.
4. Disable the old key version once every document reports the new one.

The managed identity needs `wrapKey` and `unwrapKey` on the key and `get` on key metadata.

## Verifying

- `tests/TenderSync.Infrastructure.Tests/Security/EncryptionTests.cs` covers the round trip, tamper detection and the two-key rotation sequence.
- After a production rotation, download one old and one new document through the API: both must open, and both downloads appear in `GET /api/v1/admin/audit-logs?action=DownloadCompanyDocumentCommand`.
