using TenderSync.Domain.Common;
using TenderSync.Domain.Enums;

namespace TenderSync.Domain.Tenders;

/// <summary>A procuring entity (organ of state). Keyed by the source's buyer id.</summary>
public sealed class Buyer : Entity
{
    private Buyer()
    {
    }

    public string ExternalId { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public BuyerType Type { get; private set; }

    public Province Province { get; private set; }

    public static Buyer Create(string externalId, string name, BuyerType type, Province province)
    {
        return new Buyer
        {
            ExternalId = Guard.NotEmpty(externalId, nameof(externalId)),
            Name = Guard.NotEmpty(name, nameof(name)),
            Type = type,
            Province = province,
        };
    }

    public void Update(string name, BuyerType type, Province province)
    {
        Name = Guard.NotEmpty(name, nameof(name));
        if (type != BuyerType.Unknown)
        {
            Type = type;
        }

        if (province != Province.Unknown)
        {
            Province = province;
        }
    }
}
