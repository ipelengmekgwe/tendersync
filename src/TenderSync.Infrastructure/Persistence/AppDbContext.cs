using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Domain.Applications;
using TenderSync.Domain.Categories;
using TenderSync.Domain.Companies;
using TenderSync.Domain.Geography;
using TenderSync.Domain.Notifications;
using TenderSync.Domain.Operations;
using TenderSync.Domain.Qualification;
using TenderSync.Domain.Tenders;
using TenderSync.Infrastructure.Identity;
using TenderSync.Infrastructure.Persistence.Repositories;

namespace TenderSync.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the write model plus ASP.NET Identity. Tables live in the <see cref="Schema"/> schema with
/// snake_case names (EFCore.NamingConventions). Implements <see cref="IAppDbContext"/> through lazily created repositories.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options), IAppDbContext
{
    public const string Schema = "tendersync";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    private TenderRepository? _tenders;
    private BuyerRepository? _buyers;
    private InterestCategoryRepository? _interestCategories;
    private CompanyProfileRepository? _companyProfiles;
    private EfRepository<CompanyDocument>? _companyDocuments;
    private TenderMatchRepository? _tenderMatches;
    private EfRepository<TenderRequirement>? _tenderRequirements;
    private QualificationReportRepository? _qualificationReports;
    private NotificationRepository? _notifications;
    private NotificationPreferenceRepository? _notificationPreferences;
    private OutboxRepository? _outbox;
    private EfRepository<BidApplication>? _applications;
    private EfRepository<ApplicationDocument>? _applicationDocuments;
    private EfRepository<LlmCall>? _llmCalls;
    private EfRepository<JobRun>? _jobRuns;
    private EfRepository<SourceHealthCheck>? _sourceHealthChecks;
    private EfRepository<AuditLog>? _auditLogs;
    private MunicipalityRepository? _municipalities;

    public DbSet<Buyer> BuyerSet => Set<Buyer>();

    public DbSet<Tender> TenderSet => Set<Tender>();

    public DbSet<TenderRelease> TenderReleaseSet => Set<TenderRelease>();

    public DbSet<TenderDocument> TenderDocumentSet => Set<TenderDocument>();

    public DbSet<DocumentText> DocumentTextSet => Set<DocumentText>();

    public DbSet<TenderStatusHistory> TenderStatusHistorySet => Set<TenderStatusHistory>();

    public DbSet<TenderClassification> TenderClassificationSet => Set<TenderClassification>();

    public DbSet<TenderMatch> TenderMatchSet => Set<TenderMatch>();

    public DbSet<TenderRequirement> TenderRequirementSet => Set<TenderRequirement>();

    public DbSet<InterestCategory> InterestCategorySet => Set<InterestCategory>();

    public DbSet<CompanyProfile> CompanyProfileSet => Set<CompanyProfile>();

    public DbSet<CompanyDocument> CompanyDocumentSet => Set<CompanyDocument>();

    public DbSet<QualificationReport> QualificationReportSet => Set<QualificationReport>();

    public DbSet<Notification> NotificationSet => Set<Notification>();

    public DbSet<NotificationPreference> NotificationPreferenceSet => Set<NotificationPreference>();

    public DbSet<OutboxMessage> OutboxMessageSet => Set<OutboxMessage>();

    public DbSet<BidApplication> ApplicationSet => Set<BidApplication>();

    public DbSet<ApplicationDocument> ApplicationDocumentSet => Set<ApplicationDocument>();

    public DbSet<LlmCall> LlmCallSet => Set<LlmCall>();

    public DbSet<JobRun> JobRunSet => Set<JobRun>();

    public DbSet<SourceHealthCheck> SourceHealthCheckSet => Set<SourceHealthCheck>();

    public DbSet<AuditLog> AuditLogSet => Set<AuditLog>();

    public DbSet<Municipality> MunicipalitySet => Set<Municipality>();

    public DbSet<RefreshToken> RefreshTokenSet => Set<RefreshToken>();

    public ITenderRepository Tenders => _tenders ??= new TenderRepository(this);

    public IBuyerRepository Buyers => _buyers ??= new BuyerRepository(this);

    public IInterestCategoryRepository InterestCategories => _interestCategories ??= new InterestCategoryRepository(this);

    public ICompanyProfileRepository CompanyProfiles => _companyProfiles ??= new CompanyProfileRepository(this);

    public IRepository<CompanyDocument> CompanyDocuments => _companyDocuments ??= new EfRepository<CompanyDocument>(this);

    public ITenderMatchRepository TenderMatches => _tenderMatches ??= new TenderMatchRepository(this);

    public IRepository<TenderRequirement> TenderRequirements => _tenderRequirements ??= new EfRepository<TenderRequirement>(this);

    public IQualificationReportRepository QualificationReports => _qualificationReports ??= new QualificationReportRepository(this);

    public INotificationRepository Notifications => _notifications ??= new NotificationRepository(this);

    public INotificationPreferenceRepository NotificationPreferences => _notificationPreferences ??= new NotificationPreferenceRepository(this);

    public IOutboxRepository Outbox => _outbox ??= new OutboxRepository(this);

    public IRepository<BidApplication> Applications => _applications ??= new EfRepository<BidApplication>(this);

    public IRepository<ApplicationDocument> ApplicationDocuments => _applicationDocuments ??= new EfRepository<ApplicationDocument>(this);

    public IRepository<LlmCall> LlmCalls => _llmCalls ??= new EfRepository<LlmCall>(this);

    public IRepository<JobRun> JobRuns => _jobRuns ??= new EfRepository<JobRun>(this);

    public IRepository<SourceHealthCheck> SourceHealthChecks => _sourceHealthChecks ??= new EfRepository<SourceHealthCheck>(this);

    public IRepository<AuditLog> AuditLogs => _auditLogs ??= new EfRepository<AuditLog>(this);

    public IMunicipalityRepository Municipalities => _municipalities ??= new MunicipalityRepository(this);

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        builder.Entity<ApplicationUser>().ToTable("users");
        builder.Entity<ApplicationRole>().ToTable("roles");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityRoleClaim<Guid>>().ToTable("role_claims");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<Guid>>().ToTable("user_tokens");
    }
}
