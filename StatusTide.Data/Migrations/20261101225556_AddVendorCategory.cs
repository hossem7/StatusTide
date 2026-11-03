using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StatusTide.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "category",
                table: "vendors",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "vendors",
                keyColumn: "id",
                keyValue: new Guid("0199f0a0-0000-7000-8000-00000000ac01"),
                column: "category",
                value: "Cloud");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "category",
                table: "vendors");
        }
    }
}
