using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderSync.Domain.Common;

namespace TenderSync.Infrastructure.Persistence.Configurations;

internal static class ConfigurationExtensions
{
    /// <summary>Key, audit timestamps and domain-event exclusion shared by every <see cref="Entity"/>.</summary>
    public static EntityTypeBuilder<TEntity> ConfigureEntity<TEntity>(this EntityTypeBuilder<TEntity> builder, string table)
        where TEntity : Entity
    {
        builder.ToTable(table);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.UpdatedAt).IsRequired();
        builder.Ignore(e => e.DomainEvents);
        return builder;
    }

    /// <summary>Maps a string property (nullable or not) to a jsonb column.</summary>
    public static PropertyBuilder<TProperty> IsJsonb<TProperty>(this PropertyBuilder<TProperty> builder) => builder.HasColumnType("jsonb");

    /// <summary>Stores an enum array as integer[] with a comparer so EF detects element changes.</summary>
    public static PropertyBuilder<TEnum[]> HasEnumArrayConversion<TEnum>(this PropertyBuilder<TEnum[]> builder)
        where TEnum : struct, Enum
    {
        return builder
            .HasConversion(
                values => values.Select(v => Convert.ToInt32(v, CultureInfo.InvariantCulture)).ToArray(),
                values => values.Select(v => (TEnum)Enum.ToObject(typeof(TEnum), v)).ToArray(),
                new ValueComparer<TEnum[]>(
                    (a, b) => (a ?? Array.Empty<TEnum>()).SequenceEqual(b ?? Array.Empty<TEnum>()),
                    a => a.Aggregate(0, (hash, v) => HashCode.Combine(hash, v)),
                    a => a.ToArray()))
            .HasColumnType("integer[]");
    }

    /// <summary>text[] column with a comparer so EF detects element changes.</summary>
    public static PropertyBuilder<string[]> HasStringArrayComparer(this PropertyBuilder<string[]> builder)
    {
        builder.Metadata.SetValueComparer(new ValueComparer<string[]>(
            (a, b) => (a ?? Array.Empty<string>()).SequenceEqual(b ?? Array.Empty<string>(), StringComparer.Ordinal),
            a => a.Aggregate(0, (hash, v) => HashCode.Combine(hash, StringComparer.Ordinal.GetHashCode(v))),
            a => a.ToArray()));
        return builder;
    }
}
