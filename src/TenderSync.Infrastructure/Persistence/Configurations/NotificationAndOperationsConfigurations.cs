using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderSync.Domain.Applications;
using TenderSync.Domain.Notifications;
using TenderSync.Domain.Operations;

namespace TenderSync.Infrastructure.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ConfigureEntity("notifications");
        builder.Property(n => n.DedupeKey).HasMaxLength(512).IsRequired();
        builder.Property(n => n.Recipient).HasMaxLength(512).IsRequired();
        builder.Property(n => n.Subject).HasMaxLength(1024).IsRequired();
        builder.Property(n => n.Body).IsRequired();
        builder.Property(n => n.Error).HasMaxLength(2048);
        builder.Property(n => n.PayloadJson).IsJsonb();
        builder.HasIndex(n => n.DedupeKey).IsUnique();
        builder.HasIndex(n => new { n.Status, n.CreatedAt });
        builder.HasIndex(n => n.TenderId);
    }
}

internal sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ConfigureEntity("notification_preferences");
        builder.Property(p => p.Address).HasMaxLength(1024).IsRequired();
        builder.Property(p => p.Types).HasEnumArrayConversion();
        builder.Property(p => p.QuietHoursJson).IsJsonb();
        builder.HasIndex(p => new { p.UserId, p.Channel }).IsUnique();
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ConfigureEntity("outbox_messages");
        builder.Property(m => m.Type).HasMaxLength(256).IsRequired();
        builder.Property(m => m.PayloadJson).IsJsonb().IsRequired();
        builder.Property(m => m.Error).HasMaxLength(4000);
        builder.HasIndex(m => new { m.ProcessedAt, m.OccurredAt });
    }
}

internal sealed class BidApplicationConfiguration : IEntityTypeConfiguration<BidApplication>
{
    public void Configure(EntityTypeBuilder<BidApplication> builder)
    {
        builder.ConfigureEntity("applications");
        builder.Property(a => a.ChecklistJson).IsJsonb().IsRequired();
        builder.Property(a => a.SubmittedBy).HasMaxLength(256);
        builder.Property(a => a.SubmissionReference).HasMaxLength(256);
        builder.HasIndex(a => new { a.TenderId, a.CompanyProfileId });
        builder.HasOne<Domain.Tenders.Tender>().WithMany().HasForeignKey(a => a.TenderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Domain.Companies.CompanyProfile>().WithMany().HasForeignKey(a => a.CompanyProfileId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ApplicationDocumentConfiguration : IEntityTypeConfiguration<ApplicationDocument>
{
    public void Configure(EntityTypeBuilder<ApplicationDocument> builder)
    {
        builder.ConfigureEntity("application_documents");
        builder.Property(d => d.Title).HasMaxLength(512).IsRequired();
        builder.Property(d => d.StoragePath).HasMaxLength(1024).IsRequired();
        builder.Property(d => d.ApprovedBy).HasMaxLength(256);
        builder.HasIndex(d => d.ApplicationId);
        builder.HasOne<BidApplication>().WithMany().HasForeignKey(d => d.ApplicationId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class LlmCallConfiguration : IEntityTypeConfiguration<LlmCall>
{
    public void Configure(EntityTypeBuilder<LlmCall> builder)
    {
        builder.ConfigureEntity("llm_calls");
        builder.Property(c => c.Purpose).HasMaxLength(64).IsRequired();
        builder.Property(c => c.Provider).HasMaxLength(32).IsRequired();
        builder.Property(c => c.Model).HasMaxLength(128).IsRequired();
        builder.Property(c => c.PromptVersion).HasMaxLength(32);
        builder.Property(c => c.CostUsd).HasPrecision(12, 6);
        builder.Property(c => c.Error).HasMaxLength(2048);
        builder.HasIndex(c => c.At);
        builder.HasIndex(c => c.TenderId);
    }
}

internal sealed class JobRunConfiguration : IEntityTypeConfiguration<JobRun>
{
    public void Configure(EntityTypeBuilder<JobRun> builder)
    {
        builder.ConfigureEntity("job_runs");
        builder.Property(j => j.JobName).HasMaxLength(128).IsRequired();
        builder.Property(j => j.Error).HasMaxLength(4000);
        builder.Property(j => j.DetailsJson).IsJsonb();
        builder.HasIndex(j => new { j.JobName, j.StartedAt });
    }
}

internal sealed class SourceHealthCheckConfiguration : IEntityTypeConfiguration<SourceHealthCheck>
{
    public void Configure(EntityTypeBuilder<SourceHealthCheck> builder)
    {
        builder.ConfigureEntity("source_health");
        builder.Property(s => s.Source).HasMaxLength(64).IsRequired();
        builder.Property(s => s.SchemaDriftJson).IsJsonb();
        builder.Property(s => s.Error).HasMaxLength(2048);
        builder.HasIndex(s => new { s.Source, s.CheckedAt });
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ConfigureEntity("audit_logs");
        builder.Property(a => a.ActorName).HasMaxLength(256).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(128).IsRequired();
        builder.Property(a => a.EntityType).HasMaxLength(128).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(128);
        builder.Property(a => a.BeforeJson).IsJsonb();
        builder.Property(a => a.AfterJson).IsJsonb();
        builder.Property(a => a.Ip).HasMaxLength(64);
        builder.HasIndex(a => a.At);
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
    }
}
