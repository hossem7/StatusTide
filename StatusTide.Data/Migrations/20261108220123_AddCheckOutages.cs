using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StatusTide.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckOutages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "check_outages",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_check_outages", x => x.id);
                    table.ForeignKey(
                        name: "fk_check_outages_vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "vendors",
                keyColumn: "id",
                keyValue: new Guid("0199f0a0-0000-7000-8000-00000000ac01"),
                column: "check_url",
                value: "demo://acme-cloud/health");

            migrationBuilder.CreateIndex(
                name: "ix_check_outages_vendor_id",
                table: "check_outages",
                column: "vendor_id",
                unique: true,
                filter: "resolved_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_check_outages_vendor_id_detected_at",
                table: "check_outages",
                columns: new[] { "vendor_id", "detected_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "check_outages");

            migrationBuilder.UpdateData(
                table: "vendors",
                keyColumn: "id",
                keyValue: new Guid("0199f0a0-0000-7000-8000-00000000ac01"),
                column: "check_url",
                value: null);
        }
    }
}
