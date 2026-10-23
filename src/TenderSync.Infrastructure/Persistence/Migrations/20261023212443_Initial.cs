using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TenderSync.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tendersync");

            migrationBuilder.CreateTable(
                name: "audit_logs",
                schema: "tendersync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    before_json = table.Column<string>(type: "jsonb", nullable: true),
                    after_json = table.Column<string>(type: "jsonb", nullable: true),
                    ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "buyers",
                schema: "tendersync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    province = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_buyers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "company_profiles",
                schema: "tendersync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    trading_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    registration_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    vat_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    csd_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    csd_expiry = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tax_pin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    tax_pin_expiry = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    bbbee_level = table.Column<int>(type: "integer", nullable: true),
                    bbbee_expiry = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    bbbee_certificate_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    cidb_grade = table.Column<int>(type: "integer", nullable: true),
                    cidb_classes = table.Column<string[]>(type: "text[]", nullable: false),
                    provinces = table.Column<int[]>(type: "integer[]", nullable: false),
                    years_trading = table.Column<int>(type: "integer", nullable: false),
                    employee_count = table.Column<int>(type: "integer", nullable: true),
                    annual_turnover = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    capabilities_json = table.Column<string>(type: "jsonb", nullable: false),
                    contact_json = table.Column<string>(type: "jsonb", nullable: true),
                    address_json = table.Column<string>(type: "jsonb", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "interest_categories",
                schema: "tendersync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    keywords = table.Column<string[]>(type: "text[]", nullable: false),
                    negative_keywords = table.Column<string[]>(type: "text[]", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    weight = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_interest_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "job_runs",
                schema: "tendersync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    success = table.Column<bool>(type: "boolean", nullable: true),
                    items_processed = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    details_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "llm_calls",
                schema: "tendersync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    tender_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tokens_in = table.Column<int>(type: "integer", nullable: false),
                    tokens_out = table.Column<int>(type: "integer", nullable: false),
                    cache_read_tokens = table.Column<int>(type: "integer", nullable: false),
                    cost_usd = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    success = table.Column<bool>(type: "boolean", nullable: false),
                    error = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_llm_calls", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "municipalities",
                schema: "tendersync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    normalised_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    province = table.Column<int>(type: "integer", nullable: false),
                    district_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_municipalities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_preferences",
                schema: "tendersync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    address = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    types = table.Column<int[]>(type: "integer[]", nullable: false),
                    quiet_hours_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_preferences", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "tendersync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dedupe_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    recipient = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    tender_id = table.Column<Guid>(type: "uuid", nullable: true),
                    company_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    payload_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "tendersync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "tendersync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "source_health",
                schema: "tendersync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ok = table.Column<bool>(type: "boolean", nullable: false),
                    http_status = table.Column<int>(type: "integer", nullable: true),
                    latency_ms = table.Column<int>(type: "integer", nullable: true),
                    schema_drift_json = table.Column<string>(type: "jsonb", nullable: true),
                    error = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_source_health", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "tendersync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenders",
                schema: "tendersync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ocid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    external_tender_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    bid_number = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    title = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    buyer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    province = table.Column<int>(type: "integer", nullable: false),
                    main_category = table.Column<int>(type: "integer", nullable: false),
                    additional_categories = table.Column<string[]>(type: "text[]", nullable: false),
                    procurement_method = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    procurement_method_details = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    value_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    value_currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closing_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    briefing_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    briefing_compulsory = table.Column<bool>(type: "boolean", nullable: true),
                    briefing_venue = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    submission_method = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    status_reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    source_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    source_kind = table.Column<int>(type: "integer", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    has_awards = table.Column<bool>(type: "boolean", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenders", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenders_buyers_buyer_id",
                        column: x => x.buyer_id,
                        principalSchema: "tendersync",
                        principalTable: "buyers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "company_documents",
                schema: "tendersync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    storage_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    encrypted_data_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    content_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_company_documents_company_profile_set_company_profile_id",
                        column: x => x.company_profile_id,
                        principalSchema: "tendersync",
                        principalTable: "company_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_claims",
                schema: "tendersync",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_role_claims_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "tendersync",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_claims",
                schema: "tendersync",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_claims_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "tendersync",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_logins",
                schema: "tendersync",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_user_logins_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "tendersync",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                schema: "tendersync",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "tendersync",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_roles_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "tendersync",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                schema: "tendersync",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_user_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "tendersync",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "applications",
                schema: "tendersync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    checklist_json = table.Column<string>(type: "jsonb", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    submitted_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    submission_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_applications", x => x.id);
                    table.ForeignKey(
                        name: "fk_applications_company_profile_set_company_profile_id",
                        column: x => x.company_profile_id,
                        principalSchema: "tendersync",
                        principalTable: "company_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_applications_tender_set_tender_id",
                        column: x => x.tender_id,
                        principalSchema: "tendersync",
                        principalTable: "tenders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "qualification_reports",
                schema: "tendersync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    verdict = table.Column<int>(type: "integer", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    findings_json = table.Column<string>(type: "jsonb", nullable: false),
                    missing_mandatory_json = table.Column<string>(type: "jsonb", nullable: false),
                    missing_documents_json = table.Column<string>(type: "jsonb", nullable: false),
                    estimated_functionality_score = table.Column<int>(type: "integer", nullable: true),
                    functionality_breakdown_json = table.Column<string>(type: "jsonb", nullable: false),
                    questions_json = table.Column<string>(type: "jsonb", nullable: false),
                    summary_markdown = table.Column<string>(type: "text", nullable: false),
                    model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_qualification_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_qualification_reports_company_profiles_company_profile_id",
                        column: x => x.company_profile_id,
                        principalSchema: "tendersync",
                        principalTable: "company_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_qualification_reports_tender_set_tender_id",
                        column: x => x.tender_id,
                        principalSchema: "tendersync",
                        principalTable: "tenders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tender_classifications",
                schema: "tendersync",
                columns: table => new
                {
                    tender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage = table.Column<int>(type: "integer", nullable: false),
                    category_codes = table.Column<string[]>(type: "text[]", nullable: false),
                    relevance = table.Column<int>(type: "integer", nullable: false),
                    rationale = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    prompt_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    tokens_in = table.Column<int>(type: "integer", nullable: false),
                    tokens_out = table.Column<int>(type: "integer", nullable: false),
                    classified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tender_classifications", x => x.tender_id);
                    table.ForeignKey(
                        name: "fk_tender_classifications_tender_set_tender_id",
                        column: x => x.tender_id,
                        principalSchema: "tendersync",
                        principalTable: "tenders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tender_documents",
                schema: "tendersync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    title = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    format = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    date_published = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    date_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    storage_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    page_count = table.Column<int>(type: "integer", nullable: true),
                    needs_ocr = table.Column<bool>(type: "boolean", nullable: false),
                    downloaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    extracted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tender_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_tender_documents_tenders_tender_id",
                        column: x => x.tender_id,
                        principalSchema: "tendersync",
                        principalTable: "tenders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tender_matches",
                schema: "tendersync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    relevance = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dismissed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dismiss_reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tender_matches", x => x.id);
                    table.ForeignKey(
                        name: "fk_tender_matches_company_profiles_company_profile_id",
                        column: x => x.company_profile_id,
                        principalSchema: "tendersync",
                        principalTable: "company_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_tender_matches_tenders_tender_id",
                        column: x => x.tender_id,
                        principalSchema: "tendersync",
                        principalTable: "tenders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tender_releases",
                schema: "tendersync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    release_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    release_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tag = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    raw_json = table.Column<string>(type: "jsonb", nullable: false),
                    hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tender_releases", x => x.id);
                    table.ForeignKey(
                        name: "fk_tender_releases_tenders_tender_id",
                        column: x => x.tender_id,
                        principalSchema: "tendersync",
                        principalTable: "tenders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tender_requirements",
                schema: "tendersync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    value_json = table.Column<string>(type: "jsonb", nullable: true),
                    mandatory = table.Column<bool>(type: "boolean", nullable: false),
                    weight = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    threshold = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_page = table.Column<int>(type: "integer", nullable: true),
                    source_quote = table.Column<string>(type: "text", nullable: true),
                    confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    extracted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tender_requirements", x => x.id);
                    table.ForeignKey(
                        name: "fk_tender_requirements_tenders_tender_id",
                        column: x => x.tender_id,
                        principalSchema: "tendersync",
                        principalTable: "tenders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tender_status_history",
                schema: "tendersync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<int>(type: "integer", nullable: false),
                    to_status = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tender_status_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_tender_status_history_tenders_tender_id",
                        column: x => x.tender_id,
                        principalSchema: "tendersync",
                        principalTable: "tenders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "application_documents",
                schema: "tendersync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    storage_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    generated = table.Column<bool>(type: "boolean", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_application_documents_application_set_application_id",
                        column: x => x.application_id,
                        principalSchema: "tendersync",
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "document_texts",
                schema: "tendersync",
                columns: table => new
                {
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    page = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    char_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_texts", x => new { x.document_id, x.page });
                    table.ForeignKey(
                        name: "fk_document_texts_tender_document_set_document_id",
                        column: x => x.document_id,
                        principalSchema: "tendersync",
                        principalTable: "tender_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_application_documents_application_id",
                schema: "tendersync",
                table: "application_documents",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_applications_company_profile_id",
                schema: "tendersync",
                table: "applications",
                column: "company_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_applications_tender_id_company_profile_id",
                schema: "tendersync",
                table: "applications",
                columns: new[] { "tender_id", "company_profile_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_at",
                schema: "tendersync",
                table: "audit_logs",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_entity_type_entity_id",
                schema: "tendersync",
                table: "audit_logs",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_buyers_external_id",
                schema: "tendersync",
                table: "buyers",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_buyers_name",
                schema: "tendersync",
                table: "buyers",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_company_documents_company_profile_id_type",
                schema: "tendersync",
                table: "company_documents",
                columns: new[] { "company_profile_id", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_interest_categories_code",
                schema: "tendersync",
                table: "interest_categories",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_runs_job_name_started_at",
                schema: "tendersync",
                table: "job_runs",
                columns: new[] { "job_name", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_llm_calls_at",
                schema: "tendersync",
                table: "llm_calls",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_llm_calls_tender_id",
                schema: "tendersync",
                table: "llm_calls",
                column: "tender_id");

            migrationBuilder.CreateIndex(
                name: "ix_municipalities_code",
                schema: "tendersync",
                table: "municipalities",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_municipalities_normalised_name",
                schema: "tendersync",
                table: "municipalities",
                column: "normalised_name");

            migrationBuilder.CreateIndex(
                name: "ix_notification_preferences_user_id_channel",
                schema: "tendersync",
                table: "notification_preferences",
                columns: new[] { "user_id", "channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_dedupe_key",
                schema: "tendersync",
                table: "notifications",
                column: "dedupe_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_status_created_at",
                schema: "tendersync",
                table: "notifications",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_tender_id",
                schema: "tendersync",
                table: "notifications",
                column: "tender_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_occurred_at",
                schema: "tendersync",
                table: "outbox_messages",
                columns: new[] { "processed_at", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_qualification_reports_company_profile_id",
                schema: "tendersync",
                table: "qualification_reports",
                column: "company_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_qualification_reports_tender_id_company_profile_id_created_",
                schema: "tendersync",
                table: "qualification_reports",
                columns: new[] { "tender_id", "company_profile_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_role_claims_role_id",
                schema: "tendersync",
                table: "role_claims",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "tendersync",
                table: "roles",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_source_health_source_checked_at",
                schema: "tendersync",
                table: "source_health",
                columns: new[] { "source", "checked_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tender_classifications_relevance",
                schema: "tendersync",
                table: "tender_classifications",
                column: "relevance");

            migrationBuilder.CreateIndex(
                name: "ix_tender_documents_status",
                schema: "tendersync",
                table: "tender_documents",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_tender_documents_tender_id_external_id",
                schema: "tendersync",
                table: "tender_documents",
                columns: new[] { "tender_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tender_matches_company_profile_id_status",
                schema: "tendersync",
                table: "tender_matches",
                columns: new[] { "company_profile_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_tender_matches_tender_id_company_profile_id",
                schema: "tendersync",
                table: "tender_matches",
                columns: new[] { "tender_id", "company_profile_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tender_releases_tender_id_hash",
                schema: "tendersync",
                table: "tender_releases",
                columns: new[] { "tender_id", "hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tender_requirements_tender_id_kind",
                schema: "tendersync",
                table: "tender_requirements",
                columns: new[] { "tender_id", "kind" });

            migrationBuilder.CreateIndex(
                name: "ix_tender_status_history_tender_id_at",
                schema: "tendersync",
                table: "tender_status_history",
                columns: new[] { "tender_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_tenders_buyer_id",
                schema: "tendersync",
                table: "tenders",
                column: "buyer_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenders_closing_at",
                schema: "tendersync",
                table: "tenders",
                column: "closing_at");

            migrationBuilder.CreateIndex(
                name: "ix_tenders_ocid",
                schema: "tendersync",
                table: "tenders",
                column: "ocid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenders_published_at",
                schema: "tendersync",
                table: "tenders",
                column: "published_at");

            migrationBuilder.CreateIndex(
                name: "ix_tenders_status_closing_at",
                schema: "tendersync",
                table: "tenders",
                columns: new[] { "status", "closing_at" });

            migrationBuilder.CreateIndex(
                name: "ix_user_claims_user_id",
                schema: "tendersync",
                table: "user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_logins_user_id",
                schema: "tendersync",
                table: "user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_role_id",
                schema: "tendersync",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "tendersync",
                table: "users",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "tendersync",
                table: "users",
                column: "normalized_user_name",
                unique: true);

            // Full-text search column and index (PLAN.md §6). Not part of the EF model; queried via IReadDb.
            migrationBuilder.Sql(
                """
                ALTER TABLE tendersync.tenders ADD COLUMN search_vector tsvector GENERATED ALWAYS AS (
                    setweight(to_tsvector('english', coalesce(bid_number, '')), 'A') ||
                    setweight(to_tsvector('english', coalesce(title, '')), 'A') ||
                    setweight(to_tsvector('english', coalesce(description, '')), 'B')) STORED;
                CREATE INDEX ix_tenders_search_vector ON tendersync.tenders USING GIN (search_vector);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS tendersync.ix_tenders_search_vector; ALTER TABLE tendersync.tenders DROP COLUMN IF EXISTS search_vector;");

            migrationBuilder.DropTable(
                name: "application_documents",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "audit_logs",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "company_documents",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "document_texts",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "interest_categories",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "job_runs",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "llm_calls",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "municipalities",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "notification_preferences",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "qualification_reports",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "role_claims",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "source_health",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "tender_classifications",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "tender_matches",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "tender_releases",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "tender_requirements",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "tender_status_history",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "user_claims",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "user_logins",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "user_roles",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "user_tokens",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "applications",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "tender_documents",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "users",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "company_profiles",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "tenders",
                schema: "tendersync");

            migrationBuilder.DropTable(
                name: "buyers",
                schema: "tendersync");
        }
    }
}
