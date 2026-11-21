using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContentVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DisplayAllContent",
                table: "AppUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ContentVisibilityRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TagGroupKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentVisibilityRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentVisibilityRuleTags",
                columns: table => new
                {
                    ContentVisibilityRuleId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentVisibilityRuleTags", x => new { x.ContentVisibilityRuleId, x.TagId });
                    table.ForeignKey(
                        name: "FK_ContentVisibilityRuleTags_ContentVisibilityRules_ContentVis~",
                        column: x => x.ContentVisibilityRuleId,
                        principalTable: "ContentVisibilityRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContentVisibilityRuleTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_ContentVisibilityRules_TagGroupKey",
                table: "ContentVisibilityRules",
                column: "TagGroupKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentVisibilityRuleTags_TagId",
                table: "ContentVisibilityRuleTags",
                column: "TagId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentVisibilityRuleTags");

            migrationBuilder.DropTable(
                name: "ContentVisibilityRules");

            migrationBuilder.DropColumn(
                name: "DisplayAllContent",
                table: "AppUsers");
        }
    }
}
