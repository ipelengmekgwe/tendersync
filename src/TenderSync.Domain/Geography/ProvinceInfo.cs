using TenderSync.Domain.Enums;

namespace TenderSync.Domain.Geography;

/// <summary>Names and codes of the South African provinces.</summary>
public static class ProvinceInfo
{
    private static readonly Dictionary<Province, (string Name, string Code)> Data = new()
    {
        [Province.EasternCape] = ("Eastern Cape", "EC"),
        [Province.FreeState] = ("Free State", "FS"),
        [Province.Gauteng] = ("Gauteng", "GP"),
        [Province.KwaZuluNatal] = ("KwaZulu-Natal", "KZN"),
        [Province.Limpopo] = ("Limpopo", "LP"),
        [Province.Mpumalanga] = ("Mpumalanga", "MP"),
        [Province.NorthWest] = ("North West", "NW"),
        [Province.NorthernCape] = ("Northern Cape", "NC"),
        [Province.WesternCape] = ("Western Cape", "WC"),
    };

    private static readonly Dictionary<string, Province> ByKey = BuildKeys();

    public static IReadOnlyCollection<Province> All { get; } = [.. Data.Keys];

    public static string DisplayName(Province province) => province switch
    {
        Province.National => "National",
        Province.Unknown => "Unknown",
        _ => Data[province].Name,
    };

    public static string Code(Province province) => province switch
    {
        Province.National => "ZA",
        Province.Unknown => "??",
        _ => Data[province].Code,
    };

    /// <summary>Parses a province from its display name, code or enum name. Returns Unknown when not recognised.</summary>
    public static Province Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Province.Unknown;
        }

        return ByKey.TryGetValue(Key(value), out var province) ? province : Province.Unknown;
    }

    internal static string Key(string value) =>
        new([.. value.ToLowerInvariant().Where(char.IsLetterOrDigit)]);

    private static Dictionary<string, Province> BuildKeys()
    {
        var keys = new Dictionary<string, Province>(StringComparer.Ordinal);
        foreach (var (province, (name, code)) in Data)
        {
            keys[Key(name)] = province;
            keys[Key(code)] = province;
            keys[Key(province.ToString())] = province;
        }

        keys[Key("KZN")] = Province.KwaZuluNatal;
        keys[Key("Natal")] = Province.KwaZuluNatal;
        keys[Key("National")] = Province.National;
        return keys;
    }
}
