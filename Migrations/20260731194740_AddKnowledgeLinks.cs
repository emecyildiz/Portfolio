using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KnowledgeUrl",
                table: "SecurityResearches",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KnowledgeUrl",
                table: "Projects",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KnowledgeUrl",
                table: "HomelabPosts",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KnowledgeUrl",
                table: "SecurityResearches");

            migrationBuilder.DropColumn(
                name: "KnowledgeUrl",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "KnowledgeUrl",
                table: "HomelabPosts");
        }
    }
}
