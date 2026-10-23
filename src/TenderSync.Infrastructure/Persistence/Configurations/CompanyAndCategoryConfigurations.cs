using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderSync.Domain.Categories;
using TenderSync.Domain.Companies;
using TenderSync.Domain.Geography;
using TenderSync.Domain.Qualification;

namespace TenderSync.Infrastructure.Persistence.Configurations;

internal sealed class InterestCategoryConfiguration : IEntityTypeConfiguration<InterestCategory>
{
    public void Configure(EntityTypeBuilder<InterestCategory> builder)
    {
        builder.ConfigureEntity("interest_categories");
        builder.Property(c => c.Code).HasMaxLength(32).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(128).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(1024);
        builder.Property(c => c.Keywords).HasStringArrayComparer();
        builder.Property(c => c.NegativeKeywords).HasStringArrayComparer();
        builder.HasIndex(c => c.Code).IsUnique();
    }
}

internal sealed class CompanyProfileConfiguration : IEntityTypeConfiguration<CompanyProfile>
{
    public void Configure(EntityTypeBuilder<CompanyProfile> builder)
    {
        builder.ConfigureEntity("company_profiles");
        builder.Property(p => p.Name).HasMaxLength(256).IsRequired();
        builder.Property(p => p.TradingName).HasMaxLength(256);
        builder.Property(p => p.RegistrationNumber).HasMaxLength(64);
        builder.Property(p => p.VatNumber).HasMaxLength(32);
        builder.Property(p => p.CsdNumber).HasMaxLength(32);
        builder.Property(p => p.TaxPin).HasMaxLength(32);
        builder.Property(p => p.BbbeeCertificateType).HasMaxLength(64);
        builder.Property(p => p.CidbClasses).HasStringArrayComparer();
        builder.Property(p => p.Provinces).HasEnumArrayConversion();
        builder.Property(p => p.AnnualTurnover).HasPrecision(18, 2);
        builder.Property(p => p.CapabilitiesJson).IsJsonb().IsRequired();
        builder.Property(p => p.ContactJson).IsJsonb();
        builder.Property(p => p.AddressJson).IsJsonb();
        builder.Ignore(p => p.Capabilities);
    }
}

internal sealed class CompanyDocumentConfiguration : IEntityTypeConfiguration<CompanyDocument>
{
    public void Configure(EntityTypeBuilder<CompanyDocument> builder)
    {
        builder.ConfigureEntity("company_documents");
        builder.Property(d => d.Title).HasMaxLength(512).IsRequired();
        builder.Property(d => d.StoragePath).HasMaxLength(1024).IsRequired();
        builder.Property(d => d.EncryptedDataKey).HasMaxLength(1024).IsRequired();
        builder.Property(d => d.Sha256).HasMaxLength(64).IsRequired();
        builder.Property(d => d.ContentType).HasMaxLength(128);
        builder.Ignore(d => d.IsDeleted);
        builder.HasIndex(d => new { d.CompanyProfileId, d.Type });
        builder.HasQueryFilter(d => d.DeletedAt == null);
        builder.HasOne<CompanyProfile>().WithMany().HasForeignKey(d => d.CompanyProfileId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class QualificationReportConfiguration : IEntityTypeConfiguration<QualificationReport>
{
    public void Configure(EntityTypeBuilder<QualificationReport> builder)
    {
        builder.ConfigureEntity("qualification_reports");
        builder.Property(r => r.FindingsJson).IsJsonb().IsRequired();
        builder.Property(r => r.MissingMandatoryJson).IsJsonb().IsRequired();
        builder.Property(r => r.MissingDocumentsJson).IsJsonb().IsRequired();
        builder.Property(r => r.FunctionalityBreakdownJson).IsJsonb().IsRequired();
        builder.Property(r => r.QuestionsJson).IsJsonb().IsRequired();
        builder.Property(r => r.SummaryMarkdown).IsRequired();
        builder.Property(r => r.Model).HasMaxLength(128).IsRequired();
        builder.Property(r => r.PromptVersion).HasMaxLength(32).IsRequired();
        builder.Ignore(r => r.Findings);
        builder.Ignore(r => r.MissingMandatory);
        builder.Ignore(r => r.MissingDocuments);
        builder.Ignore(r => r.FunctionalityBreakdown);
        builder.Ignore(r => r.Questions);
        builder.HasIndex(r => new { r.TenderId, r.CompanyProfileId, r.CreatedAt });
        builder.HasOne<Domain.Tenders.Tender>().WithMany().HasForeignKey(r => r.TenderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CompanyProfile>().WithMany().HasForeignKey(r => r.CompanyProfileId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MunicipalityConfiguration : IEntityTypeConfiguration<Municipality>
{
    public void Configure(EntityTypeBuilder<Municipality> builder)
    {
        builder.ConfigureEntity("municipalities");
        builder.Property(m => m.Code).HasMaxLength(16).IsRequired();
        builder.Property(m => m.Name).HasMaxLength(256).IsRequired();
        builder.Property(m => m.NormalisedName).HasMaxLength(256).IsRequired();
        builder.Property(m => m.DistrictName).HasMaxLength(256);
        builder.HasIndex(m => m.Code).IsUnique();
        builder.HasIndex(m => m.NormalisedName);
    }
}
