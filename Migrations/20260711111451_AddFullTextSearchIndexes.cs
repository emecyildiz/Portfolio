using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Migrations
{
    /// <inheritdoc />
    public partial class AddFullTextSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS idx_security_fts 
                ON ""SecurityResearches"" 
                USING GIN (to_tsvector('turkish', ""Title"" || ' ' || ""Summary""));

                CREATE INDEX IF NOT EXISTS idx_projects_fts 
                ON ""Projects"" 
                USING GIN (to_tsvector('turkish', ""Title"" || ' ' || ""Summary""));

                CREATE INDEX IF NOT EXISTS idx_homelab_fts 
                ON ""HomelabPosts"" 
                USING GIN (to_tsvector('turkish', ""Title"" || ' ' || ""Summary""));

                CREATE INDEX IF NOT EXISTS idx_blog_fts 
                ON ""BlogPosts"" 
                USING GIN (to_tsvector('turkish', ""Title"" || ' ' || ""Summary""));

                CREATE INDEX IF NOT EXISTS idx_team_fts 
                ON ""TeamProjects"" 
                USING GIN (to_tsvector('turkish', ""Title"" || ' ' || ""Summary""));

                CREATE INDEX IF NOT EXISTS idx_pages_fts 
                ON ""Pages"" 
                USING GIN (to_tsvector('turkish', ""Title"" || ' ' || ""Content""));
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS idx_security_fts;
                DROP INDEX IF EXISTS idx_projects_fts;
                DROP INDEX IF EXISTS idx_homelab_fts;
                DROP INDEX IF EXISTS idx_blog_fts;
                DROP INDEX IF EXISTS idx_team_fts;
                DROP INDEX IF EXISTS idx_pages_fts;
            ");
        }
    }
}
