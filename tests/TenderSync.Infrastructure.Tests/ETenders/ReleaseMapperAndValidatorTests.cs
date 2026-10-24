using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using TenderSync.Domain.Enums;
using TenderSync.Infrastructure.ETenders;

namespace TenderSync.Infrastructure.Tests.ETenders;

public class ReleaseMapperTests
{
    private static readonly ReleaseMapper Mapper = new(Options.Create(new ETendersOptions()));

    private static OcdsReleaseEnvelope Envelope(string ocid)
    {
        var page = ETendersClient.ParsePage(1, Fixtures.Page1());
        return page.Releases.Single(r => r.Release.Ocid == ocid);
    }

    [Fact]
    public void Maps_bid_number_title_description_buyer_and_documents_from_a_real_release()
    {
        var envelope = ETendersClient.ParsePage(1, Fixtures.Read(Fixtures.Page2File)).Releases.Single(r => r.Release.Ocid == "ocds-9t57fa-175445");

        var snapshot = Mapper.ToSnapshot(envelope);

        snapshot.Ocid.Should().Be("ocds-9t57fa-175445");
        snapshot.ExternalTenderId.Should().Be("175445");
        snapshot.BidNumber.Should().Be("WCGHCC0038/1/2025", "tender.title holds the bid number");
        snapshot.Title.Should().StartWith("SUPPLEMENTARY BID FOR THE SUPPLY AND DELIVERY OF COMFORT PACKS");
        snapshot.Title.Length.Should().BeLessThanOrEqualTo(ReleaseMapper.MaxTitleLength + 1);
        snapshot.Description.Should().Contain("30 SEPTEMBER 2028");
        snapshot.BuyerExternalId.Should().Be("723");
        snapshot.BuyerName.Should().Be("Western Cape - Health");
        snapshot.MainCategory.Should().Be(ProcurementCategory.Goods);
        snapshot.ProcurementMethodDetails.Should().Be("Request for Bid(Open-Tender)");
        snapshot.ValueAmount.Should().Be(0);
        snapshot.ClosingAt.Should().Be(new DateTimeOffset(2026, 11, 20, 11, 0, 0, TimeSpan.Zero));
        snapshot.SourceUrl.Should().Be("https://www.etenders.gov.za/Home/TenderDetails?Id=175445");
        snapshot.ReleaseTag.Should().Be("compiled");
        snapshot.Documents.Should().ContainSingle(d => d.Format == "pdf" && d.Url.Contains("Download?blobName="));
        snapshot.HasAwards.Should().BeFalse();
        snapshot.RawJson.Should().Contain("\"ocid\"");
    }

    [Fact]
    public void Every_release_in_the_sample_maps_without_error()
    {
        var page = ETendersClient.ParsePage(1, Fixtures.Page1());

        var snapshots = page.Releases.Select(Mapper.ToSnapshot).ToList();

        snapshots.Should().HaveCount(100);
        snapshots.Select(s => s.Ocid).Should().OnlyHaveUniqueItems();
        snapshots.Should().AllSatisfy(s =>
        {
            s.BidNumber.Should().NotBeNullOrWhiteSpace();
            s.Title.Should().NotBeNullOrWhiteSpace();
            s.BuyerExternalId.Should().NotBeNullOrWhiteSpace();
            s.Documents.Should().NotBeEmpty();
        });
    }

    [Theory]
    [InlineData("\"0\"", 0.0)]
    [InlineData("1234.5", 1234.5)]
    [InlineData("\"\"", null)]
    [InlineData("null", null)]
    public void Amounts_are_parsed_tolerantly(string json, double? expected)
    {
        var element = JsonDocument.Parse(json).RootElement;

        var amount = ReleaseMapper.ParseAmount(element.ValueKind == JsonValueKind.Null ? null : element);

        (amount is null ? (double?)null : (double)amount.Value).Should().Be(expected);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("not a date", null)]
    [InlineData("2026-11-16T10:00:00Z", "2026-11-16T10:00:00+00:00")]
    public void Dates_are_parsed_tolerantly(string value, string? expected)
    {
        ReleaseMapper.ParseDate(value).Should().Be(expected is null ? null : DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Title_is_the_first_sentence_capped_at_200_characters()
    {
        ReleaseMapper.TitleFrom("Appointment of a service provider for the thing. Second sentence here.", "X").Should().Be("Appointment of a service provider for the thing");
        ReleaseMapper.TitleFrom(new string('a', 500), "X").Should().HaveLength(ReleaseMapper.MaxTitleLength);
        ReleaseMapper.TitleFrom("   ", "E123").Should().Be("E123");
    }
}

public class ReleaseSchemaValidatorTests
{
    private readonly ReleaseSchemaValidator _validator = new();

    [Fact]
    public void The_recorded_pages_match_the_schema()
    {
        _validator.Validate(Fixtures.Page1()).Should().BeEmpty();
        _validator.Validate(Fixtures.Read(Fixtures.Page2File)).Should().BeEmpty();
    }

    [Fact]
    public void A_removed_required_field_is_reported_as_drift()
    {
        var node = JsonNode.Parse(Fixtures.Read(Fixtures.Page2File))!;
        node["releases"]![0]!["tender"]!.AsObject().Remove("documents");
        node["releases"]![1]!.AsObject().Remove("ocid");

        var drift = _validator.Validate(node.ToJsonString());

        drift.Should().NotBeEmpty();
        drift.Should().Contain(d => d.Contains("documents", StringComparison.Ordinal));
        drift.Should().Contain(d => d.Contains("ocid", StringComparison.Ordinal));
    }

    [Fact]
    public void A_retyped_field_is_reported_as_drift()
    {
        var node = JsonNode.Parse(Fixtures.Read(Fixtures.Page2File))!;
        node["releases"]![0]!["tender"]!["documents"] = "not an array any more";

        _validator.Validate(node.ToJsonString()).Should().Contain(d => d.Contains("documents", StringComparison.Ordinal));
    }

    [Fact]
    public void Invalid_json_is_reported()
    {
        _validator.Validate("<html>maintenance</html>").Should().ContainSingle().Which.Should().StartWith("Response is not valid JSON");
    }
}
