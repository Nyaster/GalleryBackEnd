using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddAiUsageClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AiUsage",
                table: "Images",
                type: "text",
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.Sql("""
                UPDATE "Images"
                SET "AiUsage" = CASE
                    WHEN "image_type" = 'scraped' THEN 'HumanMade'
                    ELSE 'Unknown'
                END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Images_AiUsage_ModerationStatus_Visibility_UploadedAtUtc",
                table: "Images",
                columns: new[] { "AiUsage", "ModerationStatus", "Visibility", "UploadedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Images_AiUsage_ModerationStatus_Visibility_UploadedAtUtc",
                table: "Images");

            migrationBuilder.DropColumn(
                name: "AiUsage",
                table: "Images");
        }
    }
}
