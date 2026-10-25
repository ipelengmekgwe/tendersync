using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using TenderSync.Application.Common.Auditing;
using TenderSync.Application.Common.Models;
using TenderSync.Application.Features.Admin.Queries.ListAuditLogs;
using TenderSync.Application.Features.Companies;
using TenderSync.Application.Features.Companies.Commands.RewrapCompanyDocuments;
using TenderSync.Domain.Companies;
using TenderSync.Domain.Enums;

namespace TenderSync.Api.Tests;

/// <summary>Profile CRUD with validation and audit redaction, and the encrypted document library end to end (WP09).</summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class CompanyProfileEndpointTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private static CompanyProfileInput Input(string name = "Acme Software") => new(
        name, "Acme", "2015/123456/07", "4123456789", 8, 25, 12_000_000m,
        "MAAA0001234", DateTimeOffset.UtcNow.AddMonths(6), "PIN-SECRET-9", DateTimeOffset.UtcNow.AddMonths(3), 2, DateTimeOffset.UtcNow.AddMonths(2), "Sworn affidavit",
        null, [], [Province.Gauteng, Province.WesternCape],
        new CompanyCapabilities { CategoryCodes = ["SW_DEV"], Technologies = [".NET"], Certifications = [new CompanyCertification("ISO 27001", "BSI", null)] },
        "{\"email\":\"bids@acme.test\"}", null, true);

    private static async Task<CompanyProfileDto> CreateAsync(HttpClient admin, string name = "Acme Software")
    {
        var response = await admin.PostAsJsonAsync("/api/v1/company-profiles", Input(name));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CompanyProfileDto>())!;
    }

    [Fact]
    public async Task Admins_manage_profiles_viewers_read_them_and_the_tax_pin_is_masked_everywhere()
    {
        using var viewer = await factory.CreateViewerClientAsync();
        (await viewer.PostAsJsonAsync("/api/v1/company-profiles", Input())).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var admin = await factory.CreateAdminClientAsync();
        var created = await CreateAsync(admin, $"Acme {Guid.NewGuid():N}");
        created.TaxPinMasked.Should().Be("*********T-9", "the response never carries the PIN");
        created.Provinces.Should().Equal(Province.Gauteng, Province.WesternCape);
        created.Capabilities.Certifications.Should().ContainSingle(c => c.Name == "ISO 27001");
        created.ShareKeyPersonnelWithModel.Should().BeTrue();
        created.IsActive.Should().BeTrue();

        var fetched = await viewer.GetFromJsonAsync<CompanyProfileDto>($"/api/v1/company-profiles/{created.Id}");
        fetched!.Name.Should().Be(created.Name);
        fetched.DocumentCount.Should().Be(0);
        (await viewer.GetFromJsonAsync<List<CompanyProfileListItemDto>>("/api/v1/company-profiles"))!.Should().Contain(p => p.Id == created.Id && p.CategoryCodes.Contains("SW_DEV"));

        var updated = await (await admin.PutAsJsonAsync($"/api/v1/company-profiles/{created.Id}", Input(created.Name) with { YearsTrading = 9, BbbeeLevel = 1, ShareKeyPersonnelWithModel = false })).Content.ReadFromJsonAsync<CompanyProfileDto>();
        updated!.YearsTrading.Should().Be(9);
        updated.BbbeeLevel.Should().Be(1);
        updated.ShareKeyPersonnelWithModel.Should().BeFalse();

        var kept = await (await admin.PutAsJsonAsync($"/api/v1/company-profiles/{created.Id}", Input(created.Name) with { TaxPin = null })).Content.ReadFromJsonAsync<CompanyProfileDto>();
        kept!.TaxPinMasked.Should().Be("*********T-9", "an editor that only saw the masked PIN must not wipe it");
        var cleared = await (await admin.PutAsJsonAsync($"/api/v1/company-profiles/{created.Id}", Input(created.Name) with { TaxPin = "" })).Content.ReadFromJsonAsync<CompanyProfileDto>();
        cleared!.TaxPinMasked.Should().BeNull();

        var invalid = await admin.PutAsJsonAsync($"/api/v1/company-profiles/{created.Id}", Input(created.Name) with { BbbeeLevel = 9, CidbClasses = ["GBX"], ContactJson = "not json" });
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await invalid.Content.ReadAsStringAsync();
        problem.Should().Contain("BbbeeLevel").And.Contain("CidbClasses").And.Contain("ContactJson");

        (await admin.DeleteAsync($"/api/v1/company-profiles/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.GetFromJsonAsync<List<CompanyProfileListItemDto>>("/api/v1/company-profiles"))!.Should().NotContain(p => p.Id == created.Id);
        (await viewer.GetFromJsonAsync<List<CompanyProfileListItemDto>>("/api/v1/company-profiles?includeInactive=true"))!.Should().Contain(p => p.Id == created.Id && !p.IsActive);
        (await admin.PostAsync($"/api/v1/company-profiles/{created.Id}/activate", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.GetAsync($"/api/v1/company-profiles/{Guid.CreateVersion7()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.DeleteAsync($"/api/v1/company-profiles/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.OK, "left inactive so other test classes see one active profile");

        // The audit row of the update carries the body with the PIN redacted, two levels down.
        var audit = await admin.GetFromJsonAsync<PagedResult<AuditLogDto>>($"/api/v1/admin/audit-logs?action=UpdateCompanyProfileCommand&pageSize=50");
        var entry = audit!.Items.First(e => e.EntityId == created.Id.ToString());
        entry.EntityType.Should().Be(nameof(CompanyProfile));
        using var payload = JsonDocument.Parse(entry.AfterJson!);
        payload.RootElement.GetProperty("profile").GetProperty("taxPin").GetString().Should().Be(RequestRedactor.Redacted);
        payload.RootElement.GetProperty("profile").GetProperty("csdNumber").GetString().Should().Be("MAAA0001234");
        entry.AfterJson.Should().NotContain("PIN-SECRET");
    }

    [Fact]
    public async Task Documents_are_encrypted_in_the_store_decrypted_on_download_and_every_download_is_audited()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var profile = await CreateAsync(admin, $"Docs {Guid.NewGuid():N}");
        var bytes = RandomNumberGenerator.GetBytes(50_000);
        bytes[0] = (byte)'%';

        using var upload = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        upload.Add(file, "file", "tax-pin.pdf");
        upload.Add(new StringContent("TaxPin"), "type");
        upload.Add(new StringContent("Tax PIN 2026"), "title");
        upload.Add(new StringContent(DateTimeOffset.UtcNow.AddMonths(11).ToString("O")), "expiresAt");

        using var viewer = await factory.CreateViewerClientAsync();
        (await viewer.PostAsync($"/api/v1/company-profiles/{profile.Id}/documents", upload)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var response = await admin.PostAsync($"/api/v1/company-profiles/{profile.Id}/documents", upload);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var document = (await response.Content.ReadFromJsonAsync<CompanyDocumentDto>(Json))!;
        document.Type.Should().Be(CompanyDocumentType.TaxPin);
        document.Title.Should().Be("Tax PIN 2026");
        document.SizeBytes.Should().Be(bytes.Length);
        document.Sha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(bytes)));
        document.ContentType.Should().Be("application/pdf");
        document.MasterKeyId.Should().Be("test");
        document.IsExpired.Should().BeFalse();

        // Nothing under the store root is plaintext.
        var stored = Directory.GetFiles(Path.Combine(factory.DocumentRoot, "companies", profile.Id.ToString()), "*.enc").Should().ContainSingle().Subject;
        var onDisk = await File.ReadAllBytesAsync(stored);
        onDisk.AsSpan().IndexOf(bytes.AsSpan(0, 64)).Should().Be(-1);
        onDisk.Take(4).Should().Equal("TSE1"u8.ToArray());

        var download = await admin.GetAsync($"/api/v1/company-profiles/{profile.Id}/documents/{document.Id}/download");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        download.Content.Headers.ContentDisposition!.FileName.Should().Contain("tax-pin.pdf".Replace("tax-pin.pdf", "Tax PIN 2026.pdf", StringComparison.Ordinal));
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);
        (await viewer.GetAsync($"/api/v1/company-profiles/{profile.Id}/documents/{document.Id}/download")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var audit = await admin.GetFromJsonAsync<PagedResult<AuditLogDto>>("/api/v1/admin/audit-logs?action=DownloadCompanyDocumentCommand&pageSize=50");
        audit!.Items.Should().Contain(e => e.EntityId == document.Id.ToString() && e.EntityType == nameof(CompanyDocument));
        var uploadAudit = await admin.GetFromJsonAsync<PagedResult<AuditLogDto>>("/api/v1/admin/audit-logs?action=UploadCompanyDocumentCommand&pageSize=50");
        using var uploadPayload = JsonDocument.Parse(uploadAudit!.Items.First(e => e.EntityId == profile.Id.ToString()).AfterJson!);
        uploadPayload.RootElement.GetProperty("content").GetString().Should().Be(RequestRedactor.Redacted, "the stream is never serialised");

        var listed = await viewer.GetFromJsonAsync<List<CompanyDocumentDto>>($"/api/v1/company-profiles/{profile.Id}/documents", Json);
        listed!.Should().ContainSingle(d => d.Id == document.Id);
        (await viewer.GetFromJsonAsync<CompanyProfileDto>($"/api/v1/company-profiles/{profile.Id}"))!.DocumentCount.Should().Be(1);

        var renamed = await (await admin.PutAsJsonAsync($"/api/v1/company-profiles/{profile.Id}/documents/{document.Id}", new { title = "Tax PIN 2026 (renewed)", expiresAt = DateTimeOffset.UtcNow.AddDays(-1) })).Content.ReadFromJsonAsync<CompanyDocumentDto>(Json);
        renamed!.Title.Should().Be("Tax PIN 2026 (renewed)");
        renamed.IsExpired.Should().BeTrue();

        var rewrap = await (await admin.PostAsync("/api/v1/admin/encryption/rewrap", null)).Content.ReadFromJsonAsync<RewrapResult>();
        rewrap!.ActiveKeyId.Should().Be("test");
        rewrap.AlreadyCurrent.Should().BeGreaterThanOrEqualTo(1);
        rewrap.Errors.Should().BeEmpty();

        (await viewer.DeleteAsync($"/api/v1/company-profiles/{profile.Id}/documents/{document.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.DeleteAsync($"/api/v1/company-profiles/{profile.Id}/documents/{document.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await viewer.GetFromJsonAsync<List<CompanyDocumentDto>>($"/api/v1/company-profiles/{profile.Id}/documents", Json))!.Should().BeEmpty();
        (await viewer.GetFromJsonAsync<List<CompanyDocumentDto>>($"/api/v1/company-profiles/{profile.Id}/documents?includeDeleted=true", Json))!.Should().ContainSingle(d => d.DeletedAt != null);
        (await admin.GetAsync($"/api/v1/company-profiles/{profile.Id}/documents/{document.Id}/download")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        File.Exists(stored).Should().BeTrue("soft delete keeps the encrypted blob");
        (await admin.DeleteAsync($"/api/v1/company-profiles/{profile.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_empty_upload_is_rejected_and_leaves_nothing_in_the_store()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var profile = await CreateAsync(admin, $"Empty {Guid.NewGuid():N}");
        using var upload = new MultipartFormDataContent { { new ByteArrayContent([]), "file", "empty.pdf" }, { new StringContent("Cv"), "type" } };

        var response = await admin.PostAsync($"/api/v1/company-profiles/{profile.Id}/documents", upload);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var folder = Path.Combine(factory.DocumentRoot, "companies", profile.Id.ToString());
        (Directory.Exists(folder) ? Directory.GetFiles(folder) : []).Should().BeEmpty();
        (await admin.DeleteAsync($"/api/v1/company-profiles/{profile.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
