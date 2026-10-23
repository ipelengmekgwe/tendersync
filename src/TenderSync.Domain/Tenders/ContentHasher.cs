using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TenderSync.Domain.Tenders;

/// <summary>
/// Stable hash of the fields that matter for change detection. Volatile fields (release id, release date, raw JSON)
/// are excluded so a re-published identical release produces the same hash.
/// </summary>
public static class ContentHasher
{
    public static string Compute(TenderSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var sb = new StringBuilder();
        sb.Append(snapshot.Ocid).Append('|')
          .Append(snapshot.BidNumber).Append('|')
          .Append(snapshot.Title).Append('|')
          .Append(snapshot.Description).Append('|')
          .Append(snapshot.BuyerExternalId).Append('|')
          .Append(snapshot.MainCategory).Append('|')
          .Append(string.Join(',', snapshot.AdditionalCategories.Order(StringComparer.Ordinal))).Append('|')
          .Append(snapshot.ProcurementMethod).Append('|')
          .Append(snapshot.ProcurementMethodDetails).Append('|')
          .Append(snapshot.ValueAmount?.ToString(CultureInfo.InvariantCulture)).Append('|')
          .Append(snapshot.ClosingAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).Append('|')
          .Append(snapshot.HasAwards).Append('|');

        foreach (var doc in snapshot.Documents.OrderBy(d => d.ExternalId, StringComparer.Ordinal))
        {
            sb.Append(doc.ExternalId).Append(':')
              .Append(doc.Title).Append(':')
              .Append(doc.DateModified?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).Append(';');
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexStringLower(bytes);
    }
}
