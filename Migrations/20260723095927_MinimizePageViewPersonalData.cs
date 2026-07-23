using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Migrations
{
    /// <inheritdoc />
    public partial class MinimizePageViewPersonalData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing rows contain raw IP addresses and city data. Delete them instead
            // of carrying those identifiers into the privacy-minimized schema.
            migrationBuilder.Sql("""DELETE FROM "PageViews";""");

            migrationBuilder.DropColumn(
                name: "City",
                table: "PageViews");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "PageViews");

            migrationBuilder.AlterColumn<string>(
                name: "Path",
                table: "PageViews",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Country",
                table: "PageViews",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ViewDate",
                table: "PageViews",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<string>(
                name: "VisitorHash",
                table: "PageViews",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ux_pageviews_visitor_date",
                table: "PageViews",
                columns: new[] { "VisitorHash", "ViewDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_pageviews_visitor_date",
                table: "PageViews");

            migrationBuilder.DropColumn(
                name: "ViewDate",
                table: "PageViews");

            migrationBuilder.DropColumn(
                name: "VisitorHash",
                table: "PageViews");

            migrationBuilder.AlterColumn<string>(
                name: "Path",
                table: "PageViews",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048);

            migrationBuilder.AlterColumn<string>(
                name: "Country",
                table: "PageViews",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "PageViews",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "PageViews",
                type: "text",
                nullable: true);
        }
    }
}
