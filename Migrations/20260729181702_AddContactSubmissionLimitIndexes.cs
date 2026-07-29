using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Migrations
{
    /// <inheritdoc />
    public partial class AddContactSubmissionLimitIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "idx_contact_messages_created_at",
                table: "ContactMessages",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "idx_contact_messages_ip_created_at",
                table: "ContactMessages",
                columns: new[] { "IpAddress", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_contact_messages_created_at",
                table: "ContactMessages");

            migrationBuilder.DropIndex(
                name: "idx_contact_messages_ip_created_at",
                table: "ContactMessages");
        }
    }
}
