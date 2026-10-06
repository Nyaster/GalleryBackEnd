using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscoveryPeriodIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ImageLikes_CreatedAtUtc_ImageId",
                table: "ImageLikes",
                columns: new[] { "CreatedAtUtc", "ImageId" });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_CreatedAtUtc_ImageId",
                table: "Comments",
                columns: new[] { "CreatedAtUtc", "ImageId" },
                filter: "\"DeletedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ImageLikes_CreatedAtUtc_ImageId",
                table: "ImageLikes");

            migrationBuilder.DropIndex(
                name: "IX_Comments_CreatedAtUtc_ImageId",
                table: "Comments");
        }
    }
}
