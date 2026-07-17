using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicCurrentFocusSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrentFocusTitle",
                table: "SiteSettings",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrentFocusUrl",
                table: "SiteSettings",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowCurrentFocus",
                table: "SiteSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentFocusTitle",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "CurrentFocusUrl",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "ShowCurrentFocus",
                table: "SiteSettings");
        }
    }
}
