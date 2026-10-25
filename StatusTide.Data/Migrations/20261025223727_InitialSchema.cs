using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace StatusTide.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    is_guest = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vendors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    feed_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    status_page_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    format = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    check_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    is_catalog = table.Column<bool>(type: "boolean", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    poll_interval_seconds = table.Column<int>(type: "integer", nullable: false),
                    next_poll_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_polled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "check_rollups",
                columns: table => new
                {
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hour_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    total_checks = table.Column<int>(type: "integer", nullable: false),
                    successful_checks = table.Column<int>(type: "integer", nullable: false),
                    uptime_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    average_response_time_ms = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_check_rollups", x => new { x.vendor_id, x.hour_start });
                    table.ForeignKey(
                        name: "fk_check_rollups_vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "checks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: true),
                    response_time_ms = table.Column<int>(type: "integer", nullable: true),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checks", x => x.id);
                    table.ForeignKey(
                        name: "fk_checks_vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "components",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_components", x => x.id);
                    table.ForeignKey(
                        name: "fk_components_vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "incidents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incidents", x => x.id);
                    table.ForeignKey(
                        name: "fk_incidents_vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vendor_verifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    endpoint_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    format = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    supports_conditional_requests = table.Column<bool>(type: "boolean", nullable: false),
                    terms_urls = table.Column<string[]>(type: "text[]", nullable: false),
                    clause = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    reviewer_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    reviewed_on = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_verifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_vendor_verifications_vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "follows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    component_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_in_app = table.Column<bool>(type: "boolean", nullable: false),
                    alert_email = table.Column<bool>(type: "boolean", nullable: false),
                    alert_webhook = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_follows", x => x.id);
                    table.ForeignKey(
                        name: "fk_follows_components_component_id",
                        column: x => x.component_id,
                        principalTable: "components",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_follows_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "alert_deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alert_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "fk_alert_deliveries_incidents_incident_id",
                        column: x => x.incident_id,
                        principalTable: "incidents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_alert_deliveries_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "incident_components",
                columns: table => new
                {
                    component_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incident_components", x => new { x.component_id, x.incident_id });
                    table.ForeignKey(
                        name: "fk_incident_components_components_component_id",
                        column: x => x.component_id,
                        principalTable: "components",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_incident_components_incidents_incident_id",
                        column: x => x.incident_id,
                        principalTable: "incidents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "incident_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    vendor_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incident_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_incident_events_incidents_incident_id",
                        column: x => x.incident_id,
                        principalTable: "incidents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "vendors",
                columns: new[] { "id", "check_url", "created_at", "feed_url", "format", "is_catalog", "is_enabled", "last_polled_at", "name", "next_poll_at", "poll_interval_seconds", "slug", "status_page_url" },
                values: new object[] { new Guid("0199f0a0-0000-7000-8000-00000000ac01"), null, new DateTimeOffset(new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "demo://acme-cloud", "Demo", true, true, null, "Acme Cloud (demo)", null, 60, "acme-cloud", "/vendors/acme-cloud" });

            migrationBuilder.InsertData(
                table: "components",
                columns: new[] { "id", "external_id", "name", "status", "status_changed_at", "vendor_id" },
                values: new object[,]
                {
                    { new Guid("0199f0a0-0000-7000-8000-00000000ac11"), "api", "API", "Operational", new DateTimeOffset(new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("0199f0a0-0000-7000-8000-00000000ac01") },
                    { new Guid("0199f0a0-0000-7000-8000-00000000ac12"), "dashboard", "Dashboard", "Operational", new DateTimeOffset(new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("0199f0a0-0000-7000-8000-00000000ac01") },
                    { new Guid("0199f0a0-0000-7000-8000-00000000ac13"), "webhooks", "Webhooks", "Operational", new DateTimeOffset(new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("0199f0a0-0000-7000-8000-00000000ac01") },
                    { new Guid("0199f0a0-0000-7000-8000-00000000ac14"), "database", "Managed Database", "Operational", new DateTimeOffset(new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("0199f0a0-0000-7000-8000-00000000ac01") }
                });

            migrationBuilder.InsertData(
                table: "vendor_verifications",
                columns: new[] { "id", "clause", "decision", "endpoint_url", "format", "reason", "reviewed_on", "reviewer_notes", "supports_conditional_requests", "terms_urls", "vendor_id" },
                values: new object[] { new Guid("0199f0a0-0000-7000-8000-00000000ac21"), null, "Approved", "demo://acme-cloud", "Demo", "Internal demo vendor that StatusTide controls. No third-party terms apply.", new DateOnly(2026, 10, 25), null, false, new string[0], new Guid("0199f0a0-0000-7000-8000-00000000ac01") });

            migrationBuilder.CreateIndex(
                name: "ix_alert_deliveries_incident_id",
                table: "alert_deliveries",
                column: "incident_id");

            migrationBuilder.CreateIndex(
                name: "ix_alert_deliveries_user_id_incident_id_channel_kind",
                table: "alert_deliveries",
                columns: new[] { "user_id", "incident_id", "channel", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_checks_checked_at",
                table: "checks",
                column: "checked_at");

            migrationBuilder.CreateIndex(
                name: "ix_checks_vendor_id_checked_at",
                table: "checks",
                columns: new[] { "vendor_id", "checked_at" });

            migrationBuilder.CreateIndex(
                name: "ix_components_vendor_id_external_id",
                table: "components",
                columns: new[] { "vendor_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_follows_component_id",
                table: "follows",
                column: "component_id");

            migrationBuilder.CreateIndex(
                name: "ix_follows_user_id_component_id",
                table: "follows",
                columns: new[] { "user_id", "component_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_incident_components_incident_id",
                table: "incident_components",
                column: "incident_id");

            migrationBuilder.CreateIndex(
                name: "ix_incident_events_incident_id_occurred_at",
                table: "incident_events",
                columns: new[] { "incident_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_incidents_vendor_id_external_id",
                table: "incidents",
                columns: new[] { "vendor_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_incidents_vendor_id_open",
                table: "incidents",
                column: "vendor_id",
                filter: "resolved_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_incidents_vendor_id_started_at",
                table: "incidents",
                columns: new[] { "vendor_id", "started_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_last_seen_at",
                table: "users",
                column: "last_seen_at",
                filter: "is_guest");

            migrationBuilder.CreateIndex(
                name: "ix_vendor_verifications_vendor_id_reviewed_on",
                table: "vendor_verifications",
                columns: new[] { "vendor_id", "reviewed_on" });

            migrationBuilder.CreateIndex(
                name: "ix_vendors_feed_url",
                table: "vendors",
                column: "feed_url",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vendors_next_poll_at",
                table: "vendors",
                column: "next_poll_at",
                filter: "is_enabled");

            migrationBuilder.CreateIndex(
                name: "ix_vendors_slug",
                table: "vendors",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alert_deliveries");

            migrationBuilder.DropTable(
                name: "check_rollups");

            migrationBuilder.DropTable(
                name: "checks");

            migrationBuilder.DropTable(
                name: "follows");

            migrationBuilder.DropTable(
                name: "incident_components");

            migrationBuilder.DropTable(
                name: "incident_events");

            migrationBuilder.DropTable(
                name: "vendor_verifications");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "components");

            migrationBuilder.DropTable(
                name: "incidents");

            migrationBuilder.DropTable(
                name: "vendors");
        }
    }
}
