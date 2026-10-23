using TenderSync.Domain.Enums;
using TenderSync.Domain.Geography;

namespace TenderSync.Domain.Tests.Geography;

public class ProvinceResolverTests
{
    private static readonly Dictionary<string, Province> Lookup = new(StringComparer.Ordinal)
    {
        ["greater tzaneen"] = Province.Limpopo,
        ["ingquza hill"] = Province.EasternCape,
        ["cape town"] = Province.WesternCape,
        ["metsimaholo"] = Province.FreeState,
    };

    private static Province? Resolve(string normalised) => Lookup.TryGetValue(normalised, out var p) ? p : null;

    [Theory]
    [InlineData("Western Cape - Health", BuyerType.Provincial, Province.WesternCape)]
    [InlineData("Gauteng - Health", BuyerType.Provincial, Province.Gauteng)]
    [InlineData("KwaZulu-Natal: Department of Education", BuyerType.Provincial, Province.KwaZuluNatal)]
    [InlineData("KZN Treasury", BuyerType.Provincial, Province.KwaZuluNatal)]
    [InlineData("Limpopo Provincial Treasury", BuyerType.Provincial, Province.Limpopo)]
    [InlineData("Greater Tzaneen Municipality", BuyerType.Municipal, Province.Limpopo)]
    [InlineData("Ingquza Hill Local Municipality", BuyerType.Municipal, Province.EasternCape)]
    [InlineData("City of Cape Town", BuyerType.Municipal, Province.WesternCape)]
    [InlineData("Metsimaholo Local Municipality", BuyerType.Municipal, Province.FreeState)]
    [InlineData("Some Unknown Local Municipality", BuyerType.Municipal, Province.Unknown)]
    [InlineData("ESKOM", BuyerType.StateOwnedEntity, Province.National)]
    [InlineData("Transnet SOC Ltd", BuyerType.StateOwnedEntity, Province.National)]
    [InlineData("Passenger Rail Agency of South Africa", BuyerType.StateOwnedEntity, Province.National)]
    [InlineData("Land and Agricultural Development Bank of South Africa", BuyerType.StateOwnedEntity, Province.National)]
    [InlineData("Media, Information and Communication Technologies Sector Education and training Authority (MICT SETA)", BuyerType.StateOwnedEntity, Province.National)]
    [InlineData("Public Works", BuyerType.National, Province.National)]
    [InlineData("Department of Home Affairs", BuyerType.National, Province.National)]
    [InlineData("National Treasury", BuyerType.National, Province.National)]
    [InlineData("Acme Trading", BuyerType.Other, Province.Unknown)]
    [InlineData("", BuyerType.Unknown, Province.Unknown)]
    public void Resolves_buyer_type_and_province_from_the_name(string name, BuyerType expectedType, Province expectedProvince)
    {
        var result = ProvinceResolver.Resolve(name, Resolve);

        result.Type.Should().Be(expectedType);
        result.Province.Should().Be(expectedProvince);
    }

    [Theory]
    [InlineData("Greater Tzaneen Local Municipality", "greater tzaneen")]
    [InlineData("City of Cape Town Metropolitan Municipality", "cape town")]
    [InlineData("eThekwini Metropolitan Municipality", "ethekwini")]
    [InlineData("Dr Beyers Naudé Local Municipality", "dr beyers naude")]
    [InlineData("Kai !Garib Local Municipality", "kai garib")]
    [InlineData("!Kheis Local Municipality", "kheis")]
    [InlineData("Emalahleni Local Municipality (Mpumalanga)", "emalahleni mpumalanga")]
    [InlineData("  Rustenburg   Municipality ", "rustenburg")]
    public void Normalises_municipality_names(string input, string expected)
    {
        ProvinceResolver.NormaliseName(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("Western Cape", Province.WesternCape)]
    [InlineData("wc", Province.WesternCape)]
    [InlineData("KwaZulu-Natal", Province.KwaZuluNatal)]
    [InlineData("KZN", Province.KwaZuluNatal)]
    [InlineData("north west", Province.NorthWest)]
    [InlineData("NorthernCape", Province.NorthernCape)]
    [InlineData("National", Province.National)]
    [InlineData("Narnia", Province.Unknown)]
    [InlineData(null, Province.Unknown)]
    public void Parses_province_names_and_codes(string? input, Province expected)
    {
        ProvinceInfo.Parse(input).Should().Be(expected);
    }
}
