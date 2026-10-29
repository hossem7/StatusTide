using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StatusTide.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGuestToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "guest_token_hash",
                table: "users",
                type: "bytea",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_guest_token_hash",
                table: "users",
                column: "guest_token_hash",
                unique: true,
                filter: "guest_token_hash IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_guest_token_hash",
                table: "users");

            migrationBuilder.DropColumn(
                name: "guest_token_hash",
                table: "users");
        }
    }
}
