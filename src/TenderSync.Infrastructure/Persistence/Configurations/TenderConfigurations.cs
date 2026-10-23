using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderSync.Domain.Tenders;

namespace TenderSync.Infrastructure.Persistence.Configurations;

internal sealed class BuyerConfiguration : IEntityTypeConfiguration<Buyer>
{
    public void Configure(EntityTypeBuilder<Buyer> builder)
    {
        builder.ConfigureEntity("buyers");
        builder.Property(b => b.ExternalId).HasMaxLength(64).IsRequired();
        builder.Property(b => b.Name).HasMaxLength(512).IsRequired();
        builder.HasIndex(b => b.ExternalId).IsUnique();
        builder.HasIndex(b => b.Name);
    }
}

internal sealed class TenderConfiguration : IEntityTypeConfiguration<Tender>
{
    public void Configure(EntityTypeBuilder<Tender> builder)
    {
        builder.ConfigureEntity("tenders");
        builder.Property(t => t.Ocid).HasMaxLength(64).IsRequired();
        builder.Property(t => t.ExternalTenderId).HasMaxLength(64).IsRequired();
        builder.Property(t => t.BidNumber).HasMaxLength(256).IsRequired();
        builder.Property(t => t.Title).HasMaxLength(1024).IsRequired();
        builder.Property(t => t.Description).IsRequired();
        builder.Property(t => t.AdditionalCategories).HasStringArrayComparer();
        builder.Property(t => t.ProcurementMethod).HasMaxLength(64);
        builder.Property(t => t.ProcurementMethodDetails).HasMaxLength(256);
        builder.Property(t => t.ValueAmount).HasPrecision(18, 2);
        builder.Property(t => t.ValueCurrency).HasMaxLength(8);
        builder.Property(t => t.BriefingVenue).HasMaxLength(1024);
        builder.Property(t => t.StatusReason).HasMaxLength(512);
        builder.Property(t => t.SourceUrl).HasMaxLength(2048).IsRequired();
        builder.Property(t => t.ContentHash).HasMaxLength(64).IsRequired();

        builder.HasIndex(t => t.Ocid).IsUnique();
        builder.HasIndex(t => t.BuyerId);
        builder.HasIndex(t => t.ClosingAt);
        builder.HasIndex(t => new { t.Status, t.ClosingAt });
        builder.HasIndex(t => t.PublishedAt);

        builder.HasOne(t => t.Buyer).WithMany().HasForeignKey(t => t.BuyerId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.Documents).WithOne().HasForeignKey(d => d.TenderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(t => t.Releases).WithOne().HasForeignKey(r => r.TenderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(t => t.StatusHistory).WithOne().HasForeignKey(h => h.TenderId).OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(t => t.Documents).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_documents");
        builder.Navigation(t => t.Releases).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_releases");
        builder.Navigation(t => t.StatusHistory).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_statusHistory");

        // search_vector (generated tsvector + GIN index) is added by raw SQL in the Initial migration and queried via IReadDb.
    }
}

internal sealed class TenderReleaseConfiguration : IEntityTypeConfiguration<TenderRelease>
{
    public void Configure(EntityTypeBuilder<TenderRelease> builder)
    {
        builder.ConfigureEntity("tender_releases");
        builder.Property(r => r.ReleaseId).HasMaxLength(128).IsRequired();
        builder.Property(r => r.Tag).HasMaxLength(64).IsRequired();
        builder.Property(r => r.RawJson).IsJsonb().IsRequired();
        builder.Property(r => r.Hash).HasMaxLength(64).IsRequired();
        builder.HasIndex(r => new { r.TenderId, r.Hash }).IsUnique();
    }
}

internal sealed class TenderDocumentConfiguration : IEntityTypeConfiguration<TenderDocument>
{
    public void Configure(EntityTypeBuilder<TenderDocument> builder)
    {
        builder.ConfigureEntity("tender_documents");
        builder.Property(d => d.ExternalId).HasMaxLength(128).IsRequired();
        builder.Property(d => d.Title).HasMaxLength(1024).IsRequired();
        builder.Property(d => d.Url).HasMaxLength(2048).IsRequired();
        builder.Property(d => d.Format).HasMaxLength(32);
        builder.Property(d => d.Language).HasMaxLength(16);
        builder.Property(d => d.StoragePath).HasMaxLength(1024);
        builder.Property(d => d.Sha256).HasMaxLength(64);
        builder.Property(d => d.Error).HasMaxLength(2048);
        builder.HasIndex(d => new { d.TenderId, d.ExternalId }).IsUnique();
        builder.HasIndex(d => d.Status);
    }
}

internal sealed class DocumentTextConfiguration : IEntityTypeConfiguration<DocumentText>
{
    public void Configure(EntityTypeBuilder<DocumentText> builder)
    {
        builder.ToTable("document_texts");
        builder.HasKey(t => new { t.DocumentId, t.Page });
        builder.Property(t => t.Text).IsRequired();
        builder.HasOne<TenderDocument>().WithMany().HasForeignKey(t => t.DocumentId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TenderStatusHistoryConfiguration : IEntityTypeConfiguration<TenderStatusHistory>
{
    public void Configure(EntityTypeBuilder<TenderStatusHistory> builder)
    {
        builder.ConfigureEntity("tender_status_history");
        builder.Property(h => h.Reason).HasMaxLength(512);
        builder.HasIndex(h => new { h.TenderId, h.At });
    }
}

internal sealed class TenderClassificationConfiguration : IEntityTypeConfiguration<TenderClassification>
{
    public void Configure(EntityTypeBuilder<TenderClassification> builder)
    {
        builder.ToTable("tender_classifications");
        builder.HasKey(c => c.TenderId);
        builder.Property(c => c.CategoryCodes).HasStringArrayComparer();
        builder.Property(c => c.Rationale).HasMaxLength(4000);
        builder.Property(c => c.Model).HasMaxLength(128);
        builder.Property(c => c.PromptVersion).HasMaxLength(32);
        builder.HasIndex(c => c.Relevance);
        builder.HasOne<Tender>().WithOne().HasForeignKey<TenderClassification>(c => c.TenderId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TenderMatchConfiguration : IEntityTypeConfiguration<TenderMatch>
{
    public void Configure(EntityTypeBuilder<TenderMatch> builder)
    {
        builder.ConfigureEntity("tender_matches");
        builder.Property(m => m.DismissReason).HasMaxLength(1024);
        builder.Property(m => m.Notes).HasMaxLength(4000);
        builder.HasIndex(m => new { m.TenderId, m.CompanyProfileId }).IsUnique();
        builder.HasIndex(m => new { m.CompanyProfileId, m.Status });
        builder.HasOne<Tender>().WithMany().HasForeignKey(m => m.TenderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Domain.Companies.CompanyProfile>().WithMany().HasForeignKey(m => m.CompanyProfileId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TenderRequirementConfiguration : IEntityTypeConfiguration<TenderRequirement>
{
    public void Configure(EntityTypeBuilder<TenderRequirement> builder)
    {
        builder.ConfigureEntity("tender_requirements");
        builder.Property(r => r.Text).IsRequired();
        builder.Property(r => r.ValueJson).IsJsonb();
        builder.Property(r => r.Weight).HasPrecision(9, 4);
        builder.Property(r => r.Threshold).HasPrecision(9, 4);
        builder.Property(r => r.Confidence).HasPrecision(5, 4);
        builder.Property(r => r.Model).HasMaxLength(128).IsRequired();
        builder.Property(r => r.PromptVersion).HasMaxLength(32).IsRequired();
        builder.Ignore(r => r.NeedsVerification);
        builder.OwnsOne(r => r.Source, source =>
        {
            source.Property(s => s.DocumentId).HasColumnName("source_document_id");
            source.Property(s => s.Page).HasColumnName("source_page");
            source.Property(s => s.Quote).HasColumnName("source_quote");
        });
        builder.HasIndex(r => new { r.TenderId, r.Kind });
        builder.HasOne<Tender>().WithMany().HasForeignKey(r => r.TenderId).OnDelete(DeleteBehavior.Cascade);
    }
}
