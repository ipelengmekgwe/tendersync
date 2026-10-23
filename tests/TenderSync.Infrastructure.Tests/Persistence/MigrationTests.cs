using Microsoft.Extensions.DependencyInjection;
using TenderSync.Application.Abstractions.Persistence;

namespace TenderSync.Infrastructure.Tests.Persistence;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class MigrationTests(PostgresFixture fixture)
{
    private static readonly string[] ExpectedTables =
    [
        "buyers", "tenders", "tender_releases", "tender_status_history", "tender_documents", "document_texts",
        "interest_categories", "tender_classifications", "company_profiles", "company_documents", "tender_matches",
        "tender_requirements", "qualification_reports", "notifications", "notification_preferences", "outbox_messages",
        "applications", "application_documents", "llm_calls", "job_runs", "source_health", "audit_logs", "municipalities",
        "users", "roles", "user_roles", "user_claims", "user_logins", "role_claims", "user_tokens",
        "__ef_migrations_history",
    ];

    [Fact]
    public async Task Every_table_from_the_plan_exists_in_the_tendersync_schema()
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IReadDb>();

        var tables = await db.QueryAsync<string>("select table_name from information_schema.tables where table_schema = 'tendersync'");

        tables.Should().Contain(ExpectedTables);
    }

    [Fact]
    public async Task Tenders_have_a_generated_search_vector_with_a_gin_index()
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IReadDb>();

        var column = await db.QueryFirstOrDefaultAsync<string>(
            "select data_type from information_schema.columns where table_schema = 'tendersync' and table_name = 'tenders' and column_name = 'search_vector'");
        var index = await db.QueryFirstOrDefaultAsync<string>(
            "select indexdef from pg_indexes where schemaname = 'tendersync' and tablename = 'tenders' and indexname = 'ix_tenders_search_vector'");

        column.Should().Be("tsvector");
        index.Should().Contain("USING gin");
    }

    [Fact]
    public async Task Unique_constraints_from_the_plan_are_present()
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IReadDb>();

        var uniqueIndexes = await db.QueryAsync<string>(
            "select indexname from pg_indexes where schemaname = 'tendersync' and indexdef like 'CREATE UNIQUE INDEX%'");

        uniqueIndexes.Should().Contain(["ix_tenders_ocid", "ix_buyers_external_id", "ix_interest_categories_code", "ix_tender_matches_tender_id_company_profile_id", "ix_notifications_dedupe_key", "ix_tender_documents_tender_id_external_id", "ix_tender_releases_tender_id_hash", "ix_municipalities_code"]);
    }
}
