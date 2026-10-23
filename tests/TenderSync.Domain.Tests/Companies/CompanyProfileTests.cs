using TenderSync.Domain.Common;
using TenderSync.Domain.Companies;
using TenderSync.Domain.Enums;

namespace TenderSync.Domain.Tests.Companies;

public class CompanyProfileTests
{
    [Fact]
    public void Capabilities_round_trip_through_json()
    {
        var profile = CompanyProfile.Create("Acme Software");
        var capabilities = new CompanyCapabilities
        {
            CategoryCodes = ["SW_DEV", "WEB_DEV"],
            Certifications = [new CompanyCertification("ISO 27001", "BSI", TestSnapshots.Now.AddYears(1))],
            ReferenceProjects = [new ReferenceProject("Western Cape - Health", "Patient portal", 1_200_000m, 2025, true)],
        };

        profile.UpdateCapabilities(capabilities);

        profile.CapabilitiesJson.Should().Contain("\"categoryCodes\"");
        profile.Capabilities.Should().BeEquivalentTo(capabilities);
    }

    [Fact]
    public void Compliance_validity_depends_on_expiry()
    {
        var profile = CompanyProfile.Create("Acme Software");
        profile.UpdateCompliance("MAAA0001234", TestSnapshots.Now.AddMonths(6), "ABC123", TestSnapshots.Now.AddDays(-1), 2, null, "Sworn affidavit");

        profile.HasValidCsd(TestSnapshots.Now).Should().BeTrue();
        profile.HasValidTaxPin(TestSnapshots.Now).Should().BeFalse("the tax PIN expired yesterday");
        profile.HasValidBbbee(TestSnapshots.Now).Should().BeTrue("no expiry means still valid");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void Bbbee_level_outside_1_to_8_is_rejected(int level)
    {
        var profile = CompanyProfile.Create("Acme Software");

        var act = () => profile.UpdateCompliance(null, null, null, null, level, null, null);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Provinces_drop_unknown_and_duplicates()
    {
        var profile = CompanyProfile.Create("Acme Software");

        profile.UpdateProvinces([Province.Gauteng, Province.Unknown, Province.Gauteng, Province.WesternCape]);

        profile.Provinces.Should().Equal(Province.Gauteng, Province.WesternCape);
    }
}
